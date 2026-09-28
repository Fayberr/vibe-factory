using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using FactorySim.Content;
using FactorySim.Editing;
using FactorySim.View;

namespace FactorySim.Client;

public enum ToolMode
{
    Select,
    Build,
    Delete,
    Move,
    Paste,
}

/// <summary>
/// Turns mouse and keyboard into simulation commands. One mode at a time:
///  • Select — click, Shift/Ctrl-click, drag a box; R rotates, M moves, Del deletes, Ctrl+C/V/X.
///  • Build  — click to place, drag to lay an L-shaped line (belts orient and curve themselves).
///  • Delete — click or drag a box.
///  • Move / Paste — a blueprint follows the cursor; R rotates it; click to drop.
/// All edits go through the undo history (Ctrl+Z / Ctrl+Y).
/// </summary>
public partial class BuildController : Node3D
{
    private const float DragThreshold = 6f;

    private SimHost _host = null!;
    private CameraRig _camera = null!;
    private WorldView _view = null!;
    private GhostLayer _ghosts = null!;
    private MeshInstance3D _rect = null!;
    private StandardMaterial3D _rectMat = null!;

    private Vector2 _mouse;
    private bool _overWorld;
    private GridPos? _hoverCell;
    private Entity? _hoverEntity;

    private bool _lmbDown, _dragging;
    private Vector2 _lmbPressPos;
    private GridPos? _dragStart;
    private bool? _firstLegX;
    private Vector2 _rmbPressPos;

    private Blueprint? _floating;
    private GridPos _floatingOrigin;
    private int _floatingTurns;
    private List<GridPos> _moveCells = new();

    private readonly Dictionary<int, Highlight> _applied = new();
    private readonly List<GhostSpec> _specs = new();

    public ToolMode Mode { get; private set; } = ToolMode.Select;
    public BuildingDef? Tool { get; private set; }
    public Dir Facing { get; private set; } = Dir.East;
    public int Layer { get; private set; }
    public bool Cutaway { get; private set; }
    public HashSet<int> Selection { get; } = new();
    public Blueprint? Clipboard { get; private set; }
    public Entity? HoverEntity => _hoverEntity;
    public GridPos? HoverCell => _hoverCell;

    /// <summary>Mode, tool, facing, layer or selection changed.</summary>
    public event Action? Changed;

    private World World => _host.Sim.World;
    private EditHistory History => _host.History;

    public void Init(SimHost host, CameraRig camera, WorldView view)
    {
        _host = host;
        _camera = camera;
        _view = view;
        host.WorldReplaced += () =>
        {
            Selection.Clear();
            _applied.Clear();
            SetMode(ToolMode.Select);
            SetLayer(0);
        };
        host.EventRaised += ev =>
        {
            if (ev is EntityRemoved r && Selection.Remove(r.EntityId)) Changed?.Invoke();
        };
    }

    public override void _Ready()
    {
        _ghosts = new GhostLayer { Name = "Ghosts" };
        AddChild(_ghosts);
        _rectMat = Palette.Translucent(new Color(Palette.Select, 0.18f));
        _rectMat.NoDepthTest = true;
        _rect = new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = Vector2.One },
            MaterialOverride = _rectMat,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
        };
        AddChild(_rect);
    }

    // ---- Public actions (also used by the HUD) ------------------------------

    public void SelectTool(BuildingDef? def)
    {
        if (def == null)
        {
            SetMode(ToolMode.Select);
            return;
        }
        Tool = def;
        SetMode(ToolMode.Build);
    }

    public void SetMode(ToolMode mode)
    {
        if (mode != ToolMode.Build) Tool = null;
        if (mode is not (ToolMode.Move or ToolMode.Paste)) _floating = null;
        Mode = mode;
        _dragging = false;
        _lmbDown = false;
        _view.ShowGrid(mode is ToolMode.Build or ToolMode.Move or ToolMode.Paste);
        Changed?.Invoke();
    }

    public void SetLayer(int layer)
    {
        if (_host.Sim == null) return;
        var b = World.Bounds;
        Layer = Math.Clamp(layer, b.Min.Z, b.Max.Z);
        _view.SetLayer(Layer, Cutaway);
        Changed?.Invoke();
    }

    public void ToggleCutaway()
    {
        Cutaway = !Cutaway;
        SetLayer(Layer);
    }

    public void Rotate(int turns)
    {
        switch (Mode)
        {
            case ToolMode.Build:
                Facing = Facing.RotateCW(turns);
                break;
            case ToolMode.Move:
            case ToolMode.Paste:
                _floatingTurns += turns;
                break;
            case ToolMode.Select when Selection.Count > 0:
                var cells = SelectedEntities().Select(e => e.Pos).ToList();
                var pivot = Blueprint.CenterOf(SelectedEntities());
                Report(History.Execute(new MoveBuildings(cells, GridPos.Zero, turns, pivot)));
                break;
            case ToolMode.Select when _hoverEntity != null:
                Report(History.Execute(new RotateBuilding(_hoverEntity.Pos, _hoverEntity.Facing.RotateCW(turns))));
                break;
        }
        Changed?.Invoke();
    }

    public void DeleteSelection()
    {
        if (Selection.Count == 0) return;
        var cells = SelectedEntities().Select(e => e.Pos).ToList();
        if (Report(History.Execute(new RemoveBuildings(cells)))) Notice($"Removed {cells.Count} building(s)");
        Selection.Clear();
        Changed?.Invoke();
    }

    public void CopySelection(bool enterPaste)
    {
        if (Selection.Count == 0)
        {
            Notice("Select something to copy first");
            return;
        }
        var entities = SelectedEntities().ToList();
        Clipboard = Blueprint.FromEntities(entities, Blueprint.CenterOf(entities));
        Notice($"Copied {Clipboard.Count} building(s)" + (enterPaste ? " — click to paste" : ""));
        if (enterPaste) BeginPaste();
        else Changed?.Invoke();
    }

    public void BeginPaste()
    {
        if (Clipboard == null)
        {
            Notice("Clipboard is empty — select buildings and press Ctrl+C");
            return;
        }
        _floating = Clipboard;
        _floatingOrigin = GridPos.Zero;
        _floatingTurns = 0;
        SetMode(ToolMode.Paste);
        _floating = Clipboard;
    }

    public void BeginMove()
    {
        if (Selection.Count == 0)
        {
            Notice("Select buildings to move first");
            return;
        }
        var entities = SelectedEntities().ToList();
        _floatingOrigin = Blueprint.CenterOf(entities);
        var bp = Blueprint.FromEntities(entities, _floatingOrigin);
        _moveCells = entities.Select(e => e.Pos).ToList();
        _floatingTurns = 0;
        SetMode(ToolMode.Move);
        _floating = bp;
    }

    public void Pipette()
    {
        var e = _hoverEntity ?? (_hoverCell is { } c ? World.EntityAt(c) : null);
        if (e == null) return;
        Facing = e.Facing;
        SelectTool(e.Def);
    }

    public void Undo() => Report(History.Undo());
    public void Redo() => Report(History.Redo());

    public void SelectAll()
    {
        Selection.Clear();
        foreach (var e in World.Entities)
            if (_view.IsVisible(e.Pos.Z)) Selection.Add(e.Id);
        Changed?.Invoke();
    }

    public IEnumerable<Entity> SelectedEntities()
    {
        foreach (int id in Selection)
            if (World.GetEntity(id) is { } e) yield return e;
    }

    // ---- Input --------------------------------------------------------------

    public override void _Input(InputEvent ev)
    {
        switch (ev)
        {
            case InputEventMouseMotion motion:
                _mouse = motion.Position;
                _overWorld = false; // set back to true below if the GUI doesn't take it
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false } when _lmbDown:
                _lmbDown = false;
                OnLeftRelease();
                break;
        }
    }

    public override void _UnhandledInput(InputEvent ev)
    {
        switch (ev)
        {
            case InputEventMouseMotion:
                _overWorld = true;
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } mb:
                _overWorld = true;
                _mouse = mb.Position;
                UpdateHover();
                _lmbDown = true;
                _dragging = false;
                _lmbPressPos = mb.Position;
                _dragStart = _hoverCell;
                _firstLegX = null;
                OnLeftPress();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true } mb:
                _rmbPressPos = mb.Position;
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: false } mb:
                if (mb.Position.DistanceTo(_rmbPressPos) < DragThreshold) OnRightClick();
                break;
            case InputEventMouseButton { Pressed: true, ShiftPressed: true } mb when mb.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown:
                SetLayer(Layer + (mb.ButtonIndex == MouseButton.WheelUp ? 1 : -1));
                break;
            case InputEventKey { Pressed: true } key:
                if (OnKey(key)) GetViewport().SetInputAsHandled();
                break;
        }
    }

    private bool OnKey(InputEventKey key)
    {
        bool ctrl = key.CtrlPressed || key.MetaPressed;
        if (key.Echo && key.Keycode is not (Key.Z or Key.Y)) return false;
        switch (key.Keycode)
        {
            case Key.Z when ctrl && key.ShiftPressed:
            case Key.Y when ctrl:
                Redo();
                return true;
            case Key.Z when ctrl:
                Undo();
                return true;
            case Key.C when ctrl:
                CopySelection(enterPaste: false);
                return true;
            case Key.X when ctrl:
                CopySelection(enterPaste: false);
                DeleteSelection();
                return true;
            case Key.V when ctrl:
                BeginPaste();
                return true;
            case Key.A when ctrl:
                SelectAll();
                return true;
            case Key.C:
                CopySelection(enterPaste: true);
                return true;
            case Key.R:
                Rotate(key.ShiftPressed ? -1 : 1);
                return true;
            case Key.Q or Key.Pagedown:
                SetLayer(Layer - 1);
                return true;
            case Key.E or Key.Pageup:
                SetLayer(Layer + 1);
                return true;
            case Key.Tab:
                ToggleCutaway();
                return true;
            case Key.X:
                SetMode(Mode == ToolMode.Delete ? ToolMode.Select : ToolMode.Delete);
                return true;
            case Key.V:
                SetMode(ToolMode.Select);
                return true;
            case Key.M:
                BeginMove();
                return true;
            case Key.F:
                Pipette();
                return true;
            case Key.Delete or Key.Backspace:
                DeleteSelection();
                return true;
            case Key.Escape:
                if (Mode != ToolMode.Select) SetMode(ToolMode.Select);
                else if (Selection.Count > 0)
                {
                    Selection.Clear();
                    Changed?.Invoke();
                }
                else return false; // let the HUD use Esc (menus)
                return true;
        }
        return false;
    }

    private void OnRightClick()
    {
        if (Mode != ToolMode.Select) SetMode(ToolMode.Select);
        else if (Selection.Count > 0)
        {
            Selection.Clear();
            Changed?.Invoke();
        }
    }

    private void OnLeftPress()
    {
        switch (Mode)
        {
            case ToolMode.Move when _floating != null && _hoverCell is { } at:
                var delta = at - _floatingOrigin;
                if (Report(History.Execute(new MoveBuildings(_moveCells, delta, _floatingTurns, _floatingOrigin)))) SetMode(ToolMode.Select);
                _lmbDown = false;
                break;
            case ToolMode.Paste when _floating != null && _hoverCell is { } at:
                var r = History.Execute(new PlaceBlueprint(_floating, at, _floatingTurns));
                if (Report(r))
                {
                    Selection.Clear();
                    foreach (int id in r.EntityIds ?? Array.Empty<int>()) Selection.Add(id);
                    Changed?.Invoke();
                }
                _lmbDown = false;
                break;
        }
    }

    private void OnLeftRelease()
    {
        bool wasDrag = _dragging;
        _dragging = false;
        _rect.Visible = false;
        switch (Mode)
        {
            case ToolMode.Select:
                if (wasDrag) BoxSelect();
                else ClickSelect();
                break;
            case ToolMode.Build:
                CommitPlacement();
                break;
            case ToolMode.Delete:
                var targets = wasDrag ? EntitiesInBox() : _hoverEntity != null ? new List<Entity> { _hoverEntity } : new List<Entity>();
                if (targets.Count > 0 && Report(History.Execute(new RemoveBuildings(targets.Select(e => e.Pos).ToList()))) && targets.Count > 1)
                    Notice($"Removed {targets.Count} buildings");
                break;
        }
    }

    private void ClickSelect()
    {
        bool additive = Input.IsKeyPressed(Key.Shift) || Input.IsKeyPressed(Key.Ctrl);
        if (_hoverEntity == null)
        {
            if (!additive) Selection.Clear();
        }
        else if (additive)
        {
            if (!Selection.Remove(_hoverEntity.Id)) Selection.Add(_hoverEntity.Id);
        }
        else
        {
            Selection.Clear();
            Selection.Add(_hoverEntity.Id);
        }
        Changed?.Invoke();
    }

    private void BoxSelect()
    {
        var hits = EntitiesInBox();
        if (Input.IsKeyPressed(Key.Ctrl)) foreach (var e in hits) Selection.Remove(e.Id);
        else
        {
            if (!Input.IsKeyPressed(Key.Shift)) Selection.Clear();
            foreach (var e in hits) Selection.Add(e.Id);
        }
        Changed?.Invoke();
    }

    private (GridPos Min, GridPos Max)? BoxCells()
    {
        if (_dragStart is not { } a || _hoverCell is not { } b) return null;
        return (new GridPos(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), 0), new GridPos(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y), 0));
    }

    private List<Entity> EntitiesInBox()
    {
        var result = new List<Entity>();
        if (BoxCells() is not var (min, max)) return result;
        foreach (var e in World.Entities)
        {
            var p = e.Pos;
            if (p.X >= min.X && p.X <= max.X && p.Y >= min.Y && p.Y <= max.Y && _view.IsVisible(p.Z)) result.Add(e);
        }
        return result;
    }

    // ---- Placement ------------------------------------------------------------

    private bool IsLineTool => Tool != null && Tool.MetaOr("model", "") is "belt" or "polisher";

    /// <summary>
    /// Multi-layer buildings (ramps) are anchored so their input sits on the current layer:
    /// a ramp down placed on layer 1 spans layers 0–1, one placed on the ground digs a tunnel.
    /// </summary>
    private static GridPos AnchorFor(BuildingDef def, GridPos cell) =>
        def.InputPorts.Count > 0 ? cell with { Z = cell.Z - def.Ports[def.InputPorts[0]].Cell.Z } : cell;

    /// <summary>Layer where a building's output continues (ramps change it; others keep it).</summary>
    private static int OutputLayer(BuildingDef def, GridPos anchor) =>
        def.OutputPorts.Count > 0 ? anchor.Z + def.Ports[def.OutputPorts[0]].Cell.Z : anchor.Z;

    /// <summary>Cells and facings for a drag: an L from the press cell to the hover cell (or just the hover cell).</summary>
    private List<(GridPos Cell, Dir Facing)> PlacementPath(bool fromPress)
    {
        var path = new List<(GridPos, Dir)>();
        if (_hoverCell is not { } end) return path;
        var start = fromPress && _dragStart is { } s ? s : end;
        start = start with { Z = Layer };
        end = end with { Z = Layer };
        if (start == end)
        {
            path.Add((start, Facing));
            return path;
        }

        int dx = end.X - start.X, dy = end.Y - start.Y;
        _firstLegX ??= Math.Abs(dx) >= Math.Abs(dy);
        var cells = new List<GridPos> { start };
        var p = start;
        void Walk(bool alongX, int target)
        {
            while ((alongX ? p.X : p.Y) != target)
            {
                p = alongX ? p with { X = p.X + Math.Sign(target - p.X) } : p with { Y = p.Y + Math.Sign(target - p.Y) };
                cells.Add(p);
            }
        }
        if (_firstLegX.Value) { Walk(true, end.X); Walk(false, end.Y); }
        else { Walk(false, end.Y); Walk(true, end.X); }

        // Line tools face along the path (each tile toward the next); others keep the chosen facing.
        for (int i = 0; i < cells.Count; i++)
        {
            var facing = !IsLineTool ? Facing
                : i + 1 < cells.Count ? Toward(cells[i], cells[i + 1])
                : Toward(cells[i - 1], cells[i]);
            path.Add((cells[i], facing));
        }
        return path;
    }

    private static Dir Toward(GridPos from, GridPos to)
    {
        var d = to - from;
        return d.X > 0 ? Dir.East : d.X < 0 ? Dir.West : d.Y > 0 ? Dir.South : Dir.North;
    }

    private void CommitPlacement()
    {
        if (Tool == null) return;
        var path = PlacementPath(fromPress: true);
        if (path.Count == 0) return;
        int placed = 0, turned = 0, failed = 0;
        string? firstError = null;

        History.BeginGroup();
        GridPos? lastPlaced = null;
        foreach (var (cell, facing) in path)
        {
            var existing = World.EntityAt(cell);
            CommandResult r;
            if (existing == null)
            {
                var anchor = AnchorFor(Tool, cell);
                r = History.Execute(new PlaceBuilding(Tool.Id, anchor, facing));
                if (r.Ok) lastPlaced = anchor;
            }
            else if (IsLineTool && existing.Def == Tool && existing.Pos == cell && existing.Facing != facing)
            {
                r = History.Execute(new RotateBuilding(cell, facing));
                if (r.Ok) turned++;
                continue;
            }
            else continue;

            if (r.Ok) placed++;
            else
            {
                failed++;
                firstError ??= r.Error;
            }
        }
        History.EndGroup();

        if (path.Count > 1 && IsLineTool) Facing = path[^1].Facing;

        // Ramps carry the build layer with them, so bridges and tunnels are built in one flow.
        if (lastPlaced is { } a && OutputLayer(Tool, a) != Layer)
        {
            SetLayer(OutputLayer(Tool, a));
            Notice(Layer == 0 ? "Back on the ground" : Layer > 0 ? $"Building on layer +{Layer}" : $"Building in tunnel layer {Layer}");
        }
        if (failed > 0) Notice(placed + turned > 0 ? $"Placed {placed}, {failed} blocked: {firstError}" : firstError ?? "Can't place here");
        Changed?.Invoke();
    }

    // ---- Frame ----------------------------------------------------------------

    public override void _Process(double delta)
    {
        if (_host.Sim == null) return;
        UpdateHover();
        if (_lmbDown && !_dragging && _mouse.DistanceTo(_lmbPressPos) > DragThreshold) _dragging = true;
        UpdatePreview();
        UpdateHighlights();
    }

    private void UpdateHover()
    {
        if (!_overWorld)
        {
            _hoverCell = null;
            _hoverEntity = null;
            return;
        }
        var cam = _camera.Camera;
        var origin = cam.ProjectRayOrigin(_mouse);
        var dir = cam.ProjectRayNormal(_mouse);
        _hoverCell = null;
        if (new Plane(Vector3.Up, Layer * GridMapping.LayerHeight).IntersectsRay(origin, dir) is { } p)
        {
            var cell = new GridPos(Mathf.FloorToInt(p.X), Mathf.FloorToInt(p.Z), Layer);
            if (World.Bounds.Contains(cell)) _hoverCell = cell;
        }
        _hoverEntity = Mode is ToolMode.Select or ToolMode.Delete
            ? _view.Pick(origin, dir)
            : _hoverCell is { } c ? World.EntityAt(c) : null;
    }

    private void UpdatePreview()
    {
        _specs.Clear();
        bool ports = false;
        switch (Mode)
        {
            case ToolMode.Build when Tool != null && _hoverCell != null:
                var path = PlacementPath(fromPress: _lmbDown);
                for (int i = 0; i < path.Count; i++)
                {
                    var (cell, facing) = path[i];
                    var existing = World.EntityAt(cell);
                    if (existing != null && IsLineTool && existing.Def == Tool) continue; // will be re-oriented
                    var anchor = AnchorFor(Tool, cell);
                    bool ok = World.CanPlace(Tool, anchor, facing).Ok && (World.Sandbox || World.Money >= Tool.Cost);
                    _specs.Add(new GhostSpec(Tool, anchor, facing, PreviewShape(Tool, path, i), ok));
                }
                ports = path.Count == 1;
                break;
            case ToolMode.Move or ToolMode.Paste when _floating != null && _hoverCell is { } at:
                var moving = Mode == ToolMode.Move ? Selection : null;
                foreach (var (defId, pos, facing) in _floating.Placements(at, _floatingTurns))
                {
                    if (!World.Content.Buildings.TryGetValue(defId, out var def)) continue;
                    bool ok = Entity.CellsFor(def, pos, facing).All(c =>
                        World.Bounds.Contains(c) && (World.EntityAt(c) is not { } o || (moving != null && moving.Contains(o.Id))));
                    _specs.Add(new GhostSpec(def, pos, facing, ModelFactory.ShapeFromPorts(def), ok));
                }
                break;
        }
        _ghosts.Show(_specs, ports);

        // Selection / delete rectangle on the build layer.
        if (_dragging && Mode is ToolMode.Select or ToolMode.Delete && BoxCells() is var (min, max))
        {
            _rect.Visible = true;
            _rectMat.AlbedoColor = Mode == ToolMode.Delete ? new Color(Palette.Danger, 0.22f) : new Color(Palette.Select, 0.18f);
            _rect.Scale = new Vector3(max.X - min.X + 1, 1, max.Y - min.Y + 1);
            _rect.Position = new Vector3((min.X + max.X + 1) / 2f, Layer * GridMapping.LayerHeight + 0.03f, (min.Y + max.Y + 1) / 2f);
        }
        else _rect.Visible = false;
    }

    /// <summary>Belt previews bend at the corner of an L drag, like the real belts will.</summary>
    private static PathShape PreviewShape(BuildingDef def, List<(GridPos Cell, Dir Facing)> path, int i)
    {
        var straight = ModelFactory.ShapeFromPorts(def);
        if (def.MetaOr("model", "") != "belt" || i == 0) return straight;
        var travelIn = path[i - 1].Facing;
        var side = (Side)(((int)travelIn.Opposite() - (int)path[i].Facing + 4) & 3);
        return side switch
        {
            Side.Left => new PathShape(PathKind.CurveLeft),
            Side.Right => new PathShape(PathKind.CurveRight),
            _ => straight,
        };
    }

    private void UpdateHighlights()
    {
        var want = new Dictionary<int, Highlight>();
        foreach (int id in Selection) want[id] = Highlight.Selected;
        if (Mode == ToolMode.Move)
            foreach (int id in Selection) want[id] = Highlight.Moving;
        if (_hoverEntity != null && !want.ContainsKey(_hoverEntity.Id) && Mode is ToolMode.Select or ToolMode.Delete)
            want[_hoverEntity.Id] = Mode == ToolMode.Delete ? Highlight.Danger : Highlight.Hover;
        if (_dragging)
        {
            var kind = Mode == ToolMode.Delete ? Highlight.Danger : Mode == ToolMode.Select ? Highlight.Hover : Highlight.None;
            if (kind != Highlight.None)
                foreach (var e in EntitiesInBox())
                    if (!want.ContainsKey(e.Id) || kind == Highlight.Danger) want[e.Id] = kind;
        }

        foreach (var (id, h) in _applied)
            if (!want.ContainsKey(id)) _view.SetHighlight(id, Highlight.None);
        foreach (var (id, h) in want) _view.SetHighlight(id, h);
        _applied.Clear();
        foreach (var kv in want) _applied[kv.Key] = kv.Value;
    }

    private bool Report(CommandResult r)
    {
        if (!r.Ok && r.Error != null) Notice(r.Error);
        return r.Ok;
    }

    private void Notice(string text) => _host.Notify(text);
}
