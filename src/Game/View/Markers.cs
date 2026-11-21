// Markers: team banners above regiments, selection/hover outlines, destination ghosts, order-line preview.
// Owner: S03
// Public API: Markers(BattleScene, BattleInput)
using System;
using System.Collections.Generic;
using Godot;
using FOMS.Game.PlayerInput;
using FOMS.Sim;
using SimBattle = FOMS.Sim.Battle;
using SV2 = System.Numerics.Vector2;

namespace FOMS.Game.View;

public partial class Markers : Node3D
{
    static readonly Color[] TeamColour = { new(0.2f, 0.45f, 1f), new(0.95f, 0.2f, 0.2f), new(0.9f, 0.8f, 0.2f), new(0.3f, 0.8f, 0.3f) };

    readonly Battle.BattleScene _scene;
    readonly BattleInput _input;
    readonly ImmediateMesh _mesh = new();
    readonly List<Node3D> _banners = new();
    readonly List<StandardMaterial3D> _flagMats = new();
    readonly List<(Vector3 a, Vector3 b, Vector3 c, Color col)> _tris = new();
    SimBattle _battle;
    float _lineW = 0.4f;

    public Markers(Battle.BattleScene scene, BattleInput input) { _scene = scene; _input = input; Name = "Markers"; }

    public override void _Ready()
    {
        AddChild(new MeshInstance3D
        {
            Name = "Overlay",
            Mesh = _mesh,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                VertexColorUseAsAlbedo = true,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            },
        });
    }

    void Rebuild()
    {
        foreach (var b in _banners) b.QueueFree();
        _banners.Clear(); _flagMats.Clear();
        var pole = new StandardMaterial3D { AlbedoColor = new Color(0.25f, 0.18f, 0.1f) };
        foreach (var r in _battle.Regiments)
        {
            var col = TeamColour[Math.Min(r.Team, TeamColour.Length - 1)];
            var node = new Node3D { Name = $"Banner{r.Id}" };
            node.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.18f, 6.5f, 0.18f) }, Position = new Vector3(0, 3.25f, 0), MaterialOverride = pole });
            var mat = new StandardMaterial3D { AlbedoColor = col, BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled, CullMode = BaseMaterial3D.CullModeEnum.Disabled, EmissionEnabled = true, Emission = col, EmissionEnergyMultiplier = 0.1f };
            node.AddChild(new MeshInstance3D { Mesh = new QuadMesh { Size = new Vector2(2.6f, 1.6f) }, Position = new Vector3(1.4f, 5.7f, 0), MaterialOverride = mat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
            AddChild(node);
            _banners.Add(node); _flagMats.Add(mat);
        }
    }

    public override void _Process(double delta)
    {
        var battle = _scene.Battle;
        if (battle == null) return;
        if (!ReferenceEquals(battle, _battle)) { _battle = battle; Rebuild(); }
        float dt = (float)delta;
        var cam = GetViewport().GetCamera3D();
        _lineW = cam == null ? 0.4f : Mathf.Clamp(cam.GlobalPosition.Y * 0.004f, 0.25f, 1.2f);

        foreach (var r in _battle.Regiments)
        {
            var n = _banners[r.Id];
            n.Visible = r.Alive > 0;
            var target = RegGeom.ToWorld(RegGeom.Mid(r));
            n.Position = n.Position.DistanceTo(target) > 30f ? target : n.Position.Lerp(target, 1f - MathF.Exp(-14f * dt));
            bool sel = _input.Selected.Contains(r.Id);
            var baseCol = TeamColour[Math.Min(r.Team, TeamColour.Length - 1)];
            _flagMats[r.Id].AlbedoColor = sel ? baseCol.Lerp(Colors.White, 0.35f) : baseCol * 0.8f;
            _flagMats[r.Id].EmissionEnergyMultiplier = sel ? 1.2f : 0.1f;
            n.Scale = sel ? new Vector3(1.25f, 1.25f, 1.25f) : Vector3.One;
        }

        _tris.Clear();
        DrawFeedback();
        Flush();
    }

    // ---------------- drawing ----------------
    void DrawFeedback()
    {
        var sel = new Color(1f, 0.95f, 0.5f, 0.95f);
        foreach (var r in _battle.Regiments)
        {
            if (r.Alive <= 0) continue;
            if (_input.Selected.Contains(r.Id))
            {
                Footprint(r.Centre, r.Facing, r.Width, RegGeom.Depth(r), sel, new Color(1f, 0.95f, 0.5f, 0.07f));
                if (!_input.PreviewActive) DrawOrderGhost(r);
            }
        }
        if (_input.HoverId >= 0)
        {
            var h = _battle.Regiments[_input.HoverId];
            if (h.Alive > 0)
            {
                bool enemy = h.Team != BattleInput.PlayerTeam;
                var c = enemy ? new Color(1f, 0.3f, 0.3f) : new Color(1f, 1f, 1f);
                Footprint(h.Centre, h.Facing, h.Width, RegGeom.Depth(h), c with { A = 0.85f }, c with { A = 0.16f });
            }
        }
        if (_input.PreviewActive)
            foreach (var p in _input.Preview)
                Ghost(_battle.Regiments[p.Id], p.Front, p.Facing, p.Width, new Color(0.45f, 1f, 0.5f));
    }

    void DrawOrderGhost(Regiment r)
    {
        switch (r.Order)
        {
            case Move m:
                Ghost(r, m.FrontCentre, SV2.Normalize(m.Facing), m.Width, m.Run ? new Color(1f, 0.85f, 0.3f) : new Color(0.45f, 1f, 0.5f));
                break;
            case Attack a when a.TargetRegiment >= 0 && _battle.Regiments[a.TargetRegiment].Alive > 0:
                var t = _battle.Regiments[a.TargetRegiment];
                Line(RegGeom.Mid(r), RegGeom.Mid(t), new Color(1f, 0.3f, 0.3f, 0.8f), _lineW);
                Footprint(t.Centre, t.Facing, t.Width, RegGeom.Depth(t), new Color(1f, 0.3f, 0.3f, 0.9f), new Color(1f, 0.3f, 0.3f, 0.12f));
                break;
        }
    }

    void Ghost(Regiment r, SV2 front, SV2 facing, float width, Color c)
    {
        float depth = RegGeom.Depth(r.Def.Spacing, width, r.Alive);
        Footprint(front, facing, width, depth, c with { A = 0.9f }, c with { A = 0.2f });
        Line(RegGeom.Mid(r), front - facing * (depth * 0.5f), c with { A = 0.35f }, _lineW * 0.6f);
        // facing arrow
        var right = new SV2(-facing.Y, facing.X);
        var tip = front + facing * 4f; var b0 = front + facing * 1.5f - right * 1.6f; var b1 = front + facing * 1.5f + right * 1.6f;
        Tri(tip, b0, b1, c with { A = 0.9f });
    }

    void Footprint(SV2 front, SV2 facing, float width, float depth, Color outline, Color fill)
    {
        var right = new SV2(-facing.Y, facing.X);
        var fl = front - right * (width * 0.5f); var fr = front + right * (width * 0.5f);
        var bl = fl - facing * depth; var br = fr - facing * depth;
        Tri(fl, fr, br, fill); Tri(fl, br, bl, fill);
        Line(fl, fr, outline, _lineW * 1.4f); Line(fr, br, outline, _lineW); Line(br, bl, outline, _lineW); Line(bl, fl, outline, _lineW);
    }

    const float Y = 0.14f;
    void Tri(SV2 a, SV2 b, SV2 c, Color col) => _tris.Add((RegGeom.ToWorld(a, Y), RegGeom.ToWorld(b, Y), RegGeom.ToWorld(c, Y), col));

    void Line(SV2 a, SV2 b, Color col, float w)
    {
        var d = b - a; if (d.LengthSquared() < 1e-6f) return;
        d = SV2.Normalize(d); var n = new SV2(-d.Y, d.X) * (w * 0.5f);
        Tri(a - n, a + n, b + n, col); Tri(a - n, b + n, b - n, col);
    }

    void Flush()
    {
        _mesh.ClearSurfaces();
        if (_tris.Count == 0) return;
        _mesh.SurfaceBegin(Mesh.PrimitiveType.Triangles);
        foreach (var (a, b, c, col) in _tris)
        {
            _mesh.SurfaceSetColor(col); _mesh.SurfaceAddVertex(a);
            _mesh.SurfaceSetColor(col); _mesh.SurfaceAddVertex(b);
            _mesh.SurfaceSetColor(col); _mesh.SurfaceAddVertex(c);
        }
        _mesh.SurfaceEnd();
    }
}
