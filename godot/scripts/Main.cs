using System;
using System.Collections.Generic;
using Godot;

namespace FactorySim.Client;

/// <summary>Composition root: builds the scene graph in code and wires the pieces together.</summary>
public partial class Main : Node3D
{
    public override void _Ready()
    {
        // The scripted UI test runs on default settings (keys, camera), whatever the player chose.
        bool uiTest = System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--ui-test") >= 0;
        var settings = uiTest ? new GameSettings() : GameSettings.Load();
        Keybinds.Load(settings.Keys);
        var host = new SimHost { Name = "SimHost" };
        var view = new WorldView { Name = "WorldView" };
        var camera = new CameraRig { Name = "CameraRig" };
        var tools = new BuildController { Name = "BuildController" };
        var thumbs = new Thumbnails { Name = "Thumbnails" };
        var hud = new Hud { Name = "Hud", Settings = settings };
        var audio = new AudioManager { Name = "Audio", ProcessMode = ProcessModeEnum.Always };
        var menus = new MenuLayer { Name = "Menus" };
        var flow = new GameFlow { Name = "GameFlow", Host = host, Hud = hud, Tools = tools, Camera = camera, View = view, Menus = menus, Audio = audio, Settings = settings };
        // Wire before entering the tree: _Ready() of each node may already use its collaborators.
        view.Init(host);
        tools.Init(host, camera, view);
        hud.Init(host, tools, thumbs);
        menus.Init(flow, settings);
        host.WorldReplaced += () => camera.Focus(FocusPoint(host.Sim.World), instant: true);

        AddChild(host);
        AddChild(view);
        AddChild(camera);
        AddChild(tools);
        AddChild(thumbs);
        AddChild(audio);
        AddChild(flow);
        AddChild(menus);
        AddChild(hud); // last: gets unhandled input first (hotbar/menu keys)
        audio.Hook(host);
        flow.Apply();

        var args = OS.GetCmdlineUserArgs();
        host.Autosave = System.Array.IndexOf(args, "--ui-test") < 0 && System.Array.IndexOf(args, "--smoke") < 0;
        hud.AutoStartTutorial = host.Autosave; // scripted runs never touch the player's settings
        flow.PersistSettings = host.Autosave;
        if (System.Array.IndexOf(args, "--ui-test") >= 0)
        {
            settings.TutorialDone = true; // in memory only: the scripted run opens the tutorial itself
            AddChild(new UiScenario { Name = "UiScenario", Host = host, Tools = tools, Camera = camera, View = view, Hud = hud, Flow = flow, Menus = menus });
            return;
        }

        host.Init();
        if (System.Array.IndexOf(args, "--smoke") >= 0) RunSmokeTest(host, camera, tools, hud, flow, audio, menus);
        else flow.ShowTitle();
    }

    /// <summary>Centre of the built area, or of the plot's first 16×16 cells when empty.</summary>
    internal static Vector3 FocusPoint(World world)
    {
        if (world.EntityCount == 0) return new Vector3(world.Bounds.Min.X + 16, 0, world.Bounds.Min.Y + 16);
        var sum = Vector3.Zero;
        foreach (var e in world.Entities) sum += GridMapping.CellFloor(e.Pos);
        return sum / world.EntityCount;
    }

    /// <summary>
    /// `godot --headless -- --smoke`: build the demo, run at 16×, print stats, quit (CI check).
    /// With `--showcase` it lays out every building and item instead (for checking the models).
    /// </summary>
    private void RunSmokeTest(SimHost host, CameraRig camera, BuildController tools, Hud hud, GameFlow flow, AudioManager audio, MenuLayer menus)
    {
        if (System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--title") >= 0)
        {
            // The title screen over its live backdrop (for screenshots).
            flow.ShowTitle();
            foreach (var arg in OS.GetCmdlineUserArgs())
                if (arg.StartsWith("--menu=")) menus.OpenWindow(arg["--menu=".Length..]);
            ScreenshotAndQuit(host, 4.0, requireEarnings: false);
            return;
        }
        audio.PlayGameMusic();
        bool showcase = System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--showcase") >= 0;
        bool tutorial = System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--tutorial") >= 0;
        host.NewGame(withDemo: !showcase && !tutorial);
        if (showcase) BuildShowcase(host);
        if (tutorial)
        {
            // An empty factory at the tutorial's first building step.
            hud.StartTutorial();
            hud.Tutorial.Next();
        }
        host.TimeScale = 16;
        double wait = 3.0;
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--wait=")) wait = double.Parse(arg["--wait=".Length..], System.Globalization.CultureInfo.InvariantCulture);
            if (arg == "--pause") GetTree().CreateTimer(Math.Max(0.5, wait - 1)).Timeout += flow.Pause;
            if (arg == "--windows")
            {
                hud.ProgressWindow.Visible = true;
                hud.StatsWindow.Visible = true;
            }
            if (arg == "--orders")
            {
                hud.ProgressWindow.Visible = true;
                hud.OrdersWindow.Visible = true;
            }
            if (arg.StartsWith("--tool=") && host.Content.Buildings.TryGetValue(arg["--tool=".Length..], out var toolDef))
                tools.SelectTool(toolDef); // with the pointer over the world, shows the ghost there
            if (arg.StartsWith("--select="))
            {
                // --select=x,y: open the Manage window for the building on that cell.
                var xy = System.Array.ConvertAll(arg["--select=".Length..].Split(','), int.Parse);
                if (host.Sim.World.EntityAt(new GridPos(xy[0], xy[1], 0)) is { } picked) tools.Selection.Add(picked.Id);
            }
            if (arg.StartsWith("--view="))
            {
                // --view=yaw,pitch,distance,focusX,focusZ
                var v = System.Array.ConvertAll(arg["--view=".Length..].Split(','), s => float.Parse(s, System.Globalization.CultureInfo.InvariantCulture));
                camera.SetView(v[0], v[1], v[2], new Vector3(v[3], 0, v[4]));
            }
        }

        ScreenshotAndQuit(host, wait, requireEarnings: !showcase && !tutorial);
    }

    /// <summary>After <paramref name="wait"/> s: print stats, save --screenshot=path if given, quit (1 if nothing was earned).</summary>
    private void ScreenshotAndQuit(SimHost host, double wait, bool requireEarnings)
    {
        string? screenshot = null;
        foreach (var arg in OS.GetCmdlineUserArgs())
            if (arg.StartsWith("--screenshot=")) screenshot = arg["--screenshot=".Length..];
        GetTree().CreateTimer(wait, processAlways: true).Timeout += () =>
        {
            var w = host.Sim.World;
            GD.Print($"SMOKE: {w.EntityCount} buildings, {w.Tick} ticks, money {w.Money.Format()}, " +
                     $"income {w.Stats.IncomePerSecond(10).Format()}/s");
            if (screenshot != null) GetViewport().GetTexture().GetImage().SavePng(screenshot);
            GetTree().Quit(requireEarnings && w.Stats.TotalEarned.IsZero ? 1 : 0);
        };
    }

    /// <summary>Every building in rows (a few upgraded, to show level trims) and every item shape on a shelf.</summary>
    private void BuildShowcase(SimHost host)
    {
        var sim = host.Sim;
        sim.World.Sandbox = true;
        int i = 0;
        foreach (var def in host.Content.BuildingList)
        {
            var pos = new GridPos(2 + (i % 8) * 2, 2 + (i / 8) * 3, 0);
            if (sim.Execute(new PlaceBuilding(def.Id, pos, Dir.South)).Ok && i % 3 == 1)
                sim.Execute(new SetBuildingLevels(new[] { new LevelChange(pos, 2 + i % 9) }));
            i++;
        }
        int k = 0;
        foreach (var item in host.Content.Items.Values)
        {
            var color = Palette.Parse(item.Meta.GetValueOrDefault("color"), Colors.Magenta);
            string shape = item.Meta.GetValueOrDefault("shape") ?? "box";
            AddChild(new MeshInstance3D
            {
                Mesh = ItemMeshes.Get(shape),
                MaterialOverride = new StandardMaterial3D { AlbedoColor = color, Roughness = 0.6f },
                Position = new Vector3(2.5f + (k % 11) * 0.8f, 0.12f + ItemMeshes.Lift(shape), 16.5f + (k / 11) * 0.8f),
                Scale = Vector3.One * 1.6f,
            });
            k++;
        }
    }
}
