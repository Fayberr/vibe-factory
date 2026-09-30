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

    /// <summary>Centre of the built area, or of the starting plot when the world is empty.</summary>
    internal static Vector3 FocusPoint(World world)
    {
        if (world.EntityCount == 0)
        {
            var start = world.Land.CellsOf(world.Land.Start);
            return new Vector3((start.MinX + start.MaxX + 1) / 2f, 0, (start.MinY + start.MaxY + 1) / 2f);
        }
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
            if (arg == "--research")
            {
                // Tier 1 opened for free, so the research window has something to show.
                host.Sim.World.Sandbox = true;
                host.Execute(new UnlockTier());
                host.Sim.World.Sandbox = false;
                hud.ResearchWindow.Visible = true;
            }
            if (arg.StartsWith("--run-until="))
            {
                // Starts a "Run until" (tier, money or minutes); the smoke's tick count shows it ran fast.
                host.StartFastForward(arg["--run-until=".Length..] switch { "tier" => RunUntil.NextTier, "money" => RunUntil.MoneyDoubled, _ => RunUntil.TenMinutes });
            }
            if (arg == "--alerts")
            {
                // The Alerts window (the demo factory rarely stops; this shows the empty state).
                hud.AlertWindow.Visible = true;
            }
            if (arg == "--targets")
            {
                // The Targets window with three targets on the demo factory: likely met, under, and nothing made.
                foreach (var (item, rate) in new[] { ("iron_ingot", 5.0), ("iron_plate", 600.0), ("steel", 30.0) })
                    if (!host.Execute(new SetTarget(item, rate)).Ok) GD.Print($"TARGETS: could not set {item}");
                hud.TargetsWindow.Visible = true;
                GetTree().CreateTimer(Math.Max(0.5, wait - 0.5)).Timeout += () => GD.Print($"TARGETS: {string.Join(" | ", hud.Targets.RateLines)}");
            }
            if (arg == "--history")
            {
                // The History window (the demo factory fills it after half a minute; use --wait=40).
                hud.HistoryWindow.Visible = true;
            }
            if (arg.StartsWith("--planner"))
            {
                // The Planner window, on an item if given (--planner=steel).
                hud.PlannerWindow.Visible = true;
                if (arg.StartsWith("--planner=")) hud.Planner.Choose(arg["--planner=".Length..]);
            }
            if (arg == "--bottlenecks")
            {
                // The Bottlenecks window with the world pins on (the demo factory has a few waiting machines).
                hud.BottleneckWindow.Visible = true;
                hud.Bottlenecks.OverlayOn = true;
            }
            if (arg.StartsWith("--away="))
            {
                // As if the game had been closed this long: the catch-up and its report.
                double away = double.Parse(arg["--away=".Length..], System.Globalization.CultureInfo.InvariantCulture);
                GetTree().CreateTimer(Math.Max(0.5, wait - 3)).Timeout += () => host.SimulateOffline(away);
            }
            if (arg.StartsWith("--tool=") && host.Content.Buildings.TryGetValue(arg["--tool=".Length..], out var toolDef))
                tools.SelectTool(toolDef); // with the pointer over the world, shows the ghost there
            if (arg.StartsWith("--drag="))
            {
                // --drag=x0,y0,x1,y1: press on one cell and hold the drag over another (box select on screen).
                var c = System.Array.ConvertAll(arg["--drag=".Length..].Split(','), int.Parse);
                GetTree().CreateTimer(Math.Max(0.5, wait - 2)).Timeout += () => HoldDrag(camera, new GridPos(c[0], c[1], 0), new GridPos(c[2], c[3], 0));
            }
            if (arg.StartsWith("--build-search=") && hud.BuildMenuPanel is { } menu)
            {
                // The build menu, searching for the given text (--build-search=plate).
                menu.Root.Visible = true;
                menu.Search.Text = arg["--build-search=".Length..];
                menu.Filter(menu.Search.Text);
            }
            if (arg.StartsWith("--sign="))
            {
                // --sign=x,y,text: a sign on the demo layout's cell (x, y) with that text, built for free and selected.
                var parts = arg["--sign=".Length..].Split(',', 3);
                var at = FactorySim.Samples.DemoLayout.CellOf(host.Sim, int.Parse(parts[0]), int.Parse(parts[1]));
                bool sandbox = host.Sim.World.Sandbox;
                host.Sim.World.Sandbox = true;
                var placed = host.Execute(new PlaceBuilding("sign", at, Dir.South));
                GD.Print($"SIGN: {(placed.Ok ? "placed" : placed.Error)} at {at}");
                host.Execute(new SelectRecipe(at, parts.Length > 2 ? parts[2] : "Sign"));
                host.Sim.World.Sandbox = sandbox;
                if (placed.Ok) tools.Selection.Add(placed.EntityId);
            }
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

    private static async void HoldDrag(CameraRig camera, GridPos from, GridPos to)
    {
        Vector2 At(GridPos c) => camera.Camera.UnprojectPosition(GridMapping.CellFloor(c));
        var tree = (SceneTree)Engine.GetMainLoop();
        var start = At(from);
        Input.ParseInputEvent(new InputEventMouseMotion { Position = start, GlobalPosition = start });
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = start, GlobalPosition = start });
        for (int i = 1; i <= 10; i++)
        {
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            var p = start.Lerp(At(to), i / 10f);
            Input.ParseInputEvent(new InputEventMouseMotion { Position = p, GlobalPosition = p, ButtonMask = MouseButtonMask.Left });
        }
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
