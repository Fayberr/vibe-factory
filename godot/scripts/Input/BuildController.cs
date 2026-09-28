using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using FactorySim.Behaviors;
using FactorySim.Content;
using FactorySim.Editing;
using FactorySim.View;

namespace FactorySim.Client;

public enum ToolMode
{
    Select,
    Build,
    Upgrade,
    Delete,
    Move,
    Paste,
}

/// <summary>
/// Turns mouse and keyboard into simulation commands. One mode at a time:
///  • Select:  click, Shift/Ctrl-click, drag a box; R rotates, M moves, U upgrades, Del deletes.
///  • Build:   click to place, drag to lay a line. Placing onto a compatible building replaces
///              it (polisher onto a belt, splitter into a line); a belt dragged across another
///              line bridges over it by itself.
///  • Upgrade: click or drag a box to raise building levels; Shift-click upgrades a whole line.
///  • Delete:  click or drag a box.
///  • Move / Paste: a blueprint follows the cursor; R rotates it; click to drop.
/// Height: everything is built at the current build height (0 = ground, the lowest there is).
/// Q/E, PageDown/PageUp or Shift+wheel change it; ramps carry it along (a ramp up leaves you
/// one level higher). Tab hides everything above it. All edits go through the undo history.
/// </summary>
public partial class BuildController : Node3D
{
    private const float DragThreshold = 6f;

    private SimHost _host = null!;
    private CameraRig _camera = null!;
    private WorldView _view = null!;
    private GhostLayer _ghosts = null!;
    private MeshInstance3D _rect = null!;
    private ShaderMaterial _rectMat = null!;
    private BuildPlanner _planner = null!;

    private Vector2 _mouse;
    private bool _overWorld;
    private GridPos? _hoverCell;
    private Entity? _hoverEntity;

    private bool _lmbDown, _dragging;
    private Vector2 _lmbPressPos;
    private GridPos? _dragStart;
    private readonly List<GridPos> _trail = new(); // cells the cursor passed while dragging (Shift draws along them)
    private (GridPos, GridPos, bool, string, int)? _routeKey;
    private List<GridPos>? _route;
    private int _edits; // bumped by every placement or removal, so a cached route is found again
    private bool? _firstLegX;
    private Vector2 _rmbPressPos;

    private Blueprint? _floating;
    private GridPos _floatingOrigin;
    private int _floatingTurns;
    private List<GridPos> _moveCells = new();

    private readonly Dictionary<int, Highlight> _applied = new();
    private readonly List<GhostSpec> _specs = new();
    private readonly HashSet<int> _replacing = new();
    private readonly HashSet<int> _lineHover = new();
    private readonly List<Entity> _scratch = new();

    public ToolMode Mode { get; private set; } = ToolMode.Select;
    public BuildingDef? Tool { get; private set; }
    public Dir Facing { get; private set; } = Dir.East;

    /// <summary>Build height: 0 = ground (nothing goes lower), each level up is one bridge level.</summary>
    public int Height { get; private set; }

    /// <summary>Everything above <see cref="Height"/> is hidden (to see and reach what is underneath).</summary>
    public bool HideAbove { get; private set; }

    public int MaxHeight => _host.Sim?.World.Bounds.Max.Z ?? 0;
    public HashSet<int> Selection { get; } = new();
    public Blueprint? Clipboard { get; private set; }
    public Entity? HoverEntity => _hoverEntity;

    /// <summary>What a click would do right now (shown next to the cursor), or null.</summary>
    public (string Text, bool Ok)? CursorInfo { get; private set; }

    /// <summary>Mode, tool, facing, height or selection changed.</summary>
    public event Action? Changed;

    /// <summary>Esc with nothing to cancel (no tool, no selection): the game opens its pause menu.</summary>
    public event Action? EscapeIdle;

    /// <summary>Off while menus own the screen: no input, no previews, no highlights.</summary>
    public bool Enabled
    {
        get => ProcessMode != ProcessModeEnum.Disabled;
        set
        {
            if (value == Enabled) return;
            if (!value)
            {
                SetMode(ToolMode.Select);
                Selection.Clear();
                _ghosts.HideAll();
                _rect.Visible = false;
                foreach (var id in _applied.Keys) _view.SetHighlight(id, Highlight.None);
                _applied.Clear();
                CursorInfo = null;
            }
            ProcessMode = value ? ProcessModeEnum.Inherit : ProcessModeEnum.Disabled;
        }
    }

    private World World => _host.Sim.World;
    private EditHistory History => _host.History;

    public void Init(SimHost host, CameraRig camera, WorldView view)
    {
        _host = host;
        _camera = camera;
        _view = view;
        host.WorldReplaced += () =>
        {
            _planner = new BuildPlanner(host.Sim);
            _routeKey = null;
            Selection.Clear();
            _applied.Clear();
            Height = 0;
            SetMode(ToolMode.Select);
            _view.SetLayer(Height, HideAbove);
        };
        host.EventRaised += ev =>
        {
            if (ev is EntityPlaced or EntityRemoved or EntityReoriented) _edits++;
            if (ev is EntityRemoved r && Selection.Remove(r.EntityId)) Changed?.Invoke();
        };
    }

    public override void _Ready()
    {
        _ghosts = new GhostLayer { Name = "Ghosts" };
        AddChild(_ghosts);
        _rectMat = Shaders.AreaBoxMaterial(Palette.Select);
        _rect = new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = Vector2.One },
            MaterialOverride = _rectMat,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
        };
        AddChild(_rect);
    }

    public static string HeightName(int h) => h == 0 ? "Ground" : $"Height {h}";

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

    public void SetHeight(int height)
    {
        if (_host.Sim == null) return;
        int h = Math.Clamp(height, World.Bounds.Min.Z, World.Bounds.Max.Z);
        if (h == Height)
        {
            if (height < h) Notice("Already on the ground. Nothing can be built below it");
            else if (height > h) Notice($"Maximum height is {h}");
            return;
        }
        Height = h;
        _view.SetLayer(Height, HideAbove);
        Changed?.Invoke();
    }

    public void ToggleHideAbove()
    {
        HideAbove = !HideAbove;
        _view.SetLayer(Height, HideAbove);
        Notice(HideAbove ? $"Hiding everything above {HeightName(Height).ToLowerInvariant()} (Tab to show)" : "Showing all heights");
        Changed?.Invoke();
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

    /// <summary>U: upgrade the selection; with nothing selected, toggle the upgrade tool.</summary>
    public void UpgradeKey()
    {
        if (Mode == ToolMode.Select && Selection.Count > 0) UpgradeEntities(SelectedEntities().ToList());
        else SetMode(Mode == ToolMode.Upgrade ? ToolMode.Select : ToolMode.Upgrade);
    }

    /// <summary>Raises each building one level, cheapest first, as far as the money goes. One undo step.</summary>
    public void UpgradeEntities(IReadOnlyCollection<Entity> entities)
    {
        var candidates = entities
            .Where(e => e.Def.Upgrade?.CanUpgrade(e.Level) == true)
            .Select(e => (Entity: e, Cost: e.Def.Upgrade!.UpgradeCost(e.Def, e.Level)))
            .OrderBy(x => x.Cost.ToDouble())
            .ToList();
        if (candidates.Count == 0)
        {
            Notice(entities.Count == 1 ? "Already at max level" : "Everything here is at max level");
            return;
        }

        var changes = new List<LevelChange>();
        BigNum total = BigNum.Zero;
        foreach (var (e, cost) in candidates)
        {
            if (!World.Sandbox && total + cost > World.Money) break;
            total += cost;
            changes.Add(new LevelChange(e.Pos, e.Level + 1));
        }
        if (changes.Count == 0)
        {
            Notice($"Need ${candidates[0].Cost.Format()} to upgrade");
            return;
        }
        if (!Report(History.Execute(new SetBuildingLevels(changes)))) return;
        string rest = changes.Count < candidates.Count ? $" ({candidates.Count - changes.Count} need more money)" : "";
        Notice(changes.Count == 1 && entities.Count == 1
            ? $"{candidates[0].Entity.Def.Name} → level {candidates[0].Entity.Level} (${total.Format()})"
            : $"Upgraded {changes.Count} buildings for ${total.Format()}{rest}");
        Changed?.Invoke();
    }

    /// <summary>
    /// Pieces of the same kind connected to <paramref name="start"/> along the item flow (a whole
    /// belt line), passing through other transport pieces (ramps, splitters) without taking them.
    /// </summary>
    public List<Entity> ConnectedLine(Entity start)
    {
        World.EnsureTopology();
        bool IsTransport(Entity e) => World.Content.Behaviors.Get(e.Def.Behavior) is ConveyorBehavior or RouterBehavior;
        var result = new List<Entity>();
        if (!IsTransport(start)) return new List<Entity> { start };

        var neighbours = new Dictionary<Entity, List<Entity>>();
        void Link(Entity a, Entity b)
        {
            if (!neighbours.TryGetValue(a, out var list)) neighbours[a] = list = new List<Entity>();
            list.Add(b);
        }
        foreach (var e in World.Entities)
        {
            if (!IsTransport(e)) continue;
            foreach (int port in e.Def.OutputPorts)
                if (e.Link(port).Target is { } t && IsTransport(t))
                {
                    Link(e, t);
                    Link(t, e);
                }
        }
        var seen = new HashSet<Entity> { start };
        var queue = new Queue<Entity>();
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            var e = queue.Dequeue();
            if (e.Def == start.Def) result.Add(e);
            foreach (var n in neighbours.GetValueOrDefault(e) ?? new List<Entity>())
                if (seen.Add(n)) queue.Enqueue(n);
        }
        return result;
    }

    public void ClearSelection()
    {
        if (Selection.Count == 0) return;
        Selection.Clear();
        Changed?.Invoke();
    }

    /// <summary>Sets what the selected machines produce (null = automatic). One undo step.</summary>
    public void ChooseRecipe(string? recipe)
    {
        var machines = SelectedEntities().Where(e => e.Def.Params is ProcessorParams).ToList();
        if (machines.Count == 0) return;
        History.BeginGroup();
        string? error = null;
        foreach (var e in machines)
            if (History.Execute(new SelectRecipe(e.Pos, recipe)) is { Ok: false } r) error ??= r.Error;
        History.EndGroup();
        if (error != null) Notice(error);
        else if (recipe == null) Notice(machines.Count == 1 ? $"{machines[0].Def.Name}: automatic" : $"{machines.Count} machines: automatic");
        else
        {
            var item = World.Content.Items[World.Content.Recipes[recipe].Outputs[0].Item].Name;
            Notice(machines.Count == 1 ? $"{machines[0].Def.Name} now makes {item}" : $"{machines.Count} machines now make {item}");
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
        Notice($"Copied {Clipboard.Count} building(s)" + (enterPaste ? ". Click to paste" : ""));
        if (enterPaste) BeginPaste();
        else Changed?.Invoke();
    }

    public void BeginPaste()
    {
        if (Clipboard == null)
        {
            Notice("Clipboard is empty. Select buildings and press Ctrl+C");
            return;
        }
        _floatingOrigin = GridPos.Zero;
        _floatingTurns = 0;
        Selection.Clear(); // the clipboard holds the copy; nothing stays highlighted while pasting
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
        SetHeight(_floatingOrigin.Z); // Q/E while moving lifts or lowers the selection
    }

    /// <summary>Pick the hovered building as the tool, with its direction and height.</summary>
    public void Pipette()
    {
        var e = _hoverEntity;
        if (e == null) return;
        Facing = e.Facing;
        SetHeight(BuildPlanner.HeightOf(e));
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
                _trail.Clear();
                if (_hoverCell is { } pressed) _trail.Add(pressed);
                OnLeftPress();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true } mb:
                _rmbPressPos = mb.Position;
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: false } mb:
                if (mb.Position.DistanceTo(_rmbPressPos) < DragThreshold) OnRightClick();
                break;
            case InputEventMouseButton { Pressed: true, ShiftPressed: true } wheel
                when wheel.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown && GetViewport().GuiGetHoveredControl() == null:
                SetHeight(Height + (wheel.ButtonIndex == MouseButton.WheelUp ? 1 : -1));
                GetViewport().SetInputAsHandled();
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
        // Rebindable single keys (Settings → Controls); Ctrl shortcuts, Esc and Delete are fixed.
        if (!ctrl && !key.AltPressed)
        {
            System.Action? act =
                Keybinds.Is(key, "rotate") ? () => Rotate(key.ShiftPressed ? -1 : 1)
                : Keybinds.Is(key, "pick") ? Pipette
                : Keybinds.Is(key, "height_up") ? () => SetHeight(Height + 1)
                : Keybinds.Is(key, "height_down") ? () => SetHeight(Height - 1)
                : Keybinds.Is(key, "upgrade") ? UpgradeKey
                : Keybinds.Is(key, "hide_above") ? ToggleHideAbove
                : Keybinds.Is(key, "delete_tool") ? () => SetMode(Mode == ToolMode.Delete ? ToolMode.Select : ToolMode.Delete)
                : Keybinds.Is(key, "select_tool") ? () => SetMode(ToolMode.Select)
                : Keybinds.Is(key, "move") ? BeginMove
                : Keybinds.Is(key, "copy") ? () => CopySelection(enterPaste: true)
                : null;
            if (act != null)
            {
                act();
                return true;
            }
        }
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
            case Key.Pageup:
                SetHeight(Height + 1);
                return true;
            case Key.Pagedown:
                SetHeight(Height - 1);
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
                else if (EscapeIdle != null) EscapeIdle();
                else return false;
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
                // Pasted buildings are not selected: they look like any other building right away.
                if (Report(History.Execute(new PlaceBlueprint(_floating, at, _floatingTurns)))) Changed?.Invoke();
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
            case ToolMode.Upgrade:
                var toUpgrade = wasDrag ? EntitiesInBox()
                    : _hoverEntity == null ? new List<Entity>()
                    : Input.IsKeyPressed(Key.Shift) ? ConnectedLine(_hoverEntity)
                    : new List<Entity> { _hoverEntity };
                if (toUpgrade.Count > 0) UpgradeEntities(toUpgrade);
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

    /// <summary>The direction a click would build in now: a depot turns to the belt feeding the hovered cell.</summary>
    public Dir ShownFacing => PlanNow(fromPress: false) is { Steps.Count: 1 } plan ? plan.Steps[0].Facing : Facing;

    /// <summary>What a click (or the drag so far) would build.</summary>
    private BuildPlan? PlanNow(bool fromPress)
    {
        if (Tool == null || _hoverCell is not { } hover) return null;
        int z = _planner.AnchorHeight(Tool, Height);
        var end = hover with { Z = z };
        var start = (fromPress && _dragStart is { } s ? s : hover) with { Z = z };
        if (start == end) return _planner.Click(Tool, end, Facing);
        _firstLegX ??= Math.Abs(end.X - start.X) >= Math.Abs(end.Y - start.Y);
        return _planner.Drag(Tool, DragCells(Tool, start, end, _firstLegX.Value), Facing);
    }

    /// <summary>Belts laid by dragging: the path the cursor drew with Shift held, else a route that finds its
    /// own way (around buildings, over lines, into the building the drag ends on). Other tools: an L.</summary>
    private List<GridPos> DragCells(BuildingDef tool, GridPos start, GridPos end, bool firstLegX)
    {
        if (!_planner.IsLineTool(tool)) return BuildPlanner.LPath(start, end, firstLegX);
        if (Input.IsKeyPressed(Key.Shift) && _trail.Count > 1)
            return _trail.Select(c => c with { Z = start.Z }).ToList();
        var key = (start, end, firstLegX, tool.Id, _edits);
        if (_routeKey != key)
        {
            _routeKey = key;
            _route = _planner.Route(start, end, firstLegX);
        }
        return _route ?? BuildPlanner.LPath(start, end, firstLegX);
    }

    /// <summary>The tool lays lines (belts): dragging routes them, Shift+drag draws them.</summary>
    public bool LineTool => Tool != null && _planner.IsLineTool(Tool);

    private void CommitPlacement()
    {
        if (Tool is not { } tool || PlanNow(fromPress: true) is not { } plan) return;
        if (!plan.Changes.Any())
        {
            if (plan.Steps.Count == 1) Notice($"{tool.Name} is already here");
            return;
        }

        History.BeginGroup();
        var (changed, failed, error) = BuildPlanner.Apply(plan, History.Execute);
        History.EndGroup();

        if (plan.Steps.Count > 1 && _planner.IsLineTool(tool)) Facing = plan.Steps[^1].Facing; // keep going the same way
        if (failed > 0) Notice(changed > 0 ? $"Placed {changed}, {failed} blocked: {error}" : error ?? "Can't place here");
        else if (plan.Bridges > 0) Notice(plan.Bridges == 1 ? "Bridged over the crossing line" : $"Bridged over {plan.Bridges} crossings");
        else if (plan.BridgeProblem != null) Notice(plan.BridgeProblem);

        // A ramp takes you along: after a ramp up you keep building one level higher.
        if (changed > 0 && plan.Steps.Count == 1 && BuildPlanner.IsRamp(tool))
        {
            int next = _planner.OutputHeight(tool, plan.Steps[0].Pos.Z);
            if (next != Height)
            {
                SetHeight(next);
                Notice($"Now building at {HeightName(next).ToLowerInvariant()} (Q/E to change)");
            }
        }
        Changed?.Invoke();
    }

    // ---- Frame ----------------------------------------------------------------

    public override void _Process(double delta)
    {
        if (_host.Sim == null) return;
        UpdateHover();
        if (_lmbDown && !_dragging && _mouse.DistanceTo(_lmbPressPos) > DragThreshold) _dragging = true;
        if (_lmbDown && _hoverCell is { } over && _trail.Count > 0) BuildPlanner.ExtendTrail(_trail, over with { Z = _trail[0].Z });
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

        // The cursor sits on the build height's plane, so ghosts appear right under it.
        _hoverCell = null;
        if (new Plane(Vector3.Up, Height * GridMapping.LayerHeight).IntersectsRay(origin, dir) is { } p)
        {
            var cell = new GridPos(Mathf.FloorToInt(p.X), Mathf.FloorToInt(p.Z), Height);
            if (World.Bounds.Contains(cell)) _hoverCell = cell;
        }
        _hoverEntity = _view.Pick(origin, dir);
    }

    private void UpdatePreview()
    {
        _specs.Clear();
        _replacing.Clear();
        _lineHover.Clear();
        CursorInfo = null;
        bool ports = false;
        switch (Mode)
        {
            case ToolMode.Build when Tool != null && PlanNow(fromPress: _lmbDown) is { } plan:
                PreviewPlan(Tool, plan);
                ports = plan.Steps.Count == 1;
                break;

            case ToolMode.Move or ToolMode.Paste when _floating != null && _hoverCell is { } at:
                var moving = Mode == ToolMode.Move ? Selection : null;
                bool allOk = true;
                foreach (var (defId, pos, facing, _, _) in _floating.Placements(at, _floatingTurns))
                {
                    if (!World.Content.Buildings.TryGetValue(defId, out var def)) continue;
                    bool ok = Entity.CellsFor(def, pos, facing).All(c =>
                        World.Bounds.Contains(c) && (World.EntityAt(c) is not { } o || (moving != null && moving.Contains(o.Id))));
                    allOk &= ok;
                    _specs.Add(new GhostSpec(def, pos, facing, ModelFactory.ShapeFromPorts(def), ok));
                }
                CursorInfo = !allOk ? ("Blocked: find a free spot", false)
                    : Mode == ToolMode.Paste ? ($"Paste {_floating.Count}  ${_floating.Cost(World.Content).Format()}{AtHeight()}", true)
                    : ($"Move {_floating.Count} here{AtHeight()}", true);
                break;

            case ToolMode.Upgrade when !_dragging && _hoverEntity is { } up:
                var line = Input.IsKeyPressed(Key.Shift) ? ConnectedLine(up) : null;
                if (line is { Count: > 1 })
                {
                    foreach (var e in line) _lineHover.Add(e.Id);
                    var next = line.Where(e => e.Def.Upgrade?.CanUpgrade(e.Level) == true).ToList();
                    var sum = next.Aggregate(BigNum.Zero, (s, e) => s + e.Def.Upgrade!.UpgradeCost(e.Def, e.Level));
                    CursorInfo = next.Count == 0 ? ($"Line of {line.Count} {up.Def.Name}s: all at max level", false)
                        : ($"Upgrade {next.Count} {up.Def.Name}s one level  ${sum.Format()}", World.Sandbox || World.Money >= sum);
                    break;
                }
                var track = up.Def.Upgrade;
                if (track == null || !track.CanUpgrade(up.Level)) CursorInfo = ($"{up.Def.Name}: max level {up.Level}", false);
                else
                {
                    var cost = track.UpgradeCost(up.Def, up.Level);
                    string hint = World.Content.Behaviors.Get(up.Def.Behavior) is ConveyorBehavior or RouterBehavior ? "  (Shift: whole line)" : "";
                    CursorInfo = ($"Upgrade {up.Def.Name} → level {up.Level + 1}  ${cost.Format()}{hint}", World.Sandbox || World.Money >= cost);
                }
                break;

            case ToolMode.Delete when !_dragging && _hoverEntity is { } del:
                CursorInfo = ($"Remove {del.Def.Name}  +${World.InvestedIn(del).Format()}", true);
                break;

            case ToolMode.Upgrade or ToolMode.Delete when _dragging:
                var box = EntitiesInBox();
                CursorInfo = (Mode == ToolMode.Delete ? $"Remove {box.Count} buildings" : $"Upgrade {box.Count} buildings", true);
                break;
        }
        _ghosts.Show(_specs, ports);

        // Selection / action rectangle on the build plane.
        if (_dragging && Mode is ToolMode.Select or ToolMode.Delete or ToolMode.Upgrade && BoxCells() is var (min, max))
        {
            _rect.Visible = true;
            _rectMat.SetShaderParameter("tint", Mode switch
            {
                ToolMode.Delete => Palette.Danger,
                ToolMode.Upgrade => Palette.Upgrade,
                _ => Palette.Select,
            });
            var size = new Vector2(max.X - min.X + 1, max.Y - min.Y + 1);
            _rectMat.SetShaderParameter("size", size);
            _rect.Scale = new Vector3(size.X, 1, size.Y);
            _rect.Position = new Vector3((min.X + max.X + 1) / 2f, Height * GridMapping.LayerHeight + 0.03f, (min.Y + max.Y + 1) / 2f);
        }
        else _rect.Visible = false;
    }

    private string AtHeight() => Height > 0 ? $"  · height {Height}" : "";

    /// <summary>Ghosts, cost, replaced buildings and the cursor text for a build plan.</summary>
    private void PreviewPlan(BuildingDef tool, BuildPlan plan)
    {
        BigNum total = BigNum.Zero;
        string? problem = null;
        string? replacedName = null;
        var counts = new Dictionary<BuildingDef, int>();
        foreach (var step in plan.Steps)
        {
            if (step.Action == PlanAction.Keep) continue;
            bool ok = true;
            if (step.Action == PlanAction.Place)
            {
                var check = World.CanPlaceReplacing(step.Def, step.Pos, step.Facing, _scratch);
                int count = counts[step.Def] = counts.GetValueOrDefault(step.Def) + 1;
                string? why = !check.Ok ? check.Reason
                    : _host.Sim.LockReason(step.Def) ?? _host.Sim.LimitReason(step.Def, count, _scratch.Count(r => r.Def == step.Def));
                ok = why == null;
                problem ??= why;
                if (ok)
                {
                    foreach (var r in _scratch)
                    {
                        _replacing.Add(r.Id);
                        replacedName ??= r.Def.Name;
                        total -= World.InvestedIn(r) * _host.Sim.RefundFraction;
                    }
                    total += step.Def.Cost;
                }
            }
            _specs.Add(new GhostSpec(step.Def, step.Pos, step.Facing, ShapeFor(step), ok));
        }

        int valid = _specs.Count(s => s.Valid);
        bool affordable = World.Sandbox || World.Money >= total;
        if (!affordable) for (int i = 0; i < _specs.Count; i++) _specs[i] = _specs[i] with { Valid = false };
        string price = total.Sign < 0 ? $"+${(-total).Format()}" : $"${total.Format()}";

        if (_specs.Count == 0) CursorInfo = (plan.Steps.Count == 1 ? $"{tool.Name} is already here" : "Already built", false);
        else if (valid == 0) CursorInfo = (problem ?? "Can't build here", false);
        else if (!affordable) CursorInfo = ($"Need ${total.Format()} (have ${World.Money.Format()})", false);
        else
        {
            string text = plan.Steps.Count == 1
                ? (replacedName != null ? $"Replace {replacedName} with {tool.Name}  {price}" : $"{tool.Name}  {price}")
                : $"{tool.Name} ×{valid}  {price}";
            if (valid < _specs.Count) text += $"  · {_specs.Count - valid} blocked";
            if (plan.Bridges > 0) text += plan.Bridges == 1 ? "  · bridges 1 crossing" : $"  · bridges {plan.Bridges} crossings";
            else if (plan.BridgeProblem != null) text += $"  · {plan.BridgeProblem}";
            CursorInfo = (text + AtHeight(), true);
        }
    }

    /// <summary>Belt previews bend where the drag turns, like the real belts will.</summary>
    private static PathShape ShapeFor(PlanStep step)
    {
        var straight = ModelFactory.ShapeFromPorts(step.Def);
        if (step.Def.MetaOr("model", "") != "belt" || step.Incoming is not { } travel || travel == step.Facing) return straight;
        var side = (Side)(((int)travel.Opposite() - (int)step.Facing + 4) & 3);
        return side switch
        {
            Side.Left => new PathShape(PathKind.CurveLeft, straight.StartZ, straight.EndZ),
            Side.Right => new PathShape(PathKind.CurveRight, straight.StartZ, straight.EndZ),
            _ => straight,
        };
    }

    private void UpdateHighlights()
    {
        var want = new Dictionary<int, Highlight>();
        foreach (int id in Selection) want[id] = Highlight.Selected;
        if (Mode == ToolMode.Move)
            foreach (int id in Selection) want[id] = Highlight.Moving;
        foreach (int id in _replacing) want[id] = Highlight.Danger;
        foreach (int id in _lineHover) want[id] = Highlight.Upgrade;
        if (_hoverEntity != null && !want.ContainsKey(_hoverEntity.Id))
        {
            var h = Mode switch
            {
                ToolMode.Select => Highlight.Hover,
                ToolMode.Delete => Highlight.Danger,
                ToolMode.Upgrade => Highlight.Upgrade,
                _ => Highlight.None,
            };
            if (h != Highlight.None) want[_hoverEntity.Id] = h;
        }
        if (_dragging)
        {
            var kind = Mode switch
            {
                ToolMode.Delete => Highlight.Danger,
                ToolMode.Upgrade => Highlight.Upgrade,
                ToolMode.Select => Highlight.Selected, // what the box will select, in the selection's own colour
                _ => Highlight.None,
            };
            if (kind != Highlight.None)
                foreach (var e in EntitiesInBox())
                    want[e.Id] = kind;
        }

        foreach (var (id, _) in _applied)
            if (!want.ContainsKey(id)) _view.SetHighlight(id, Highlight.None);
        foreach (var (id, h) in want) _view.SetHighlight(id, h);
        _applied.Clear();
        foreach (var kv in want) _applied[kv.Key] = kv.Value;
    }

    private bool Report(CommandResult r)
    {
        if (!r.Ok && r.Error != null) _host.Fail(r.Error);
        return r.Ok;
    }

    private void Notice(string text) => _host.Notify(text);
}
