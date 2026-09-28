using Godot;

namespace FactorySim.Client;

/// <summary>Composition root: builds the scene graph in code and wires the pieces together.</summary>
public partial class Main : Node3D
{
    public override void _Ready()
    {
        var host = new SimHost { Name = "SimHost" };
        var view = new WorldView { Name = "WorldView" };
        var camera = new CameraRig { Name = "CameraRig" };
        var tools = new BuildController { Name = "BuildController" };
        var thumbs = new Thumbnails { Name = "Thumbnails" };
        var hud = new Hud { Name = "Hud" };
        // Wire before entering the tree: _Ready() of each node may already use its collaborators.
        view.Init(host);
        tools.Init(host, camera, view);
        hud.Init(host, tools, thumbs);
        host.WorldReplaced += () => camera.Focus(FocusPoint(host.Sim.World), instant: true);

        AddChild(host);
        AddChild(view);
        AddChild(camera);
        AddChild(tools);
        AddChild(thumbs);
        AddChild(hud); // last: gets unhandled input first (hotbar/menu keys)

        var args = OS.GetCmdlineUserArgs();
        host.Autosave = System.Array.IndexOf(args, "--ui-test") < 0 && System.Array.IndexOf(args, "--smoke") < 0;
        if (System.Array.IndexOf(args, "--ui-test") >= 0)
        {
            AddChild(new UiScenario { Name = "UiScenario", Host = host, Tools = tools, Camera = camera, View = view });
            return;
        }

        bool smoke = System.Array.IndexOf(args, "--smoke") >= 0;
        host.Start(loadSave: !smoke);
        if (smoke) RunSmokeTest(host, camera);
    }

    /// <summary>Centre of the built area, or of the plot's first 16×16 cells when empty.</summary>
    private static Vector3 FocusPoint(World world)
    {
        if (world.EntityCount == 0) return new Vector3(world.Bounds.Min.X + 16, 0, world.Bounds.Min.Y + 16);
        var sum = Vector3.Zero;
        foreach (var e in world.Entities) sum += GridMapping.CellFloor(e.Pos);
        return sum / world.EntityCount;
    }

    /// <summary>`godot --headless -- --smoke`: build the demo, run at 16×, print stats, quit (CI check).</summary>
    private void RunSmokeTest(SimHost host, CameraRig camera)
    {
        host.NewGame(withDemo: true);
        host.TimeScale = 16;
        string? screenshot = null;
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--screenshot=")) screenshot = arg["--screenshot=".Length..];
            if (arg.StartsWith("--view="))
            {
                // --view=yaw,pitch,distance,focusX,focusZ
                var v = System.Array.ConvertAll(arg["--view=".Length..].Split(','), s => float.Parse(s, System.Globalization.CultureInfo.InvariantCulture));
                camera.SetView(v[0], v[1], v[2], new Vector3(v[3], 0, v[4]));
            }
        }

        GetTree().CreateTimer(3.0).Timeout += () =>
        {
            var w = host.Sim.World;
            GD.Print($"SMOKE: {w.EntityCount} buildings, {w.Tick} ticks, money {w.Money.Format()}, " +
                     $"income {w.Stats.IncomePerSecond(10).Format()}/s");
            if (screenshot != null) GetViewport().GetTexture().GetImage().SavePng(screenshot);
            GetTree().Quit(w.Stats.TotalEarned.IsZero ? 1 : 0);
        };
    }
}
