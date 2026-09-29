using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace FactorySim.Client;

/// <summary>What the menus ask the game to do (implemented by <see cref="GameFlow"/>).</summary>
public interface IMenuActions
{
    void NewGame(int slot, string name, bool withDemo);
    void LoadGame(int slot);
    void Resume();
    void SaveGame();
    void QuitToMenu();
    void QuitGame();
    void SettingsChanged();
}

/// <summary>
/// Everything around the game: the title screen over a live factory, save slots, new-factory
/// and confirm dialogs, settings, credits and the pause menu. Runs while the tree is paused.
/// </summary>
public partial class MenuLayer : CanvasLayer
{
    private IMenuActions _actions = null!;
    private GameSettings _settings = null!;
    private Control _root = null!;
    private Control _title = null!;
    private Label _titleVersion = null!;
    private Control _pause = null!;
    private Button _continue = null!;
    private Label _continueInfo = null!;
    private HudWindow _slots = null!, _name = null!, _confirm = null!, _settingsWindow = null!, _credits = null!;
    private VBoxContainer _slotRows = null!;
    private bool _slotsForNew;
    private LineEdit _nameEdit = null!;
    private CheckBox _demo = null!;
    private int _nameSlot;
    private Label _confirmText = null!;
    private Action? _confirmed;
    private SettingsPanel _settingsPanel = null!;

    public bool TitleVisible => _title.Visible;
    public bool PauseVisible => _pause.Visible;
    public bool SettingsVisible => _settingsWindow.Visible;
    public SettingsPanel SettingsPanel => _settingsPanel;

    public void Init(IMenuActions actions, GameSettings settings)
    {
        _actions = actions;
        _settings = settings;
    }

    public override void _Ready()
    {
        Layer = 5;
        ProcessMode = ProcessModeEnum.Always;
        _root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Theme = UiTheme.Create() };
        _root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_root);
        BuildTitle();
        BuildPause();
        BuildWindows();
    }

    // ---- Title screen -------------------------------------------------------------

    private void BuildTitle()
    {
        _title = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        _title.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(_title);

        // Darken the left side so the menu reads over the running factory.
        var shade = new TextureRect
        {
            Texture = new GradientTexture2D
            {
                Gradient = new Gradient { Colors = new[] { new Color(0.04f, 0.05f, 0.08f, 0.92f), new Color(0.04f, 0.05f, 0.08f, 0f) }, Offsets = new[] { 0.35f, 1f } },
                Width = 256,
                Height = 4,
            },
            StretchMode = TextureRect.StretchModeEnum.Scale,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        shade.AnchorBottom = 1;
        shade.AnchorRight = 0.62f;
        _title.AddChild(shade);

        var col = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        col.AddThemeConstantOverride("separation", 10);
        var name = Ui.Label("Vibe Factory", 72);
        name.AddThemeFontOverride("font", UiTheme.Bold);
        col.AddChild(name);
        col.AddChild(Ui.Label("Build it. Automate it. Chill.", 20, UiTheme.Muted));
        col.AddChild(new Control { CustomMinimumSize = new Vector2(0, 28), MouseFilter = Control.MouseFilterEnum.Ignore });

        _continue = MenuButton("Continue", () => { if (SimHost.MostRecentSlot() is > 0 and var s) _actions.LoadGame(s); }, primary: true);
        col.AddChild(_continue);
        _continueInfo = Ui.Label("", 13, UiTheme.Muted);
        col.AddChild(_continueInfo);
        col.AddChild(MenuButton("New factory", () => OpenSlots(forNew: true)));
        col.AddChild(MenuButton("Load factory", () => OpenSlots(forNew: false)));
        col.AddChild(MenuButton("Settings", () => Open(_settingsWindow)));
        col.AddChild(MenuButton("Credits", () => Open(_credits)));
        col.AddChild(MenuButton("Quit", () => _actions.QuitGame()));
        _title.AddChild(Ui.Anchor(col, 0, 0.5f, 96, 0, Control.GrowDirection.End, Control.GrowDirection.Both));

        var credit = Ui.Label("Music by Kevin MacLeod · Sounds by Kenney · Made with Godot", 12, new Color(1, 1, 1, 0.45f));
        _title.AddChild(Ui.Anchor(credit, 0, 1, 96, -24, Control.GrowDirection.End, Control.GrowDirection.Begin));

        // The same build stamp as the HUD, mirrored into the other bottom corner, so a screenshot of
        // the menu names its build too. White rather than the credits line's dim white, since the menu
        // is drawn over the running factory rather than over a panel and this is the line a bug report
        // is asked for; the size comes from Ui.VersionStamp.
        _titleVersion = Ui.VersionStamp(new Color(1, 1, 1, 0.62f));
        _title.AddChild(Ui.Anchor(_titleVersion, 1, 1, -24, -24, Control.GrowDirection.Begin, Control.GrowDirection.Begin));
    }

    private static Button MenuButton(string text, Action action, bool primary = false)
    {
        var b = new Button
        {
            Text = text,
            ThemeTypeVariation = primary ? "PrimaryButton" : "Button",
            FocusMode = Control.FocusModeEnum.None,
            CustomMinimumSize = new Vector2(340, 54),
            Alignment = HorizontalAlignment.Left,
        };
        b.AddThemeFontOverride("font", UiTheme.Bold);
        b.AddThemeFontSizeOverride("font_size", 20);
        b.Pressed += action;
        return b;
    }

    public void ShowTitle()
    {
        _pause.Visible = false;
        _title.Visible = true;
        int last = SimHost.MostRecentSlot();
        _continue.Visible = _continueInfo.Visible = last > 0;
        if (last > 0 && SimHost.ReadSlot(last) is { } info)
            _continueInfo.Text = $"{info.Name} · tier {info.Tier} {info.TierName} · saved {Ago(info.SavedAtUtc)}";
    }

    public void HideAll()
    {
        _title.Visible = false;
        _pause.Visible = false;
        foreach (var w in new[] { _slots, _name, _confirm, _settingsWindow, _credits }) w.Visible = false;
    }

    // ---- Pause ----------------------------------------------------------------------

    private void BuildPause()
    {
        _pause = new Control { Visible = false, MouseFilter = Control.MouseFilterEnum.Stop };
        _pause.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        var dim = new ColorRect { Color = new Color(0.02f, 0.03f, 0.05f, 0.6f), MouseFilter = Control.MouseFilterEnum.Ignore };
        dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _pause.AddChild(dim);

        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 10);
        var title = Ui.Label("Paused", 44);
        title.AddThemeFontOverride("font", UiTheme.Bold);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        col.AddChild(title);
        col.AddChild(new Control { CustomMinimumSize = new Vector2(0, 8) });
        col.AddChild(MenuButton("Resume", () => _actions.Resume(), primary: true));
        col.AddChild(MenuButton("Save", () => _actions.SaveGame()));
        col.AddChild(MenuButton("Settings", () => Open(_settingsWindow)));
        col.AddChild(MenuButton("Save and quit to menu", () => _actions.QuitToMenu()));
        col.AddChild(MenuButton("Save and quit game", () => _actions.QuitGame()));
        _pause.AddChild(Ui.Anchor(Ui.Panel(Ui.Pad(col, 18, 18)), 0.5f, 0.5f, 0, 0, Control.GrowDirection.Both, Control.GrowDirection.Both));
        _root.AddChild(_pause);
    }

    public void ShowPause(bool on)
    {
        _pause.Visible = on;
        if (!on) foreach (var w in new[] { _settingsWindow, _confirm }) w.Visible = false;
    }

    // ---- Windows ------------------------------------------------------------------------

    private void BuildWindows()
    {
        _slots = new HudWindow("Load factory", Icon.Game, 600);
        _slotRows = new VBoxContainer();
        _slotRows.AddThemeConstantOverride("separation", 0);
        _slots.Body.AddChild(_slotRows);
        AddWindow(_slots);

        _name = new HudWindow("New factory", Icon.Build, 420);
        var nameBody = new VBoxContainer();
        nameBody.AddChild(Ui.Label("Name", 13, UiTheme.Muted));
        _nameEdit = new LineEdit { MaxLength = 32, CustomMinimumSize = new Vector2(0, 40) };
        _nameEdit.TextSubmitted += _ => StartNamed();
        nameBody.AddChild(_nameEdit);
        _demo = new CheckBox { Text = "Start with the example factory", FocusMode = Control.FocusModeEnum.None };
        nameBody.AddChild(_demo);
        _name.Body.AddChild(Ui.Pad(nameBody, 16, 12));
        var start = new Button { ThemeTypeVariation = "PrimaryButton", Text = "Start building", FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 48) };
        start.Pressed += StartNamed;
        _name.Body.AddChild(start);
        AddWindow(_name);

        _confirm = new HudWindow("Are you sure?", Icon.Help, 400);
        _confirmText = Ui.Label("", 15);
        _confirmText.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _confirmText.CustomMinimumSize = new Vector2(360, 0);
        _confirm.Body.AddChild(Ui.Pad(_confirmText, 16, 14));
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 0);
        var cancel = new Button { ThemeTypeVariation = "FlatButton", Text = "Cancel", FocusMode = Control.FocusModeEnum.None, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 46) };
        cancel.Pressed += () => _confirm.Visible = false;
        row.AddChild(cancel);
        var ok = new Button { ThemeTypeVariation = "PrimaryButton", Text = "Yes", FocusMode = Control.FocusModeEnum.None, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 46) };
        ok.Pressed += () =>
        {
            _confirm.Visible = false;
            _confirmed?.Invoke();
        };
        row.AddChild(ok);
        _confirm.Body.AddChild(row);
        AddWindow(_confirm);

        _settingsWindow = new HudWindow("Settings", Icon.Game, 520);
        _settingsPanel = new SettingsPanel(_settings, () => _actions.SettingsChanged());
        // Tabs differ in height: re-centre once the window has taken the new page's size.
        _settingsPanel.PageChanged += async () =>
        {
            if (!IsInsideTree() || !_settingsWindow.Visible) return;
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Center(_settingsWindow);
        };
        _settingsWindow.Body.AddChild(_settingsPanel.Root);
        AddWindow(_settingsWindow);

        _credits = new HudWindow("Credits", Icon.Help, 520);
        var text = new RichTextLabel
        {
            BbcodeEnabled = true,
            FitContent = true,
            CustomMinimumSize = new Vector2(480, 0),
            ScrollActive = false,
            Text = Credits,
        };
        text.AddThemeFontSizeOverride("normal_font_size", 14);
        text.AddThemeFontSizeOverride("bold_font_size", 15);
        text.MetaClicked += meta => OS.ShellOpen(meta.AsString());
        _credits.Body.AddChild(Ui.Pad(text, 18, 14));
        AddWindow(_credits);
    }

    private const string Credits =
        "[b]Vibe Factory[/b]\nMade with Godot Engine 4 and .NET.\n\n" +
        "[b]Music[/b]\n\"Airport Lounge\", \"Chill Wave\", \"Lobby Time\" and \"Dreamer\"\n" +
        "Kevin MacLeod ([url=https://incompetech.com]incompetech.com[/url])\n" +
        "Licensed under Creative Commons: By Attribution 4.0\n[url=https://creativecommons.org/licenses/by/4.0/]creativecommons.org/licenses/by/4.0[/url]\n\n" +
        "[b]Sound effects[/b]\nKenney ([url=https://kenney.nl]kenney.nl[/url]): Interface Sounds, Impact Sounds, Casino Audio, Music Jingles\n" +
        "Released into the public domain (CC0 1.0)";

    private void AddWindow(HudWindow w)
    {
        _root.AddChild(w.Root);
        w.Activated += () => { if (w.Visible) Center(w); };
    }

    private static void Center(HudWindow w)
    {
        w.Root.ResetSize();
        var screen = w.Root.GetViewportRect().Size;
        w.Root.Position = ((screen - w.Root.Size) / 2).Round();
    }

    private static void Open(HudWindow w)
    {
        w.Visible = true;
        w.Root.MoveToFront();
        Center(w);
    }

    private void OpenSlots(bool forNew)
    {
        _slotsForNew = forNew;
        _slots.Title = forNew ? "New factory" : "Load factory";
        RefreshSlots();
        Open(_slots);
    }

    private void RefreshSlots()
    {
        foreach (var child in _slotRows.GetChildren()) child.QueueFree();
        for (int slot = 1; slot <= SimHost.SlotCount; slot++)
        {
            int s = slot;
            var info = SimHost.ReadSlot(slot);
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 12);
            var badge = Ui.Label(slot.ToString(), 22, UiTheme.Muted);
            badge.AddThemeFontOverride("font", UiTheme.Bold);
            badge.CustomMinimumSize = new Vector2(26, 0);
            badge.VerticalAlignment = VerticalAlignment.Center;
            row.AddChild(badge);
            var text = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            text.AddThemeConstantOverride("separation", 2);
            var title = Ui.Label(info?.Name ?? "Empty slot", 17, info == null ? UiTheme.Muted : UiTheme.Text);
            title.AddThemeFontOverride("font", UiTheme.Bold);
            text.AddChild(title);
            text.AddChild(Ui.Label(info == null ? "Start a new factory here" :
                $"Tier {info.Tier} {info.TierName} · ${((BigNum)info.Money).Format()} · {info.Buildings} buildings · {SimHost.FormatDuration(info.PlaySeconds)} played · saved {Ago(info.SavedAtUtc)}",
                12, UiTheme.Muted));
            row.AddChild(text);

            if (_slotsForNew)
            {
                row.AddChild(SmallButton(info == null ? "Start" : "Overwrite", () =>
                {
                    if (info == null) AskName(s);
                    else Ask($"Replace \"{info.Name}\" with a new factory? The old one is lost.", () => AskName(s));
                }, primary: info == null));
            }
            else if (info != null) row.AddChild(SmallButton("Load", () => _actions.LoadGame(s), primary: true));
            if (info != null)
                row.AddChild(SmallButton("Delete", () => Ask($"Delete \"{info.Name}\" for good?", () =>
                {
                    SimHost.DeleteSlot(s);
                    RefreshSlots();
                    ShowTitle();
                })));
            _slotRows.AddChild(Ui.Pad(row, 16, 10));
            if (slot < SimHost.SlotCount) _slotRows.AddChild(new HSeparator());
        }
    }

    private static Button SmallButton(string text, Action action, bool primary = false)
    {
        var b = new Button { Text = text, ThemeTypeVariation = primary ? "PrimaryButton" : "FlatButton", FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(104, 40), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        b.AddThemeFontSizeOverride("font_size", 15);
        b.Pressed += action;
        return b;
    }

    private void AskName(int slot)
    {
        _nameSlot = slot;
        _nameEdit.Text = $"Factory {slot}";
        _demo.ButtonPressed = false;
        Open(_name);
        _nameEdit.GrabFocus();
        _nameEdit.SelectAll();
    }

    private void StartNamed()
    {
        string name = string.IsNullOrWhiteSpace(_nameEdit.Text) ? $"Factory {_nameSlot}" : _nameEdit.Text.Trim();
        _name.Visible = false;
        _slots.Visible = false;
        _actions.NewGame(_nameSlot, name, _demo.ButtonPressed);
    }

    private void Ask(string question, Action yes)
    {
        _confirmText.Text = question;
        _confirmed = yes;
        Open(_confirm);
    }

    public void OpenSettings() => Open(_settingsWindow);

    /// <summary>Opens a title-screen window by name ("settings", "new", "load", "credits"); for screenshots.</summary>
    public void OpenWindow(string which)
    {
        switch (which)
        {
            case "settings": OpenSettings(); break;
            case "controls":
                OpenSettings();
                _settingsPanel.ShowPage(2);
                break;
            case "new": OpenSlots(forNew: true); break;
            case "load": OpenSlots(forNew: false); break;
            case "credits": Open(_credits); break;
        }
    }

    /// <summary>Esc: close the top menu window; true if one was open.</summary>
    public bool CloseTopWindow()
    {
        foreach (var w in new[] { _confirm, _name, _slots, _settingsWindow, _credits })
            if (w.Visible)
            {
                w.Close();
                return true;
            }
        return false;
    }

    public static string Ago(DateTimeOffset when)
    {
        var span = DateTimeOffset.UtcNow - when;
        return span.TotalMinutes < 1 ? "just now"
            : span.TotalHours < 1 ? $"{(int)span.TotalMinutes} min ago"
            : span.TotalDays < 1 ? $"{(int)span.TotalHours} h ago"
            : $"{(int)span.TotalDays} days ago";
    }
}

/// <summary>
/// Settings in four tabs (Audio, Display, Controls, Gameplay); every change applies at once and
/// is saved. Controls lists every rebindable key: click one, press the new key (Esc cancels).
/// </summary>
public sealed class SettingsPanel
{
    public readonly Control Root;
    private readonly GameSettings _s;
    private readonly Action _changed;
    private readonly Control[] _pages = new Control[4];
    private readonly Button[] _tabs = new Button[4];
    private readonly Dictionary<string, Button> _bindings = new();
    private readonly Label _keyStatus = Ui.Label("", 12, UiTheme.Muted);
    private Button? _listening;

    public static readonly string[] Tabs = { "Audio", "Display", "Controls", "Gameplay" };

    public SettingsPanel(GameSettings settings, Action changed)
    {
        _s = settings;
        _changed = changed;
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 0);

        var tabs = new HBoxContainer();
        tabs.AddThemeConstantOverride("separation", 6);
        var group = new ButtonGroup();
        for (int i = 0; i < Tabs.Length; i++)
        {
            int page = i;
            var tab = new Button
            {
                Text = Tabs[i], ToggleMode = true, ButtonGroup = group, ButtonPressed = i == 0,
                FocusMode = Control.FocusModeEnum.None, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                CustomMinimumSize = new Vector2(0, 36),
            };
            tab.Toggled += on => { if (on) ShowPage(page); };
            _tabs[i] = tab;
            tabs.AddChild(tab);
        }
        col.AddChild(Ui.Pad(tabs, 14, 10));
        col.AddChild(new HSeparator());

        _pages[0] = Page(
            Header("VOLUME"),
            Slider("Master", () => _s.MasterVolume, v => _s.MasterVolume = v),
            Slider("Music", () => _s.MusicVolume, v => _s.MusicVolume = v),
            Slider("Effects", () => _s.EffectsVolume, v => _s.EffectsVolume = v),
            Slider("Interface", () => _s.InterfaceVolume, v => _s.InterfaceVolume = v),
            Header("WHEN YOU SWITCH AWAY"),
            Check("Mute the game in the background", () => _s.MuteInBackground, v => _s.MuteInBackground = v));

        // Steps, not a slider: rescaling the interface under a dragged slider moves the slider.
        var sizes = new[] { 0.8f, 0.9f, 1f, 1.1f, 1.25f, 1.5f };
        string SizeName(float f) => $"{f * 100:0}%";
        var fps = new[] { ("Unlimited", 0), ("30", 30), ("60", 60), ("120", 120), ("144", 144), ("240", 240) };
        _pages[1] = Page(
            Header("WINDOW"),
            Check("Fullscreen", () => _s.Fullscreen, v => _s.Fullscreen = v),
            Check("VSync", () => _s.VSync, v => _s.VSync = v),
            Choice("Frame rate limit", Array.ConvertAll(fps, f => f.Item1),
                () => Array.Find(fps, f => f.Item2 == _s.MaxFps).Item1 ?? "Unlimited",
                v => _s.MaxFps = Array.Find(fps, f => f.Item1 == v).Item2),
            Check("Show frames per second", () => _s.ShowFps, v => _s.ShowFps = v),
            Header("LOOK"),
            Choice("Graphics", new[] { "Low", "Medium", "High" }, () => _s.Quality, v => _s.Quality = v),
            Choice("Interface size", Array.ConvertAll(sizes, SizeName),
                () => SizeName(sizes.MinBy(f => Math.Abs(f - _s.UiScale))),
                v => _s.UiScale = sizes[Array.FindIndex(sizes, f => SizeName(f) == v)]));

        var speeds = new[] { ("Slow", 0.6f), ("Normal", 1f), ("Fast", 1.6f) };
        var controls = new List<Control>
        {
            Header("CAMERA"),
            Choice("Pan speed", Array.ConvertAll(speeds, p => p.Item1),
                () => speeds.MinBy(p => Math.Abs(p.Item2 - _s.PanSpeed)).Item1,
                v => _s.PanSpeed = Array.Find(speeds, p => p.Item1 == v).Item2),
            Check("Pan when the mouse touches the edge", () => _s.EdgePan, v => _s.EdgePan = v),
            Check("Invert zoom (wheel up zooms out)", () => _s.InvertZoom, v => _s.InvertZoom = v),
        };
        foreach (var grp in Keybinds.All.GroupBy(a => a.Group))
        {
            controls.Add(Header($"KEYS · {grp.Key.ToUpperInvariant()}"));
            controls.AddRange(grp.Select(Binding));
        }
        controls.Add(Ui.Label("Fixed: Esc, 1 to 0 (hotbar), Ctrl shortcuts, Delete, arrow keys, PageUp/PageDown.", 12, UiTheme.Muted));
        controls.Add(_keyStatus);
        controls.Add(Ui.TextButton("Reset all keys to their defaults", () =>
        {
            Keybinds.ResetAll();
            _s.Keys = new Dictionary<string, string>();
            _keyStatus.Text = "";
            Changed();
        }));
        _pages[2] = Page(controls.ToArray());

        var autosave = new[] { ("Off", 0), ("Every 30 s", 30), ("Every minute", 60), ("Every 2 minutes", 120), ("Every 5 minutes", 300) };
        _pages[3] = Page(
            Header("SAVING"),
            Choice("Autosave", Array.ConvertAll(autosave, a => a.Item1),
                () => Array.Find(autosave, a => a.Item2 == _s.AutosaveSeconds).Item1 ?? "Every minute",
                v => _s.AutosaveSeconds = Array.Find(autosave, a => a.Item1 == v).Item2),
            Header("ON SCREEN"),
            Check("Money floating over depots as they sell", () => _s.ShowIncomePopups, v => _s.ShowIncomePopups = v),
            Check("Key hints above the hotbar", () => _s.ShowKeyHints, v => _s.ShowKeyHints = v),
            Check("Pop-ups for orders and goals", () => _s.ShowNotifications, v => _s.ShowNotifications = v),
            Header("TUTORIAL"),
            Ui.TextButton("Show the tutorial again next time", () =>
            {
                _s.TutorialDone = false;
                Changed();
            }));

        foreach (var page in _pages) col.AddChild(page);
        ShowPage(0);
        Keybinds.Changed += RefreshBindings;
        Root = col;
    }

    /// <summary>Raised when another tab is shown (the window resizes to it).</summary>
    public event Action? PageChanged;

    public void ShowPage(int page)
    {
        for (int i = 0; i < _pages.Length; i++)
        {
            _pages[i].Visible = i == page;
            _tabs[i]?.SetPressedNoSignal(i == page);
        }
        StopListening();
        PageChanged?.Invoke();
    }

    private static Control Page(params Control[] rows)
    {
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 8);
        foreach (var r in rows) col.AddChild(r);
        return Ui.Pad(col, 18, 14);
    }

    /// <summary>A key binding row: the action and a button with its key; click it and press a new key.</summary>
    private Control Binding(Keybinds.Action action)
    {
        var row = new HBoxContainer();
        var name = Ui.Label(action.Label, 14);
        name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.AddChild(name);
        var button = new Button { Text = Keybinds.Name(action.Id), CustomMinimumSize = new Vector2(130, 34), FocusMode = Control.FocusModeEnum.All };
        button.Pressed += () =>
        {
            StopListening();
            _listening = button;
            button.Text = "Press a key…";
            _keyStatus.Text = "Press the new key, or Esc to keep the old one.";
            button.GrabFocus();
        };
        button.GuiInput += ev =>
        {
            if (_listening != button || ev is not InputEventKey { Pressed: true } key) return;
            button.AcceptEvent(); // the key is the answer, not a shortcut
            _listening = null;
            if (key.Keycode == Key.Escape) _keyStatus.Text = "";
            else if (!Keybinds.Set(action.Id, key.Keycode)) _keyStatus.Text = $"{Keybinds.NameOf(key.Keycode)} can't be rebound: it has a fixed job.";
            else
            {
                _s.Keys = Keybinds.Stored();
                _keyStatus.Text = $"{action.Label}: {Keybinds.Name(action.Id)}";
                Changed();
            }
            RefreshBindings();
            button.ReleaseFocus();
        };
        button.FocusExited += () => { if (_listening == button) StopListening(); };
        _bindings[action.Id] = button;
        row.AddChild(button);
        return row;
    }

    private void StopListening()
    {
        if (_listening == null) return;
        _listening = null;
        _keyStatus.Text = "";
        RefreshBindings();
    }

    private void RefreshBindings()
    {
        foreach (var (id, button) in _bindings)
            if (button != _listening) button.Text = Keybinds.Name(id);
    }

    /// <summary>For scripted tests: starts listening for a new key for <paramref name="action"/>.</summary>
    public Button BindingButton(string action) => _bindings[action];

    private void Changed() => _changed(); // applies, and saves a moment later

    private static Label Header(string text)
    {
        var l = Ui.Label(text, 11, UiTheme.Muted);
        l.AddThemeFontOverride("font", UiTheme.Bold);
        return l;
    }

    private Control Slider(string label, Func<float> get, Action<float> set, float min = 0, float max = 1, float step = 0.05f)
    {
        var row = new HBoxContainer();
        var name = Ui.Label(label, 14);
        name.CustomMinimumSize = new Vector2(130, 0);
        row.AddChild(name);
        var slider = new HSlider { MinValue = min, MaxValue = max, Step = step, Value = get(), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, FocusMode = Control.FocusModeEnum.None, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        var value = Ui.Label($"{get() * 100:0}%", 13, UiTheme.Muted);
        value.CustomMinimumSize = new Vector2(48, 0);
        value.HorizontalAlignment = HorizontalAlignment.Right;
        slider.ValueChanged += v =>
        {
            set((float)v);
            value.Text = $"{v * 100:0}%";
            Changed();
        };
        row.AddChild(slider);
        row.AddChild(value);
        return row;
    }

    private Control Check(string label, Func<bool> get, Action<bool> set)
    {
        var c = new CheckButton { Text = label, ButtonPressed = get(), FocusMode = Control.FocusModeEnum.None };
        c.Toggled += on =>
        {
            set(on);
            Changed();
        };
        return c;
    }

    private Control Choice(string label, string[] options, Func<string> get, Action<string> set)
    {
        var row = new HBoxContainer();
        var name = Ui.Label(label, 14);
        name.CustomMinimumSize = new Vector2(130, 0);
        row.AddChild(name);
        var pick = new OptionButton { FocusMode = Control.FocusModeEnum.None, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        foreach (var o in options) pick.AddItem(o);
        pick.Selected = Math.Max(0, Array.IndexOf(options, get()));
        pick.ItemSelected += i =>
        {
            set(options[i]);
            Changed();
        };
        row.AddChild(pick);
        return row;
    }
}
