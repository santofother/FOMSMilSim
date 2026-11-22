using System;
using System.Numerics;
using FOMS.Sim.Terrain;

namespace FOMS.Sim;

/// <summary>Applies orders and advances regiment formations once per simulation tick.</summary>
internal sealed class RegimentOrders(Regiment[] regiments, bool[] autoAttack, ITerrain terrain)
{
    internal const float LooseSpacing = 1.5f;

    const float FacingTolerance = 0.001f;
    const float TravelFacingTolerance = 0.15f;
    const float MaxTurnRate = MathF.PI / 2f;

    /// <summary>S04b: a Move order that has arrived holds its ground (pushes and follow-ups don't get walked back).</summary>
    readonly bool[] _arrived = new bool[regiments.Length];

    // S04c: attacks pick an enemy face and line up on it; nobody walks into anybody.
    /// <summary>Carrot distance when lining up on a face: the attacker steers for a point this far out along the
    /// face's normal, so it swings onto the line and comes in square instead of corner first.</summary>
    const float LineUpLook = 8f;
    /// <summary>Within this gap of the enemy a regiment no longer faces about in one go while closing.</summary>
    const float NoSnapRange = 15f;
    /// <summary>Destination nudge: step and maximum shift sideways when two destinations would overlap.</summary>
    const float NudgeStep = 0.5f, NudgeMax = 60f;
    /// <summary>Face of the enemy each regiment is attacking (-1 = not chosen yet) and which enemy it belongs to.</summary>
    readonly int[] _face = Fill(regiments.Length, -1);
    readonly int[] _faceEnemy = Fill(regiments.Length, -1);
    /// <summary>Planned stop along the chosen face (offset from its centre) and the front/facing it claims, so two
    /// friends attacking the same enemy take different stretches of it.</summary>
    readonly float[] _faceU = new float[regiments.Length];
    readonly bool[] _hasClaim = new bool[regiments.Length];
    readonly Vector2[] _claimStop = new Vector2[regiments.Length], _claimFace = new Vector2[regiments.Length];
    /// <summary>Re-plan an approach every this many ticks (staggered by regiment), or at once when it has none.</summary>
    const int ReplanTicks = 10;
    /// <summary>Lateral search step along a face for a stretch no friend has claimed, and the least shared front.</summary>
    const float SlideStep = 1.5f, MinOverlap = 6f;
    /// <summary>Faces further round than this (FaceScore) aren't considered: getting there means going round.</summary>
    const float FarSide = -0.2f;
    int _tick;
    /// <summary>Every regiment's box, refreshed as each one moves this tick (clearance is checked against these).
    /// A pure start-of-tick snapshot lets two blocks closing on each other creep together a step per tick.</summary>
    readonly Box[] _boxes = new Box[regiments.Length];
    /// <summary>Regiments that got a Move this tick; their destinations are nudged clear of friends in Step.</summary>
    readonly System.Collections.Generic.List<int> _newMoves = new();
    int[] _contact = Array.Empty<int>();

    static int[] Fill(int n, int v) { var a = new int[n]; Array.Fill(a, v); return a; }

    public bool Apply(Order order)
    {
        var reg = regiments[order.RegimentId];
        switch (order)
        {
            case SetFormation { Formation: FormationKind.Line or FormationKind.Loose } formation:
                reg.Formation = formation.Formation;
                reg.Width = reg.Cols * reg.SlotSpacing;
                return true;
            case Move move when float.IsFinite(move.FrontCentre.X) && float.IsFinite(move.FrontCentre.Y)
                            && float.IsFinite(move.Facing.LengthSquared()) && move.Facing.LengthSquared() > 0f
                            && float.IsFinite(move.Width) && move.Width > 0:
                Resize(reg, move.Width);
                break;
            case Attack attack when Enemy(reg, attack.TargetRegiment, routers: true) != null: // FIX(S05): pursue
            case Halt:
            case Withdraw:
                break;
            default:
                return false;
        }

        reg.Order = order;
        reg.TargetRegiment = -1;
        _arrived[reg.Id] = false;
        _face[reg.Id] = -1;
        _hasClaim[reg.Id] = false;
        if (order is Move) _newMoves.Add(reg.Id);
        return true;
    }

    /// <param name="contact">Soldiers in melee contact per regiment, from the last tick (MeleeSystem.Contact).</param>
    public void Step(float dt, int[] contact = null)
    {
        _contact = contact ?? Array.Empty<int>();
        _tick++;
        foreach (var r in regiments) _boxes[r.Id] = RegimentContact.BoxOf(r);
        NudgeNewDestinations();
        foreach (var reg in regiments)
        {
            if (reg.Routing) continue;
            var c0 = reg.Centre;
            var f0 = reg.Facing;

            switch (reg.Order)
            {
                case Move move:
                    MoveTo(reg, move, dt);
                    break;
                case Attack attack:
                    CloseWith(reg, Enemy(reg, attack.TargetRegiment, routers: true), dt);
                    break;
                case Withdraw:
                    WithdrawFrom(reg, ClosestEnemy(reg), dt);
                    break;
                case null when autoAttack[reg.Team]:
                    CloseWith(reg, Enemy(reg, reg.TargetRegiment) ?? ClosestEnemy(reg), dt);
                    break;
                default:
                    reg.Activity = RegimentActivity.Idle;
                    break;
            }
            if (reg.Order is not Withdraw && (c0 != reg.Centre || f0 != reg.Facing)) KeepClear(reg, c0, f0);
            _boxes[reg.Id] = RegimentContact.BoxOf(reg); // live: later regiments see where this one went
        }
    }

    bool InContact(Regiment reg) => reg.Id < _contact.Length && _contact[reg.Id] > 0;

    /// <summary>
    /// S04c: undo (or deflect) this tick's step if it brought the block too close to another one. A friend in the way
    /// is side-stepped; an enemy in the way stops the regiment, and a Move that runs into an enemy ends there.
    /// Steps that don't make an existing overlap worse are always allowed, so blocks can still back out or square up.
    /// </summary>
    void KeepClear(Regiment reg, Vector2 c0, Vector2 f0)
    {
        float before = RegimentContact.Clearance(regiments, _boxes, reg, RegimentContact.BoxAt(reg, c0, f0, reg.Cols), out _);
        if (Fine(reg, reg.Centre, reg.Facing, before, out int blocker)) return;

        var c1 = reg.Centre;
        var f1 = reg.Facing;
        var other = regiments[blocker];
        if (other.Team == reg.Team)
        {
            if (SideStep(reg, c0, c1, f1, before, other)) return;
        }
        else if (reg.Order is Move && !_arrived[reg.Id])
        {
            // Ran into the enemy: the move ends here and the men fight.
            _arrived[reg.Id] = true;
            reg.TargetRegiment = other.Id;
            reg.Activity = RegimentActivity.Fighting;
        }
        else if (!InContact(reg) && SideStep(reg, c0, c1, f1, before, other)) return; // attacker slides round a corner

        // Keep the turn without the step if that alone is fine, else stand still. Turning toward the regiment you are
        // fighting is always allowed: the men sort out the last metre, and refusing it can deadlock two blocks.
        reg.Centre = c0;
        if (blocker == reg.TargetRegiment || Fine(reg, c0, f1, before, out _)) return;
        reg.Facing = f0;
    }

    static readonly float[] SideSteps = { 45f, 90f, -45f, -90f };

    /// <summary>Turn this tick's step away from the blocker's middle (then the other way) until it's clear.</summary>
    bool SideStep(Regiment reg, Vector2 c0, Vector2 c1, Vector2 f1, float before, Regiment other)
    {
        var step = c1 - c0;
        if (step.LengthSquared() <= 1e-10f) return false;
        var toOther = _boxes[other.Id].Mid - c0;
        float away = step.X * toOther.Y - step.Y * toOther.X > 0 ? -1f : 1f;
        foreach (float deg in SideSteps)
        {
            var c = c0 + Rotate(step, away * deg * (MathF.PI / 180f));
            if (terrain.Walkable(c) && Fine(reg, c, f1, before, out _)) { reg.Centre = c; return true; }
        }
        return false;
    }

    bool Fine(Regiment reg, Vector2 centre, Vector2 facing, float before, out int blocker)
    {
        float after = RegimentContact.Clearance(regiments, _boxes, reg, RegimentContact.BoxAt(reg, centre, facing, reg.Cols),
            out blocker);
        return after >= 0f || after >= before - 1e-4f;
    }

    static Vector2 Rotate(Vector2 v, float a)
    {
        (float sin, float cos) = MathF.SinCos(a);
        return new Vector2(v.X * cos - v.Y * sin, v.X * sin + v.Y * cos);
    }

    /// <summary>
    /// S04c: a new Move whose destination overlaps a friend's (or where a friend stands) slides sideways along its
    /// frontage until it's clear. All of this tick's orders are applied first, so a group order sees its own
    /// regiments' new destinations, not where they stand now.
    /// </summary>
    void NudgeNewDestinations()
    {
        foreach (int id in _newMoves)
        {
            var reg = regiments[id];
            if (reg.Order is not Move move) continue;
            var facing = Vector2.Normalize(move.Facing);
            var right = new Vector2(-facing.Y, facing.X);
            if (Claimed(reg, move.FrontCentre, facing, out var claim) >= 0f) continue;
            float side = Vector2.Dot(RegimentContact.BoxAt(reg, move.FrontCentre, facing, reg.Cols).Mid - claim, right)
                         >= 0f ? 1f : -1f;
            for (int pass = 0; pass < 2; pass++, side = -side)
            {
                for (float shift = NudgeStep; shift <= NudgeMax; shift += NudgeStep)
                {
                    var front = move.FrontCentre + right * (side * shift);
                    if (!terrain.Walkable(front) || Claimed(reg, front, facing, out _) < 0f) continue;
                    reg.Order = move with { FrontCentre = front };
                    pass = 2;
                    break;
                }
            }
        }
        _newMoves.Clear();
    }

    /// <summary>Smallest gap − FriendGap between a destination box and every friend's claim (its destination if it's
    /// still marching there, else where it stands).</summary>
    float Claimed(Regiment reg, Vector2 front, Vector2 facing, out Vector2 claimMid)
    {
        var me = RegimentContact.BoxAt(reg, front, facing, reg.Cols);
        float worst = float.PositiveInfinity;
        claimMid = me.Mid;
        foreach (var o in regiments)
        {
            if (o == reg || o.Team != reg.Team || o.Routing || o.Alive <= 0) continue;
            var box = o.Order is Move m && !_arrived[o.Id]
                ? RegimentContact.BoxAt(o, m.FrontCentre, Vector2.Normalize(m.Facing), o.Cols)
                : RegimentContact.BoxOf(o);
            float c = RegimentContact.Separation(me, box) - RegimentContact.FriendGap;
            if (c < worst) { worst = c; claimMid = box.Mid; }
        }
        return worst;
    }

    // FIX(S04): fatigue slows walking and running (FatigueRules.SpeedMul).
    public static float Pace(Regiment reg) => FatigueRules.SpeedMul(reg.Fatigue) * reg.Order switch
    {
        Move { Run: true } or Attack { Run: true } => reg.Def.RunSpeed,
        null when reg.TargetRegiment >= 0 => reg.Def.RunSpeed,
        _ => reg.Def.Speed,
    };

    static void Resize(Regiment reg, float width)
    {
        float columns = MathF.Round(width / reg.SlotSpacing, MidpointRounding.AwayFromZero);
        reg.Cols = (int)Math.Clamp(columns, 1, reg.Count);
        reg.Rows = 1 + (reg.Count - 1) / reg.Cols;
        reg.Width = reg.Cols * reg.SlotSpacing;
    }

    // S04b: wheel on the march, face about for big turns, walk straight into the final formation.
    void MoveTo(Regiment reg, Move move, float dt)
    {
        if (_arrived[reg.Id]) { reg.Activity = RegimentActivity.Idle; return; }
        reg.Activity = RegimentActivity.Moving;
        var facing = Vector2.Normalize(move.Facing);
        if (!FormationSteering.Approach(reg, move.FrontCentre, facing, Pace(reg), dt, terrain)) return;
        _arrived[reg.Id] = true;
        reg.Activity = RegimentActivity.Idle;
    }

    // S04c: pick a face of the enemy (front, a flank or the rear), swing onto the line square to it, and come in
    // front to front. In contact, square up to that face rather than turning toward a corner.
    void CloseWith(Regiment reg, Regiment enemy, float dt)
    {
        reg.TargetRegiment = enemy?.Id ?? -1;
        reg.Activity = RegimentActivity.Idle;
        if (enemy == null) { _face[reg.Id] = -1; _hasClaim[reg.Id] = false; return; }

        if (_faceEnemy[reg.Id] != enemy.Id) { _faceEnemy[reg.Id] = enemy.Id; _face[reg.Id] = -1; }
        bool contact = InContact(reg);
        // The plan is locked while the men are fighting; otherwise re-made every few ticks as both sides move.
        if (_face[reg.Id] < 0 || (!contact && (_tick + reg.Id) % ReplanTicks == 0)) Plan(reg, enemy);
        var enemyBox = RegimentContact.BoxOf(enemy);
        RegimentContact.FaceLine(enemyBox, (Face)_face[reg.Id], out var faceCentre, out var n, out var t, out _);
        var stop = faceCentre + t * _faceU[reg.Id] + n * reg.Def.Reach;
        var face = -n;
        _claimStop[reg.Id] = stop;
        _claimFace[reg.Id] = face;
        _hasClaim[reg.Id] = true;
        var rel = reg.Centre - stop;
        float a = Vector2.Dot(rel, n), b = Vector2.Dot(rel, t);
        float pace = Pace(reg);
        float step = pace * dt;

        if (contact || (a <= step + 1e-3f && MathF.Abs(b) <= 0.5f))
        {
            reg.Activity = RegimentActivity.Fighting;
            // Small turns only, about the fighting front, so the rank in contact stays where it is.
            if (TurnTowards(reg, face, pace, dt) <= TravelFacingTolerance && a > 1e-3f)
                Advance(reg, reg.Centre - n * MathF.Min(step, a));
            return;
        }

        reg.Activity = RegimentActivity.Engaging;
        // Pure pursuit of a carrot on the face's normal line: far out it pulls the regiment onto the line, close in
        // it is the stop point itself. Never further out than the regiment is now, and at least |b| out, so the
        // last stretch comes in within ~45° of square.
        float along = MathF.Max(0f, MathF.Min(a, MathF.Max(a - LineUpLook, MathF.Abs(b))));
        var carrot = stop + n * along;
        bool snap = RegimentContact.Separation(RegimentContact.BoxOf(reg), enemyBox) > NoSnapRange;
        FormationSteering.Approach(reg, carrot, face, pace, dt, terrain, snap);
    }

    /// <summary>
    /// S04c: choose the face to hit and where along it. The preferred face (with hysteresis) first, then the others
    /// this side of the enemy, best first. Along each face: the spot that needs the least sideways shift while the
    /// shorter frontage overlaps the longer completely, then spots further along (keeping at least MinOverlap of
    /// shared front) until one no friend has claimed. Nothing free: queue for the preferred spot.
    /// </summary>
    void Plan(Regiment reg, Regiment enemy)
    {
        // Two regiments attacking each other meet front to front. Without this each runs for the other's flank and
        // they chase each other in circles (seen in m0_clash).
        // Only when we're already on its front side; going round it to reach its front would deadlock on its flank.
        bool mutual = enemy.TargetRegiment == reg.Id && enemy.Order is null or Attack
                      && RegimentContact.FaceScore(reg, enemy, Face.Front) >= 0f;
        var pref = mutual ? Face.Front : RegimentContact.ChooseFace(reg, enemy, _face[reg.Id]);
        Span<int> faces = stackalloc int[4];
        Span<float> scores = stackalloc float[4];
        int count = 0;
        faces[count++] = (int)pref;
        for (int f = 0; f < 4; f++)
        {
            if (f == (int)pref) continue;
            float sc = RegimentContact.FaceScore(reg, enemy, (Face)f);
            if (sc < FarSide) continue;
            int k = count++;
            while (k > 1 && scores[k - 1] < sc) { faces[k] = faces[k - 1]; scores[k] = scores[k - 1]; k--; }
            faces[k] = f;
            scores[k] = sc;
        }

        var enemyBox = RegimentContact.BoxOf(enemy);
        float ownHalf = (reg.Cols - 1) * reg.SlotSpacing * 0.5f;
        float prefU = 0f;
        for (int k = 0; k < count; k++)
        {
            RegimentContact.FaceLine(enemyBox, (Face)faces[k], out var c, out var n, out var t, out float half);
            // A longer attacker centres on the face and laps both ends evenly; a shorter one shifts as little as it can.
            float lim = MathF.Max(0f, half - ownHalf);
            float proj = Vector2.Dot(reg.Centre - c, t);
            float u0 = Math.Clamp(proj, -lim, lim);
            float first = proj >= 0f ? 1f : -1f; // look along the face toward where we stand first (no handedness)
            if (k == 0) prefU = u0;
            float minOv = MathF.Min(MathF.Min(2f * half, 2f * ownHalf), MinOverlap);
            float range = MathF.Max(lim, half + ownHalf - minOv);
            for (int j = 0; j <= (int)(2f * range / SlideStep) + 1; j++)
            {
                float u = u0 + first * (j % 2 == 1 ? 1 : -1) * ((j + 1) / 2) * SlideStep;
                if (MathF.Abs(u) > range + 1e-3f) continue;
                var stop = c + t * u + n * reg.Def.Reach;
                if (!FreeOfFriends(reg, RegimentContact.BoxAt(reg, stop, -n, reg.Cols))) continue;
                _face[reg.Id] = faces[k];
                _faceU[reg.Id] = u;
                return;
            }
        }
        _face[reg.Id] = (int)pref;
        _faceU[reg.Id] = prefU;
    }

    /// <summary>True if <paramref name="box"/> keeps FriendGap from every friend's claim (where it's attacking, or
    /// else where it stands).</summary>
    bool FreeOfFriends(Regiment reg, in Box box)
    {
        foreach (var o in regiments)
        {
            if (o == reg || o.Team != reg.Team || o.Routing || o.Alive <= 0) continue;
            var claim = _hasClaim[o.Id] && o.Order is not Move
                ? RegimentContact.BoxAt(o, _claimStop[o.Id], _claimFace[o.Id], o.Cols)
                : _boxes[o.Id];
            if (RegimentContact.Separation(box, claim) < RegimentContact.FriendGap) return false;
        }
        return true;
    }

    void WithdrawFrom(Regiment reg, Regiment enemy, float dt)
    {
        reg.Activity = RegimentActivity.Idle;
        if (enemy == null) return;

        var delta = enemy.Centre - reg.Centre;
        var direction = delta.LengthSquared() > 1e-8f ? Vector2.Normalize(delta) : reg.Facing;
        reg.Activity = RegimentActivity.Moving;
        if (TurnTowards(reg, direction, reg.Def.Speed, dt) > TravelFacingTolerance) return;
        Advance(reg, reg.Centre - reg.Facing * (reg.Def.Speed * dt));
    }

    void Advance(Regiment reg, Vector2 next)
    {
        // TODO(B-terrain): pathfinding. Follow a straight line and stop at unwalkable ground.
        if (terrain.Walkable(next)) reg.Centre = next;
    }

    static float TurnTowards(Regiment reg, Vector2 facing, float pace, float dt)
    {
        float cross = reg.Facing.X * facing.Y - reg.Facing.Y * facing.X;
        float turn = MathF.Atan2(cross, Vector2.Dot(reg.Facing, facing));
        float radius = new Vector2((reg.Cols - 1) * 0.5f, reg.Rows - 1).Length() * reg.SlotSpacing;
        float limit = MathF.Min(MaxTurnRate, pace / MathF.Max(radius, 1f)) * dt;
        if (MathF.Abs(turn) <= MathF.Max(limit, FacingTolerance))
        {
            reg.Facing = facing;
            return 0f;
        }

        float step = MathF.CopySign(limit, turn);
        (float sin, float cos) = MathF.SinCos(step);
        var old = reg.Facing;
        reg.Facing = Vector2.Normalize(new Vector2(old.X * cos - old.Y * sin, old.X * sin + old.Y * cos));
        return MathF.Abs(turn - step);
    }

    // FIX(S05): an explicit Attack may pursue a routing regiment (until its men leave the field); auto-attack doesn't.
    Regiment Enemy(Regiment reg, int id, bool routers = false)
    {
        if ((uint)id >= (uint)regiments.Length) return null;
        var enemy = regiments[id];
        return enemy.Team != reg.Team && (routers ? !enemy.Fled : !enemy.Routing) && enemy.Alive > 0 ? enemy : null;
    }

    Regiment ClosestEnemy(Regiment reg)
    {
        Regiment nearest = null;
        float bestDistance = float.PositiveInfinity;
        foreach (var enemy in regiments)
        {
            if (Enemy(reg, enemy.Id) == null) continue;
            float distance = Vector2.DistanceSquared(reg.Centre, enemy.Centre);
            if (distance >= bestDistance) continue;
            bestDistance = distance;
            nearest = enemy;
        }
        return nearest;
    }
}
