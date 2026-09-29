using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;
using FactorySim.Content;

namespace FactorySim.Client;

/// <summary>
/// Screen UI: tool bar (top), sidebar (left), factory card with the next goal (bottom-left),
/// build-height ladder, hotbar and key hints (bottom), build menu, help overlay, toasts, a
/// tooltip at the cursor saying what a click will do, and windows: Manage (the selection)
/// plus Progress, Statistics and Game, which can all be open at once and dragged around.
/// Owns hotbar/menu/window hotkeys; everything else goes to the BuildController.
/// </summary>
public partial class Hud : CanvasLayer
{
    private const string HotbarPath = "user://hotbar.json";
    private static readonly string[] DefaultHotbar =
        { "conveyor", "splitter", "merger", "ramp_up", "ramp_down", "iron_miner", "smelter", "polisher", "seller", "copper_miner" };
    private static readonly int[] Speeds = { 1, 4, 16 };

    private SimHost _host = null!;
    private BuildController _tools = null!;
    private Thumbnails _thumbs = null!;

    private Control _root = null!;
    private readonly Dictionary<ToolMode, Button> _toolButtons = new();
    private Button _buildButton = null!, _undo = null!, _redo = null!, _hideAbove = null!;
    private Label _money = null!, _income = null!, _goal = null!;
    private ProgressBar _goalBar = null!;
    private readonly List<Button> _heightButtons = new();
    private RichTextLabel _hints = null!;
    private Control _hintPanel = null!;
    private GridContainer _helpGrid = null!;
    private Label _pausedText = null!, _fps = null!, _version = null!;
    private readonly List<(Control Control, Func<string> Tip)> _keyTips = new();
    private Control _card = null!, _bottomRow = null!;
    private PanelContainer _cursorTip = null!;
    private Label _cursorText = null!;
    private readonly List<BuildingTile> _slots = new();
    private readonly string?[] _hotbar = new string?[10];
    private BuildMenu? _menu;
    private ManageWindow _manage = null!;
    private ProgressPanel _progress = null!;
    private StatsPanel _stats = null!;
    private HudWindow _buyWindow = null!;
    private Label _buyText = null!;
    private PlotId _buyPlot;
    private HudWindow _progressWindow = null!, _statsWindow = null!, _gameWindow = null!, _ordersWindow = null!, _researchWindow = null!, _awayWindow = null!;
    private OrdersPanel _orders = null!;
    private ResearchPanel _research = null!;
    private AwayReportPanel _away = null!;
    private Button _researchButton = null!;
    private readonly List<HudWindow> _openWindows = new(); // most recently opened last
    private TutorialPanel _tutorial = null!;
    private Button _progressButton = null!;
    private Control _ladder = null!;
    private PanelContainer _pausedBadge = null!;
    private Control _windows = null!;
    private PanelContainer _help = null!;
    private Toasts _toasts = null!;
    private Button _speedButton = null!;
    private CheckButton _sandbox = null!;
    private double _refresh;

    public ManageWindow Manage => _manage;
    public TutorialPanel Tutorial => _tutorial;
    public Control? BuildMenu => _menu?.Root;

    /// <summary>The build stamp in the corner of the screen. For scripted checks.</summary>
    public Label Version => _version;

    /// <summary>Remember finishing the tutorial (off for scripted runs, which must not touch settings).</summary>
    public bool AutoStartTutorial { get; set; } = true;

    public GameSettings Settings { get; set; } = new();

    /// <summary>Hooks into the surrounding game (menus); unset in scripted runs.</summary>
    public Action? OpenSettings { get; set; }
    public Action? QuitToMenu { get; set; }
    public HudWindow ProgressWindow => _progressWindow;
    public HudWindow StatsWindow => _statsWindow;
    public HudWindow OrdersWindow => _ordersWindow;
    public HudWindow ResearchWindow => _researchWindow;

    public void Init(SimHost host, BuildController tools, Thumbnails thumbs)
    {
        _host = host;
        _tools = tools;
        _thumbs = thumbs;
        host.Notice += text => _toasts.Show(this, text);
        host.WorldReplaced += OnWorldReplaced;
        host.CameBack += OnCameBack;
        host.EventRaised += ev =>
        {
            if (ev is EntityPlaced or EntityRemoved) _slotsDirty = true; // build limits changed
            string Item(string id) => host.Content.Items[id].Name;
            switch (ev)
            {
                case ContractOffered o when !_ordersWindow.Visible:
                    if (Settings.ShowNotifications) _toasts.Show(this, $"New order: {o.Contract.Quantity} {Item(o.Contract.Item)} for +${o.Contract.Reward.Format()} ({K("orders")})");
                    return;
                case ContractCompleted c when Settings.ShowNotifications:
                    _toasts.Show(this, $"Order complete: {c.Contract.Quantity} {Item(c.Contract.Item)}, +${c.Contract.Reward.Format()}");
                    return;
                case ContractExpired x when Settings.ShowNotifications:
                    _toasts.Show(this, $"Order for {Item(x.Contract.Item)} ran out of time");
                    return;
                case MilestoneReached m when Settings.ShowNotifications:
                    _toasts.Show(this, $"Goal reached: {m.Name}, +${m.Reward.Format()}");
                    return;
                case UpgradePurchased u when host.Content.Upgrades.TryGetValue(u.UpgradeId, out var upgrade):
                    _toasts.Show(this, $"Researched {upgrade.Name}, level {u.Level}");
                    return;
            }
            if (ev is PlotBought) return; // the tool says so itself; a bought plot is not news to whoever clicked
            if (ev is not TierUnlocked t) return;
            bool research = host.Content.UpgradeList.Any(u => u.Tier == t.Tier);
            _toasts.Show(this, $"Tier {t.Tier} unlocked: {t.Name}! New buildings are in the build menu." +
                               (research ? $" Research opens too ({K("research")})." : ""));
            RefreshHotbar();
        };
        tools.Changed += () =>
        {
            _slotsDirty = true;
            _refresh = 0; // selection changes show in the Manage window right away
            RefreshToolState();
            if (_buyWindow != null && _tools.Mode is not (ToolMode.Build or ToolMode.Move or ToolMode.Paste)) _buyWindow.Visible = false;
        };
        thumbs.Updated += _ => RefreshHotbar();
    }

    public override void _Ready()
    {
        _root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Theme = UiTheme.Create() };
        _root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_root);

        BuildTopBar();
        BuildSideBar();
        BuildFactoryCard();
        BuildBottom();
        BuildWindows();
        BuildHelp();

        _fps = Ui.Label("", 12, UiTheme.Muted);
        _fps.MouseFilter = Control.MouseFilterEnum.Ignore;
        _root.AddChild(Ui.Anchor(_fps, 1, 0, -12, 8, Control.GrowDirection.Begin, Control.GrowDirection.End));

        // The version, always on screen and out of the way: bottom right, a size above the other small
        // print and a step brighter than it used to be, because a bug report starts with it and it was
        // hard to read over the factory. Hovering it names the commit the build came from.
        _version = Ui.VersionStamp();
        _root.AddChild(Ui.Anchor(_version, 1, 1, -12, -10, Control.GrowDirection.Begin, Control.GrowDirection.Begin));

        _toasts = new Toasts();
        _root.AddChild(Ui.Anchor(_toasts.Root, 0.5f, 0, 0, 72, Control.GrowDirection.Both, Control.GrowDirection.End));

        var paused = _pausedText = Ui.Label("", 15);
        paused.AddThemeFontOverride("font", UiTheme.Bold);
        _pausedBadge = Ui.Panel(paused);
        _pausedBadge.AddThemeStyleboxOverride("panel", UiTheme.Box(new Color(UiTheme.Primary, 0.9f), 8, 14, 6));
        _pausedBadge.Visible = false;
        _pausedBadge.MouseFilter = Control.MouseFilterEnum.Ignore;
        _root.AddChild(Ui.Anchor(_pausedBadge, 0.5f, 0, 0, 72, Control.GrowDirection.Both, Control.GrowDirection.End));

        _cursorText = Ui.Label("", 13);
        _cursorTip = Ui.Panel(_cursorText);
        _cursorTip.AddThemeStyleboxOverride("panel", UiTheme.Box(new Color(0.05f, 0.06f, 0.08f, 0.88f), 7, 9, 5));
        _cursorTip.MouseFilter = Control.MouseFilterEnum.Ignore;
        _cursorTip.Visible = false;
        _root.AddChild(_cursorTip);

        Keybinds.Changed += RefreshKeyLabels;
        RefreshKeyLabels();
    }

    public override void _ExitTree() => Keybinds.Changed -= RefreshKeyLabels;

    // ---- Layout ---------------------------------------------------------------

    private void BuildTopBar()
    {
        var bar = new HBoxContainer();
        void Tool(ToolMode mode, Icon icon, string tip, Action action)
        {
            var b = Ui.IconButton(icon, tip, action, toggle: true);
            _toolButtons[mode] = b;
            bar.AddChild(b);
        }
        Tool(ToolMode.Select, Icon.Select, "", () => _tools.SetMode(ToolMode.Select));
        Keyed(_toolButtons[ToolMode.Select], () => $"Select ({K("select_tool")})\nClick, Shift-click or drag a box");
        _buildButton = Ui.IconButton(Icon.Build, "", ToggleBuildMenu, toggle: true);
        Keyed(_buildButton, () => $"Build menu ({K("build_menu")})");
        bar.AddChild(_buildButton);
        Tool(ToolMode.Upgrade, Icon.Upgrades, "", () => _tools.SetMode(_tools.Mode == ToolMode.Upgrade ? ToolMode.Select : ToolMode.Upgrade));
        Keyed(_toolButtons[ToolMode.Upgrade], () => $"Upgrade ({K("upgrade")})\nClick a building, drag a box, Shift-click a whole belt line.\nWith a selection, {K("upgrade")} upgrades it.");
        Tool(ToolMode.Delete, Icon.Delete, "", () => _tools.SetMode(_tools.Mode == ToolMode.Delete ? ToolMode.Select : ToolMode.Delete));
        Keyed(_toolButtons[ToolMode.Delete], () => $"Delete ({K("delete_tool")})\nClick or drag a box");
        Tool(ToolMode.Move, Icon.Move, "", () => _tools.BeginMove());
        Keyed(_toolButtons[ToolMode.Move], () => $"Move selection ({K("move")})");
        Tool(ToolMode.Paste, Icon.Copy, "", () => _tools.CopySelection(enterPaste: true));
        Keyed(_toolButtons[ToolMode.Paste], () => $"Copy selection & paste ({K("copy")})\nCtrl+C / Ctrl+V / Ctrl+X");
        bar.AddChild(new VSeparator());
        _undo = Ui.IconButton(Icon.Undo, "Undo (Ctrl+Z)", () => _tools.Undo());
        _redo = Ui.IconButton(Icon.Redo, "Redo (Ctrl+Y)", () => _tools.Redo());
        bar.AddChild(_undo);
        bar.AddChild(_redo);
        _root.AddChild(Ui.Anchor(Ui.Panel(bar), 0.5f, 0, 0, 12, Control.GrowDirection.Both, Control.GrowDirection.End));
    }

    private void BuildSideBar()
    {
        var col = new VBoxContainer();
        col.AddChild(Keyed(Ui.IconButton(Icon.Build, "", ToggleBuildMenu, 44), () => $"Build menu ({K("build_menu")})"));
        _progressButton = Keyed(Ui.IconButton(Icon.Progress, "", () => ToggleWindow(_progressWindow), 44), () => $"Progress: tiers, build limits and goals ({K("progress")})");
        col.AddChild(_progressButton);
        col.AddChild(Keyed(Ui.IconButton(Icon.Orders, "", () => ToggleWindow(_ordersWindow), 44), () => $"Orders: deliveries for bonus cash ({K("orders")})"));
        // Hidden until research opens (see ResearchPanel.IsOpen), and for good if the content has none.
        _researchButton = Keyed(Ui.IconButton(Icon.Research, "", () => ToggleWindow(_researchWindow), 44), () => $"Research: permanent bonuses paid in science packs ({K("research")})");
        _researchButton.Visible = false;
        col.AddChild(_researchButton);
        col.AddChild(Keyed(Ui.IconButton(Icon.Stats, "", () => ToggleWindow(_statsWindow), 44), () => $"Statistics ({K("stats")})"));
        col.AddChild(Keyed(Ui.IconButton(Icon.Game, "", () => ToggleWindow(_gameWindow), 44), () => $"Game: save, speed, settings ({K("game_menu")})"));
        col.AddChild(Keyed(Ui.IconButton(Icon.Help, "", () => _help.Visible = !_help.Visible, 44), () => $"Controls ({K("help")})"));
        _root.AddChild(Ui.Anchor(Ui.Panel(col), 0, 0.5f, 12, 0, Control.GrowDirection.End, Control.GrowDirection.Both));
    }

    private void BuildFactoryCard()
    {
        var body = new VBoxContainer();
        body.AddChild(Ui.Label("MY FACTORY", 12, UiTheme.Muted));
        _money = Ui.Label("$ 0", 30);
        body.AddChild(_money);
        _income = Ui.Label("+$0/s", 15, UiTheme.Money);
        body.AddChild(_income);
        body.AddChild(new HSeparator());
        _goal = Ui.Label("", 12, UiTheme.Muted);
        _goal.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _goal.CustomMinimumSize = new Vector2(220, 0);
        body.AddChild(_goal);
        _goalBar = new ProgressBar { CustomMinimumSize = new Vector2(0, 6), ShowPercentage = false, MaxValue = 1 };
        body.AddChild(_goalBar);
        var panel = Ui.Panel(body);
        panel.CustomMinimumSize = new Vector2(240, 0);
        _card = panel;
        _root.AddChild(Ui.Anchor(panel, 0, 1, 12, -12, Control.GrowDirection.End, Control.GrowDirection.Begin));
    }

    private void BuildBottom()
    {
        // [height ladder] [key hints over the hotbar]: the hints sit directly on the hotbar.
        var col = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Alignment = BoxContainer.AlignmentMode.End, SizeFlagsVertical = Control.SizeFlags.ShrinkEnd };
        col.AddThemeConstantOverride("separation", 6);
        _hints = new RichTextLabel
        {
            BbcodeEnabled = true,
            FitContent = true,
            AutowrapMode = TextServer.AutowrapMode.Off,
            ScrollActive = false,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _hints.AddThemeFontSizeOverride("normal_font_size", 13);
        var hintPanel = Ui.Panel(_hints);
        hintPanel.AddThemeStyleboxOverride("panel", UiTheme.Box(new Color(0.05f, 0.06f, 0.08f, 0.6f), 8, 12, 4));
        hintPanel.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
        hintPanel.MouseFilter = Control.MouseFilterEnum.Ignore;
        _hintPanel = hintPanel;
        col.AddChild(hintPanel);

        var bar = new HBoxContainer();
        for (int i = 0; i < 10; i++)
        {
            int slot = i;
            var tile = new BuildingTile(new Vector2(78, 84), showName: false);
            tile.Button.Pressed += () => PickSlot(slot);
            _slots.Add(tile);
            bar.AddChild(tile.Button);
        }
        var panel = Ui.Panel(bar);
        col.AddChild(panel);

        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 8);
        _ladder = BuildHeightLadder();
        row.AddChild(_ladder);
        row.AddChild(col);
        _bottomRow = row;
        _root.AddChild(Ui.Anchor(row, 0.5f, 1, 0, -12, Control.GrowDirection.Both, Control.GrowDirection.Begin));
    }

    /// <summary>
    /// Elevator-style build height selector: G (ground) at the bottom, levels above it. The
    /// highlighted floor is where the next building goes; the eye hides everything above it.
    /// </summary>
    private Control BuildHeightLadder()
    {
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 3);
        var title = Ui.Label("HEIGHT", 10, UiTheme.Muted);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        col.AddChild(title);
        for (int h = 4; h >= 0; h--)
        {
            int level = h;
            var b = new Button
            {
                Text = h == 0 ? "G" : h.ToString(),
                ToggleMode = true,
                FocusMode = Control.FocusModeEnum.None,
                CustomMinimumSize = new Vector2(40, 22),
                TooltipText = h == 0
                    ? "Ground: the lowest level (Q · PageDown · Shift+wheel)"
                    : $"Build at height {h} (E/Q · PageUp/PageDown · Shift+wheel).\nBelts up here are bridges; drag a belt across another line and it bridges by itself.",
            };
            b.AddThemeFontSizeOverride("font_size", 12);
            b.Pressed += () => _tools.SetHeight(level);
            _heightButtons.Add(b);
            col.AddChild(b);
        }
        _hideAbove = Ui.IconButton(Icon.Eye, "Hide everything above the build height (Tab)", () => _tools.ToggleHideAbove(), 30, toggle: true);
        _hideAbove.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
        col.AddChild(_hideAbove);
        var panel = Ui.Panel(col);
        panel.SizeFlagsVertical = Control.SizeFlags.ShrinkEnd;
        return panel;
    }

    private void BuildWindows()
    {
        _windows = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        _windows.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(_windows);

        _manage = new ManageWindow(
            upgrade: () => _tools.UpgradeEntities(_tools.SelectedEntities().ToList()),
            delete: _tools.DeleteSelection,
            choose: recipe => _tools.ChooseRecipe(recipe),
            filter: (output, item) => _tools.ChooseFilter(output, item),
            rotate: () => _tools.Rotate(1),
            move: () => _tools.BeginMove(),
            copy: () => _tools.CopySelection(enterPaste: true),
            close: _tools.ClearSelection);
        AddWindow(_manage.Window);

        _progress = new ProgressPanel(() => _host.Execute(new UnlockTier()));
        // Info windows stay open through Esc (it goes to the pause menu); P, O and I toggle them.
        _progressWindow = new HudWindow("Progress", Icon.Progress, 340) { EscCloses = false };
        _progressWindow.Body.AddChild(_progress.Root);
        AddWindow(_progressWindow);

        _orders = new OrdersPanel(id => _host.Execute(new RerollContract(id)));
        _ordersWindow = new HudWindow("Orders", Icon.Orders, 420) { EscCloses = false };
        _ordersWindow.Body.AddChild(_orders.Root);
        AddWindow(_ordersWindow);

        _research = new ResearchPanel(id => _host.Execute(new BuyUpgrade(id)));
        _researchWindow = new HudWindow("Research", Icon.Research, 420) { EscCloses = false };
        _researchWindow.Body.AddChild(_research.Root);
        AddWindow(_researchWindow);

        _away = new AwayReportPanel(id => _tools.ShowEntity(id));
        _awayWindow = new HudWindow("While you were away", Icon.Clock, 440);
        _awayWindow.Body.AddChild(_away.Root);
        AddWindow(_awayWindow);

        _stats = new StatsPanel();
        _statsWindow = new HudWindow("Statistics", Icon.Stats, 300) { EscCloses = false };
        _statsWindow.Body.AddChild(_stats.Root);
        AddWindow(_statsWindow);

        var game = new VBoxContainer();
        game.AddChild(Ui.TextButton("Save", () => _host.Save()));
        game.AddChild(Keyed(Ui.TextButton("Pause / resume", TogglePause), () => $"Pauses the factory; you can keep building ({K("pause")})"));
        _speedButton = Ui.TextButton("Speed ×1", CycleSpeed, "Simulation speed");
        game.AddChild(_speedButton);
        _sandbox = new CheckButton { Text = "Sandbox (free building)", FocusMode = Control.FocusModeEnum.None };
        _sandbox.Toggled += on => _host.Sim.World.Sandbox = on;
        game.AddChild(_sandbox);
        game.AddChild(Ui.TextButton("Simulate 1 h offline", () => _host.SimulateOffline(3600), "Test the offline catch-up"));
        game.AddChild(Ui.TextButton("Start the tutorial", StartTutorial, "A short guided first factory"));
        game.AddChild(Ui.TextButton("Settings", () => { _gameWindow.Visible = false; OpenSettings?.Invoke(); }));
        game.AddChild(Ui.TextButton("Save and quit to menu", () => QuitToMenu?.Invoke()));
        _gameWindow = new HudWindow("Vibe Factory", Icon.Game, 280);
        _gameWindow.Body.AddChild(Ui.Pad(game, 14, 12));
        AddWindow(_gameWindow);

        _tutorial = new TutorialPanel(FocusTarget);
        _tutorial.Ended += () =>
        {
            if (!AutoStartTutorial) return;
            Settings.TutorialDone = true;
            Settings.Save();
        };
        AddWindow(_tutorial.Window);
        _root.AddChild(_tutorial.Highlight);

        BuildBuyWindow();
    }

    /// <summary>"Buy this plot for $X?": opened by a click on a buy tag while placing.</summary>
    private void BuildBuyWindow()
    {
        _buyWindow = new HudWindow("Buy land", Icon.Coin, 340);
        _buyText = Ui.Label("", 17);
        _buyText.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _buyText.CustomMinimumSize = new Vector2(300, 0);
        _buyWindow.Body.AddChild(Ui.Pad(_buyText, 16, 14));
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 0);
        var cancel = new Button { ThemeTypeVariation = "FlatButton", Text = "Cancel", FocusMode = Control.FocusModeEnum.None, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 46) };
        cancel.Pressed += _buyWindow.Close;
        row.AddChild(cancel);
        var buy = new Button { ThemeTypeVariation = "PrimaryButton", Text = "Buy", FocusMode = Control.FocusModeEnum.None, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 46) };
        buy.Pressed += ConfirmBuy;
        row.AddChild(buy);
        _buyWindow.Body.AddChild(row);
        AddWindow(_buyWindow);
        _tools.BuyPlotRequested += AskToBuy;
    }

    private void AskToBuy(PlotId plot)
    {
        var world = _host.Sim.World;
        _buyPlot = plot;
        _buyText.Text = $"Buy this plot for ${world.Land.PriceOf(plot).Format()}?\nYou have ${world.Money.Format()}.";
        _buyWindow.Placed = true; // centred here, not by PlaceWindow
        _buyWindow.Visible = true;
        _buyWindow.Fit();
        var screen = _root.GetViewportRect().Size;
        _buyWindow.Root.Position = new Vector2(Mathf.Round((screen.X - _buyWindow.Root.Size.X) / 2), Mathf.Round((screen.Y - _buyWindow.Root.Size.Y) / 2 - 60));
        _buyWindow.Fit();
    }

    private void ConfirmBuy()
    {
        _buyWindow.Close();
        var price = _host.Sim.World.Land.PriceOf(_buyPlot);
        if (_host.Execute(new BuyPlot(_buyPlot.Column, _buyPlot.Row)).Ok) _host.Notify($"Bought the plot for ${price.Format()}");
    }

    public void StartTutorial()
    {
        _gameWindow.Visible = false;
        _tutorial.Start();
    }

    /// <summary>The control a tutorial step points at.</summary>
    private Control? FocusTarget(string focus)
    {
        if (focus.StartsWith("slot:"))
        {
            string id = focus["slot:".Length..];
            int i = Array.IndexOf(_hotbar, id);
            return i >= 0 ? _slots[i].Button : _buildButton;
        }
        return focus switch
        {
            "upgrade" => _manage.Window.Visible ? _manage.UpgradeButton : _toolButtons[ToolMode.Upgrade],
            "progress" => _progressButton,
            "height" => _ladder,
            _ => null,
        };
    }


    private void AddWindow(HudWindow w)
    {
        _windows.AddChild(w.Root);
        w.Activated += () =>
        {
            _openWindows.Remove(w);
            if (w.Visible)
            {
                _openWindows.Add(w);
                PlaceWindow(w);
            }
            _refresh = 0;
        };
    }

    /// <summary>First time a window opens: Manage on the right; Progress, Statistics and Orders side by side next to the sidebar.</summary>
    private void PlaceWindow(HudWindow w)
    {
        if (w.Placed) return;
        w.Placed = true;
        var screen = _root.GetViewportRect().Size;
        w.Fit();
        w.Root.Position = w == _manage.Window
            ? new Vector2(screen.X - w.Root.CustomMinimumSize.X - 12, 70)
            : w == _tutorial.Window ? new Vector2(screen.X - w.Root.Size.X - 12, screen.Y - w.Root.Size.Y - 128)
            : w == _progressWindow ? new Vector2(84, 70)
            : w == _statsWindow ? new Vector2(84 + 350, 70)
            : w == _ordersWindow ? new Vector2(84 + 350 + 320, 70)
            : w == _researchWindow ? new Vector2(84 + 350, 70)
            : w == _awayWindow ? new Vector2(84, 70) // clear of the toasts at the top centre
            : new Vector2(84 + 350 + 310, 70);
        w.Fit();
    }

    private void ToggleWindow(HudWindow w)
    {
        w.Toggle();
        _refresh = 0;
    }

    private void BuildHelp()
    {
        var body = new VBoxContainer();
        var head = new HBoxContainer();
        head.AddChild(Ui.Label("Controls", 22));
        head.AddChild(Ui.Spacer());
        head.AddChild(Ui.IconButton(Icon.Close, "Close (Esc)", () => _help.Visible = false, 32));
        body.AddChild(head);
        _helpGrid = new GridContainer { Columns = 4 };
        body.AddChild(_helpGrid);
        FillHelp();
        _help = Ui.Panel(body);
        _help.Visible = false;
        _root.AddChild(Ui.Anchor(_help, 0.5f, 0.5f, 0, 0, Control.GrowDirection.Both, Control.GrowDirection.Both));
    }

    private void FillHelp()
    {
        foreach (var child in _helpGrid.GetChildren()) child.QueueFree();
        (string, string)[] keys =
        {
            ("1 to 0", "Hotbar building"), (K("build_menu"), "Build menu"),
            ("LMB", "Place / select"), ("Drag", "Belt finds its way (around, over, into)"),
            ("Shift+Drag", "Draw the belt's path yourself"), ($"{K("rotate")} / Shift+{K("rotate")}", "Rotate"),
            ("Drop on belt", "Replaces it (polisher, splitter…)"), ($"{K("height_up")} / {K("height_down")}", "Build height up / down"),
            ("Shift+wheel", "Build height"), (K("hide_above"), "Hide above build height"),
            (K("pick"), "Pick hovered building"), (K("upgrade"), "Upgrade tool / selection"),
            ($"Shift+LMB ({K("upgrade")})", "Upgrade whole belt line"), (K("delete_tool"), "Delete tool"),
            ("Click a plot next to yours", "Buy it (it asks first)"), ("Del", "Delete selection"), (K("move"), "Move selection"),
            (K("copy"), "Copy & paste selection"), ("Ctrl+C / V / X", "Copy / paste / cut"),
            ("Ctrl+Z / Y", "Undo / redo"), ("Ctrl+A", "Select all"),
            ("Esc / RMB click", "Cancel tool"), ($"{K("pan_forward")}{K("pan_left")}{K("pan_back")}{K("pan_right")}, arrows", "Pan (Shift = fast)"),
            ("RMB drag", "Orbit camera"), ("MMB drag", "Pan"),
            ("Wheel", "Zoom to cursor"), (K("progress"), "Progress: tiers, limits, goals"),
            (K("orders"), "Orders"), (K("research"), "Research"), (K("stats"), "Statistics"),
            (K("pause"), "Pause the factory"), (K("game_menu"), "Game menu"),
            (K("help"), "This help"),
            ("Esc (nothing to cancel)", "Pause menu (windows stay open)"),
            ("Click building", "Manage: upgrade, recipe"), ("Drag title bar", "Move a window"),
        };
        foreach (var (key, action) in keys)
        {
            var k = Ui.Label(key, 14, UiTheme.Accent);
            k.CustomMinimumSize = new Vector2(130, 0);
            _helpGrid.AddChild(k);
            var a = Ui.Label(action, 14);
            a.CustomMinimumSize = new Vector2(200, 0);
            _helpGrid.AddChild(a);
        }
    }

    // ---- World / content ------------------------------------------------------

    /// <summary>Absences shorter than this get the toast, not the window.</summary>
    private const double AwayReportAfterSeconds = 120;

    /// <summary>After a catch-up: the "While you were away" window, or with it turned off (or a short
    /// absence, or an empty factory) the one line welcome toast.</summary>
    private void OnCameBack(OfflineReport report)
    {
        if (Settings.ShowAwayReport && report.ElapsedSeconds >= AwayReportAfterSeconds && _host.Sim.World.EntityCount > 0)
        {
            _away.Fill(report, _host.Content, _thumbs);
            _awayWindow.Visible = true;
            _refresh = 0;
            return;
        }
        if (!report.Earned.IsZero)
            _toasts.Show(this, $"Welcome back! {SimHost.FormatDuration(report.ElapsedSeconds)} offline: earned ${report.Earned.Format()} " +
                               $"(${report.IncomePerSecond.Format()}/s)");
    }

    private void OnWorldReplaced()
    {
        var content = _host.Content;
        if (_menu == null)
        {
            _menu = new BuildMenu(content, _thumbs, def =>
            {
                _tools.SelectTool(def);
                _menu!.Root.Visible = false;
                RefreshToolState();
            }, KeyOf);
            _root.AddChild(Ui.Anchor(_menu.Root, 0.5f, 0.5f, 0, -20, Control.GrowDirection.Both, Control.GrowDirection.Both));

            LoadHotbar(content);
            _thumbs.RenderAll(content.BuildingList, content.Items.Values);
        }
        _sandbox.SetPressedNoSignal(_host.Sim.World.Sandbox);
        _pausedBadge.Visible = _host.Paused;
        RefreshHotbar();
        RefreshToolState();
    }

    private string KeyOf(BuildingDef def)
    {
        int i = Array.IndexOf(_hotbar, def.Id);
        return i < 0 ? "" : i == 9 ? "0" : (i + 1).ToString();
    }

    private void LoadHotbar(ContentRegistry content)
    {
        string[]? saved = null;
        if (FileAccess.FileExists(HotbarPath))
        {
            using var f = FileAccess.Open(HotbarPath, FileAccess.ModeFlags.Read);
            try { saved = JsonSerializer.Deserialize<string[]>(f.GetAsText()); } catch (JsonException) { }
        }
        var ids = saved ?? DefaultHotbar;
        for (int i = 0; i < _hotbar.Length; i++)
            _hotbar[i] = i < ids.Length && ids[i] != null && content.Buildings.ContainsKey(ids[i]) ? ids[i] : null;
    }

    private void SaveHotbar()
    {
        try
        {
            SafeFile.Write(ProjectSettings.GlobalizePath(HotbarPath), JsonSerializer.Serialize(_hotbar), keepBackup: false);
        }
        catch (Exception ex)
        {
            GD.PushWarning($"Could not save the hotbar: {ex.Message}");
        }
    }

    private void RefreshHotbar()
    {
        if (_host?.Sim == null) return;
        for (int i = 0; i < _slots.Count; i++)
        {
            var def = _hotbar[i] is { } id ? _host.Content.Buildings.GetValueOrDefault(id) : null;
            _slots[i].Set(def, def != null ? _thumbs.Get(def.Id) : null, i == 9 ? "0" : (i + 1).ToString(), false, _host.Sim);
        }
        _menu?.Refresh(_thumbs, KeyOf, _host.Sim);
        RefreshToolState();
    }

    private void PickSlot(int slot)
    {
        var def = _hotbar[slot] is { } id ? _host.Content.Buildings.GetValueOrDefault(id) : null;
        if (def == null)
        {
            ToggleBuildMenu();
            return;
        }
        _tools.SelectTool(_tools.Mode == ToolMode.Build && _tools.Tool == def ? null : def);
    }

    // ---- Panels ---------------------------------------------------------------

    private void ToggleBuildMenu()
    {
        if (_menu == null) return;
        _menu.Root.Visible = !_menu.Root.Visible;
        RefreshToolState();
    }

    /// <summary>Freezes or resumes the simulation; building still works while it's frozen.</summary>
    /// <summary>The name of the key bound to an action, for labels ("R", "Tab").</summary>
    private static string K(string action) => Keybinds.Name(action);

    /// <summary>Gives <paramref name="control"/> a tooltip that names keys, refreshed when bindings change.</summary>
    private T Keyed<T>(T control, Func<string> tip) where T : Control
    {
        _keyTips.Add((control, tip));
        control.TooltipText = tip();
        return control;
    }

    private void RefreshKeyLabels()
    {
        foreach (var (control, tip) in _keyTips) control.TooltipText = tip();
        _pausedText.Text = $"PAUSED · {K("pause")} to resume";
        FillHelp();
        if (_host?.Sim != null) RefreshToolState();
    }

    public void TogglePause()
    {
        _host.Paused = !_host.Paused;
        _pausedBadge.Visible = _host.Paused;
    }

    private void CycleSpeed()
    {
        int i = Array.IndexOf(Speeds, _host.TimeScale);
        _host.TimeScale = Speeds[(i + 1) % Speeds.Length];
        _speedButton.Text = $"Speed ×{_host.TimeScale}";
    }

    // ---- Input ------------------------------------------------------------------

    public override void _UnhandledInput(InputEvent ev)
    {
        if (ev is not InputEventKey { Pressed: true, Echo: false } key || key.CtrlPressed || key.AltPressed) return;
        bool handled = true;
        switch (key.Keycode)
        {
            case >= Key.Key0 and <= Key.Key9:
                int slot = key.Keycode == Key.Key0 ? 9 : (int)(key.Keycode - Key.Key1);
                if (_menu is { Root.Visible: true, Hovered: { } hovered })
                {
                    for (int i = 0; i < _hotbar.Length; i++)
                        if (_hotbar[i] == hovered.Id) _hotbar[i] = null;
                    _hotbar[slot] = hovered.Id;
                    SaveHotbar();
                    RefreshHotbar();
                    _toasts.Show(this, $"{hovered.Name} → slot {(slot == 9 ? 0 : slot + 1)}");
                }
                else PickSlot(slot);
                break;
            case var _ when Keybinds.Is(key, "build_menu"):
                ToggleBuildMenu();
                break;
            case var _ when Keybinds.Is(key, "progress"):
                ToggleWindow(_progressWindow);
                break;
            case var _ when Keybinds.Is(key, "stats"):
                ToggleWindow(_statsWindow);
                break;
            case var _ when Keybinds.Is(key, "orders"):
                ToggleWindow(_ordersWindow);
                break;
            case var _ when Keybinds.Is(key, "research") && _researchButton.Visible:
                ToggleWindow(_researchWindow);
                break;
            case var _ when Keybinds.Is(key, "game_menu"):
                ToggleWindow(_gameWindow);
                break;
            case var _ when Keybinds.Is(key, "help"):
                _help.Visible = !_help.Visible;
                break;
            case var _ when Keybinds.Is(key, "pause"):
                TogglePause();
                break;
            case Key.Escape:
                if (_help.Visible) _help.Visible = false;
                else if (_menu is { Root.Visible: true }) _menu.Root.Visible = false;
                // Menus like the Game window close first; info windows stay open, and Manage closes
                // with the selection (BuildController). With nothing left to cancel: pause menu.
                else if (_openWindows.LastOrDefault(w => w != _manage.Window && w.EscCloses) is { } last) last.Close();
                else handled = false;
                RefreshToolState();
                break;
            default:
                handled = false;
                break;
        }
        if (handled) GetViewport().SetInputAsHandled();
    }

    // ---- Refresh ----------------------------------------------------------------

    private void RefreshToolState()
    {
        if (_host?.Sim == null) return;
        foreach (var (mode, b) in _toolButtons) b.SetPressedNoSignal(_tools.Mode == mode);
        _buildButton.SetPressedNoSignal(_tools.Mode == ToolMode.Build || _menu is { Root.Visible: true });
        _hideAbove.SetPressedNoSignal(_tools.HideAbove);
        int max = _tools.MaxHeight;
        for (int i = 0; i < _heightButtons.Count; i++)
        {
            int h = _heightButtons.Count - 1 - i;
            _heightButtons[i].SetPressedNoSignal(h == _tools.Height);
            _heightButtons[i].Visible = h <= max;
        }
        for (int i = 0; i < _slots.Count; i++)
            _slots[i].Button.SetPressedNoSignal(_tools.Mode == ToolMode.Build && _tools.Tool?.Id == _hotbar[i]);
        _undo.Disabled = !_host.History.CanUndo;
        _redo.Disabled = !_host.History.CanRedo;
        RefreshHints();
        _hintPanel.Visible = Settings.ShowKeyHints && _menu is not { Root.Visible: true };
    }

    private void RefreshHints()
    {
        string text = "[center]" + HintsFor() + "[/center]";
        if (_hints.Text != text) _hints.Text = text;
    }

    private static string UpDown => $"{K("height_up")}/{K("height_down")}";

    private string HintsFor()
    {
        static string Chip(string key) => $"[bgcolor=#ffffff24] {key} [/bgcolor]";
        static string H(params (string Key, string Action)[] items) =>
            string.Join("    ", items.Select(i => $"{Chip(i.Key)} [color=#c9d2dd]{i.Action}[/color]"));
        return _tools.Mode switch
        {
            ToolMode.Build => $"[color=#4fb6ff]{_tools.Tool?.Name}[/color] facing {_tools.ShownFacing} · {BuildController.HeightName(_tools.Height).ToLowerInvariant()}    " +
                              (_tools.LineTool
                                  ? H(("LMB", "Place"), ("Drag", "Route"), ("Shift+Drag", "Draw path"), (K("rotate"), "Rotate"), (UpDown, "Height"), ("Esc", "Cancel"))
                                  : H(("LMB", "Place"), ("Drag", "Row"), (K("rotate"), "Rotate"), (UpDown, "Height"), (K("pick"), "Pick"), ("Esc", "Cancel"))),
            ToolMode.Upgrade => H(("LMB", "Upgrade"), ("Shift+LMB", "Whole line"), ("Drag", "Upgrade area"), ("Esc", "Cancel")),
            ToolMode.Delete => H(("LMB", "Delete"), ("Drag", "Delete area"), ("Ctrl+Z", "Undo"), ("Esc", "Cancel")),
            ToolMode.Move => H(("LMB", "Drop here"), (K("rotate"), "Rotate"), (UpDown, "Up/down"), ("Esc", "Cancel")),
            ToolMode.Paste => H(("LMB", "Paste"), (K("rotate"), "Rotate"), (UpDown, "Height"), ("Esc", "Done")),
            _ when _tools.Selection.Count > 0 => H((K("upgrade"), "Upgrade"), (K("rotate"), "Rotate"), (K("move"), "Move"), (K("copy"), "Copy"), ("Del", "Delete"), ("Shift+LMB", "Add"), ("Esc", "Deselect")),
            _ => H(("1-0", "Hotbar"), (K("build_menu"), "Build menu"), ("LMB", "Select"), ("Drag", "Box select"), (K("upgrade"), "Upgrade"), (K("delete_tool"), "Delete"), (K("progress"), "Progress"), (K("help"), "Help")),
        };
    }

    /// <summary>Centres the hotbar, but never over the factory card (narrow screens, large interface).</summary>
    private void KeepBottomClear()
    {
        float width = _root.Size.X;
        float left = Mathf.Max((width - _bottomRow.Size.X) / 2, _card.Position.X + _card.Size.X + 12);
        _bottomRow.Position = _bottomRow.Position with { X = Mathf.Round(Mathf.Min(left, Mathf.Max(0, width - _bottomRow.Size.X - 12))) };
    }

    public override void _Process(double delta)
    {
        if (_host?.Sim == null) return;
        KeepBottomClear();
        _fps.Visible = Settings.ShowFps;
        if (Settings.ShowFps) _fps.Text = $"{Engine.GetFramesPerSecond():0} FPS";
        UpdateCursorTip();
        _tutorial.Update(_host.Sim.World, delta);
        _refresh -= delta;
        if (_refresh > 0) return;
        _refresh = 0.15;
        if (_tools.Mode == ToolMode.Build) RefreshHints(); // the facing follows the cursor (depots face their belt)

        var world = _host.Sim.World;
        _money.Text = "$ " + world.Money.Format();
        _income.Text = $"+${world.Stats.IncomePerSecond(10).Format()}/s";
        _manage.Show(_host.Sim, _tools.SelectedEntities().ToList(), _thumbs);
        if (_progressWindow.Visible) _progress.Refresh(_host.Sim);
        if (_statsWindow.Visible) _stats.Refresh(world);
        if (_ordersWindow.Visible) _orders.Refresh(_host.Sim, _thumbs);
        _researchButton.Visible = ResearchPanel.HasResearch(_host.Content) && ResearchPanel.IsOpen(world);
        if (!_researchButton.Visible) _researchWindow.Visible = false;
        if (_researchWindow.Visible) _research.Refresh(_host.Sim, _thumbs);
        if (_awayWindow.Visible) _away.Refresh(_thumbs);
        foreach (var w in new[] { _progressWindow, _statsWindow, _gameWindow, _ordersWindow, _researchWindow, _awayWindow })
            if (w.Visible) w.Fit();
        _undo.Disabled = !_host.History.CanUndo;
        _redo.Disabled = !_host.History.CanRedo;
        UpdateGoal(world);

        // Money changes what is affordable and limits change with every placement.
        if (_menu is { Root.Visible: true } || _slotsDirty)
        {
            _slotsDirty = false;
            RefreshHotbar();
        }
    }

    private bool _slotsDirty = true;

    /// <summary>The next tier as the standing goal, so there is always something to work toward.</summary>
    private void UpdateGoal(World world)
    {
        var tiers = world.Content.Tiers;
        if (world.UnlockedTier + 1 >= tiers.Count)
        {
            _goal.Text = "All tiers unlocked. Keep upgrading!";
            _goalBar.Value = 1;
            return;
        }
        var next = tiers[world.UnlockedTier + 1];
        double need = next.RequiredEarnings.ToDouble(), have = world.Stats.TotalEarned.ToDouble();
        _goalBar.Value = need <= 0 ? 1 : Math.Min(1, have / need);

        // A tier also asks for goods: say how many are still missing, one at a time.
        var missing = next.Deliver.Where(d => TierDef.SoldOf(world.Stats, d) < d.Count).ToList();
        string delivery = string.Join(", ", missing.Select(d =>
            $"{d.Count - TierDef.SoldOf(world.Stats, d)} {world.Content.Item(d.Item).Name}"));
        if (have < need)
            _goal.Text = $"Next: {next.Name}. Earn ${next.RequiredEarnings.Format()} in total ({K("progress")})";
        else if (missing.Count > 0)
            _goal.Text = $"Next: {next.Name}. Still to sell: {delivery} ({K("progress")})";
        else
            _goal.Text = $"{next.Name} is ready to unlock for ${next.Cost.Format()}. Press P";
    }

    private void UpdateCursorTip()
    {
        var info = _tools.CursorInfo;
        bool show = info != null && _root.GetViewport().GuiGetHoveredControl() == null;
        _cursorTip.Visible = show;
        if (!show) return;
        var (text, ok) = info!.Value;
        _cursorText.Text = text;
        _cursorText.AddThemeColorOverride("font_color", ok ? UiTheme.Text : new Color("#ff8a8a"));
        var mouse = _root.GetViewport().GetMousePosition();
        var size = _cursorTip.GetCombinedMinimumSize();
        var screen = _root.GetViewportRect().Size;
        _cursorTip.Size = size;
        _cursorTip.Position = new Vector2(Mathf.Min(mouse.X + 20, screen.X - size.X - 8), Mathf.Min(mouse.Y + 24, screen.Y - size.Y - 8));
    }
}
