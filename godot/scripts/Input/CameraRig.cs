using Godot;

namespace FactorySim.Client;

/// <summary>
/// Orbit camera around a ground focus point.
/// WASD/arrows pan · wheel zooms toward the cursor · RMB drag orbits · MMB drag pans · Home recentres.
/// </summary>
public partial class CameraRig : Node3D
{
    /// <summary>The yaw the camera starts at and returns to.</summary>
    public const float DefaultYaw = 40f;

    private float _yaw = DefaultYaw, _pitch = -52f, _distance = 20f;
    private float _yawTarget = DefaultYaw, _pitchTarget = -52f, _distanceTarget = 20f;
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

    /// <summary>Zoom distance the camera is heading to.</summary>
    public float Distance => _distanceTarget;

    /// <summary>Slowly circles the focus point (title screen backdrop).</summary>
    public bool AutoOrbit { get; set; }

    /// <summary>Keyboard (and edge) panning speed multiplier (Settings → Controls).</summary>
    public float PanSpeed { get; set; } = 1;

    /// <summary>Wheel up zooms out instead of in.</summary>
    public bool InvertZoom { get; set; }

    /// <summary>Pan when the mouse touches the window's edge.</summary>
    public bool EdgePan { get; set; }

    /// <summary>Which way the mouse at the window's edge pushes the view (zero away from the edges or outside the window).</summary>
    private Vector2 EdgeDirection()
    {
        if (!DisplayServer.WindowIsFocused()) return Vector2.Zero;
        var mouse = DisplayServer.MouseGetPosition() - DisplayServer.WindowGetPosition();
        var size = DisplayServer.WindowGetSize();
        if (mouse.X < 0 || mouse.Y < 0 || mouse.X >= size.X || mouse.Y >= size.Y) return Vector2.Zero;
        const int band = 6;
        return new Vector2(mouse.X < band ? -1 : mouse.X >= size.X - band ? 1 : 0, mouse.Y < band ? -1 : mouse.Y >= size.Y - band ? 1 : 0);
    }

    /// <summary>Player control of the camera (off behind menus).</summary>
    public bool Interactive { get; set; } = true;

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
        if (AutoOrbit) _yawTarget += (float)delta * 2.5f;
        float dt = (float)delta;
        if (Interactive && !Input.IsKeyPressed(Key.Ctrl))
        {
            var move = Vector2.Zero;
            if (Keybinds.Held("pan_forward") || Input.IsKeyPressed(Key.Up)) move.Y -= 1;
            if (Keybinds.Held("pan_back") || Input.IsKeyPressed(Key.Down)) move.Y += 1;
            if (Keybinds.Held("pan_left") || Input.IsKeyPressed(Key.Left)) move.X -= 1;
            if (Keybinds.Held("pan_right") || Input.IsKeyPressed(Key.Right)) move.X += 1;
            if (EdgePan) move += EdgeDirection();
            float boost = Input.IsKeyPressed(Key.Shift) ? 2.2f : 1f;
            if (move != Vector2.Zero) Pan(move.Normalized() * _distanceTarget * 0.9f * PanSpeed * boost * dt);
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
        if (!Interactive) return;
        switch (ev)
        {
            case InputEventMouseButton { Pressed: true } mb when mb.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown:
                if (mb.ShiftPressed || mb.CtrlPressed) return; // reserved for tools
                // A list scrolled to its end lets the wheel through; never zoom under the UI.
                if (GetViewport().GuiGetHoveredControl() != null) return;
                ZoomAt(mb.Position, (mb.ButtonIndex == MouseButton.WheelUp) != InvertZoom ? 0.87f : 1.15f);
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
            case InputEventKey { Pressed: true, Echo: false, CtrlPressed: false } key when Keybinds.Is(key, "reset_view"):
                _yawTarget = DefaultYaw;
                _pitchTarget = -52;
                break;
        }
    }

    /// <summary>Zooms so the ground point under the cursor stays under the cursor.</summary>
    private void ZoomAt(Vector2 screen, float factor)
    {
        var before = GroundUnder(screen);
        float old = _distanceTarget;
        _distanceTarget = Mathf.Clamp(_distanceTarget * factor, 5f, 120f);
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
