using System.Linq;
using System.Threading.Tasks;
using Godot;
using FactorySim.View;

namespace FactorySim.Client;

/// <summary>
/// Scripted end-to-end check of the building UX: injects real mouse/keyboard events,
/// asserts on simulation state, optionally saves screenshots.
///   godot --path godot -- --ui-test [--shots=/abs/dir]
/// Exits with code 0 when every check passes.
/// </summary>
public partial class UiScenario : Node
{
    public SimHost Host = null!;
    public BuildController Tools = null!;
    public CameraRig Camera = null!;
    public WorldView View = null!;
    public Hud Hud = null!;

    private string? _shots;
    private int _failures, _checks;

    public override async void _Ready()
    {
        foreach (var arg in OS.GetCmdlineUserArgs())
            if (arg.StartsWith("--shots=")) _shots = arg["--shots=".Length..];

        Host.Start(loadSave: false);
        Host.Sim.World.Sandbox = true;
        Camera.Focus(new Vector3(8, 0, 9), instant: true);
        await Frames(20);
        var w = Host.Sim.World;

        // 0. Tutorial: opens on the welcome, Next moves on, placing a drill completes that step.
        Hud.StartTutorial();
        await Frames(5);
        Check(Hud.Tutorial.Active && Hud.Tutorial.Current?.Id == "welcome", "the tutorial starts with a welcome");
        await Click(Hud.Tutorial.NextButton.GetGlobalRect().GetCenter());
        Check(Hud.Tutorial.Current?.Id == "drill", "Next moves to placing a drill");
        await Frames(5);
        Check(Hud.Tutorial.Highlight.Visible, "the drill step points at its hotbar slot");
        await Key(Godot.Key.Key6);
        await Click(Cell(14, 3));
        for (int i = 0; i < 400 && Hud.Tutorial.Current?.Id == "drill"; i++) await Frames(1);
        Check(Hud.Tutorial.Current?.Id == "belt", "placing a drill completes the step and moves on");
        await Key(Godot.Key.Z, ctrl: true);
        await Key(Godot.Key.Escape);
        Hud.Tutorial.Window.Close();
        Check(!Hud.Tutorial.Active && w.EntityCount == 0, "closing ends the tutorial");

        // 1. Hotbar + L-shaped belt drag.
        await Key(Godot.Key.Key1);
        Check(Tools.Mode == ToolMode.Build && Tools.Tool?.Id == "conveyor", "key 1 selects the conveyor");
        await Drag(Cell(4, 5), Cell(9, 5), Cell(9, 9));
        Check(w.EntityCount == 10, $"L drag placed 10 belts (got {w.EntityCount})");
        var corner = w.EntityAt(new GridPos(9, 5, 0));
        Check(corner?.Facing == Dir.South, "corner belt turns south");
        w.EnsureTopology();
        Check(corner != null && TransportPath.ShapeOf(corner).Kind == PathKind.CurveRight, "corner renders as a curve");

        // 2. Undo / redo the whole line as one step.
        await Key(Godot.Key.Z, ctrl: true);
        Check(w.EntityCount == 0, "Ctrl+Z removes the line");
        await Key(Godot.Key.Y, ctrl: true);
        Check(w.EntityCount == 10, "Ctrl+Y restores it");

        // 3. Source and sink, then let it run.
        await Key(Godot.Key.Key6);
        while (Tools.Facing != Dir.East) await Key(Godot.Key.R);
        await Click(Cell(3, 5));
        await Key(Godot.Key.Key9);
        await Click(Cell(9, 10));
        Check(w.EntityAt(new GridPos(3, 5, 0))?.Def.Id == "iron_miner", "drill placed with hotkey 6");
        Check(w.EntityAt(new GridPos(9, 10, 0))?.Def.Id == "seller", "depot placed with hotkey 9");
        Host.TimeScale = 16;
        await Frames(90);
        Host.TimeScale = 1;
        Check(!w.Stats.TotalEarned.IsZero, "the line sells ore");
        await Shot("01-line");

        // 4. Box select everything, copy, paste elsewhere.
        await Key(Godot.Key.Escape);
        Check(Tools.Mode == ToolMode.Select, "Esc returns to select");
        await Drag(Cell(2, 4), Cell(10, 11));
        Check(Tools.Selection.Count == 12, $"box select picks 12 buildings (got {Tools.Selection.Count})");
        await Shot("02-selection");
        await Key(Godot.Key.C, ctrl: true);
        await Key(Godot.Key.V, ctrl: true);
        Check(Tools.Mode == ToolMode.Paste, "Ctrl+V enters paste mode");
        await Move(Cell(5, 16));
        await Shot("03-paste-preview");
        await Click(Cell(5, 16));
        Check(w.EntityCount == 24, $"paste places a full copy (got {w.EntityCount})");
        await Frames(4);
        Check(Tools.Selection.Count == 0 && w.Entities.All(e => View.HighlightOf(e.Id) == Highlight.None),
            "pasted buildings are not left highlighted");
        await Key(Godot.Key.Escape);

        // 5. Delete tool with a box.
        await Key(Godot.Key.X);
        Check(Tools.Mode == ToolMode.Delete, "X selects the delete tool");
        await Drag(Cell(0, 13), Cell(12, 21));
        Check(w.EntityCount == 12, $"box delete removes the copy (got {w.EntityCount})");
        await Key(Godot.Key.Z, ctrl: true);
        Check(w.EntityCount == 24, "undo brings the copy back");
        await Key(Godot.Key.Escape);

        // 6. Move the original line two cells north, rotating nothing.
        await Drag(Cell(2, 4), Cell(10, 11));
        var anyId = Tools.Selection.First();
        var before = w.GetEntity(anyId)!.Pos;
        await Key(Godot.Key.M);
        Check(Tools.Mode == ToolMode.Move, "M starts moving the selection");
        var origin = FactorySim.Editing.Blueprint.CenterOf(Tools.SelectedEntities());
        await Move(Cell(origin.X, origin.Y - 2));
        await Click(Cell(origin.X, origin.Y - 2));
        Check(w.GetEntity(anyId)?.Pos == before + new GridPos(0, -2, 0), "move keeps ids and shifts by (0,-2)");

        // 7. Pipette.
        await Key(Godot.Key.Escape);
        await Key(Godot.Key.Escape);
        await Move(Cell(3, 3));
        await Key(Godot.Key.F);
        Check(Tools.Mode == ToolMode.Build && Tools.Tool?.Id == "iron_miner", "F picks the hovered building");
        await Key(Godot.Key.Escape);

        // 8. Replace on place: a polisher dropped on a belt takes its place and direction.
        int count = w.EntityCount;
        await Key(Godot.Key.Key8);
        Check(Tools.Tool?.Id == "polisher", "key 8 selects the polisher");
        while (Tools.Facing != Dir.North) await Key(Godot.Key.R); // deliberately "wrong"
        await Click(Cell(6, 3));
        var polished = w.EntityAt(new GridPos(6, 3, 0));
        Check(polished?.Def.Id == "polisher" && polished.Facing == Dir.East, $"polisher replaces the belt and keeps its direction (got {polished?.Def.Id} {polished?.Facing})");
        Check(w.EntityCount == count, "replacing does not add or leave anything behind");
        await Key(Godot.Key.Escape);

        // 9. Drag a belt across another line: it bridges over by itself.
        await Focus(17, 14);
        await Key(Godot.Key.Key1);
        await Drag(Cell(14, 14), Cell(20, 14));
        await Drag(Cell(17, 11), Cell(17, 17));
        Check(w.EntityAt(new GridPos(17, 13, 0))?.Def.Id == "ramp_up", "auto-bridge: ramp up before the crossing");
        Check(w.EntityAt(new GridPos(17, 14, 1))?.Def.Id == "conveyor", "auto-bridge: belt one level up over the crossing");
        Check(w.EntityAt(new GridPos(17, 15, 0))?.Def.Id == "ramp_down", "auto-bridge: ramp down after it");
        Check(w.EntityAt(new GridPos(17, 14, 0))?.Facing == Dir.East, "the crossed line is untouched");
        await Key(Godot.Key.Escape);
        await Shot("08-auto-bridge");

        // 10. Build height: E/Q, never below the ground, ramps carry the height.
        await Focus(12, 18);
        await Key(Godot.Key.Key1);
        await Key(Godot.Key.E);
        Check(Tools.Height == 1, $"E raises the build height (got {Tools.Height})");
        await Click(Cell(12, 17));
        Check(w.EntityAt(new GridPos(12, 17, 1))?.Def.Id == "conveyor", "a click at height 1 builds at height 1");
        await Key(Godot.Key.Q);
        await Key(Godot.Key.Q);
        Check(Tools.Height == 0, $"Q stops at the ground (got {Tools.Height})");
        await Key(Godot.Key.Key5);
        await Click(Cell(12, 19));
        Check(w.EntityAt(new GridPos(12, 19, 0))?.Def.Id == "ramp_down" && w.EntityAt(new GridPos(12, 19, 1)) != null,
            "a ramp down placed on the ground stands on it (spans ground and height 1)");
        Check(w.Entities.All(e => e.Pos.Z >= 0), "nothing is below the ground");

        await Focus(5, 11);
        await Key(Godot.Key.Key4);
        while (Tools.Facing != Dir.East) await Key(Godot.Key.R);
        await Click(Cell(3, 11));
        Check(Tools.Height == 1, $"placing a ramp up moves the build height to 1 (got {Tools.Height})");
        await Key(Godot.Key.Key1);
        await Drag(Cell(4, 11), Cell(6, 11));
        Check(w.EntityAt(new GridPos(5, 11, 1))?.Def.Id == "conveyor", "belts are laid at height 1");
        await Key(Godot.Key.Key5);
        while (Tools.Facing != Dir.East) await Key(Godot.Key.R);
        await Click(Cell(7, 11));
        Check(w.EntityAt(new GridPos(7, 11, 0))?.Def.Id == "ramp_down" && Tools.Height == 0, "the ramp down lands on the ground and brings the height back");
        await Key(Godot.Key.Escape);
        await Shot("07-bridge");

        // 11. Per-building upgrades: U on a selection, Shift-click a whole line in the upgrade tool.
        await Focus(4, 4);
        await Click(Cell(3, 3));
        var drill = w.EntityAt(new GridPos(3, 3, 0));
        await Key(Godot.Key.U);
        Check(drill?.Level == 2, $"U upgrades the selected drill (level {drill?.Level})");
        await Frames(10);
        await Shot("06-inspector-upgrades");
        await Key(Godot.Key.Escape);
        await Key(Godot.Key.U);
        Check(Tools.Mode == ToolMode.Upgrade, "U without a selection opens the upgrade tool");
        await Focus(17, 14);
        await KeyDown(Godot.Key.Shift);
        await Click(Cell(15, 14));
        await KeyUp(Godot.Key.Shift);
        var line = Enumerable.Range(14, 7).Select(x => w.EntityAt(new GridPos(x, 14, 0))).ToList();
        Check(line.All(e => e?.Level == 2), $"Shift-click upgrades the whole belt line ({line.Count(e => e?.Level == 2)}/{line.Count})");
        await Key(Godot.Key.Escape);

        // 12. Manage window: choose what a press makes by clicking its tile; Progress and
        //     Statistics can be open at the same time.
        await Focus(12, 8);
        Tools.SelectTool(w.Content.Buildings["press"]);
        await Click(Cell(12, 8));
        await Key(Godot.Key.Escape);
        await Click(Cell(12, 8));
        await Frames(12);
        Check(Hud.Manage.Window.Visible, "selecting a machine opens the Manage window");
        var wire = Hud.Manage.TileFor("draw_wire");
        Check(wire != null, "the press offers copper wire as a choice");
        if (wire != null) await Click(wire.GetGlobalRect().GetCenter());
        var press = w.EntityAt(new GridPos(12, 8, 0));
        Check(press?.Behavior.Selection(press) == "draw_wire", "clicking the tile sets the press to wire");
        await Frames(10);
        await Shot("10-manage-press");
        await Click(Hud.Manage.UpgradeButton.GetGlobalRect().GetCenter());
        Check(press?.Level == 2, $"the Upgrade button raises the level (level {press?.Level})");
        await Key(Godot.Key.P);
        await Key(Godot.Key.I);
        Check(Hud.ProgressWindow.Visible && Hud.StatsWindow.Visible, "Progress and Statistics are open together");
        await Frames(10);
        await Shot("11-windows");
        await Key(Godot.Key.Escape);
        Check(!Hud.StatsWindow.Visible && Hud.ProgressWindow.Visible, "Esc closes the last opened window first");
        await Key(Godot.Key.Escape);
        await Key(Godot.Key.Escape);
        await Frames(12);
        Check(!Hud.Manage.Window.Visible, "Esc then clears the selection and the Manage window closes");

        // 13. Menus, as a normal (non-sandbox) game sees them: locked tiers, limits.
        Host.Sim.World.Sandbox = false;
        await Key(Godot.Key.B);
        await Frames(30);
        if (Hud.BuildMenu is { } menu)
        {
            var over = menu.GetGlobalRect().GetCenter();
            await Move(over);
            float zoom = Camera.Distance;
            foreach (var wheel in new[] { MouseButton.WheelDown, MouseButton.WheelUp })
                for (int i = 0; i < 15; i++)
                {
                    Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = wheel, Pressed = true, Position = over, GlobalPosition = over });
                    Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = wheel, Pressed = false, Position = over, GlobalPosition = over });
                    await Frames(1);
                }
            Check(Mathf.IsEqualApprox(Camera.Distance, zoom), $"scrolling the build menu past its ends never zooms the map ({zoom} -> {Camera.Distance})");
        }
        await Shot("04-build-menu");
        await Key(Godot.Key.Escape);
        await Key(Godot.Key.F1);
        await Shot("05-help");
        await Key(Godot.Key.Escape);
        await Key(Godot.Key.P);
        await Frames(10);
        await Shot("09-progress");
        await Key(Godot.Key.P);

        GD.Print($"UI TEST: {_checks - _failures}/{_checks} checks passed");
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    private void Check(bool ok, string what)
    {
        _checks++;
        if (!ok) _failures++;
        GD.Print($"{(ok ? "  ok  " : "  FAIL")} {what}");
    }

    /// <summary>Screen point of a cell at the build height. Warns when it would land under the HUD.</summary>
    private Vector2 Cell(int x, int y)
    {
        var p = Camera.Camera.UnprojectPosition(new Vector3(x + 0.5f, Tools.Height, y + 0.5f));
        var size = GetViewport().GetVisibleRect().Size;
        if (p.X < 120 || p.X > size.X - 120 || p.Y < 90 || p.Y > size.Y - 260)
            GD.Print($"  warn cell ({x},{y}) is at {p}, near the HUD or off screen");
        return p;
    }

    /// <summary>Centres the camera on a cell (default angle and zoom).</summary>
    private async Task Focus(int x, int y)
    {
        Camera.SetView(40, -52, 20, new Vector3(x + 0.5f, 0, y + 0.5f));
        await Frames(5);
    }

    private async Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async Task Move(Vector2 p)
    {
        Input.ParseInputEvent(new InputEventMouseMotion { Position = p, GlobalPosition = p });
        await Frames(3);
    }

    private async Task Button(MouseButton b, bool pressed, Vector2 p)
    {
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = b, Pressed = pressed, Position = p, GlobalPosition = p });
        await Frames(3);
    }

    private async Task Click(Vector2 p)
    {
        await Move(p);
        await Button(MouseButton.Left, true, p);
        await Button(MouseButton.Left, false, p);
    }

    private async Task Drag(Vector2 from, params Vector2[] through)
    {
        await Move(from);
        await Button(MouseButton.Left, true, from);
        var last = from;
        foreach (var p in through)
        {
            for (int i = 1; i <= 6; i++) await Move(last.Lerp(p, i / 6f));
            last = p;
        }
        await Button(MouseButton.Left, false, last);
    }

    private async Task Key(Key key, bool ctrl = false, bool shift = false)
    {
        foreach (bool pressed in new[] { true, false })
        {
            Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = pressed, CtrlPressed = ctrl, ShiftPressed = shift });
            await Frames(2);
        }
    }

    private async Task KeyDown(Key key)
    {
        Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = true });
        await Frames(2);
    }

    private async Task KeyUp(Key key)
    {
        Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false });
        await Frames(2);
    }

    private async Task Shot(string name)
    {
        if (_shots == null) return;
        await Frames(6);
        var image = GetViewport().GetTexture()?.GetImage();
        image?.SavePng($"{_shots}/{name}.png");
    }
}
