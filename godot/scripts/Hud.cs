using System.Collections.Generic;
using System.Linq;
using Godot;
using FactorySim.Content;

namespace FactorySim.Client;

/// <summary>Screen UI built in code: stats, toolbar, upgrades, game controls, notices.</summary>
public partial class Hud : CanvasLayer
{
    private static readonly int[] Speeds = { 1, 4, 16 };

    private SimHost _host = null!;
    private BuildTool _tool = null!;

    private Label _money = null!, _info = null!, _notice = null!;
    private VBoxContainer _upgradeBox = null!;
    private HBoxContainer _toolbar = null!;
    private Button _speedButton = null!;
    private CheckButton _sandbox = null!;
    private readonly Dictionary<string, Button> _upgradeButtons = new();
    private readonly List<(BuildingDef Def, Button Button)> _toolButtons = new();
    private double _refresh, _noticeTimer;

    public void Init(SimHost host, BuildTool tool)
    {
        _host = host;
        _tool = tool;
        host.Notice += ShowNotice;
        host.WorldReplaced += RebuildContentButtons;
        tool.StateChanged += RefreshToolbar;
    }

    public override void _Ready()
    {
        var root = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(root);

        var margin = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        foreach (var side in new[] { "left", "right", "top", "bottom" }) margin.AddThemeConstantOverride($"margin_{side}", 12);
        root.AddChild(margin);

        var top = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        margin.AddChild(top);

        // Left: stats.
        var stats = Panel(top);
        _money = new Label();
        _money.AddThemeFontSizeOverride("font_size", 26);
        _info = new Label();
        stats.AddChild(_money);
        stats.AddChild(_info);
        stats.AddChild(new Label
        {
            Text = "1-0 select · LMB place/drag · RMB remove · R rotate · Q/E layer · Tab cutaway\n" +
                   "WASD/MMB pan · wheel zoom · Z/C turn view · Esc deselect",
            Modulate = new Color(1, 1, 1, 0.6f),
        });

        // Centre: notices.
        _notice = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        top.AddChild(_notice);

        // Right: upgrades + game controls.
        var right = Panel(top);
        right.AddChild(new Label { Text = "Upgrades" });
        _upgradeBox = new VBoxContainer();
        right.AddChild(_upgradeBox);
        right.AddChild(new HSeparator());
        right.AddChild(new Label { Text = "Game" });
        AddButton(right, "Save", () => _host.Save());
        AddButton(right, "Load", () => { if (!_host.TryLoad()) ShowNotice("No save found."); });
        AddButton(right, "New (empty)", () => _host.NewGame(withDemo: false));
        AddButton(right, "New (demo layout)", () => _host.NewGame(withDemo: true));
        AddButton(right, "Simulate 1 h offline", () => _host.SimulateOffline(3600));
        _speedButton = AddButton(right, "Speed ×1", CycleSpeed);
        _sandbox = new CheckButton { Text = "Sandbox (free)", FocusMode = Control.FocusModeEnum.None };
        _sandbox.Toggled += on => _host.Sim.World.Sandbox = on;
        right.AddChild(_sandbox);

        // Bottom: building toolbar.
        var bottom = new CenterContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        root.AddChild(bottom);
        var toolbarPanel = new PanelContainer();
        bottom.AddChild(toolbarPanel);
        _toolbar = new HBoxContainer();
        toolbarPanel.AddChild(_toolbar);
    }

    private static VBoxContainer Panel(Control parent)
    {
        var panel = new PanelContainer { SizeFlagsVertical = Control.SizeFlags.ShrinkBegin };
        parent.AddChild(panel);
        var box = new VBoxContainer();
        panel.AddChild(box);
        return box;
    }

    private static Button AddButton(Control parent, string text, System.Action onPressed)
    {
        var b = new Button { Text = text, FocusMode = Control.FocusModeEnum.None };
        b.Pressed += onPressed;
        parent.AddChild(b);
        return b;
    }

    private void RebuildContentButtons()
    {
        foreach (var child in _toolbar.GetChildren()) child.QueueFree();
        foreach (var child in _upgradeBox.GetChildren()) child.QueueFree();
        _toolButtons.Clear();
        _upgradeButtons.Clear();

        var list = _host.Content.BuildingList;
        for (int i = 0; i < list.Count; i++)
        {
            var def = list[i];
            string key = i < 9 ? $"{i + 1}" : i == 9 ? "0" : "";
            var b = AddButton(_toolbar, $"{key} {def.Name}\n{def.Cost.Format()}", () => _tool.Select(_tool.Selected == def ? null : def));
            b.ToggleMode = true;
            _toolButtons.Add((def, b));
        }

        foreach (var up in _host.Content.UpgradeList)
            _upgradeButtons[up.Id] = AddButton(_upgradeBox, up.Name, () => _host.Execute(new BuyUpgrade(up.Id)));

        _sandbox.SetPressedNoSignal(_host.Sim.World.Sandbox);
        RefreshToolbar();
    }

    private void RefreshToolbar()
    {
        foreach (var (def, button) in _toolButtons) button.SetPressedNoSignal(_tool.Selected == def);
    }

    private void CycleSpeed()
    {
        int i = System.Array.IndexOf(Speeds, _host.TimeScale);
        _host.TimeScale = Speeds[(i + 1) % Speeds.Length];
        _speedButton.Text = $"Speed ×{_host.TimeScale}";
    }

    private void ShowNotice(string text)
    {
        _notice.Text = text;
        _noticeTimer = 6;
    }

    public override void _Process(double delta)
    {
        if (_host?.Sim == null) return;

        _noticeTimer -= delta;
        if (_noticeTimer <= 0) _notice.Text = "";

        _refresh -= delta;
        if (_refresh > 0) return;
        _refresh = 0.1;

        var world = _host.Sim.World;
        _money.Text = $"$ {world.Money.Format()}";
        string hover = _tool.Hover is { } h && world.EntityAt(h) is { } e
            ? $"\nHover: {e.Def.Name} {e.Facing} — {e.Behavior.GetStatus(e).Detail}"
            : "";
        _info.Text =
            $"Income {world.Stats.IncomePerSecond(10).Format()}/s · lifetime {world.Stats.TotalEarned.Format()}\n" +
            $"Time {world.Tick / Simulation.TicksPerSecond / 60}:{world.Tick / Simulation.TicksPerSecond % 60:00} · " +
            $"{world.EntityCount} buildings\n" +
            $"Layer {_tool.Layer}{(_tool.Cutaway ? " (cutaway)" : "")} · Tool {_tool.Selected?.Name ?? "none"} facing {_tool.Facing}" +
            hover;

        foreach (var up in _host.Content.UpgradeList)
        {
            if (!_upgradeButtons.TryGetValue(up.Id, out var b)) continue;
            int level = world.UpgradeLevel(up.Id);
            bool maxed = up.MaxLevel is int max && level >= max;
            var cost = up.CostForLevel(level);
            b.Text = maxed ? $"{up.Name} Lv {level} (max)" : $"{up.Name} Lv {level} → {cost.Format()}";
            b.Disabled = maxed || (!world.Sandbox && world.Money < cost);
        }
    }
}
