using System;
using Godot;
using FactorySim.Content;

namespace FactorySim.Client;

/// <summary>
/// Mouse/keyboard building: picks a cell on the current layer, shows a ghost, and turns
/// clicks into simulation Commands. Dragging a belt tool lays and auto-orients a line.
/// </summary>
public partial class BuildTool : Node3D
{
    private SimHost _host = null!;
    private CameraRig _camera = null!;
    private WorldView _view = null!;

    private Node3D _ghost = null!;
    private StandardMaterial3D _ghostMaterial = null!;
    private GridPos? _hover;
    private GridPos? _dragLast;
    private bool _placing, _removing;

    public BuildingDef? Selected { get; private set; }
    public Dir Facing { get; private set; } = Dir.East;
    public int Layer { get; private set; }
    public bool Cutaway { get; private set; }
    public GridPos? Hover => _hover;

    /// <summary>Raised when tool, facing or layer changes (HUD refresh).</summary>
    public event Action? StateChanged;

    public void Init(SimHost host, CameraRig camera, WorldView view)
    {
        _host = host;
        _camera = camera;
        _view = view;
        host.WorldReplaced += () => SetLayer(0);
    }

    public override void _Ready()
    {
        _ghostMaterial = new StandardMaterial3D
        {
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = new Color(0.3f, 1f, 0.5f, 0.35f),
        };
        _ghost = new Node3D { Name = "Ghost", Visible = false };
        AddChild(_ghost);
    }

    public void Select(BuildingDef? def)
    {
        Selected = def;
        RebuildGhost();
        StateChanged?.Invoke();
    }

    public void SetLayer(int layer)
    {
        if (_host.Sim == null) return;
        var b = _host.Sim.World.Bounds;
        Layer = Math.Clamp(layer, b.Min.Z, b.Max.Z);
        _view.SetLayer(Layer, Cutaway);
        StateChanged?.Invoke();
    }

    public override void _UnhandledInput(InputEvent ev)
    {
        switch (ev)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } mb:
                _placing = mb.Pressed && Selected != null;
                _dragLast = null;
                if (_placing) PlaceAtHover();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Right } mb:
                _removing = mb.Pressed;
                if (_removing) RemoveAtHover();
                break;
            case InputEventKey { Pressed: true, Echo: false } key:
                OnKey(key);
                break;
        }
    }

    private void OnKey(InputEventKey key)
    {
        switch (key.Keycode)
        {
            case Key.R when Selected == null && _hover is { } cell:
                _host.Execute(new RotateBuilding(cell));
                break;
            case Key.R:
                Facing = Facing.RotateCW(key.ShiftPressed ? -1 : 1);
                StateChanged?.Invoke();
                break;
            case Key.Q:
                SetLayer(Layer - 1);
                break;
            case Key.E:
                SetLayer(Layer + 1);
                break;
            case Key.Tab:
                Cutaway = !Cutaway;
                SetLayer(Layer);
                break;
            case Key.Escape:
                Select(null);
                break;
            case >= Key.Key0 and <= Key.Key9:
                int index = key.Keycode == Key.Key0 ? 9 : (int)(key.Keycode - Key.Key1);
                var list = _host.Content.BuildingList;
                if (index < list.Count) Select(Selected == list[index] ? null : list[index]);
                break;
        }
    }

    public override void _Process(double delta)
    {
        if (_host.Sim == null) return;
        var newHover = PickCell();
        if (newHover != _hover)
        {
            _hover = newHover;
            if (_placing) PlaceAtHover();
            if (_removing) RemoveAtHover();
        }
        UpdateGhost();
    }

    private GridPos? PickCell()
    {
        var cam = _camera.Camera;
        var mouse = GetViewport().GetMousePosition();
        var plane = new Plane(Vector3.Up, Layer * GridMapping.LayerHeight);
        Vector3? hit = plane.IntersectsRay(cam.ProjectRayOrigin(mouse), cam.ProjectRayNormal(mouse));
        if (hit is not { } p) return null;
        var cell = new GridPos(Mathf.FloorToInt(p.X), Mathf.FloorToInt(p.Z), Layer);
        return _host.Sim.World.Bounds.Contains(cell) ? cell : null;
    }

    private void PlaceAtHover()
    {
        if (Selected == null || _hover is not { } cell) return;

        // Belt painting: orient along the drag and turn the previous tile toward this one.
        bool isBelt = Selected.MetaOr("shape", "") == "belt";
        if (isBelt && _dragLast is { } last && TryDirection(last, cell, out var dir))
        {
            Facing = dir;
            if (_host.Sim.World.EntityAt(last)?.Def == Selected) _host.Execute(new RotateBuilding(last, dir));
            StateChanged?.Invoke();
        }
        _dragLast = cell;

        if (_host.Sim.World.EntityAt(cell) == null) _host.Execute(new PlaceBuilding(Selected.Id, cell, Facing));
    }

    private void RemoveAtHover()
    {
        if (_hover is { } cell && _host.Sim.World.EntityAt(cell) != null) _host.Execute(new RemoveBuilding(cell));
    }

    private static bool TryDirection(GridPos from, GridPos to, out Dir dir)
    {
        var d = to - from;
        dir = (d.X, d.Y) switch
        {
            (1, 0) => Dir.East,
            (-1, 0) => Dir.West,
            (0, 1) => Dir.South,
            (0, -1) => Dir.North,
            _ => Dir.North,
        };
        return Math.Abs(d.X) + Math.Abs(d.Y) == 1 && d.Z == 0;
    }

    // ---- Ghost preview ----------------------------------------------------

    private void RebuildGhost()
    {
        foreach (var child in _ghost.GetChildren()) child.QueueFree();
        if (Selected == null) return;

        foreach (var cell in Selected.Footprint)
        {
            _ghost.AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(0.96f, 0.9f * GridMapping.LayerHeight, 0.96f) },
                MaterialOverride = _ghostMaterial,
                Position = GridMapping.LocalOffset(cell) + new Vector3(0, 0.45f * GridMapping.LayerHeight, 0),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            });
        }
        var arrow = new MeshInstance3D
        {
            Mesh = new PrismMesh { Size = new Vector3(0.5f, 0.5f, 0.05f) },
            MaterialOverride = _ghostMaterial,
            Position = new Vector3(0, 0.95f * GridMapping.LayerHeight, 0),
            Rotation = new Vector3(-Mathf.Pi / 2, 0, 0),
        };
        _ghost.AddChild(arrow);
    }

    private void UpdateGhost()
    {
        if (Selected == null || _hover is not { } cell)
        {
            _ghost.Visible = false;
            return;
        }
        var world = _host.Sim.World;
        bool ok = world.CanPlace(Selected, cell, Facing).Ok && (world.Sandbox || world.Money >= Selected.Cost);
        _ghostMaterial.AlbedoColor = ok ? new Color(0.3f, 1f, 0.5f, 0.35f) : new Color(1f, 0.3f, 0.3f, 0.35f);
        _ghost.Position = GridMapping.CellFloor(cell);
        _ghost.Rotation = new Vector3(0, GridMapping.Yaw(Facing), 0);
        _ghost.Visible = true;
    }
}
