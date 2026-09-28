using Godot;

namespace FactorySim.Client;

/// <summary>Orthographic isometric camera: WASD/arrows or middle-drag to pan, wheel to zoom, Z/C to turn.</summary>
public partial class CameraRig : Node3D
{
    private const float Pitch = -35.264f; // true isometric
    private const float Distance = 80f;

    private float _yaw = 45f;
    private float _targetYaw = 45f;
    private float _zoom = 12f;
    private bool _dragging;

    public Camera3D Camera { get; private set; } = null!;

    public override void _Ready()
    {
        Camera = new Camera3D
        {
            Projection = Camera3D.ProjectionType.Orthogonal,
            Size = _zoom,
            Near = 0.1f,
            Far = 400f,
            Position = new Vector3(0, 0, Distance),
        };
        AddChild(Camera);
        Apply();
    }

    public void Focus(Vector3 target) => Position = new Vector3(target.X, 0, target.Z);

    public override void _Process(double delta)
    {
        var move = Vector2.Zero;
        if (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up)) move.Y -= 1;
        if (Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down)) move.Y += 1;
        if (Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left)) move.X -= 1;
        if (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right)) move.X += 1;
        if (move != Vector2.Zero) Pan(move.Normalized() * _zoom * 0.9f * (float)delta);

        _yaw = Mathf.Lerp(_yaw, _targetYaw, 1 - Mathf.Exp(-12f * (float)delta));
        Apply();
    }

    public override void _UnhandledInput(InputEvent ev)
    {
        switch (ev)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp, Pressed: true }:
                _zoom = Mathf.Max(4f, _zoom * 0.9f);
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelDown, Pressed: true }:
                _zoom = Mathf.Min(80f, _zoom * 1.1f);
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Middle } mb:
                _dragging = mb.Pressed;
                break;
            case InputEventMouseMotion mm when _dragging:
                float pixelsToWorld = _zoom / GetViewport().GetVisibleRect().Size.Y;
                Pan(-mm.Relative * pixelsToWorld * new Vector2(1, 1.6f));
                break;
            case InputEventKey { Pressed: true, Echo: false, Keycode: Key.Z }:
                _targetYaw -= 90f;
                break;
            case InputEventKey { Pressed: true, Echo: false, Keycode: Key.C }:
                _targetYaw += 90f;
                break;
        }
    }

    /// <summary>Screen-relative pan on the ground plane.</summary>
    private void Pan(Vector2 screen)
    {
        float yaw = Mathf.DegToRad(_yaw);
        var right = Vector3.Right.Rotated(Vector3.Up, yaw);
        var forward = Vector3.Forward.Rotated(Vector3.Up, yaw);
        Position += right * screen.X - forward * screen.Y;
    }

    private void Apply()
    {
        RotationDegrees = new Vector3(Pitch, _yaw, 0);
        Camera.Size = _zoom;
    }
}
