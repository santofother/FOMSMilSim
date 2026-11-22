// Formation steering: how a regiment's anchor (front centre + facing) moves toward a point. Small turns wheel on
// the march, big turns face about / re-form in place (MeleeSpacing re-assigns slots), and the last stretch is a
// straight walk into the final formation. Owner: S04b.
// Public API: none (internal to the sim; RegimentOrders calls it).
using System;
using System.Numerics;
using FOMS.Sim.Terrain;

namespace FOMS.Sim;

internal static class FormationSteering
{
    /// <summary>Destinations up to this far off the facing are reached by wheeling on the march.</summary>
    public const float WheelLimit = MathF.PI / 3f;
    /// <summary>Facing changes larger than this snap (men face about and re-form) instead of turning the block.</summary>
    public const float SnapAngle = MathF.PI / 6f;
    /// <summary>Inside this distance (or the formation's depth) the regiment walks straight into its final place.</summary>
    public const float FormUpDistance = 6f;
    /// <summary>Wheel rate = this × pace / frontage: the inner flank keeps walking, the outer one hurries.</summary>
    const float WheelFactor = 1.5f;
    const float MaxTurnRate = MathF.PI / 2f;
    const float FacingTolerance = 0.001f;

    public static float SignedAngle(Vector2 from, Vector2 to)
        => MathF.Atan2(from.X * to.Y - from.Y * to.X, Vector2.Dot(from, to));

    static Vector2 Rotate(Vector2 v, float a)
    {
        (float sin, float cos) = MathF.SinCos(a);
        return Vector2.Normalize(new Vector2(v.X * cos - v.Y * sin, v.X * sin + v.Y * cos));
    }

    public static float WheelRate(Regiment reg, float pace)
        => MathF.Min(MaxTurnRate, WheelFactor * pace / MathF.Max(reg.Width, 2f));

    static float HalfDepth(Regiment reg) => (reg.Rows - 1) * reg.SlotSpacing * 0.5f;

    /// <summary>
    /// Face a new way about the formation's middle. The block's footprint stays put; MeleeSpacing notices the jump and
    /// re-assigns slots by position, so a 180° turn is an about-face (rear rank becomes the front).
    /// </summary>
    public static void Reface(Regiment reg, Vector2 face)
    {
        float h = HalfDepth(reg);
        var mid = reg.Centre - reg.Facing * h;
        reg.Facing = face;
        reg.Centre = mid + face * h;
    }

    static void RotateAboutMiddle(Regiment reg, float angle)
    {
        float h = HalfDepth(reg);
        var mid = reg.Centre - reg.Facing * h;
        reg.Facing = Rotate(reg.Facing, angle);
        reg.Centre = mid + reg.Facing * h;
    }

    /// <summary>
    /// Turns toward <paramref name="face"/>: small differences turn gradually, big ones snap (unless
    /// <paramref name="snap"/> is false: S04c, closing with an enemy). True when aligned.
    /// </summary>
    public static bool FaceTowards(Regiment reg, Vector2 face, float omega, float dt, bool snap = true)
    {
        float a = SignedAngle(reg.Facing, face);
        if (MathF.Abs(a) <= FacingTolerance) { reg.Facing = face; return true; }
        if (snap && MathF.Abs(a) > SnapAngle) { Reface(reg, face); return false; }
        float limit = omega * dt;
        if (MathF.Abs(a) <= limit) { RotateAboutMiddle(reg, a); reg.Facing = face; return true; }
        RotateAboutMiddle(reg, MathF.CopySign(limit, a));
        return false;
    }

    /// <summary>
    /// One tick of moving the front centre toward <paramref name="target"/>, ending up facing <paramref name="face"/>.
    /// True when the regiment is there and facing the right way. <paramref name="snap"/> false (S04c, near an enemy):
    /// never face about in one go, turn gradually instead, so the men don't re-form under the enemy's nose.
    /// </summary>
    public static bool Approach(Regiment reg, Vector2 target, Vector2 face, float pace, float dt, ITerrain terrain,
                                bool snap = true)
    {
        float step = pace * dt;
        float omega = WheelRate(reg, pace);
        var delta = target - reg.Centre;
        float dist = delta.Length();
        float formUp = MathF.Max(FormUpDistance, reg.Rows * reg.SlotSpacing);

        if (dist > formUp)
        {
            var d = delta / dist;
            float theta = SignedAngle(reg.Facing, d);
            float radius = pace / MathF.Max(omega, 1e-4f);
            if (MathF.Abs(theta) <= WheelLimit && dist >= 2f * radius * MathF.Sin(MathF.Abs(theta)))
            {
                // WHY: wheel about the front, moving along the facing (pure pursuit). The inner flank keeps a slow
                // forward step instead of walking backwards as it did when the block spun about its centre.
                float turn = Math.Clamp(theta, -omega * dt, omega * dt);
                reg.Facing = MathF.Abs(theta - turn) <= FacingTolerance ? d : Rotate(reg.Facing, turn);
                Advance(reg, reg.Centre + reg.Facing * step, terrain);
                return false;
            }

            // Big turn: face the way you're going if that's roughly the final facing (then march and wheel),
            // otherwise keep the final facing and march by the flank / step back with men turned to walk.
            var march = MathF.Abs(SignedAngle(face, d)) <= WheelLimit ? d : face;
            if (!snap && MathF.Abs(SignedAngle(reg.Facing, march)) > WheelLimit)
            {
                FaceTowards(reg, march, omega, dt, snap: false); // turn first, then march
                return false;
            }
            FaceTowards(reg, march, omega, dt, snap);
            Advance(reg, reg.Centre + (target - reg.Centre) / MathF.Max((target - reg.Centre).Length(), 1e-6f) * step,
                terrain);
            return false;
        }

        bool aligned = FaceTowards(reg, face, omega, dt, snap);
        delta = target - reg.Centre;
        dist = delta.Length();
        if (dist > 1e-5f) Advance(reg, dist <= step ? target : reg.Centre + delta / dist * step, terrain);
        return aligned && Vector2.DistanceSquared(target, reg.Centre) <= 1e-8f;
    }

    static void Advance(Regiment reg, Vector2 next, ITerrain terrain)
    {
        // TODO(B-terrain): pathfinding. Follow a straight line and stop at unwalkable ground.
        if (terrain.Walkable(next)) reg.Centre = next;
    }
}
