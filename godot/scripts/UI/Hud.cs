using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;
using FactorySim.Content;

namespace FactorySim.Client;

/// <summary>
/// Screen UI: tool bar (top), sidebar (left), factory card (bottom-left), hotbar and key
/// hints (bottom), inspector and panels (right), build menu, help overlay, toasts.
/// Owns hotbar/menu hotkeys; everything else goes to the BuildController.
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
    private Button _buildButton = null!, _undo = null!, _redo = null!, _cutaway = null!;
    private Label _layerLabel = null!, _money = null!, _income = null!;
    private RichTextLabel _hints = null!;
    private readonly List<BuildingTile> _slots = new();
    private readonly string?[] _hotbar = new string?[10];
    private BuildMenu? _menu;
    private InspectorPanel _inspector = null!;
    private UpgradesPanel? _upgrades;
    private StatsPanel _stats = null!;
    private PanelContainer _game = null!, _help = null!;
    private Control _rightColumn = null!;
    private Control? _activePanel;
    private Toasts _toasts = null!;
    private Button _speedButton = null!;
    private CheckButton _sandbox = null!;
    private double _refresh;

    public void Init(SimHost host, BuildController tools, Thumbnails thumbs)
    {
        _host = host;
        _tools = tools;
        _thumbs = thumbs;
        host.Notice += text => _toasts.Show(this, text);
        host.WorldReplaced += OnWorldReplaced;
        tools.Changed += RefreshToolState;
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
        BuildRightColumn();
        BuildHelp();

        _toasts = new Toasts();
        _root.AddChild(Ui.Anchor(_toasts.Root, 0.5f, 0, 0, 72, Control.GrowDirection.Both, Control.GrowDirection.End));
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
        Tool(ToolMode.Delete, Icon.Delete, "Delete (X)\nClick or drag a box", () => _tools.SetMode(_tools.Mode == ToolMode.Delete ? ToolMode.Select : ToolMode.Delete));
        Tool(ToolMode.Move, Icon.Move, "Move selection (M)", () => _tools.BeginMove());
        Tool(ToolMode.Paste, Icon.Copy, "Copy selection & paste (C)\nCtrl+C / Ctrl+V / Ctrl+X", () => _tools.CopySelection(enterPaste: true));
        bar.AddChild(new VSeparator());
        _undo = Ui.IconButton(Icon.Undo, "Undo (Ctrl+Z)", () => _tools.Undo());
        _redo = Ui.IconButton(Icon.Redo, "Redo (Ctrl+Y)", () => _tools.Redo());
        bar.AddChild(_undo);
        bar.AddChild(_redo);
        bar.AddChild(new VSeparator());
        bar.AddChild(Ui.IconButton(Icon.LayerDown, "Layer down (Q · Shift+wheel)", () => _tools.SetLayer(_tools.Layer - 1)));
        _layerLabel = Ui.Label("Ground", 14);
        _layerLabel.CustomMinimumSize = new Vector2(96, 0);
        _layerLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _layerLabel.VerticalAlignment = VerticalAlignment.Center;
        bar.AddChild(_layerLabel);
        bar.AddChild(Ui.IconButton(Icon.LayerUp, "Layer up (E · Shift+wheel)", () => _tools.SetLayer(_tools.Layer + 1)));
        _cutaway = Ui.IconButton(Icon.Eye, "Cutaway: hide layers above the current one (Tab)", () => _tools.ToggleCutaway(), toggle: true);
        bar.AddChild(_cutaway);
        _root.AddChild(Ui.Anchor(Ui.Panel(bar), 0.5f, 0, 0, 12, Control.GrowDirection.Both, Control.GrowDirection.End));
    }

    private void BuildSideBar()
    {
        var col = new VBoxContainer();
        col.AddChild(Ui.IconButton(Icon.Build, "Build menu (B)", ToggleBuildMenu, 44));
        col.AddChild(Ui.IconButton(Icon.Upgrades, "Upgrades (U)", () => TogglePanel(_upgrades?.Root), 44));
        col.AddChild(Ui.IconButton(Icon.Stats, "Statistics (I)", () => TogglePanel(_stats.Root), 44));
        col.AddChild(Ui.IconButton(Icon.Game, "Game: save, load, speed (G)", () => TogglePanel(_game), 44));
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
        var panel = Ui.Panel(body);
        panel.CustomMinimumSize = new Vector2(210, 0);
        _root.AddChild(Ui.Anchor(panel, 0, 1, 12, -12, Control.GrowDirection.End, Control.GrowDirection.Begin));
    }

    private void BuildBottom()
    {
        var col = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Alignment = BoxContainer.AlignmentMode.End };
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
        panel.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
        col.AddChild(panel);
        _root.AddChild(Ui.Anchor(col, 0.5f, 1, 0, -12, Control.GrowDirection.Both, Control.GrowDirection.Begin));
    }

    private void BuildRightColumn()
    {
        var col = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _inspector = new InspectorPanel(
            () => _tools.Rotate(1),
            () => _tools.BeginMove(),
            () => _tools.CopySelection(enterPaste: true),
            _tools.DeleteSelection);
        _inspector.Root.Visible = false;
        col.AddChild(_inspector.Root);

        _stats = new StatsPanel();
        _stats.Root.Visible = false;
        col.AddChild(_stats.Root);

        var game = new VBoxContainer { CustomMinimumSize = new Vector2(300, 0) };
        game.AddChild(Ui.Label("Game", 19));
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
        _game = Ui.Panel(game);
        _game.Visible = false;
        col.AddChild(_game);

        _rightColumn = col;
        _root.AddChild(Ui.Anchor(col, 1, 0, -12, 84, Control.GrowDirection.Begin, Control.GrowDirection.End));
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
            ("1 – 0", "Hotbar building"), ("B", "Build menu"),
            ("LMB", "Place / select"), ("Drag", "Belt line · box select"),
            ("R / Shift+R", "Rotate"), ("F", "Pick hovered building"),
            ("Q / E", "Layer down / up"), ("Tab", "Cutaway view"),
            ("X", "Delete tool"), ("Del", "Delete selection"),
            ("M", "Move selection"), ("C", "Copy & paste selection"),
            ("Ctrl+C / V / X", "Copy / paste / cut"), ("Ctrl+Z / Y", "Undo / redo"),
            ("Ctrl+A", "Select all"), ("Esc / RMB click", "Cancel tool"),
            ("WASD", "Pan (Shift = fast)"), ("RMB drag", "Orbit camera"),
            ("MMB drag", "Pan"), ("Wheel", "Zoom to cursor"),
            ("U", "Upgrades"), ("I", "Statistics"),
            ("G", "Game menu"), ("F1", "This help"),
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

            _upgrades = new UpgradesPanel(content, def => _host.Execute(new BuyUpgrade(def.Id)));
            _upgrades.Root.Visible = false;
            _rightColumn.AddChild(_upgrades.Root);
            _rightColumn.MoveChild(_upgrades.Root, 1);

            LoadHotbar(content);
            _thumbs.RenderAll(content.BuildingList);
        }
        _sandbox.SetPressedNoSignal(_host.Sim.World.Sandbox);
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
        using var f = FileAccess.Open(HotbarPath, FileAccess.ModeFlags.Write);
        f?.StoreString(JsonSerializer.Serialize(_hotbar));
    }

    private void RefreshHotbar()
    {
        if (_host?.Sim == null) return;
        for (int i = 0; i < _slots.Count; i++)
        {
            var def = _hotbar[i] is { } id ? _host.Content.Buildings.GetValueOrDefault(id) : null;
            _slots[i].Set(def, def != null ? _thumbs.Get(def.Id) : null, i == 9 ? "0" : (i + 1).ToString(), false);
        }
        _menu?.Refresh(_thumbs, KeyOf);
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

    private void TogglePanel(Control? panel)
    {
        if (panel == null) return;
        bool show = _activePanel != panel || !panel.Visible;
        foreach (var p in new Control?[] { _upgrades?.Root, _stats.Root, _game })
            if (p != null) p.Visible = false;
        panel.Visible = show;
        _activePanel = show ? panel : null;
        _refresh = 0;
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
            case Key.U:
                TogglePanel(_upgrades?.Root);
                break;
            case Key.I:
                TogglePanel(_stats.Root);
                break;
            case Key.G:
                TogglePanel(_game);
                break;
            case Key.F1:
                _help.Visible = !_help.Visible;
                break;
            case Key.Escape:
                if (_help.Visible) _help.Visible = false;
                else if (_menu is { Root.Visible: true }) _menu.Root.Visible = false;
                else if (_activePanel is { Visible: true })
                {
                    _activePanel.Visible = false;
                    _activePanel = null;
                }
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
        _cutaway.SetPressedNoSignal(_tools.Cutaway);
        _layerLabel.Text = _tools.Layer switch
        {
            0 => "Ground",
            > 0 => $"Layer +{_tools.Layer}",
            _ => $"Tunnel {_tools.Layer}",
        };
        for (int i = 0; i < _slots.Count; i++)
            _slots[i].Button.SetPressedNoSignal(_tools.Mode == ToolMode.Build && _tools.Tool?.Id == _hotbar[i]);
        _undo.Disabled = !_host.History.CanUndo;
        _redo.Disabled = !_host.History.CanRedo;
        _hints.Text = "[center]" + HintsFor() + "[/center]";
    }

    private string HintsFor()
    {
        static string K(string key) => $"[bgcolor=#ffffff24] {key} [/bgcolor]";
        static string H(params (string Key, string Action)[] items) =>
            string.Join("    ", items.Select(i => $"{K(i.Key)} [color=#c9d2dd]{i.Action}[/color]"));
        return _tools.Mode switch
        {
            ToolMode.Build => $"[color=#4fb6ff]{_tools.Tool?.Name}[/color] facing {_tools.Facing}    " +
                              H(("LMB", "Place"), ("Drag", "Line"), ("R", "Rotate"), ("Q/E", "Layer"), ("F", "Pick"), ("Esc", "Cancel")),
            ToolMode.Delete => H(("LMB", "Delete"), ("Drag", "Delete area"), ("Ctrl+Z", "Undo"), ("Esc", "Cancel")),
            ToolMode.Move => H(("LMB", "Drop here"), ("R", "Rotate"), ("Q/E", "Layer"), ("Esc", "Cancel")),
            ToolMode.Paste => H(("LMB", "Paste"), ("R", "Rotate"), ("Q/E", "Layer"), ("Esc", "Done")),
            _ when _tools.Selection.Count > 0 => H(("R", "Rotate"), ("M", "Move"), ("C", "Copy"), ("Del", "Delete"), ("Shift+LMB", "Add"), ("Esc", "Deselect")),
            _ => H(("1-0", "Hotbar"), ("B", "Build menu"), ("LMB", "Select"), ("Drag", "Box select"), ("F", "Pick"), ("X", "Delete"), ("F1", "Help")),
        };
    }

    public override void _Process(double delta)
    {
        if (_host?.Sim == null) return;
        _refresh -= delta;
        if (_refresh > 0) return;
        _refresh = 0.15;

        var world = _host.Sim.World;
        _money.Text = "$ " + world.Money.Format();
        _income.Text = $"+${world.Stats.IncomePerSecond(10).Format()}/s";
        _inspector.Show(_tools.SelectedEntities().ToList(), _thumbs);
        if (_upgrades is { Root.Visible: true }) _upgrades.Refresh(world);
        if (_stats.Root.Visible) _stats.Refresh(world);
        _undo.Disabled = !_host.History.CanUndo;
        _redo.Disabled = !_host.History.CanRedo;
    }
}
