// BattleInput: selection, orders, hotkeys, pause and speed. Issues orders only via Battle.Issue (C1).
// Owner: S03
// Keys: LMB select / shift add / drag box / double-click = same type on screen; Ctrl+1-9 save group, 1-9 recall;
//       RMB ground = Move, RMB-drag = draw the line, RMB enemy = Attack, double RMB = run;
//       H halt, Backspace withdraw, Esc clear, Space pause, +/- speed.
// Public API: BattleInput (Selected, HoverId, Preview, PreviewActive, Select*, PlayerTeam)
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;
using FOMS.Sim;
using SimBattle = FOMS.Sim.Battle;
using SV2 = System.Numerics.Vector2;

namespace FOMS.Game.PlayerInput;

/// <summary>One regiment's planned spot, used for the order and for the ghost markers.</summary>
public readonly record struct Placement(int Id, SV2 Front, SV2 Facing, float Width);

public partial class BattleInput : Node
{
    public const int PlayerTeam = 0;
    const float DragPixels = 8f, LineDragPixels = 14f, DoubleClickSec = 0.35f, Gap = 1.5f;
    static readonly float[] Speeds = { 0.5f, 1f, 1.5f, 2f, 2.5f, 3f };

    readonly Battle.BattleScene _scene;
    readonly BoxOverlay _box = new();
    readonly Dictionary<int, int[]> _groups = new();
    readonly List<Placement> _preview = new();
    SimBattle _lastBattle;

    public readonly HashSet<int> Selected = new();
    public int HoverId { get; private set; } = -1;
    public IReadOnlyList<Placement> Preview => _preview;
    public bool PreviewActive { get; private set; }

    bool _lDown, _lBox, _rDown, _rDrag;
    Vector2 _lStart, _rStart, _mouse;
    SV2 _rStartGround, _rEndGround;
    double _lastRightClick = -10;
    Vector2 _lastRightPos;

    public BattleInput(Battle.BattleScene scene) { _scene = scene; Name = "BattleInput"; }

    public override void _Ready()
    {
        var layer = new CanvasLayer { Layer = 5 };
        AddChild(layer);
        layer.AddChild(_box);
    }

    SimBattle Sim => _scene.Battle;
    Camera3D Cam => GetViewport().GetCamera3D();

    // ---------------- selection API (also used by the cards bar) ----------------
    public void SelectOnly(int id) { Selected.Clear(); Selected.Add(id); }
    public void Toggle(int id) { if (!Selected.Remove(id)) Selected.Add(id); }
    public void Clear() => Selected.Clear();

    // ---------------- ground picking ----------------
    SV2? RayGround(Vector2 screen)
    {
        var cam = Cam; if (cam == null) return null;
        var o = cam.ProjectRayOrigin(screen); var d = cam.ProjectRayNormal(screen);
        if (MathF.Abs(d.Y) < 1e-4f) return null;
        float t = -o.Y / d.Y;
        if (t < 0) return null;
        var p = o + d * t;
        return new SV2(p.X, p.Z);
    }

    public Vector3 BannerPos(Regiment r) => RegGeom.ToWorld(RegGeom.Mid(r), 6.5f);

    bool OnScreen(Vector3 world, out Vector2 screen)
    {
        screen = default; var cam = Cam;
        if (cam == null || cam.IsPositionBehind(world)) return false;
        screen = cam.UnprojectPosition(world);
        return GetViewport().GetVisibleRect().HasPoint(screen);
    }

    int Pick(Vector2 screen)
    {
        var sim = Sim; int best = -1; float bestD = float.MaxValue;
        var g = RayGround(screen);
        if (g != null)
            foreach (var r in sim.Regiments)
            {
                if (r.Alive <= 0 || !RegGeom.Contains(r, g.Value)) continue;
                float d = SV2.Distance(g.Value, RegGeom.Mid(r));
                if (d < bestD) { bestD = d; best = r.Id; }
            }
        if (best >= 0) return best;
        bestD = 24f; // banner click radius in pixels
        foreach (var r in sim.Regiments)
        {
            if (r.Alive <= 0 || !OnScreen(BannerPos(r), out var sp)) continue;
            float d = sp.DistanceTo(screen);
            if (d < bestD) { bestD = d; best = r.Id; }
        }
        return best;
    }

    // ---------------- frame ----------------
    public override void _Process(double delta)
    {
        if (!ReferenceEquals(Sim, _lastBattle)) { _lastBattle = Sim; Selected.Clear(); _groups.Clear(); HoverId = -1; PreviewActive = false; }
        if (Sim == null) return;
        Selected.RemoveWhere(id => Sim.Regiments[id].Alive <= 0);
        HoverId = Pick(_mouse);
        bool enemyHover = HoverId >= 0 && Sim.Regiments[HoverId].Team != PlayerTeam && Selected.Count > 0;
        Input.SetDefaultCursorShape(enemyHover ? Input.CursorShape.Cross : Input.CursorShape.Arrow);
        _box.Set(_lBox, new Rect2(_lStart, _mouse - _lStart).Abs());
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (Sim == null) return;
        switch (e)
        {
            case InputEventMouseMotion mm: OnMotion(mm); break;
            case InputEventMouseButton mb when mb.ButtonIndex == MouseButton.Left: OnLeft(mb); break;
            case InputEventMouseButton mb when mb.ButtonIndex == MouseButton.Right: OnRight(mb); break;
            case InputEventKey { Pressed: true, Echo: false } k: OnKey(k); break;
        }
    }

    void OnMotion(InputEventMouseMotion mm)
    {
        _mouse = mm.Position;
        if (_lDown && !_lBox && _mouse.DistanceTo(_lStart) > DragPixels) _lBox = true;
        if (_rDown)
        {
            if (!_rDrag && _mouse.DistanceTo(_rStart) > LineDragPixels) _rDrag = true;
            if (_rDrag && RayGround(_mouse) is { } g) { _rEndGround = g; RebuildPreview(); }
        }
    }

    // ---------------- left button: selection ----------------
    void OnLeft(InputEventMouseButton mb)
    {
        _mouse = mb.Position;
        if (mb.Pressed) { _lDown = true; _lBox = false; _lStart = mb.Position; return; }
        if (!_lDown) return;
        bool shift = mb.ShiftPressed;
        if (_lBox) BoxSelect(new Rect2(_lStart, mb.Position - _lStart).Abs(), shift);
        else
        {
            int id = Pick(mb.Position);
            bool mine = id >= 0 && Sim.Regiments[id].Team == PlayerTeam;
            if (mb.DoubleClick && mine) SelectSameType(id);
            else if (mine) { if (shift) Toggle(id); else SelectOnly(id); }
            else if (!shift) Clear();
        }
        _lDown = false; _lBox = false;
    }

    void BoxSelect(Rect2 box, bool add)
    {
        if (!add) Selected.Clear();
        foreach (var r in Sim.Regiments)
            if (r.Team == PlayerTeam && r.Alive > 0 && OnScreen(RegGeom.ToWorld(RegGeom.Mid(r)), out var sp) && box.HasPoint(sp))
                Selected.Add(r.Id);
    }

    void SelectSameType(int id)
    {
        string unit = Sim.Regiments[id].UnitId;
        Selected.Clear();
        foreach (var r in Sim.Regiments)
            if (r.Team == PlayerTeam && r.Alive > 0 && r.UnitId == unit && OnScreen(RegGeom.ToWorld(RegGeom.Mid(r)), out _))
                Selected.Add(r.Id);
        Selected.Add(id);
    }

    // ---------------- right button: orders ----------------
    void OnRight(InputEventMouseButton mb)
    {
        _mouse = mb.Position;
        if (mb.Pressed)
        {
            if (RayGround(mb.Position) is not { } g) return;
            _rDown = true; _rDrag = false; _rStart = mb.Position; _rStartGround = g; _rEndGround = g;
            return;
        }
        if (!_rDown) return;
        _rDown = false; PreviewActive = false;
        var ids = SelectedIds();
        if (ids.Count == 0) { _rDrag = false; return; }

        if (_rDrag) { _rDrag = false; MoveSelected(ids, _rStartGround, _rEndGround, false); return; }

        double now = Time.GetTicksMsec() / 1000.0;
        bool dbl = now - _lastRightClick < DoubleClickSec && mb.Position.DistanceTo(_lastRightPos) < 20f;
        _lastRightClick = dbl ? -10 : now; _lastRightPos = mb.Position;

        int target = HoverId;
        if (target >= 0 && Sim.Regiments[target].Team != PlayerTeam)
            foreach (var id in ids) Send(new Attack(id, target, dbl));
        else
            MoveSelected(ids, _rStartGround, _rStartGround, dbl);
    }

    List<int> SelectedIds() => Selected.Where(id => Sim.Regiments[id].Alive > 0).OrderBy(i => i).ToList();

    void MoveSelected(List<int> ids, SV2 a, SV2 b, bool run)
    {
        foreach (var p in Layout(ids, a, b))
            Send(new Move(p.Id, p.Front, p.Facing, p.Width, run));
    }

    void RebuildPreview()
    {
        _preview.Clear();
        var ids = SelectedIds();
        if (ids.Count == 0) { PreviewActive = false; return; }
        _preview.AddRange(Layout(ids, _rStartGround, _rEndGround));
        PreviewActive = true;
    }

    /// <summary>
    /// Where each regiment goes. A click (a ≈ b) keeps every regiment's width and packs them side by side around the
    /// click, facing away from the group's current position. A drag spreads them along the dragged line, facing
    /// perpendicular to it (the side away from the group).
    /// </summary>
    List<Placement> Layout(List<int> ids, SV2 a, SV2 b)
    {
        var regs = ids.Select(i => Sim.Regiments[i]).ToList();
        var centroid = regs.Aggregate(SV2.Zero, (s, r) => s + RegGeom.Mid(r)) / regs.Count;
        var result = new List<Placement>();
        float len = SV2.Distance(a, b);

        if (len < 1.5f)
        {
            var dir = a - centroid;
            dir = dir.LengthSquared() > 1e-4f ? SV2.Normalize(dir) : regs[0].Facing;
            var right = new SV2(-dir.Y, dir.X);
            var ordered = regs.OrderBy(r => SV2.Dot(RegGeom.Mid(r) - centroid, right)).ToList();
            float total = ordered.Sum(r => r.Width) + Gap * (ordered.Count - 1);
            float x = -total * 0.5f;
            foreach (var r in ordered)
            {
                result.Add(new Placement(r.Id, a + right * (x + r.Width * 0.5f), dir, r.Width));
                x += r.Width + Gap;
            }
            return result;
        }

        var d = (b - a) / len;
        var f = new SV2(-d.Y, d.X);
        if (SV2.Dot(f, (a + b) * 0.5f - centroid) < 0) f = -f;
        var byPos = regs.OrderBy(r => SV2.Dot(RegGeom.Mid(r) - a, d)).ToList();
        float seg = len / byPos.Count;
        for (int i = 0; i < byPos.Count; i++)
        {
            var r = byPos[i];
            float w = Math.Clamp(seg - (byPos.Count > 1 ? Gap : 0f), r.Def.Spacing * 2f, r.Count * r.Def.Spacing);
            result.Add(new Placement(r.Id, a + d * (seg * (i + 0.5f)), f, w));
        }
        return result;
    }

    void Send(Order o)
    {
        string t = o switch
        {
            Move m => $"move r{m.RegimentId} -> ({m.FrontCentre.X:0.0},{m.FrontCentre.Y:0.0}) facing ({m.Facing.X:0.00},{m.Facing.Y:0.00}) width {m.Width:0.0}{(m.Run ? " run" : "")}",
            Attack at => $"attack r{at.RegimentId} -> r{at.TargetRegiment}{(at.Run ? " run" : "")}",
            Halt => $"halt r{o.RegimentId}",
            Withdraw => $"withdraw r{o.RegimentId}",
            _ => $"{o.GetType().Name} r{o.RegimentId}",
        };
        Sim.Log($"[Orders] {t}");
        Sim.Issue(o);
    }

    // ---------------- keys ----------------
    void OnKey(InputEventKey k)
    {
        int digit = k.Keycode >= Key.Key1 && k.Keycode <= Key.Key9 ? (int)(k.Keycode - Key.Key0) : 0;
        if (digit > 0)
        {
            if (k.CtrlPressed || k.MetaPressed) { _groups[digit] = Selected.ToArray(); Sim.Log($"[Input] group {digit} saved ({Selected.Count})"); }
            else if (_groups.TryGetValue(digit, out var g))
            {
                Selected.Clear();
                foreach (var id in g) if (Sim.Regiments[id].Alive > 0) Selected.Add(id);
            }
            return;
        }
        switch (k.Keycode)
        {
            case Key.Escape: Clear(); break;
            case Key.H: foreach (var id in SelectedIds()) Send(new Halt(id)); break;
            case Key.Backspace: foreach (var id in SelectedIds()) Send(new Withdraw(id)); break;
            case Key.Space: _scene.Paused = !_scene.Paused; break;
            case Key.Equal or Key.Plus or Key.KpAdd: ChangeSpeed(+1); break;
            case Key.Minus or Key.KpSubtract: ChangeSpeed(-1); break;
        }
    }

    void ChangeSpeed(int dir)
    {
        int i = Array.FindIndex(Speeds, s => MathF.Abs(s - _scene.Speed) < 0.01f);
        if (i < 0) i = 1;
        _scene.Speed = Speeds[Math.Clamp(i + dir, 0, Speeds.Length - 1)];
    }
}
