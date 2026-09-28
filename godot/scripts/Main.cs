using Godot;

namespace FactorySim.Client;

/// <summary>Composition root: builds the scene graph in code and wires the pieces together.</summary>
public partial class Main : Node3D
{
    public override void _Ready()
    {
        AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new Color(0.10f, 0.11f, 0.13f),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color(0.62f, 0.66f, 0.75f),
                AmbientLightEnergy = 0.55f,
                TonemapMode = Godot.Environment.ToneMapper.Filmic,
            },
        });
        AddChild(new DirectionalLight3D
        {
            RotationDegrees = new Vector3(-55, -35, 0),
            LightEnergy = 1.1f,
            ShadowEnabled = true,
        });

        var host = new SimHost { Name = "SimHost" };
        var view = new WorldView { Name = "WorldView" };
        var camera = new CameraRig { Name = "CameraRig" };
        var tool = new BuildTool { Name = "BuildTool" };
        var hud = new Hud { Name = "Hud" };
        AddChild(host);
        AddChild(view);
        AddChild(camera);
        AddChild(tool);
        AddChild(hud);

        view.Init(host);
        tool.Init(host, camera, view);
        hud.Init(host, tool);
        host.WorldReplaced += () => camera.Focus(FocusPoint(host.Sim.World));

        host.Start();

        // `godot --headless -- --smoke`: build the demo, run at 16×, print stats, quit (CI check).
        if (System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--smoke") >= 0) RunSmokeTest(host);
    }

    /// <summary>Centre of the built area, or of the plot's first 16×16 cells when empty.</summary>
    private static Vector3 FocusPoint(World world)
    {
        if (world.EntityCount == 0) return new Vector3(world.Bounds.Min.X + 8, 0, world.Bounds.Min.Y + 8);
        var sum = Vector3.Zero;
        foreach (var e in world.Entities) sum += GridMapping.CellFloor(e.Pos);
        return sum / world.EntityCount;
    }

    private void RunSmokeTest(SimHost host)
    {
        host.NewGame(withDemo: true);
        host.TimeScale = 16;
        string? screenshot = null;
        foreach (var arg in OS.GetCmdlineUserArgs())
            if (arg.StartsWith("--screenshot=")) screenshot = arg["--screenshot=".Length..];

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
