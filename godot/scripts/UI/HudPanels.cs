using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using FactorySim.Content;
using FactorySim.View;

namespace FactorySim.Client;

/// <summary>Small factory functions for consistent HUD widgets.</summary>
public static class Ui
{
    public static T Anchor<T>(T c, float ax, float ay, float ox, float oy, Control.GrowDirection gh, Control.GrowDirection gv) where T : Control
    {
        c.AnchorLeft = c.AnchorRight = ax;
        c.AnchorTop = c.AnchorBottom = ay;
        c.OffsetLeft = c.OffsetRight = ox;
        c.OffsetTop = c.OffsetBottom = oy;
        c.GrowHorizontal = gh;
        c.GrowVertical = gv;
        return c;
    }

    public static Label Label(string text, int size = 15, Color? color = null)
    {
        var l = new Label { Text = text, MouseFilter = Control.MouseFilterEnum.Ignore };
        if (size != 15) l.AddThemeFontSizeOverride("font_size", size);
        if (color is { } c) l.AddThemeColorOverride("font_color", c);
        return l;
    }

    public static Button IconButton(Icon icon, string tooltip, Action onPressed, int size = 40, bool toggle = false)
    {
        var b = new Button
        {
            CustomMinimumSize = new Vector2(size, size),
            FocusMode = Control.FocusModeEnum.None,
            TooltipText = tooltip,
            ToggleMode = toggle,
        };
        var iconView = new IconView { Icon = icon };
        iconView.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        iconView.OffsetLeft = iconView.OffsetTop = 8;
        iconView.OffsetRight = iconView.OffsetBottom = -8;
        b.AddChild(iconView);
        b.Pressed += onPressed;
        return b;
    }

    public static Button TextButton(string text, Action onPressed, string? tooltip = null)
    {
        var b = new Button { Text = text, FocusMode = Control.FocusModeEnum.None, TooltipText = tooltip ?? "" };
        b.Pressed += onPressed;
        return b;
    }

    public static PanelContainer Panel(Control content)
    {
        var p = new PanelContainer();
        p.AddChild(content);
        return p;
    }

    /// <summary>Wraps <paramref name="content"/> in padding.</summary>
    public static MarginContainer Pad(Control content, int x, int y)
    {
        // Stretch like the content does, so expanding content actually gets the room.
        var m = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore, SizeFlagsHorizontal = content.SizeFlagsHorizontal };
        m.AddThemeConstantOverride("margin_left", x);
        m.AddThemeConstantOverride("margin_right", x);
        m.AddThemeConstantOverride("margin_top", y);
        m.AddThemeConstantOverride("margin_bottom", y);
        m.AddChild(content);
        return m;
    }

    public static Control Spacer(bool horizontal = true) => new Control
    {
        SizeFlagsHorizontal = horizontal ? Control.SizeFlags.ExpandFill : Control.SizeFlags.Fill,
        SizeFlagsVertical = horizontal ? Control.SizeFlags.Fill : Control.SizeFlags.ExpandFill,
        MouseFilter = Control.MouseFilterEnum.Ignore,
    };

    public static string CategoryName(string category) => category switch
    {
        "logistics" => "Logistics",
        "production" => "Production",
        "processing" => "Processing",
        "crafting" => "Crafting",
        "economy" => "Economy",
        _ => char.ToUpperInvariant(category[0]) + category[1..],
    };

    public static readonly string[] CategoryOrder = { "logistics", "production", "processing", "crafting", "economy" };

    /// <summary>Why a building can't be placed right now (tier lock or build limit), or null.</summary>
    public static string? Blocker(Simulation sim, BuildingDef def) => sim.LockReason(def) ?? sim.LimitReason(def);

    /// <summary>"3/6" for limited buildings, "" otherwise.</summary>
    public static string LimitText(World world, BuildingDef def) =>
        world.LimitOf(def) is int max ? $"{world.CountOf(def.Id)}/{max}" : "";
}

/// <summary>A building tile: rendered thumbnail, name, cost, optional hotkey badge.</summary>
public sealed class BuildingTile
{
    public readonly Button Button;
    private readonly TextureRect _image;
    private readonly Label _key;
    private readonly Label _placeholder;
    private readonly Label _limit;
    private readonly IconView _lock;
    public BuildingDef? Def { get; private set; }

    public BuildingTile(Vector2 size, bool showName)
    {
        Button = new Button { CustomMinimumSize = size, FocusMode = Control.FocusModeEnum.None, ToggleMode = true, ClipContents = true };
        _placeholder = Ui.Label("", 26, UiTheme.Muted);
        _placeholder.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _placeholder.HorizontalAlignment = HorizontalAlignment.Center;
        _placeholder.VerticalAlignment = VerticalAlignment.Center;
        Button.AddChild(_placeholder);

        _image = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _image.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _image.OffsetLeft = _image.OffsetRight = 0;
        _image.OffsetTop = 2;
        _image.OffsetBottom = showName ? -42 : -16;
        Button.AddChild(_image);

        _key = Ui.Label("", 12, UiTheme.Muted);
        _key.Position = new Vector2(6, 3);
        Button.AddChild(_key);

        _limit = Ui.Label("", 11, UiTheme.Muted);
        _limit.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        _limit.HorizontalAlignment = HorizontalAlignment.Right;
        _limit.OffsetLeft = -60;
        _limit.OffsetRight = -6;
        _limit.OffsetTop = 3;
        Button.AddChild(_limit);

        _lock = new IconView { Icon = Icon.Lock, Color = UiTheme.Muted, Visible = false };
        _lock.SetAnchorsPreset(Control.LayoutPreset.Center);
        _lock.OffsetLeft = _lock.OffsetTop = -14;
        _lock.OffsetRight = _lock.OffsetBottom = 14;
        Button.AddChild(_lock);

        Name = Ui.Label("", showName ? 13 : 11);
        Name.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
        Name.HorizontalAlignment = HorizontalAlignment.Center;
        Name.OffsetTop = showName ? -42 : -19;
        Name.OffsetBottom = -4;
        Name.ClipText = true;
        Button.AddChild(Name);
    }

    public Label Name { get; }

    public void Set(BuildingDef? def, Texture2D? image, string key, bool showName, Simulation? sim = null)
    {
        Def = def;
        _key.Text = key;
        _image.Texture = image;
        _placeholder.Text = def == null ? "" : image == null ? def.MetaOr("glyph", "?") is { Length: 1 } g ? g : def.Name[..1] : "";
        Name.Text = def == null ? "" : showName ? $"{def.Name}\n${def.Cost.Format()}" : $"${def.Cost.Format()}";

        string? locked = def != null && sim != null ? sim.LockReason(def) : null;
        string? full = def != null && sim != null && locked == null ? sim.LimitReason(def) : null;
        _limit.Text = def != null && sim != null && locked == null ? Ui.LimitText(sim.World, def) : "";
        _limit.AddThemeColorOverride("font_color", full != null ? Palette.Danger : UiTheme.Muted);
        _lock.Visible = locked != null;
        _image.Modulate = locked != null ? new Color(0.35f, 0.38f, 0.45f, 0.8f) : Colors.White;

        string limitLine = def?.Limit != null && sim != null ? $"\nLimit {Ui.LimitText(sim.World, def)}. Later tiers allow more." : "";
        Button.TooltipText = def == null
            ? "Empty slot. Hover a building in the build menu (B) and press a number to assign it"
            : $"{def.Name}  ${def.Cost.Format()}\n{def.MetaOr("description", "")}{limitLine}" + (locked != null ? $"\nLocked: {locked}" : "");
        Button.Modulate = def == null ? new Color(1, 1, 1, 0.45f) : Colors.White;
    }
}

/// <summary>Categorised catalogue of all buildings (B).</summary>
public sealed class BuildMenu
{
    public readonly PanelContainer Root;
    private readonly List<BuildingTile> _tiles = new();
    public BuildingDef? Hovered { get; private set; }

    private Simulation? _sim;

    public BuildMenu(ContentRegistry content, Thumbnails thumbs, Action<BuildingDef> onPick, Func<BuildingDef, string> keyOf)
    {
        var body = new VBoxContainer();
        var header = new HBoxContainer();
        header.AddChild(Ui.Label("Build", 22));
        header.AddChild(Ui.Spacer());
        header.AddChild(Ui.Label("Click to build · hover + 1 to 0 to put on the hotbar · locked ones unlock in Progress (P)", 13, UiTheme.Muted));
        body.AddChild(header);

        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(740, 520), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        var list = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        scroll.AddChild(list);
        body.AddChild(scroll);

        var categories = Ui.CategoryOrder.Concat(content.BuildingList.Select(b => b.Category)).Distinct();
        foreach (var cat in categories)
        {
            var defs = content.BuildingList.Where(b => b.Category == cat).ToList();
            if (defs.Count == 0) continue;
            list.AddChild(Ui.Label(Ui.CategoryName(cat).ToUpperInvariant(), 13, UiTheme.Muted));
            var grid = new GridContainer { Columns = 5 };
            list.AddChild(grid);
            foreach (var def in defs)
            {
                var tile = new BuildingTile(new Vector2(138, 168), showName: true);
                tile.Set(def, thumbs.Get(def.Id), keyOf(def), true);
                tile.Button.ToggleMode = false;
                tile.Button.Pressed += () => onPick(def);
                tile.Button.MouseEntered += () => Hovered = def;
                tile.Button.MouseExited += () => { if (Hovered == def) Hovered = null; };
                grid.AddChild(tile.Button);
                _tiles.Add(tile);
            }
        }
        Root = Ui.Panel(body);
        Root.Visible = false;
        thumbs.Updated += id => Refresh(thumbs, keyOf, _sim);
    }

    public void Refresh(Thumbnails thumbs, Func<BuildingDef, string> keyOf, Simulation? sim)
    {
        _sim = sim;
        foreach (var t in _tiles)
            if (t.Def != null) t.Set(t.Def, thumbs.Get(t.Def.Id), keyOf(t.Def), true, sim);
    }
}

/// <summary>Tiers (P): what the next one unlocks, how far along it is, and the build limits.</summary>
public sealed class ProgressPanel
{
    public readonly Control Root;
    private readonly Label _current = Ui.Label("", 14, UiTheme.Muted);
    private readonly Label _nextName = Ui.Label("", 19);
    private readonly Label _nextInfo = Ui.Label("", 13, UiTheme.Muted);
    private readonly ProgressBar _earned = new() { CustomMinimumSize = new Vector2(0, 8), ShowPercentage = false, MaxValue = 1 };
    private readonly Label _earnedText = Ui.Label("", 12, UiTheme.Muted);
    private readonly Button _unlock;
    private readonly Label _limits = Ui.Label("", 13);
    private readonly VBoxContainer _nextBox = new();

    public ProgressPanel(Action unlock)
    {
        var body = new VBoxContainer();
        body.AddChild(_current);
        body.AddChild(new HSeparator());

        _nextBox.AddChild(Ui.Label("NEXT TIER", 11, UiTheme.Muted));
        _nextBox.AddChild(_nextName);
        _nextInfo.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _nextInfo.CustomMinimumSize = new Vector2(300, 0);
        _nextBox.AddChild(_nextInfo);
        _nextBox.AddChild(_earned);
        _nextBox.AddChild(_earnedText);
        _unlock = Ui.TextButton("", unlock);
        _unlock.CustomMinimumSize = new Vector2(0, 40);
        _nextBox.AddChild(_unlock);
        body.AddChild(_nextBox);

        body.AddChild(new HSeparator());
        body.AddChild(Ui.Label("BUILD LIMITS", 11, UiTheme.Muted));
        body.AddChild(_limits);
        body.AddChild(Ui.Label("Upgrades are per building: select one and press U.", 12, UiTheme.Muted));
        Root = Ui.Pad(body, 14, 12);
    }

    public void Refresh(Simulation sim)
    {
        var world = sim.World;
        var tiers = world.Content.Tiers;
        int t = world.UnlockedTier;
        _current.Text = $"Tier {t} · {tiers[t].Name} · plot {world.Bounds.Max.X - world.Bounds.Min.X + 1}×{world.Bounds.Max.Y - world.Bounds.Min.Y + 1}";

        _nextBox.Visible = t + 1 < tiers.Count;
        if (t + 1 < tiers.Count)
        {
            var next = tiers[t + 1];
            var unlocks = world.Content.BuildingList.Where(b => b.Tier == t + 1).Select(b => b.Name);
            _nextName.Text = $"{t + 1} · {next.Name}";
            _nextInfo.Text = $"{next.Description}\nUnlocks: {string.Join(", ", unlocks)}\nPlot grows to {next.PlotSize}×{next.PlotSize}, build limits rise.";
            double need = next.RequiredEarnings.ToDouble(), have = world.Stats.TotalEarned.ToDouble();
            _earned.Value = need <= 0 ? 1 : Math.Min(1, have / need);
            _earnedText.Text = $"Lifetime earnings ${world.Stats.TotalEarned.Format()} / ${next.RequiredEarnings.Format()}";
            bool earned = world.Sandbox || have >= need;
            bool afford = world.Sandbox || world.Money >= next.Cost;
            _unlock.Text = !earned ? $"Earn ${(next.RequiredEarnings - world.Stats.TotalEarned).Format()} more" : $"Unlock for ${next.Cost.Format()}";
            _unlock.Disabled = !earned || !afford;
        }

        var lines = world.Content.BuildingList
            .Where(b => b.Limit != null && b.Tier <= t)
            .Select(b => $"{b.Name,-16} {Ui.LimitText(world, b)}");
        _limits.Text = string.Join("\n", lines);
    }
}

/// <summary>Production statistics.</summary>
public sealed class StatsPanel
{
    public readonly Control Root;
    private readonly Label _text = Ui.Label("", 14);

    public StatsPanel()
    {
        Root = Ui.Pad(_text, 14, 12);
    }

    public void Refresh(World world)
    {
        var s = world.Stats;
        var lines = new List<string>
        {
            $"Income (10 s):   ${s.IncomePerSecond(10).Format()}/s",
            $"Income (60 s):   ${s.IncomePerSecond().Format()}/s",
            $"Lifetime:        ${s.TotalEarned.Format()}",
            $"Buildings:       {world.EntityCount}",
            "",
            "Sold",
        };
        foreach (var (item, count) in s.Sold.OrderByDescending(kv => kv.Value))
            lines.Add($"  {world.Content.Items.GetValueOrDefault(item)?.Name ?? item}: {count}");
        _text.Text = string.Join("\n", lines);
    }
}

/// <summary>Temporary messages under the top bar.</summary>
public sealed class Toasts
{
    public readonly VBoxContainer Root = new() { MouseFilter = Control.MouseFilterEnum.Ignore, Alignment = BoxContainer.AlignmentMode.Begin };

    public void Show(Node owner, string text)
    {
        while (Root.GetChildCount() >= 4) Root.GetChild(0).Free();
        var label = Ui.Label(text, 14);
        var panel = Ui.Panel(label);
        panel.MouseFilter = Control.MouseFilterEnum.Ignore;
        panel.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
        Root.AddChild(panel);
        var tween = owner.CreateTween();
        tween.TweenInterval(3.5);
        tween.TweenProperty(panel, "modulate:a", 0f, 0.5);
        tween.TweenCallback(Callable.From(() => { if (GodotObject.IsInstanceValid(panel)) panel.QueueFree(); }));
    }
}
