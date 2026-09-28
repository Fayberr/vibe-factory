using Godot;

namespace FactorySim.Client;

/// <summary>
/// Orbit camera around a ground focus point.
/// WASD/arrows pan · wheel zooms toward the cursor · RMB drag orbits · MMB drag pans · Home recentres.
/// </summary>
public partial class CameraRig : Node3D
{
    private float _yaw = 40f, _pitch = -52f, _distance = 20f;
    private float _yawTarget = 40f, _pitchTarget = -52f, _distanceTarget = 20f;
    private Vector3 _focus, _focusTarget;
    private bool _orbiting, _panning;
    private Vector2 _mouse;

    public Camera3D Camera { get; private set; } = null!;

    /// <summary>Screen distance the right button travelled since it was pressed (tools use it to tell clicks from orbits).</summary>
    public float RightDragDistance { get; private set; }

    public override void _Ready()
    {
        Camera = new Camera3D { Fov = 36, Near = 0.2f, Far = 600f };
        AddChild(Camera);
        Apply();
    }

    public void Focus(Vector3 target, bool instant = false)
    {
        _focusTarget = target with { Y = 0 };
        if (instant) _focus = _focusTarget;
    }

    public Vector3 FocusPoint => _focusTarget;

    /// <summary>Jumps to an exact view (used by scripted screenshots).</summary>
    public void SetView(float yaw, float pitch, float distance, Vector3 focus)
    {
        _yaw = _yawTarget = yaw;
        _pitch = _pitchTarget = pitch;
        _distance = _distanceTarget = distance;
        _focus = _focusTarget = focus with { Y = 0 };
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        if (!Input.IsKeyPressed(Key.Ctrl))
        {
            var move = Vector2.Zero;
            if (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up)) move.Y -= 1;
            if (Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down)) move.Y += 1;
            if (Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left)) move.X -= 1;
            if (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right)) move.X += 1;
            float boost = Input.IsKeyPressed(Key.Shift) ? 2.2f : 1f;
            if (move != Vector2.Zero) Pan(move.Normalized() * _distanceTarget * 0.9f * boost * dt);
        }

        float k = 1 - Mathf.Exp(-14f * dt);
        _yaw = Mathf.Lerp(_yaw, _yawTarget, k);
        _pitch = Mathf.Lerp(_pitch, _pitchTarget, k);
        _distance = Mathf.Lerp(_distance, _distanceTarget, k);
        _focus = _focus.Lerp(_focusTarget, k);
        Apply();
    }

    public override void _Input(InputEvent ev)
    {
        // Track releases globally so a drag that ends over the UI still ends.
        if (ev is InputEventMouseButton { Pressed: false } mb)
        {
            if (mb.ButtonIndex == MouseButton.Right) _orbiting = false;
            if (mb.ButtonIndex == MouseButton.Middle) _panning = false;
        }
        if (ev is InputEventMouseMotion motion) _mouse = motion.Position;
    }

    public override void _UnhandledInput(InputEvent ev)
    {
        switch (ev)
        {
            case InputEventMouseButton { Pressed: true } mb when mb.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown:
                if (mb.ShiftPressed || mb.CtrlPressed) return; // reserved for tools
                ZoomAt(mb.Position, mb.ButtonIndex == MouseButton.WheelUp ? 0.87f : 1.15f);
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true }:
                _orbiting = true;
                RightDragDistance = 0;
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Middle, Pressed: true }:
                _panning = true;
                break;
            case InputEventMouseMotion mm when _orbiting:
                RightDragDistance += mm.Relative.Length();
                if (RightDragDistance < 6) break;
                _yawTarget -= mm.Relative.X * 0.3f;
                _pitchTarget = Mathf.Clamp(_pitchTarget - mm.Relative.Y * 0.22f, -84f, -22f);
                break;
            case InputEventMouseMotion mm when _panning:
                float scale = _distance * 0.0018f;
                Pan(new Vector2(-mm.Relative.X, -mm.Relative.Y * 1.3f) * scale);
                break;
            case InputEventKey { Pressed: true, Echo: false, Keycode: Key.Home }:
                _yawTarget = 40;
                _pitchTarget = -52;
                break;
        }
    }

    /// <summary>Zooms so the ground point under the cursor stays under the cursor.</summary>
    private void ZoomAt(Vector2 screen, float factor)
    {
        var before = GroundUnder(screen);
        float old = _distanceTarget;
        _distanceTarget = Mathf.Clamp(_distanceTarget * factor, 5f, 80f);
        if (before is { } p) _focusTarget += (p - _focusTarget) with { Y = 0 } * (1 - _distanceTarget / old);
    }

    public Vector3? GroundUnder(Vector2 screen, float height = 0)
    {
        var plane = new Plane(Vector3.Up, height);
        return plane.IntersectsRay(Camera.ProjectRayOrigin(screen), Camera.ProjectRayNormal(screen));
    }

    private void Pan(Vector2 screen)
    {
        float yaw = Mathf.DegToRad(_yaw);
        var right = Vector3.Right.Rotated(Vector3.Up, yaw);
        var forward = Vector3.Forward.Rotated(Vector3.Up, yaw);
        _focusTarget += right * screen.X - forward * screen.Y;
    }

    private void Apply()
    {
        Position = _focus;
        RotationDegrees = new Vector3(_pitch, _yaw, 0);
        Camera.Position = new Vector3(0, 0, _distance);
    }
}
