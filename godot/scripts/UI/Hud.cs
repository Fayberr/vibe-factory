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
    private const string SettingsPath = "user://settings.json";
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
    private PanelContainer _cursorTip = null!;
    private Label _cursorText = null!;
    private readonly List<BuildingTile> _slots = new();
    private readonly string?[] _hotbar = new string?[10];
    private BuildMenu? _menu;
    private ManageWindow _manage = null!;
    private ProgressPanel _progress = null!;
    private StatsPanel _stats = null!;
    private HudWindow _progressWindow = null!, _statsWindow = null!, _gameWindow = null!;
    private readonly List<HudWindow> _openWindows = new(); // most recently opened last (Esc closes it)
    private TutorialPanel _tutorial = null!;
    private Button _progressButton = null!;
    private Control _ladder = null!;
    private bool _tutorialChecked;
    private Control _windows = null!;
    private PanelContainer _help = null!;
    private Toasts _toasts = null!;
    private Button _speedButton = null!;
    private CheckButton _sandbox = null!;
    private double _refresh;

    public ManageWindow Manage => _manage;
    public TutorialPanel Tutorial => _tutorial;
    public Control? BuildMenu => _menu?.Root;

    /// <summary>Open the tutorial on a player's first new factory (off for scripted runs).</summary>
    public bool AutoStartTutorial { get; set; } = true;
    public HudWindow ProgressWindow => _progressWindow;
    public HudWindow StatsWindow => _statsWindow;

    public void Init(SimHost host, BuildController tools, Thumbnails thumbs)
    {
        _host = host;
        _tools = tools;
        _thumbs = thumbs;
        host.Notice += text => _toasts.Show(this, text);
        host.WorldReplaced += OnWorldReplaced;
        host.EventRaised += ev =>
        {
            if (ev is EntityPlaced or EntityRemoved) _slotsDirty = true; // build limits changed
            if (ev is not TierUnlocked t) return;
            var b = host.Sim.World.Bounds;
            _toasts.Show(this, $"Tier {t.Tier} unlocked: {t.Name}! Plot is now {b.Max.X - b.Min.X + 1}×{b.Max.Y - b.Min.Y + 1}, new buildings in the build menu.");
            RefreshHotbar();
        };
        tools.Changed += () =>
        {
            _slotsDirty = true;
            _refresh = 0; // selection changes show in the Manage window right away
            RefreshToolState();
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

        _toasts = new Toasts();
        _root.AddChild(Ui.Anchor(_toasts.Root, 0.5f, 0, 0, 72, Control.GrowDirection.Both, Control.GrowDirection.End));

        _cursorText = Ui.Label("", 13);
        _cursorTip = Ui.Panel(_cursorText);
        _cursorTip.AddThemeStyleboxOverride("panel", UiTheme.Box(new Color(0.05f, 0.06f, 0.08f, 0.88f), 7, 9, 5));
        _cursorTip.MouseFilter = Control.MouseFilterEnum.Ignore;
        _cursorTip.Visible = false;
        _root.AddChild(_cursorTip);
    }

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
        Tool(ToolMode.Select, Icon.Select, "Select (V)\nClick, Shift-click or drag a box", () => _tools.SetMode(ToolMode.Select));
        _buildButton = Ui.IconButton(Icon.Build, "Build menu (B)", ToggleBuildMenu, toggle: true);
        bar.AddChild(_buildButton);
        Tool(ToolMode.Upgrade, Icon.Upgrades, "Upgrade (U)\nClick a building, drag a box, Shift-click a whole belt line.\nWith a selection, U upgrades it.", () => _tools.SetMode(_tools.Mode == ToolMode.Upgrade ? ToolMode.Select : ToolMode.Upgrade));
        Tool(ToolMode.Delete, Icon.Delete, "Delete (X)\nClick or drag a box", () => _tools.SetMode(_tools.Mode == ToolMode.Delete ? ToolMode.Select : ToolMode.Delete));
        Tool(ToolMode.Move, Icon.Move, "Move selection (M)", () => _tools.BeginMove());
        Tool(ToolMode.Paste, Icon.Copy, "Copy selection & paste (C)\nCtrl+C / Ctrl+V / Ctrl+X", () => _tools.CopySelection(enterPaste: true));
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
        col.AddChild(Ui.IconButton(Icon.Build, "Build menu (B)", ToggleBuildMenu, 44));
        _progressButton = Ui.IconButton(Icon.Progress, "Progress: tiers and build limits (P)", () => ToggleWindow(_progressWindow), 44);
        col.AddChild(_progressButton);
        col.AddChild(Ui.IconButton(Icon.Stats, "Statistics (I)", () => ToggleWindow(_statsWindow), 44));
        col.AddChild(Ui.IconButton(Icon.Game, "Game: save, load, speed (G)", () => ToggleWindow(_gameWindow), 44));
        col.AddChild(Ui.IconButton(Icon.Help, "Controls (F1)", () => _help.Visible = !_help.Visible, 44));
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
            rotate: () => _tools.Rotate(1),
            move: () => _tools.BeginMove(),
            copy: () => _tools.CopySelection(enterPaste: true),
            close: _tools.ClearSelection);
        AddWindow(_manage.Window);

        _progress = new ProgressPanel(() => _host.Execute(new UnlockTier()));
        _progressWindow = new HudWindow("Progress", Icon.Progress, 340);
        _progressWindow.Body.AddChild(_progress.Root);
        AddWindow(_progressWindow);

        _stats = new StatsPanel();
        _statsWindow = new HudWindow("Statistics", Icon.Stats, 300);
        _statsWindow.Body.AddChild(_stats.Root);
        AddWindow(_statsWindow);

        var game = new VBoxContainer();
        game.AddChild(Ui.TextButton("Save", () => _host.Save()));
        game.AddChild(Ui.TextButton("Load last save", () => { if (!_host.TryLoad()) _toasts.Show(this, "No save found"); }));
        game.AddChild(Ui.TextButton("New factory (empty)", () => _host.NewGame(withDemo: false)));
        game.AddChild(Ui.TextButton("New factory (demo layout)", () => _host.NewGame(withDemo: true)));
        _speedButton = Ui.TextButton("Speed ×1", CycleSpeed, "Simulation speed");
        game.AddChild(_speedButton);
        _sandbox = new CheckButton { Text = "Sandbox (free building)", FocusMode = Control.FocusModeEnum.None };
        _sandbox.Toggled += on => _host.Sim.World.Sandbox = on;
        game.AddChild(_sandbox);
        game.AddChild(Ui.TextButton("Simulate 1 h offline", () => _host.SimulateOffline(3600), "Test the offline catch-up"));
        game.AddChild(Ui.TextButton("Start the tutorial", StartTutorial, "A short guided first factory"));
        _gameWindow = new HudWindow("Vibe Factory", Icon.Game, 280);
        _gameWindow.Body.AddChild(Ui.Pad(game, 14, 12));
        AddWindow(_gameWindow);

        _tutorial = new TutorialPanel(FocusTarget);
        _tutorial.Ended += () => { if (AutoStartTutorial) SaveSettings(tutorialDone: true); };
        AddWindow(_tutorial.Window);
        _root.AddChild(_tutorial.Highlight);
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

    private bool TutorialDone()
    {
        if (!FileAccess.FileExists(SettingsPath)) return false;
        using var f = FileAccess.Open(SettingsPath, FileAccess.ModeFlags.Read);
        try { return JsonSerializer.Deserialize<Dictionary<string, bool>>(f.GetAsText())?.GetValueOrDefault("tutorialDone") == true; }
        catch (JsonException) { return false; }
    }

    private static void SaveSettings(bool tutorialDone)
    {
        using var f = FileAccess.Open(SettingsPath, FileAccess.ModeFlags.Write);
        f?.StoreString(JsonSerializer.Serialize(new Dictionary<string, bool> { ["tutorialDone"] = tutorialDone }));
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

    /// <summary>First time a window opens: Manage on the right, the others side by side next to the sidebar.</summary>
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
        var grid = new GridContainer { Columns = 4 };
        (string, string)[] keys =
        {
            ("1 to 0", "Hotbar building"), ("B", "Build menu"),
            ("LMB", "Place / select"), ("Drag", "Belt line (bridges crossings)"),
            ("Drop on belt", "Replaces it (polisher, splitter…)"), ("R / Shift+R", "Rotate"),
            ("E / Q", "Build height up / down"), ("Shift+wheel", "Build height"),
            ("Tab", "Hide above build height"), ("F", "Pick hovered building"),
            ("U", "Upgrade tool / selection"), ("Shift+LMB (U)", "Upgrade whole belt line"),
            ("X", "Delete tool"), ("Del", "Delete selection"),
            ("M", "Move selection"), ("C", "Copy & paste selection"),
            ("Ctrl+C / V / X", "Copy / paste / cut"), ("Ctrl+Z / Y", "Undo / redo"),
            ("Ctrl+A", "Select all"), ("Esc / RMB click", "Cancel tool"),
            ("WASD", "Pan (Shift = fast)"), ("RMB drag", "Orbit camera"),
            ("MMB drag", "Pan"), ("Wheel", "Zoom to cursor"),
            ("P", "Progress: tiers & limits"), ("I", "Statistics"),
            ("G", "Game menu"), ("F1", "This help"),
            ("Click building", "Manage: upgrade, recipe"), ("Drag title bar", "Move a window"),
        };
        foreach (var (key, action) in keys)
        {
            var k = Ui.Label(key, 14, UiTheme.Accent);
            k.CustomMinimumSize = new Vector2(130, 0);
            grid.AddChild(k);
            var a = Ui.Label(action, 14);
            a.CustomMinimumSize = new Vector2(200, 0);
            grid.AddChild(a);
        }
        body.AddChild(grid);
        _help = Ui.Panel(body);
        _help.Visible = false;
        _root.AddChild(Ui.Anchor(_help, 0.5f, 0.5f, 0, 0, Control.GrowDirection.Both, Control.GrowDirection.Both));
    }

    // ---- World / content ------------------------------------------------------

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
        RefreshHotbar();
        RefreshToolState();

        // First launch with a fresh factory: offer the guided start.
        if (!_tutorialChecked)
        {
            _tutorialChecked = true;
            if (AutoStartTutorial && _host.Sim.World.EntityCount == 0 && _host.Sim.World.Stats.TotalEarned.IsZero && !TutorialDone())
                CallDeferred(nameof(StartTutorial));
        }
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
        using var f = FileAccess.Open(HotbarPath, FileAccess.ModeFlags.Write);
        f?.StoreString(JsonSerializer.Serialize(_hotbar));
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
            case Key.B:
                ToggleBuildMenu();
                break;
            case Key.P:
                ToggleWindow(_progressWindow);
                break;
            case Key.I:
                ToggleWindow(_statsWindow);
                break;
            case Key.G:
                ToggleWindow(_gameWindow);
                break;
            case Key.F1:
                _help.Visible = !_help.Visible;
                break;
            case Key.Escape:
                if (_help.Visible) _help.Visible = false;
                else if (_menu is { Root.Visible: true }) _menu.Root.Visible = false;
                // The last opened window closes first; Manage closes with the selection (BuildController).
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
        _hints.Text = "[center]" + HintsFor() + "[/center]";
        _hintPanel.Visible = _menu is not { Root.Visible: true };
    }

    private string HintsFor()
    {
        static string K(string key) => $"[bgcolor=#ffffff24] {key} [/bgcolor]";
        static string H(params (string Key, string Action)[] items) =>
            string.Join("    ", items.Select(i => $"{K(i.Key)} [color=#c9d2dd]{i.Action}[/color]"));
        return _tools.Mode switch
        {
            ToolMode.Build => $"[color=#4fb6ff]{_tools.Tool?.Name}[/color] facing {_tools.Facing} · {BuildController.HeightName(_tools.Height).ToLowerInvariant()}    " +
                              H(("LMB", "Place"), ("Drag", "Line"), ("R", "Rotate"), ("E/Q", "Height"), ("F", "Pick"), ("Esc", "Cancel")),
            ToolMode.Upgrade => H(("LMB", "Upgrade"), ("Shift+LMB", "Whole line"), ("Drag", "Upgrade area"), ("Esc", "Cancel")),
            ToolMode.Delete => H(("LMB", "Delete"), ("Drag", "Delete area"), ("Ctrl+Z", "Undo"), ("Esc", "Cancel")),
            ToolMode.Move => H(("LMB", "Drop here"), ("R", "Rotate"), ("E/Q", "Up/down"), ("Esc", "Cancel")),
            ToolMode.Paste => H(("LMB", "Paste"), ("R", "Rotate"), ("E/Q", "Height"), ("Esc", "Done")),
            _ when _tools.Selection.Count > 0 => H(("U", "Upgrade"), ("R", "Rotate"), ("M", "Move"), ("C", "Copy"), ("Del", "Delete"), ("Shift+LMB", "Add"), ("Esc", "Deselect")),
            _ => H(("1-0", "Hotbar"), ("B", "Build menu"), ("LMB", "Select"), ("Drag", "Box select"), ("U", "Upgrade"), ("X", "Delete"), ("P", "Progress"), ("F1", "Help")),
        };
    }

    public override void _Process(double delta)
    {
        if (_host?.Sim == null) return;
        UpdateCursorTip();
        _tutorial.Update(_host.Sim.World, delta);
        _refresh -= delta;
        if (_refresh > 0) return;
        _refresh = 0.15;

        var world = _host.Sim.World;
        _money.Text = "$ " + world.Money.Format();
        _income.Text = $"+${world.Stats.IncomePerSecond(10).Format()}/s";
        _manage.Show(_host.Sim, _tools.SelectedEntities().ToList(), _thumbs);
        if (_progressWindow.Visible) _progress.Refresh(_host.Sim);
        if (_statsWindow.Visible) _stats.Refresh(world);
        foreach (var w in new[] { _progressWindow, _statsWindow, _gameWindow })
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
        _goal.Text = have < need
            ? $"Next: {next.Name}. Earn ${next.RequiredEarnings.Format()} in total (P)"
            : $"{next.Name} is ready to unlock for ${next.Cost.Format()}. Press P";
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
