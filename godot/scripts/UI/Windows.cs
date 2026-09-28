using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using FactorySim.Behaviors;
using FactorySim.Content;
using FactorySim.View;

namespace FactorySim.Client;

/// <summary>
/// A HUD window: title bar with an icon, optional action buttons and a close button. Drag
/// it by the title bar; any number can be open at once. Sections inside run edge to edge.
/// </summary>
public sealed class HudWindow
{
    public readonly PanelContainer Root;
    public readonly VBoxContainer Body;
    private readonly ScrollContainer _scroll;
    private bool _watchingScreen;
    private readonly HBoxContainer _headerRow;
    private readonly Label _title;
    private bool _dragging;
    private Vector2 _grab;

    /// <summary>True once the window has a position (its default, or where the player dragged it).</summary>
    public bool Placed { get; set; }

    /// <summary>Stay against the right screen edge as the content changes (until the player drags it).</summary>
    public bool KeepRight { get; set; }

    /// <summary>Whether Esc closes this window (the tutorial must not vanish on a stray Esc).</summary>
    public bool EscCloses { get; init; } = true;

    /// <summary>Raised when the close button is pressed (the window hides itself first).</summary>
    public event Action? Closed;

    /// <summary>Raised when the window is shown, hidden or brought to the front.</summary>
    public event Action? Activated;

    public HudWindow(string title, Icon icon, float width)
    {
        Root = new PanelContainer { ThemeTypeVariation = "HudWindowPanel", CustomMinimumSize = new Vector2(width, 0), Visible = false };
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 0);
        Root.AddChild(col);

        var header = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Stop, MouseDefaultCursorShape = Control.CursorShape.Move };
        header.AddThemeStyleboxOverride("panel", UiTheme.Box(new Color(0, 0, 0, 0), 0, 14, 8));
        _headerRow = new HBoxContainer();
        header.AddChild(_headerRow);
        _headerRow.AddChild(new IconView { Icon = icon, CustomMinimumSize = new Vector2(30, 30), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter });
        _title = Ui.Label(title, 24);
        _title.AddThemeFontOverride("font", UiTheme.Bold);
        _headerRow.AddChild(_title);
        _headerRow.AddChild(Ui.Spacer());
        _headerRow.AddChild(Ui.IconButton(Icon.Close, "Close", Close, 36));
        header.GuiInput += OnHeaderInput;
        col.AddChild(header);
        col.AddChild(new HSeparator());

        // The body scrolls once it is taller than the screen (long lists, a large interface size).
        _scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        Body = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        Body.AddThemeConstantOverride("separation", 0);
        _scroll.AddChild(Body);
        col.AddChild(_scroll);
        Body.MinimumSizeChanged += FitHeight;
        Root.TreeEntered += () =>
        {
            if (!_watchingScreen) Root.GetViewport().SizeChanged += FitHeight;
            _watchingScreen = true;
            FitHeight();
        };
    }

    /// <summary>Room left for the body: the screen height minus the title bar and a margin.</summary>
    private void FitHeight()
    {
        float room = Root.IsInsideTree() ? Root.GetViewportRect().Size.Y - 150 : float.MaxValue;
        float want = Body.GetCombinedMinimumSize().Y;
        float height = Mathf.Min(want, Mathf.Max(160, room));
        if (Mathf.IsEqualApprox(_scroll.CustomMinimumSize.Y, height)) return;
        _scroll.CustomMinimumSize = new Vector2(0, height);
        if (Root.IsInsideTree()) Callable.From(Fit).CallDeferred();
    }

    public string Title
    {
        set => _title.Text = value;
    }

    public bool Visible
    {
        get => Root.Visible;
        set
        {
            if (Root.Visible == value) return;
            Root.Visible = value;
            if (value) Root.MoveToFront();
            AudioManager.Instance?.Play(value ? "open" : "close");
            Activated?.Invoke();
        }
    }

    public void Toggle() => Visible = !Visible;

    public void Close()
    {
        Visible = false;
        Closed?.Invoke();
    }

    /// <summary>Adds a small action button to the title bar, left of the close button.</summary>
    public Button AddHeaderButton(Icon icon, string tooltip, Action action)
    {
        var b = Ui.IconButton(icon, tooltip, action, 36);
        _headerRow.AddChild(b);
        _headerRow.MoveChild(b, _headerRow.GetChildCount() - 2);
        return b;
    }

    /// <summary>Shrinks the window to its content and keeps it on screen.</summary>
    public void Fit()
    {
        if (!Root.IsInsideTree()) return;
        Root.ResetSize();
        if (KeepRight) Root.Position = Root.Position with { X = Root.GetViewportRect().Size.X - Root.Size.X - 12 };
        Root.Position = Clamp(Root.Position);
    }

    private void OnHeaderInput(InputEvent ev)
    {
        switch (ev)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } mb:
                _dragging = mb.Pressed;
                _grab = mb.GlobalPosition - Root.Position;
                Placed = true;
                KeepRight = false;
                Root.MoveToFront();
                Activated?.Invoke();
                Root.AcceptEvent();
                break;
            case InputEventMouseMotion mm when _dragging:
                Root.Position = Clamp(mm.GlobalPosition - _grab);
                Root.AcceptEvent();
                break;
        }
    }

    private Vector2 Clamp(Vector2 p)
    {
        var screen = Root.GetViewportRect().Size;
        var size = Root.Size;
        // The title bar never leaves the screen; a window may be pushed partly below the bottom edge.
        return new Vector2(Mathf.Clamp(p.X, 0, Mathf.Max(0, screen.X - size.X)), Mathf.Clamp(p.Y, 0, Mathf.Max(0, screen.Y - 64)));
    }
}

/// <summary>Icon, label and value in one row (Level 6 · Value $2.58M · Speed ×6).</summary>
public sealed class StatLine
{
    public readonly HBoxContainer Root = new() { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
    private readonly IconView _icon;
    private readonly Label _label;
    private readonly Label _value;

    public StatLine(Icon icon, string label)
    {
        _icon = new IconView { Icon = icon, CustomMinimumSize = new Vector2(20, 20), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        Root.AddChild(_icon);
        _label = Ui.Label(label, 14);
        _label.AddThemeFontOverride("font", UiTheme.Bold);
        Root.AddChild(_label);
        _value = Ui.Label("", 14, UiTheme.Muted);
        _value.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _value.HorizontalAlignment = HorizontalAlignment.Right;
        _value.ClipText = true;
        _value.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        Root.AddChild(_value);
    }

    public void Set(string label, string value, Color? color = null)
    {
        _label.Text = label;
        _value.Text = value;
        _value.AddThemeColorOverride("font_color", color ?? UiTheme.Muted);
        _value.TooltipText = value;
    }

    public bool Visible
    {
        set => Root.Visible = value;
    }
}

/// <summary>
/// "Manage" window for the selection: a card with the building (picture, name, description,
/// level, value, speed, status), a big Upgrade button with Delete beside it, and for machines
/// a grid to choose what to produce, with the chosen item's parts, value and time.
/// Several selected buildings of one kind can be upgraded or set together.
/// </summary>
public sealed class ManageWindow
{
    public readonly HudWindow Window;

    private readonly Action _upgrade;
    private readonly Action _delete;
    private readonly Action<string?> _choose;

    private readonly TextureRect _image = new()
    {
        CustomMinimumSize = new Vector2(112, 112),
        ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
        StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
    };
    private readonly Label _name = Ui.Label("", 20);
    private readonly Label _description = Ui.Label("", 13, UiTheme.Muted);
    private readonly StatLine _level = new(Icon.Star, "Level");
    private readonly StatLine _value = new(Icon.Coin, "Value");
    private readonly StatLine _speed = new(Icon.Gauge, "Speed");
    private readonly StatLine _status = new(Icon.Clock, "Status");
    private readonly ProgressBar _progress = new() { CustomMinimumSize = new Vector2(0, 4), ShowPercentage = false, MaxValue = 1 };
    private readonly Button _upgradeButton;
    private readonly Button _deleteButton;

    private readonly VBoxContainer _produce = new();
    private readonly Label _produceTitle = Ui.Label("", 15);
    private readonly Control _tilesSection;
    private readonly GridContainer _tiles = new() { Columns = 4 };
    private readonly List<(string? Recipe, Button Button, TextureRect Image, Label Name)> _tileList = new();
    private string? _tilesKey;
    private string? _hoverRecipe;
    private bool _hovering;

    private readonly TextureRect _itemImage = new()
    {
        CustomMinimumSize = new Vector2(104, 104),
        ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
        StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
    };
    private readonly Label _itemName = Ui.Label("", 18);
    private readonly HBoxContainer _parts = new();
    private readonly Label _noParts = Ui.Label("None", 16, UiTheme.Muted);
    private string? _partsKey;
    private readonly StatLine _itemValue = new(Icon.Coin, "Value");
    private readonly StatLine _itemTime = new(Icon.Clock, "Time");
    private readonly Button _deselect;

    private readonly GridContainer _info = new() { Columns = 2 };
    private readonly Control _infoSection;
    private readonly List<InfoLine> _lines = new();

    private static readonly HashSet<string> CoveredLines = new() { "Producing", "Recipe", "Produces", "Rate" };
    private static readonly double RawShare = new SellerParams().RawMultiplier;

    public ManageWindow(Action upgrade, Action delete, Action<string?> choose, Action rotate, Action move, Action copy, Action close)
    {
        _upgrade = upgrade;
        _delete = delete;
        _choose = choose;
        Window = new HudWindow("Manage", Icon.Search, 420) { KeepRight = true };
        Window.AddHeaderButton(Icon.Rotate, "Rotate (R)", rotate);
        Window.AddHeaderButton(Icon.Move, "Move (M)", move);
        Window.AddHeaderButton(Icon.Copy, "Copy & paste (C)", copy);
        Window.Closed += close;
        var body = Window.Body;

        // Card: picture | name, description, stats.
        var card = new HBoxContainer();
        card.AddThemeConstantOverride("separation", 0);
        card.AddChild(Ui.Pad(_image, 6, 6));
        card.AddChild(new VSeparator());
        var right = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        right.AddThemeConstantOverride("separation", 4);
        _name.AddThemeFontOverride("font", UiTheme.Bold);
        right.AddChild(_name);
        _description.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _description.CustomMinimumSize = new Vector2(250, 0);
        _name.ClipText = true;
        _name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        right.AddChild(_description);
        right.AddChild(new HSeparator());
        var stats = new GridContainer { Columns = 2 };
        stats.AddThemeConstantOverride("h_separation", 18);
        stats.AddThemeConstantOverride("v_separation", 4);
        foreach (var s in new[] { _level, _value, _speed }) stats.AddChild(s.Root);
        right.AddChild(stats);
        right.AddChild(_status.Root);
        card.AddChild(Ui.Pad(right, 12, 10));
        body.AddChild(card);
        body.AddChild(_progress);

        // Upgrade | Delete.
        var actions = new HBoxContainer();
        actions.AddThemeConstantOverride("separation", 0);
        _upgradeButton = new Button { ThemeTypeVariation = "PrimaryButton", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 48) };
        _upgradeButton.Pressed += () => _upgrade();
        actions.AddChild(_upgradeButton);
        _deleteButton = new Button { ThemeTypeVariation = "FlatButton", Text = "Delete", FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(120, 48), TooltipText = "Delete (Del). Refunds everything invested." };
        _deleteButton.Pressed += () => _delete();
        actions.AddChild(_deleteButton);
        body.AddChild(actions);

        // What it produces: choice grid, then the chosen item's details.
        _produceTitle.AddThemeFontOverride("font", UiTheme.Bold);
        _produceTitle.HorizontalAlignment = HorizontalAlignment.Center;
        _produce.AddThemeConstantOverride("separation", 0);
        _produce.AddChild(Ui.Pad(_produceTitle, 8, 8));
        var tiles = new VBoxContainer();
        tiles.AddThemeConstantOverride("separation", 0);
        tiles.AddChild(new HSeparator());
        _tiles.AddThemeConstantOverride("h_separation", 0);
        _tiles.AddThemeConstantOverride("v_separation", 0);
        tiles.AddChild(_tiles);
        _tilesSection = tiles;
        _produce.AddChild(tiles);
        _produce.AddChild(new HSeparator());

        var detail = new HBoxContainer();
        detail.AddThemeConstantOverride("separation", 0);
        detail.AddChild(Ui.Pad(_itemImage, 10, 8));
        detail.AddChild(new VSeparator());
        var info = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        info.AddThemeConstantOverride("separation", 4);
        _itemName.AddThemeFontOverride("font", UiTheme.Bold);
        _itemName.ClipText = true;
        _itemName.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        info.AddChild(_itemName);
        info.AddChild(Ui.Label("Parts:", 13, UiTheme.Muted));
        _parts.AddThemeConstantOverride("separation", 10);
        _parts.CustomMinimumSize = new Vector2(0, 40);
        info.AddChild(_parts);
        _noParts.HorizontalAlignment = HorizontalAlignment.Center;
        info.AddChild(_noParts);
        info.AddChild(new HSeparator());
        var footer = new HBoxContainer();
        footer.AddThemeConstantOverride("separation", 12);
        _itemValue.Root.SizeFlagsStretchRatio = 1.35f; // prices need a bit more room than times
        footer.AddChild(_itemValue.Root);
        footer.AddChild(_itemTime.Root);
        info.AddChild(footer);
        detail.AddChild(Ui.Pad(info, 12, 8));
        _produce.AddChild(detail);
        _produce.AddChild(new HSeparator());
        _deselect = new Button { ThemeTypeVariation = "FlatButton", Text = "Deselect (back to automatic)", FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 44) };
        _deselect.Pressed += () => _choose(null);
        _produce.AddChild(_deselect);
        body.AddChild(_produce);

        // Anything else worth knowing (buffers, earnings, belt speed).
        var infoBox = new VBoxContainer();
        infoBox.AddThemeConstantOverride("separation", 0);
        infoBox.AddChild(new HSeparator());
        _info.AddThemeConstantOverride("h_separation", 14);
        _info.AddThemeConstantOverride("v_separation", 2);
        infoBox.AddChild(Ui.Pad(_info, 14, 8));
        _infoSection = infoBox;
        body.AddChild(infoBox);
    }

    /// <summary>The choice tile for <paramref name="recipe"/> (null = Automatic), if shown. For scripted tests.</summary>
    public Button? TileFor(string? recipe) => _tileList.FirstOrDefault(t => t.Recipe == recipe).Button;

    public Button UpgradeButton => _upgradeButton;

    public void Show(Simulation sim, IReadOnlyList<Entity> selection, Thumbnails thumbs)
    {
        Window.Visible = selection.Count > 0;
        if (selection.Count == 0) return;
        var world = sim.World;
        _lines.Clear();

        var first = selection[0];
        bool sameKind = selection.All(e => e.Def == first.Def);
        var upgradable = selection.Where(e => e.Def.Upgrade?.CanUpgrade(e.Level) == true).ToList();
        var cost = upgradable.Aggregate(BigNum.Zero, (s, e) => s + e.Def.Upgrade!.UpgradeCost(e.Def, e.Level));
        var cheapest = upgradable.Count == 0 ? BigNum.Zero : upgradable.Min(e => e.Def.Upgrade!.UpgradeCost(e.Def, e.Level).ToDouble());
        _upgradeButton.Text = upgradable.Count == 0 ? "Max level"
            : selection.Count == 1 ? $"Upgrade (${cost.Format()})"
            : $"Upgrade {upgradable.Count} (${cost.Format()})";
        _upgradeButton.Disabled = upgradable.Count == 0 || (!world.Sandbox && world.Money < cheapest);
        string key = Keybinds.Name("upgrade");
        _upgradeButton.TooltipText = selection.Count == 1 ? $"Upgrade ({key})" : $"Upgrade all ({key}): cheapest first, as far as the money goes";
        _deleteButton.Text = selection.Count == 1 ? "Delete" : "Delete all";

        if (selection.Count == 1) ShowOne(sim, first, thumbs);
        else ShowMany(world, selection, thumbs, sameKind);

        // Production choice for machines (several of one kind are set together).
        bool processor = sameKind && first.Def.Params is ProcessorParams;
        bool miner = selection.Count == 1 && first.Def.Params is MinerParams;
        _produce.Visible = processor || miner;
        if (processor) ShowRecipes(sim, first, selection, thumbs);
        else if (miner) ShowMined(sim, first, thumbs);

        for (int i = _lines.Count - 1; i >= 0; i--)
            if (CoveredLines.Contains(_lines[i].Label) && (processor || miner)) _lines.RemoveAt(i);
        ShowInfo();
        Window.Fit();
    }

    private void ShowOne(Simulation sim, Entity e, Thumbnails thumbs)
    {
        var track = e.Def.Upgrade;
        var status = e.Behavior.GetStatus(e);
        Window.Title = "Manage";
        _image.Texture = thumbs.Get(e.Def.Id);
        _name.Text = e.Def.Name;
        _description.Text = e.Def.MetaOr("description", Ui.CategoryName(e.Def.Category));
        _level.Set("Level", track?.MaxLevel is int max ? $"{e.Level} / {max}" : $"{e.Level}");
        _value.Set("Value", "$" + sim.World.InvestedIn(e).Format());
        if (e.Def.Params is SellerParams) _speed.Set("Price", $"×{e.ValueFactor:0.##}");
        else _speed.Set("Speed", $"×{e.SpeedFactor:0.##}");
        string text = string.IsNullOrEmpty(status.Detail) ? (status.Working ? "Working" : "Idle") : char.ToUpperInvariant(status.Detail[0]) + status.Detail[1..];
        _status.Set("Status", text, status.Working ? Palette.Ok : status.Detail is "idle" or "empty" ? new Color("#f2c14e") : Palette.Danger);
        _progress.Visible = status.Progress > 0;
        _progress.Value = status.Progress;
        e.Behavior.Describe(e, _lines);
    }

    private void ShowMany(World world, IReadOnlyList<Entity> selection, Thumbnails thumbs, bool sameKind)
    {
        var groups = selection.GroupBy(e => e.Def).OrderByDescending(g => g.Count()).ToList();
        Window.Title = "Manage";
        _image.Texture = thumbs.Get(groups[0].Key.Id);
        _name.Text = sameKind ? $"{selection.Count}× {groups[0].Key.Name}" : $"{selection.Count} buildings";
        _description.Text = string.Join(" · ", groups.Take(4).Select(g => $"{g.Count()}× {g.Key.Name}")) + (groups.Count > 4 ? " · …" : "");
        int lo = selection.Min(e => e.Level), hi = selection.Max(e => e.Level);
        _level.Set("Level", lo == hi ? $"{lo}" : $"{lo} to {hi}");
        _value.Set("Value", "$" + selection.Aggregate(BigNum.Zero, (s, e) => s + world.InvestedIn(e)).Format());
        _speed.Set("Speed", sameKind ? $"×{selection.Average(e => e.SpeedFactor):0.##}" : "mixed");
        int working = selection.Count(e => e.Behavior.GetStatus(e).Working);
        _status.Set("Working", $"{working} / {selection.Count}", working > 0 ? Palette.Ok : UiTheme.Muted);
        _progress.Visible = false;
        foreach (var g in groups.Take(8)) _lines.Add(new InfoLine(g.Key.Name, $"×{g.Count()}"));
    }

    private void ShowRecipes(Simulation sim, Entity e, IReadOnlyList<Entity> selection, Thumbnails thumbs)
    {
        var p = (ProcessorParams)e.Def.Params!;
        var recipes = p.Recipes.Select(id => sim.Content.Recipes[id]).ToList();
        _produceTitle.Text = selection.Count == 1 ? "Choose what to produce" : $"Choose what all {selection.Count} produce";
        _tilesSection.Visible = true;
        var choices = new List<(string? Recipe, string? Item)> { (null, null) };
        choices.AddRange(recipes.Select(r => ((string?)r.Id, (string?)r.Outputs[0].Item)));
        BuildTiles(e.Def.Id, choices, sim.Content, thumbs);

        var chosen = selection.Select(x => x.Behavior.Selection(x)).Distinct().ToList();
        string? common = chosen.Count == 1 ? chosen[0] : "\0"; // "\0": mixed, nothing highlighted
        foreach (var (tileRecipe, button, image, name) in _tileList)
        {
            button.SetPressedNoSignal(tileRecipe == common);
            name.AddThemeColorOverride("font_color", tileRecipe == common ? UiTheme.Primary : UiTheme.Text);
            if (tileRecipe != null) image.Texture = thumbs.GetItem(sim.Content.Recipes[tileRecipe].Outputs[0].Item);
        }
        _deselect.Visible = chosen.Any(x => x != null);

        // Details: the hovered choice, else the chosen one, else what it is making now.
        var running = (e.State as ProcessorState)?.Recipe;
        string? shown = _hovering ? _hoverRecipe : common is { } picked && picked != "\0" ? picked : running;
        var recipe = shown != null ? sim.Content.Recipes[shown] : recipes[0];
        var output = recipe.Outputs[0];
        var item = sim.Content.Items[output.Item];
        bool auto = !_hovering && common == null;
        _itemImage.Texture = thumbs.GetItem(item.Id);
        _itemName.Text = (output.Count > 1 ? $"{item.Name} ×{output.Count}" : item.Name) + (auto ? "  (automatic)" : "");
        SetParts(recipe.Inputs.Select(i => (i.Item, i.Count)).ToList(), sim.Content, thumbs);
        double each = sim.Content.ItemValue.TryGetValue(item.Id, out var v) ? v.Value * e.ValueFactor : 0;
        _itemValue.Set("Value", "$" + ((BigNum)each).Format(), UiTheme.Money);
        double seconds = recipe.Ticks / (Simulation.TicksPerSecond * e.SpeedFactor * sim.World.Stat(StatIds.MachineSpeed));
        _itemTime.Set("Time", $"{seconds:0.##}s");
    }

    private void ShowMined(Simulation sim, Entity e, Thumbnails thumbs)
    {
        var p = (MinerParams)e.Def.Params!;
        var item = sim.Content.Items[p.Item];
        _produceTitle.Text = "Produces";
        _tilesSection.Visible = false;
        _deselect.Visible = false;
        _itemImage.Texture = thumbs.GetItem(item.Id);
        _itemName.Text = p.Amount > 1 ? $"{item.Name} ×{p.Amount}" : item.Name;
        SetParts(new List<(string, int)>(), sim.Content, thumbs);
        var value = (BigNum)item.BaseValue;
        _itemValue.Set("Value", item.Raw ? $"${value.Format()} · raw ${(value * RawShare).Format()}" : "$" + value.Format(), UiTheme.Money);
        double seconds = p.Interval / (Simulation.TicksPerSecond * e.SpeedFactor * sim.World.Stat(StatIds.MinerRate));
        _itemTime.Set("Time", $"{seconds:0.##}s");
    }

    /// <summary>(Re)builds the choice tiles when a different kind of machine is shown.</summary>
    private void BuildTiles(string key, List<(string? Recipe, string? Item)> choices, ContentRegistry content, Thumbnails thumbs)
    {
        if (_tilesKey == key) return;
        _tilesKey = key;
        _hovering = false;
        foreach (var t in _tileList) t.Button.QueueFree();
        _tileList.Clear();
        foreach (var (recipe, itemId) in choices)
        {
            var button = new Button
            {
                ThemeTypeVariation = "Tile",
                ToggleMode = true,
                FocusMode = Control.FocusModeEnum.None,
                CustomMinimumSize = new Vector2(96, 104),
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                ClipContents = true,
                TooltipText = recipe == null ? "Automatic: makes whatever its ingredients allow" : "",
            };
            var image = new TextureRect
            {
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            image.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            image.OffsetLeft = image.OffsetTop = 10;
            image.OffsetRight = -10;
            image.OffsetBottom = -28;
            button.AddChild(image);
            var icon = new IconView { Icon = Icon.Auto, Visible = recipe == null };
            icon.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            icon.OffsetLeft = 30;
            icon.OffsetRight = -30;
            icon.OffsetTop = 18;
            icon.OffsetBottom = -36;
            button.AddChild(icon);
            var name = Ui.Label(itemId == null ? "Automatic" : content.Items[itemId].Name, 12);
            name.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
            name.OffsetTop = -26;
            name.OffsetBottom = -6;
            name.HorizontalAlignment = HorizontalAlignment.Center;
            name.ClipText = true;
            button.AddChild(name);
            var r = recipe;
            button.Pressed += () => _choose(r);
            button.MouseEntered += () => { _hovering = r != null; _hoverRecipe = r; };
            button.MouseExited += () => { if (_hoverRecipe == r) _hovering = false; };
            _tiles.AddChild(button);
            _tileList.Add((recipe, button, image, name));
        }
        _tiles.Columns = Math.Min(4, Math.Max(1, _tileList.Count));
    }

    /// <summary>Ingredient icons with counts ("Parts:").</summary>
    private void SetParts(List<(string Item, int Count)> parts, ContentRegistry content, Thumbnails thumbs)
    {
        string key = string.Join(",", parts.Select(x => $"{x.Item}:{x.Count}"));
        _noParts.Visible = parts.Count == 0;
        _parts.Visible = parts.Count > 0;
        if (_partsKey != key)
        {
            _partsKey = key;
            foreach (var child in _parts.GetChildren()) child.QueueFree();
            foreach (var (item, count) in parts)
            {
                var cell = new HBoxContainer { TooltipText = $"{count}× {content.Items[item].Name}", MouseFilter = Control.MouseFilterEnum.Pass };
                cell.AddThemeConstantOverride("separation", 2);
                cell.AddChild(new TextureRect
                {
                    Name = item,
                    CustomMinimumSize = new Vector2(40, 40),
                    ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                });
                if (count > 1) cell.AddChild(Ui.Label($"×{count}", 13, UiTheme.Muted));
                _parts.AddChild(cell);
            }
        }
        // Icons may arrive after the row was built.
        foreach (var cell in _parts.GetChildren().OfType<HBoxContainer>())
            if (cell.GetChildCount() > 0 && cell.GetChild(0) is TextureRect tex) tex.Texture = thumbs.GetItem(tex.Name);
    }

    private void ShowInfo()
    {
        _infoSection.Visible = _lines.Count > 0;
        while (_info.GetChildCount() < _lines.Count * 2)
        {
            _info.AddChild(Ui.Label("", 13, UiTheme.Muted));
            var value = Ui.Label("", 13);
            value.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            value.CustomMinimumSize = new Vector2(230, 0);
            _info.AddChild(value);
        }
        for (int i = 0; i < _info.GetChildCount(); i++)
        {
            var label = (Label)_info.GetChild(i);
            int line = i / 2;
            label.Visible = line < _lines.Count;
            if (line < _lines.Count) label.Text = i % 2 == 0 ? _lines[line].Label : _lines[line].Value;
        }
    }
}
