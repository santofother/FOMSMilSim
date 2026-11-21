// RegGeom: ground geometry of a regiment's footprint, shared by input, markers and HUD.
// Owner: S03
// Public API: RegGeom.Mid, Depth, Contains
using System;
using Godot;
using FOMS.Sim;
using SV2 = System.Numerics.Vector2;

namespace FOMS.Game.PlayerInput;

public static class RegGeom
{
    /// <summary>Depth (metres) of a block of <paramref name="alive"/> men, <paramref name="width"/> wide.</summary>
    public static float Depth(float spacing, float width, int alive)
    {
        int cols = Math.Max(1, (int)MathF.Round(width / spacing));
        int rows = Math.Max(1, (alive + cols - 1) / cols);
        return rows * spacing;
    }

    public static float Depth(Regiment r) => Depth(r.Def.Spacing, r.Width, Math.Max(1, r.Alive));

    /// <summary>Middle of the footprint on the ground.</summary>
    public static SV2 Mid(Regiment r) => r.Centre - r.Facing * (Depth(r) * 0.5f);

    public static Vector3 ToWorld(SV2 p, float y = 0f) => new(p.X, y, p.Y);

    /// <summary>True when ground point <paramref name="g"/> is inside the footprint (plus a margin).</summary>
    public static bool Contains(Regiment r, SV2 g, float margin = 1.5f)
    {
        var rel = g - r.Centre;
        float x = SV2.Dot(rel, r.Right);
        float back = -SV2.Dot(rel, r.Facing);
        return MathF.Abs(x) <= r.Width * 0.5f + margin && back >= -margin && back <= Depth(r) + margin;
    }
}
