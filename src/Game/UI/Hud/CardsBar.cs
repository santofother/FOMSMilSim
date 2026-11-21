// CardsBar: one card per player regiment along the bottom (name, alive/count, activity, morale and fatigue bars).
// Click selects, shift-click adds. Owner: S03
// Public API: CardsBar(BattleScene, BattleInput)
using System.Collections.Generic;
using Godot;
using FOMS.Game.PlayerInput;
using FOMS.Sim;
using SimBattle = FOMS.Sim.Battle;

namespace FOMS.Game.UI.Hud;

public partial class CardsBar : CanvasLayer
{
    sealed class Card
    {
        public int Id; public PanelContainer Panel; public StyleBoxFlat Style; public Label Name, Info; public ProgressBar Morale, Fatigue;
    }

    readonly Battle.BattleScene _scene;
    readonly BattleInput _input;
    readonly List<Card> _cards = new();
    HBoxContainer _row;
    SimBattle _battle;

    public CardsBar(Battle.BattleScene scene, BattleInput input) { _scene = scene; _input = input; Name = "CardsBar"; Layer = 4; }

    public override void _Ready()
    {
        _row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Alignment = BoxContainer.AlignmentMode.Center };
        _row.AddThemeConstantOverride("separation", 6);
        _row.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
        _row.OffsetTop = -96; _row.OffsetBottom = -8;
        AddChild(_row);
    }

    void Rebuild()
    {
        foreach (var c in _cards) c.Panel.QueueFree();
        _cards.Clear();
        var counts = new Dictionary<string, int>();
        foreach (var r in _battle.Regiments)
        {
            if (r.Team != BattleInput.PlayerTeam) continue;
            counts[r.UnitId] = counts.GetValueOrDefault(r.UnitId) + 1;
            string name = $"{(string.IsNullOrEmpty(r.Def.Name) ? r.UnitId : r.Def.Name)} {counts[r.UnitId]}";
            _cards.Add(MakeCard(r.Id, name));
        }
    }

    Card MakeCard(int id, string name)
    {
        var card = new Card { Id = id };
        card.Style = new StyleBoxFlat { BgColor = new Color(0.08f, 0.09f, 0.12f, 0.85f), BorderColor = new Color(0.4f, 0.45f, 0.55f), CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4, CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4, ContentMarginLeft = 8, ContentMarginRight = 8, ContentMarginTop = 5, ContentMarginBottom = 6 };
        card.Style.SetBorderWidthAll(1);
        card.Panel = new PanelContainer { CustomMinimumSize = new Vector2(128, 0), MouseFilter = Control.MouseFilterEnum.Stop };
        card.Panel.AddThemeStyleboxOverride("panel", card.Style);
        var box = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        box.AddThemeConstantOverride("separation", 2);
        card.Name = Lbl(name, 14); card.Info = Lbl("", 12);
        card.Morale = Bar(new Color(0.3f, 0.6f, 1f)); card.Fatigue = Bar(new Color(1f, 0.65f, 0.2f));
        foreach (var n in new Control[] { card.Name, card.Info, card.Morale, card.Fatigue }) box.AddChild(n);
        card.Panel.AddChild(box);
        card.Panel.GuiInput += e =>
        {
            if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mb)
            {
                if (mb.ShiftPressed) _input.Toggle(id); else _input.SelectOnly(id);
                card.Panel.AcceptEvent();
            }
        };
        _row.AddChild(card.Panel);
        return card;
    }

    static Label Lbl(string t, int size)
    {
        var l = new Label { Text = t, MouseFilter = Control.MouseFilterEnum.Ignore };
        l.AddThemeFontSizeOverride("font_size", size);
        return l;
    }

    static ProgressBar Bar(Color fill)
    {
        var b = new ProgressBar { MinValue = 0, MaxValue = 100, ShowPercentage = false, CustomMinimumSize = new Vector2(0, 6), MouseFilter = Control.MouseFilterEnum.Ignore };
        b.AddThemeStyleboxOverride("fill", new StyleBoxFlat { BgColor = fill });
        b.AddThemeStyleboxOverride("background", new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0.5f) });
        return b;
    }

    public override void _Process(double delta)
    {
        var battle = _scene.Battle;
        if (battle == null) return;
        if (!ReferenceEquals(battle, _battle)) { _battle = battle; Rebuild(); }
        foreach (var c in _cards)
        {
            var r = _battle.Regiments[c.Id];
            c.Info.Text = $"{r.Alive}/{r.Count}  {r.Activity}";
            c.Morale.Value = r.Morale;
            c.Fatigue.Value = r.Fatigue * 100f;
            bool sel = _input.Selected.Contains(c.Id);
            c.Style.BorderColor = sel ? new Color(1f, 0.95f, 0.5f) : new Color(0.4f, 0.45f, 0.55f);
            c.Style.SetBorderWidthAll(sel ? 2 : 1);
            c.Panel.Modulate = r.Alive > 0 ? Colors.White : new Color(1, 1, 1, 0.35f);
        }
    }
}
