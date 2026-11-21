// RTS camera (copied from the M0 PerfTest camera). Owner: S01
// Public API: RtsCamera (Yaw, Pitch, Distance, InputEnabled)
using Godot;

namespace FOMS.Game.Camera;

// Simple Total War-style camera: WASD/arrows pan, Q/E rotate, wheel zoom,
// right- or middle-drag to rotate and tilt.
public partial class RtsCamera : Node3D
{
    public float Yaw = Mathf.DegToRad(35);
    public float Pitch = Mathf.DegToRad(-38);
    public float Distance = 170f;
    public bool InputEnabled = true;

    Camera3D _cam;
    bool _dragging;

    public override void _Ready()
    {
        _cam = new Camera3D { Far = 2000f, Fov = 55f };
        AddChild(_cam);
        Apply();
    }

    public override void _Process(double delta)
    {
        if (!InputEnabled) return;
        float dt = (float)delta;
        var move = Vector2.Zero;
        if (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up)) move.Y -= 1;
        if (Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down)) move.Y += 1;
        if (Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left)) move.X -= 1;
        if (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right)) move.X += 1;
        if (Input.IsKeyPressed(Key.Q)) Yaw += 1.5f * dt;
        if (Input.IsKeyPressed(Key.E)) Yaw -= 1.5f * dt;
        if (move != Vector2.Zero)
        {
            float speed = Distance * 0.9f * (Input.IsKeyPressed(Key.Shift) ? 2.5f : 1f);
            var basis = new Basis(Vector3.Up, Yaw);
            var dir = basis * new Vector3(move.X, 0, move.Y);
            Position += dir.Normalized() * speed * dt;
        }
        Apply();
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (!InputEnabled) return;
        if (e is InputEventMouseButton mb)
        {
            if (mb.ButtonIndex == MouseButton.WheelUp && mb.Pressed) Distance = Mathf.Max(8f, Distance * 0.9f);
            if (mb.ButtonIndex == MouseButton.WheelDown && mb.Pressed) Distance = Mathf.Min(600f, Distance * 1.1f);
            if (mb.ButtonIndex is MouseButton.Right or MouseButton.Middle) _dragging = mb.Pressed;
        }
        else if (e is InputEventMouseMotion mm && _dragging)
        {
            Yaw -= mm.Relative.X * 0.005f;
            Pitch = Mathf.Clamp(Pitch - mm.Relative.Y * 0.004f, Mathf.DegToRad(-85), Mathf.DegToRad(-8));
        }
    }

    void Apply()
    {
        if (_cam == null) return;
        Rotation = new Vector3(0, Yaw, 0);
        _cam.Rotation = new Vector3(Pitch, 0, 0);
        _cam.Position = new Vector3(0, 0, 0) - _cam.Basis.Z * -Distance;
    }
}
