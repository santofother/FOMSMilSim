// Melee spacing: column-fill formation slots (rear ranks press up), stepping into contact on a leash, contact-zone
// separation between enemies, and regiment pushing by mass. Owner: S04.
// Public API: none (internal to the sim; Battle calls it).
using System;
using System.Numerics;

namespace FOMS.Sim;

internal sealed class MeleeSpacing
{
    /// <summary>Same-team personal space, metres.</summary>
    const float FriendRadius = 0.75f;
    /// <summary>Enemies don't come closer than this: the contact zone between two lines.</summary>
    const float EnemyRadius = 0.95f;
    /// <summary>How far past their reach soldiers look for someone to step up to.</summary>
    const float StepInRange = 1.0f;
    /// <summary>How far a soldier may leave his slot to close with an enemy.</summary>
    const float StepLeash = 0.8f;
    /// <summary>Front-rank soldiers of an attacking regiment may lap round the enemy's end this far.</summary>
    const float WrapRange = 4f, WrapLeash = 2.5f;
    /// <summary>Soldiers stop closing at this fraction of their reach.</summary>
    const float HoldAt = 0.85f;
    /// <summary>Ground gained per second by an infinitely heavier regiment.</summary>
    const float PushSpeed = 0.6f;
    /// <summary>Soldiers in contact needed for a full push.</summary>
    const int FullPushContact = 8;
    /// <summary>An attacker whose front men mostly can't reach closes up at this fraction of its pace.</summary>
    const float FollowUpPace = 0.5f, FollowUpMinGap = 0.25f;
    const uint SaltRetarget = 113, SaltJitter = 127;

    readonly Battle _b;
    readonly SpatialGrid _grid;
    bool[] _alive = Array.Empty<bool>();
    /// <summary>Formation slot per soldier after the column fill, indexed by soldier.</summary>
    int[] _slot = Array.Empty<int>();

    public MeleeSpacing(Battle battle, SpatialGrid grid)
    {
        _b = battle;
        _grid = grid;
    }

    /// <summary>
    /// Sequential, before the soldier phase. Each column keeps its soldiers in their original rank order and closes
    /// up from the front, so when a front-rank man falls the men behind him step up one rank.
    /// </summary>
    public void BeginTick(bool[] aliveSnapshot)
    {
        _alive = aliveSnapshot;
        var s = _b.Soldiers;
        if (_slot.Length < s.Count) Array.Resize(ref _slot, s.Count);
        foreach (var reg in _b.Regiments)
        {
            int cols = Math.Max(1, reg.Cols);
            for (int c = 0; c < cols && c < reg.Count; c++)
            {
                int row = 0;
                for (int k = c; k < reg.Count; k += cols)
                {
                    int i = reg.First + k;
                    if (!aliveSnapshot[i]) { _slot[i] = k; continue; }
                    _slot[i] = row * cols + c;
                    row++;
                }
            }
        }
    }

    /// <summary>Slot index of soldier <paramref name="i"/> this tick (pass to Regiment.Slot).</summary>
    public int SlotOf(int i) => _slot[i];

    /// <summary>
    /// Soldier phase: picks the enemy soldier <paramref name="i"/> fights, -1 = none. Keeps the current target
    /// while it is alive and close, re-searches every ~0.5 s.
    /// </summary>
    public int ChooseTarget(int i, Regiment reg, Vector2 p, bool melee, float dt)
    {
        var s = _b.Soldiers;
        if (!melee) return -1;
        bool wrap = reg.Engaged && _slot[i] < reg.Cols;
        float radius = wrap ? WrapRange : reg.Def.Reach + StepInRange;
        int t = s.Target[i];
        s.Retarget[i] -= dt;
        bool valid = t >= 0 && _alive[t] && s.Team[t] != s.Team[i]
                     && Vector2.DistanceSquared(p, s.Pos[t]) <= (radius + 0.5f) * (radius + 0.5f);
        if (valid && s.Retarget[i] > 0) return t;
        s.Retarget[i] = 0.4f + _b.Rand(i, SaltRetarget) * 0.2f;
        return FindNearestEnemy(i, p, radius);
    }

    /// <summary>
    /// Soldier phase: desired velocity. Without a target, walk to the slot. With one, step up to just inside reach
    /// but never further than the leash from the slot, so the regiment keeps its ranks and depth.
    /// </summary>
    public Vector2 Desired(int i, Regiment reg, Vector2 p, int t, float pace)
    {
        var slot = reg.Slot(_slot[i]);
        if (t < 0) return Toward(p, slot, pace);

        var d = _b.Soldiers.Pos[t] - p;
        float dist = d.Length();
        float hold = reg.Def.Reach * HoldAt;
        if (dist <= hold || dist < 1e-4f) return Vector2.Zero;

        float leash = reg.Engaged && _slot[i] < reg.Cols ? WrapLeash : StepLeash;
        var want = p + d / dist * (dist - hold);
        var off = want - slot;
        float ol = off.Length();
        if (ol > leash) want = slot + off * (leash / ol);
        return Toward(p, want, pace);
    }

    static Vector2 Toward(Vector2 p, Vector2 target, float pace)
    {
        var delta = target - p;
        float len = delta.Length();
        if (len <= 0.22f) return Vector2.Zero;
        return delta / len * MathF.Min(len * 2f, pace);
    }

    /// <summary>
    /// Soldier phase: personal space. Friends keep 0.75 m apart; enemies keep a contact gap and the heavier man
    /// gives way less (push weighted by mass).
    /// </summary>
    public Vector2 Separation(int i, Vector2 p)
    {
        var s = _b.Soldiers;
        var regs = _b.Regiments;
        byte team = s.Team[i];
        float mi = regs[s.RegimentId[i]].Def.Mass;
        Vector2 push = Vector2.Zero;
        var sorted = _grid.Sorted;
        SpatialGrid.CellCoords(p, out int cx, out int cz);
        for (int z = cz - 1; z <= cz + 1; z++)
        for (int x = cx - 1; x <= cx + 1; x++)
        {
            _grid.CellRange(x, z, out int a, out int b);
            for (int k = a; k < b; k++)
            {
                int j = sorted[k];
                if (j == i) continue;
                bool enemy = s.Team[j] != team;
                float r = enemy ? EnemyRadius : FriendRadius;
                var d = p - s.Pos[j];
                float d2 = d.LengthSquared();
                if (d2 >= r * r) continue;
                if (d2 < 1e-6f) { d = Jitter(i + j); d2 = d.LengthSquared() + 1e-6f; }
                float dist = MathF.Sqrt(d2);
                float w = 12f;
                if (enemy)
                {
                    float mj = regs[s.RegimentId[j]].Def.Mass;
                    w = 16f * 2f * mj / (mi + mj);
                }
                push += d / dist * (r - dist) * w;
            }
        }
        return push;
    }

    Vector2 Jitter(int i) => new(_b.RandAt(0, i, SaltJitter) * 2f - 1f, _b.RandAt(0, i, SaltJitter + 1) * 2f - 1f);

    int FindNearestEnemy(int i, Vector2 p, float radius)
    {
        var s = _b.Soldiers;
        byte team = s.Team[i];
        var sorted = _grid.Sorted;
        int r = (int)MathF.Ceiling(radius / SpatialGrid.CellSize);
        SpatialGrid.CellCoords(p, out int cx, out int cz);
        int best = -1;
        float bestD2 = radius * radius;
        for (int z = cz - r; z <= cz + r; z++)
        for (int x = cx - r; x <= cx + r; x++)
        {
            _grid.CellRange(x, z, out int a, out int b);
            for (int k = a; k < b; k++)
            {
                int j = sorted[k];
                if (s.Team[j] == team) continue;
                float d2 = Vector2.DistanceSquared(p, s.Pos[j]);
                if (d2 < bestD2) { bestD2 = d2; best = j; }
            }
        }
        return best;
    }

    /// <summary>
    /// Sequential, end of tick: a regiment locked in melee with a lighter one drives it back along its own
    /// facing and follows it. Equal masses (after fatigue) don't move. Regiments holding a Move order aren't
    /// displaced (the order would walk them straight back), so pushing them does nothing.
    /// </summary>
    public void Push(int[] contact, int[] opponent, float dt)
    {
        var regs = _b.Regiments;
        for (int a = 0; a < regs.Length; a++)
        {
            int o = opponent[a];
            if (o < 0 || contact[a] == 0) continue;
            var ra = regs[a];
            var rb = regs[o];
            if (!Pushable(ra) || !Pushable(rb)) continue;
            float ma = ra.Def.Mass * FatigueRules.SpeedMul(ra.Fatigue);
            float mb = rb.Def.Mass * FatigueRules.SpeedMul(rb.Fatigue);
            if (ma <= mb) continue;
            float share = MathF.Min(1f, contact[a] / (float)FullPushContact);
            var step = ra.Facing * (PushSpeed * (ma - mb) / (ma + mb) * share * dt);
            var na = ra.Centre + step;
            var nb = rb.Centre + step;
            if (!_b.Terrain.Walkable(na) || !_b.Terrain.Walkable(nb)) continue;
            ra.Centre = na;
            rb.Centre = nb;
        }
    }

    /// <summary>
    /// Sequential, end of tick: an attacking regiment whose fighters mostly stand short of their targets (the enemy's
    /// flank file or front rank has fallen) steps forward into the gap. Without this, lines eroded from the side
    /// would stand a pace apart forever: the order system closes on the formation box, not the living men.
    /// </summary>
    public void FollowUp(int[] contact, int[] reaching, float[] gap, float[] enemyFront, float dt)
    {
        foreach (var reg in _b.Regiments)
        {
            int a = reg.Id;
            if (!reg.Engaged || reg.TargetRegiment < 0 || !Pushable(reg)) continue;
            if (reaching[a] == 0 || contact[a] >= reaching[a] || gap[a] < FollowUpMinGap) continue;
            // Never bring the front rank's slots closer than holding distance to the nearest enemy being fought.
            float room = enemyFront[a] - reg.Def.Reach * HoldAt - Vector2.Dot(reg.Centre, reg.Facing);
            float step = MathF.Min(MathF.Min(gap[a], room), RegimentOrders.Pace(reg) * FollowUpPace * dt);
            if (step <= 0f) continue;
            var next = reg.Centre + reg.Facing * step;
            if (_b.Terrain.Walkable(next)) reg.Centre = next;
        }
    }

    static bool Pushable(Regiment r) => !r.Routing && r.Order is not Move and not Withdraw;
}
