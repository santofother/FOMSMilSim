// Regiment contact: formation boxes, which face of an enemy to attack, and the clearance regiments keep from each
// other (enemies: a little under reach; friends: a pace) so blocks never walk into or through one another.
// Owner: S04c.
// Public API: none (internal to the sim; RegimentOrders calls it).
using System;
using System.Numerics;

namespace FOMS.Sim;

/// <summary>A side of an enemy formation to attack.</summary>
internal enum Face { Front, Right, Rear, Left }

/// <summary>A regiment's footprint: the rectangle through its outermost men (not padded).</summary>
internal readonly record struct Box(Vector2 Mid, Vector2 F, Vector2 R, float Hw, float Hd);

internal static class RegimentContact
{
    /// <summary>Friendly blocks keep at least this gap between their outer men.</summary>
    public const float FriendGap = 1.0f;
    /// <summary>A regiment stops advancing when its men come within this fraction of its reach of an enemy block.</summary>
    public const float EnemyGapOfReach = 0.8f;
    /// <summary>Face choice: the score is distance off a face / (half extent + this pad), so a long line's front
    /// stays the front until the attacker is well round its end.</summary>
    const float FacePad = 10f;
    /// <summary>Switch to another face only when it scores this much better than the current one.</summary>
    const float FaceHysteresis = 1.25f;

    public static Box BoxOf(Regiment reg) => BoxAt(reg, reg.Centre, reg.Facing, reg.Cols);

    /// <summary>Footprint of <paramref name="reg"/> with its front centre at <paramref name="front"/>.</summary>
    public static Box BoxAt(Regiment reg, Vector2 front, Vector2 facing, int cols)
    {
        float s = reg.SlotSpacing;
        int c = Math.Max(1, cols);
        int alive = Math.Max(1, reg.Alive);
        int rows = (alive + c - 1) / c; // column fill brings the living forward, so count the ranks they fill
        float hw = (c - 1) * s * 0.5f, hd = (rows - 1) * s * 0.5f;
        var right = new Vector2(-facing.Y, facing.X);
        return new Box(front - facing * hd, facing, right, hw, hd);
    }

    /// <summary>
    /// Gap between two boxes along the best separating axis (negative = overlapping by that much). A lower bound of
    /// the true distance; exact for parallel blocks, which is the case that matters.
    /// </summary>
    public static float Separation(in Box a, in Box b)
    {
        var d = b.Mid - a.Mid;
        return MathF.Max(MathF.Max(Gap(a, b, d, a.F), Gap(a, b, d, a.R)), MathF.Max(Gap(a, b, d, b.F), Gap(a, b, d, b.R)));
    }

    static float Gap(in Box a, in Box b, Vector2 d, Vector2 axis)
    {
        float ra = MathF.Abs(Vector2.Dot(a.F, axis)) * a.Hd + MathF.Abs(Vector2.Dot(a.R, axis)) * a.Hw;
        float rb = MathF.Abs(Vector2.Dot(b.F, axis)) * b.Hd + MathF.Abs(Vector2.Dot(b.R, axis)) * b.Hw;
        return MathF.Abs(Vector2.Dot(d, axis)) - ra - rb;
    }

    /// <summary>Gap <paramref name="reg"/> must keep from <paramref name="other"/>.</summary>
    public static float Margin(Regiment reg, Regiment other)
        => other.Team == reg.Team ? FriendGap : EnemyGapOfReach * reg.Def.Reach;

    /// <summary>
    /// Smallest (gap − margin) between <paramref name="me"/> and every standing regiment, and which one it is.
    /// Negative = too close. Routing and destroyed regiments don't block (routers flee through anything).
    /// </summary>
    /// <param name="boxes">Every regiment's current box (RegimentOrders keeps them up to date as it steps).</param>
    public static float Clearance(Regiment[] regs, Box[] boxes, Regiment reg, in Box me, out int blocker)
    {
        float worst = float.PositiveInfinity;
        blocker = -1;
        foreach (var o in regs)
        {
            if (o == reg || o.Routing || o.Alive <= 0) continue;
            float c = Separation(me, boxes[o.Id]) - Margin(reg, o);
            if (c < worst) { worst = c; blocker = o.Id; }
        }
        return worst;
    }

    /// <summary>How squarely <paramref name="att"/> stands off <paramref name="face"/> of <paramref name="enemy"/>
    /// (distance off the face over half extent + pad; negative = on the far side).</summary>
    public static float FaceScore(Regiment att, Regiment enemy, Face face)
    {
        var e = BoxOf(enemy);
        var o = BoxOf(att).Mid - e.Mid;
        float f = Vector2.Dot(o, e.F) / (e.Hd + FacePad), r = Vector2.Dot(o, e.R) / (e.Hw + FacePad);
        return face switch { Face.Front => f, Face.Rear => -f, Face.Right => r, _ => -r };
    }

    /// <summary>Which face of <paramref name="enemy"/> to attack from where <paramref name="att"/> stands.</summary>
    public static Face ChooseFace(Regiment att, Regiment enemy, int current)
    {
        var e = BoxOf(enemy);
        var o = BoxOf(att).Mid - e.Mid;
        float f = Vector2.Dot(o, e.F) / (e.Hd + FacePad), r = Vector2.Dot(o, e.R) / (e.Hw + FacePad);
        Face best = MathF.Abs(f) >= MathF.Abs(r) ? (f >= 0 ? Face.Front : Face.Rear) : (r >= 0 ? Face.Right : Face.Left);
        if (current < 0 || (Face)current == best) return best;
        float Score(Face face) => face switch { Face.Front => f, Face.Rear => -f, Face.Right => r, _ => -r };
        return Score(best) > Score((Face)current) * FaceHysteresis + 1e-3f ? best : (Face)current;
    }

    /// <summary>The line through the men of one face: its centre, outward normal, tangent and half length.</summary>
    public static void FaceLine(in Box b, Face face, out Vector2 centre, out Vector2 normal, out Vector2 tangent,
                                out float half)
    {
        switch (face)
        {
            case Face.Front: normal = b.F; centre = b.Mid + b.F * b.Hd; tangent = b.R; half = b.Hw; break;
            case Face.Rear: normal = -b.F; centre = b.Mid - b.F * b.Hd; tangent = b.R; half = b.Hw; break;
            case Face.Right: normal = b.R; centre = b.Mid + b.R * b.Hw; tangent = b.F; half = b.Hd; break;
            default: normal = -b.R; centre = b.Mid - b.R * b.Hw; tangent = b.F; half = b.Hd; break;
        }
    }
}
