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
        "economy" => "Economy",
        _ => char.ToUpperInvariant(category[0]) + category[1..],
    };

    public static readonly string[] CategoryOrder = { "logistics", "production", "processing", "economy" };
}

/// <summary>A building tile: rendered thumbnail, name, cost, optional hotkey badge.</summary>
public sealed class BuildingTile
{
    public readonly Button Button;
    private readonly TextureRect _image;
    private readonly Label _key;
    private readonly Label _placeholder;
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

        Name = Ui.Label("", showName ? 13 : 11);
        Name.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
        Name.HorizontalAlignment = HorizontalAlignment.Center;
        Name.OffsetTop = showName ? -42 : -19;
        Name.OffsetBottom = -4;
        Name.ClipText = true;
        Button.AddChild(Name);
    }

    public Label Name { get; }

    public void Set(BuildingDef? def, Texture2D? image, string key, bool showName)
    {
        Def = def;
        _key.Text = key;
        _image.Texture = image;
        _placeholder.Text = def == null ? "" : image == null ? def.MetaOr("glyph", "?") is { Length: 1 } g ? g : def.Name[..1] : "";
        Name.Text = def == null ? "" : showName ? $"{def.Name}\n${def.Cost.Format()}" : $"${def.Cost.Format()}";
        Button.TooltipText = def == null ? "Empty slot — hover a building in the build menu (B) and press a number to assign it" : $"{def.Name} — ${def.Cost.Format()}\n{def.MetaOr("description", "")}";
        Button.Modulate = def == null ? new Color(1, 1, 1, 0.45f) : Colors.White;
    }
}

/// <summary>Categorised catalogue of all buildings (B).</summary>
public sealed class BuildMenu
{
    public readonly PanelContainer Root;
    private readonly List<BuildingTile> _tiles = new();
    public BuildingDef? Hovered { get; private set; }

    public BuildMenu(ContentRegistry content, Thumbnails thumbs, Action<BuildingDef> onPick, Func<BuildingDef, string> keyOf)
    {
        var body = new VBoxContainer();
        var header = new HBoxContainer();
        header.AddChild(Ui.Label("Build", 22));
        header.AddChild(Ui.Spacer());
        header.AddChild(Ui.Label("Click to build · hover + 1–0 to put on the hotbar", 13, UiTheme.Muted));
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
        thumbs.Updated += id => Refresh(thumbs, keyOf);
    }

    public void Refresh(Thumbnails thumbs, Func<BuildingDef, string> keyOf)
    {
        foreach (var t in _tiles)
            if (t.Def != null) t.Set(t.Def, thumbs.Get(t.Def.Id), keyOf(t.Def), true);
    }
}

/// <summary>Details and actions for the current selection.</summary>
public sealed class InspectorPanel
{
    public readonly PanelContainer Root;
    private readonly TextureRect _image = new() { CustomMinimumSize = new Vector2(64, 64), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered };
    private readonly Label _title = Ui.Label("", 19);
    private readonly Label _subtitle = Ui.Label("", 13, UiTheme.Muted);
    private readonly ColorRect _dot = new() { CustomMinimumSize = new Vector2(10, 10), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
    private readonly Label _status = Ui.Label("", 14);
    private readonly ProgressBar _progress = new() { CustomMinimumSize = new Vector2(0, 6), ShowPercentage = false, MaxValue = 1 };
    private readonly GridContainer _details = new() { Columns = 2 };
    private readonly List<InfoLine> _lines = new();

    public InspectorPanel(Action rotate, Action move, Action copy, Action delete)
    {
        var body = new VBoxContainer { CustomMinimumSize = new Vector2(290, 0) };
        var head = new HBoxContainer();
        head.AddChild(_image);
        var titles = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, Alignment = BoxContainer.AlignmentMode.Center };
        titles.AddChild(_title);
        titles.AddChild(_subtitle);
        head.AddChild(titles);
        body.AddChild(head);

        var status = new HBoxContainer();
        status.AddChild(_dot);
        status.AddChild(_status);
        body.AddChild(status);
        body.AddChild(_progress);
        body.AddChild(new HSeparator());
        body.AddChild(_details);

        var actions = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        actions.AddChild(Ui.IconButton(Icon.Rotate, "Rotate (R)", rotate, 36));
        actions.AddChild(Ui.IconButton(Icon.Move, "Move (M)", move, 36));
        actions.AddChild(Ui.IconButton(Icon.Copy, "Copy & paste (C) · Ctrl+C copies", copy, 36));
        var del = Ui.IconButton(Icon.Delete, "Delete (Del)", delete, 36);
        del.Modulate = new Color("#ff9c9c");
        actions.AddChild(del);
        body.AddChild(actions);
        Root = Ui.Panel(body);
    }

    public void Show(IReadOnlyList<Entity> selection, Thumbnails thumbs)
    {
        Root.Visible = selection.Count > 0;
        if (selection.Count == 0) return;
        _lines.Clear();

        if (selection.Count == 1)
        {
            var e = selection[0];
            var status = e.Behavior.GetStatus(e);
            _image.Texture = thumbs.Get(e.Def.Id);
            _image.Visible = true;
            _title.Text = e.Def.Name;
            _subtitle.Text = $"{Ui.CategoryName(e.Def.Category)} · layer {e.Pos.Z} · facing {e.Facing}";
            _dot.Color = status.Working ? Palette.Ok : status.Detail == "idle" || status.Detail == "empty" ? new Color("#f2c14e") : Palette.Danger;
            _status.Text = string.IsNullOrEmpty(status.Detail) ? (status.Working ? "Working" : "Idle") : char.ToUpperInvariant(status.Detail[0]) + status.Detail[1..];
            _progress.Visible = status.Progress > 0;
            _progress.Value = status.Progress;
            e.Behavior.Describe(e, _lines);
        }
        else
        {
            _image.Visible = false;
            _title.Text = $"{selection.Count} buildings";
            _subtitle.Text = "Selection";
            _dot.Color = UiTheme.Accent;
            _status.Text = $"Value ${selection.Aggregate(BigNum.Zero, (sum, e) => sum + e.Def.Cost).Format()}";
            _progress.Visible = false;
            foreach (var g in selection.GroupBy(e => e.Def.Name).OrderByDescending(g => g.Count()).Take(8))
                _lines.Add(new InfoLine(g.Key, $"×{g.Count()}"));
        }

        while (_details.GetChildCount() < _lines.Count * 2)
        {
            _details.AddChild(Ui.Label("", 13, UiTheme.Muted));
            var value = Ui.Label("", 13);
            value.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            value.CustomMinimumSize = new Vector2(190, 0);
            _details.AddChild(value);
        }
        for (int i = 0; i < _details.GetChildCount(); i++)
        {
            var label = (Label)_details.GetChild(i);
            int line = i / 2;
            label.Visible = line < _lines.Count;
            if (line < _lines.Count) label.Text = i % 2 == 0 ? _lines[line].Label : _lines[line].Value;
        }
    }
}

/// <summary>Upgrade shop (U).</summary>
public sealed class UpgradesPanel
{
    public readonly PanelContainer Root;
    private readonly List<(UpgradeDef Def, Label Info, Button Buy)> _rows = new();

    public UpgradesPanel(ContentRegistry content, Action<UpgradeDef> buy)
    {
        var body = new VBoxContainer { CustomMinimumSize = new Vector2(300, 0) };
        body.AddChild(Ui.Label("Upgrades", 19));
        foreach (var up in content.UpgradeList)
        {
            var row = new HBoxContainer();
            var text = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            text.AddChild(Ui.Label(up.Name, 15));
            var info = Ui.Label("", 12, UiTheme.Muted);
            text.AddChild(info);
            row.AddChild(text);
            var b = Ui.TextButton("", () => buy(up));
            b.CustomMinimumSize = new Vector2(96, 36);
            row.AddChild(b);
            body.AddChild(row);
            _rows.Add((up, info, b));
        }
        Root = Ui.Panel(body);
    }

    public void Refresh(World world)
    {
        foreach (var (def, info, buy) in _rows)
        {
            int level = world.UpgradeLevel(def.Id);
            bool maxed = def.MaxLevel is int max && level >= max;
            string Effect(int lv) => def.Effect == UpgradeEffectKind.Multiply
                ? $"×{Math.Pow(def.PerLevel, lv):0.##}"
                : $"+{def.PerLevel * lv:0.##}";
            info.Text = maxed ? $"Lv {level} · {Effect(level)} (max)" : $"Lv {level} · {Effect(level)} → {Effect(level + 1)}";
            var cost = def.CostForLevel(level);
            buy.Text = maxed ? "Max" : $"${cost.Format()}";
            buy.Disabled = maxed || (!world.Sandbox && world.Money < cost);
        }
    }
}

/// <summary>Production statistics.</summary>
public sealed class StatsPanel
{
    public readonly PanelContainer Root;
    private readonly Label _text = Ui.Label("", 14);

    public StatsPanel()
    {
        var body = new VBoxContainer { CustomMinimumSize = new Vector2(300, 0) };
        body.AddChild(Ui.Label("Statistics", 19));
        body.AddChild(_text);
        Root = Ui.Panel(body);
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
