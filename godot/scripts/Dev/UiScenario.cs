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

        // 7. Pipette and rotate-in-place.
        await Key(Godot.Key.Escape);
        await Key(Godot.Key.Escape);
        await Move(Cell(3, 3));
        await Key(Godot.Key.F);
        Check(Tools.Mode == ToolMode.Build && Tools.Tool?.Id == "iron_miner", "F picks the hovered building");
        await Key(Godot.Key.Escape);

        // 8. Bridge flow: ramp up lifts the build layer, belts go on top, ramp down returns to ground.
        await Key(Godot.Key.Key4);
        while (Tools.Facing != Dir.East) await Key(Godot.Key.R);
        await Click(Cell(3, 11));
        Check(Tools.Layer == 1, $"placing a ramp up moves the build layer to +1 (got {Tools.Layer})");
        await Key(Godot.Key.Key1);
        await Drag(Cell(4, 11), Cell(6, 11));
        Check(w.EntityAt(new GridPos(5, 11, 1))?.Def.Id == "conveyor", "belts are laid on layer +1");
        await Key(Godot.Key.Key5);
        while (Tools.Facing != Dir.East) await Key(Godot.Key.R);
        await Click(Cell(7, 11));
        Check(w.EntityAt(new GridPos(7, 11, 0))?.Def.Id == "ramp_down" && Tools.Layer == 0, "ramp down spans 0–1 and returns to the ground");
        await Key(Godot.Key.Escape);
        await Shot("07-bridge");

        // 9. Menus.
        await Key(Godot.Key.B);
        await Frames(30);
        await Shot("04-build-menu");
        await Key(Godot.Key.Escape);
        await Key(Godot.Key.F1);
        await Shot("05-help");
        await Key(Godot.Key.Escape);
        await Click(Cell(3, 3));
        await Key(Godot.Key.U);
        await Frames(10);
        await Shot("06-inspector-upgrades");

        GD.Print($"UI TEST: {_checks - _failures}/{_checks} checks passed");
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    private void Check(bool ok, string what)
    {
        _checks++;
        if (!ok) _failures++;
        GD.Print($"{(ok ? "  ok  " : "  FAIL")} {what}");
    }

    private Vector2 Cell(int x, int y) => Camera.Camera.UnprojectPosition(new Vector3(x + 0.5f, Tools.Layer, y + 0.5f));

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

    private async Task Shot(string name)
    {
        if (_shots == null) return;
        await Frames(6);
        var image = GetViewport().GetTexture()?.GetImage();
        image?.SavePng($"{_shots}/{name}.png");
    }
}
