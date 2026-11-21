// BoxOverlay: draws the drag-select rectangle on screen. Owner: S03
using Godot;

namespace FOMS.Game.PlayerInput;

public partial class BoxOverlay : Control
{
    public bool Active;
    public Rect2 Box;

    public BoxOverlay()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);
    }

    public void Set(bool active, Rect2 box)
    {
        if (Active == active && Box == box) return;
        Active = active; Box = box; QueueRedraw();
    }

    public override void _Draw()
    {
        if (!Active) return;
        DrawRect(Box, new Color(0.4f, 0.8f, 1f, 0.12f));
        DrawRect(Box, new Color(0.6f, 0.9f, 1f, 0.9f), false, 1.5f);
    }
}
