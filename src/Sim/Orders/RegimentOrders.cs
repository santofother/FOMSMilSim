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
            case Attack attack when Enemy(reg, attack.TargetRegiment) != null:
            case Halt:
            case Withdraw:
                break;
            default:
                return false;
        }

        reg.Order = order;
        reg.TargetRegiment = -1;
        _arrived[reg.Id] = false;
        return true;
    }

    public void Step(float dt)
    {
        foreach (var reg in regiments)
        {
            if (reg.Routing) continue;

            switch (reg.Order)
            {
                case Move move:
                    MoveTo(reg, move, dt);
                    break;
                case Attack attack:
                    CloseWith(reg, Enemy(reg, attack.TargetRegiment), dt);
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
        }
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

    void CloseWith(Regiment reg, Regiment enemy, float dt)
    {
        reg.TargetRegiment = enemy?.Id ?? -1;
        reg.Activity = RegimentActivity.Idle;
        if (enemy == null) return;

        // Approach the nearest edge of the enemy formation, including its flanks and rear.
        var offset = reg.Centre - enemy.Centre;
        float halfFront = (enemy.Cols - 1) * enemy.SlotSpacing * 0.5f;
        float depth = (enemy.Rows - 1) * enemy.SlotSpacing;
        var contact = enemy.Centre
            + enemy.Right * Math.Clamp(Vector2.Dot(offset, enemy.Right), -halfFront, halfFront)
            + enemy.Facing * Math.Clamp(Vector2.Dot(offset, enemy.Facing), -depth, 0f);
        var delta = contact - reg.Centre;
        float gap = delta.Length();
        float pace = Pace(reg);
        float distanceToContact = MathF.Max(0f, gap - reg.Def.Reach);
        reg.Activity = distanceToContact <= pace * dt
            ? RegimentActivity.Fighting : RegimentActivity.Engaging;
        if (gap <= 1e-4f) return;

        var direction = delta / gap;
        if (reg.Activity == RegimentActivity.Engaging)
        {
            // S04b: approach like a Move (wheel / face about), ending at reach, facing the contact point.
            FormationSteering.Approach(reg, reg.Centre + direction * distanceToContact, direction, pace, dt, terrain);
            return;
        }
        // In contact: only small turns about the front, so the fighting rank stays where it is.
        if (TurnTowards(reg, direction, pace, dt) <= TravelFacingTolerance && distanceToContact > 0f)
            Advance(reg, reg.Centre + direction * MathF.Min(pace * dt, distanceToContact));
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

    Regiment Enemy(Regiment reg, int id)
    {
        if ((uint)id >= (uint)regiments.Length) return null;
        var enemy = regiments[id];
        return enemy.Team != reg.Team && !enemy.Routing && enemy.Alive > 0 ? enemy : null;
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
