using System;
using System.Collections.Generic;
using System.Linq;
using FactorySim.Content;
using FactorySim.View;
using Godot;

namespace FactorySim.Client;

/// <summary>
/// The Alerts window (idea F4): the <see cref="AlertLog"/> as a list, newest first, each entry with a
/// Show button (buildings) or an Orders button (orders) and a dismiss cross. Entries that fixed themselves
/// stay, greyed and marked as such, until dismissed or pushed out.
///
/// To remove: this file, the window, its key and badge in <c>Hud</c>, the <c>alerts</c> keybind, the
/// <c>Bell</c> icon, and <c>Alerts.cs</c> in the core (see there).
/// </summary>
public sealed class AlertPanel
{
    public readonly Control Root;
    private readonly VBoxContainer _list = new();
    private readonly Action<int> _show;
    private readonly Action _orders;
    private readonly List<(TextureRect Rect, string Key)> _pictures = new();
    private string _shown = "";

    public AlertPanel(Action<int> show, Action orders)
    {
        _show = show;
        _orders = orders;
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 0);

        var head = new HBoxContainer();
        var intro = Ui.Label($"Machines that stopped for {AlertLog.StoppedSeconds} seconds or more, and orders running out.", 12, UiTheme.Muted);
        intro.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        intro.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        intro.CustomMinimumSize = new Vector2(300, 0);
        head.AddChild(intro);
        var clear = new Button { Text = "Clear all", ThemeTypeVariation = "FlatButton", FocusMode = Control.FocusModeEnum.None, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        clear.AddThemeFontSizeOverride("font_size", 13);
        clear.Pressed += () => Log?.Clear();
        head.AddChild(clear);
        col.AddChild(Ui.Pad(head, 16, 8));
        col.AddChild(new HSeparator());

        _list.AddThemeConstantOverride("separation", 0);
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, CustomMinimumSize = new Vector2(420, 0) };
        scroll.AddChild(_list);
        _list.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        col.AddChild(scroll);
        _scroll = scroll;
        Root = col;
    }

    private readonly ScrollContainer _scroll;
    private AlertLog? Log { get; set; }

    public void Refresh(Simulation sim, AlertLog log, Thumbnails thumbs)
    {
        Log = log;
        log.MarkSeen();
        long now = sim.World.Tick;
        // Rebuild when the list changes, or once a minute for the "ago" labels.
        string key = $"{log.Version}/{now / (60 * Simulation.TicksPerSecond)}";
        if (key != _shown)
        {
            _shown = key;
            foreach (var child in _list.GetChildren()) child.QueueFree();
            _pictures.Clear();
            if (log.Entries.Count == 0)
                _list.AddChild(Ui.Pad(Ui.Label("Nothing to report. The factory runs.", 13, Palette.Ok), 16, 8));
            foreach (var alert in log.Entries)
                _list.AddChild(Row(alert, sim.Content, now, log));
            // Up to five rows without scrolling, then scroll.
            _scroll.CustomMinimumSize = new Vector2(420, Math.Min(Math.Max(1, log.Entries.Count), 5) * 62);
        }

        foreach (var (rect, pictureKey) in _pictures)
            if (GodotObject.IsInstanceValid(rect) && rect.Texture == null)
                rect.Texture = pictureKey.StartsWith("item:", StringComparison.Ordinal) ? thumbs.GetItem(pictureKey[5..]) : thumbs.Get(pictureKey);
    }

    /// <summary>The line a toast shows for a new alert.</summary>
    public static string Headline(Alert alert, ContentRegistry content) => alert.Kind switch
    {
        AlertKind.Stopped or AlertKind.Jammed => $"{Subject(alert, content)} {(alert.Kind == AlertKind.Jammed ? "jammed" : "stopped")}" +
                                                 (alert.Detail.Length > 0 ? $": {alert.Detail}" : ""),
        AlertKind.OrderEnding => $"Order ending soon: {Goods(alert.Order, content)}",
        _ => $"Order ran out of time: {Goods(alert.Order, content)}",
    };

    private static string Subject(Alert alert, ContentRegistry content)
    {
        string name = content.Buildings.TryGetValue(alert.Building, out var def) ? def.Name : alert.Building;
        return alert.Count == 1 ? name : $"{alert.Count}× {name}";
    }

    private static string Goods(Contract? order, ContentRegistry content) => order == null ? "" :
        string.Join(", ", order.Lines().Select(l => $"{l.Delivered}/{l.Quantity} {(content.Items.TryGetValue(l.Item, out var item) ? item.Name : l.Item)}"));

    private Control Row(Alert alert, ContentRegistry content, long now, AlertLog log)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        bool building = alert.Kind is AlertKind.Stopped or AlertKind.Jammed;
        row.AddChild(Picture(building ? alert.Building : "item:" + (alert.Order?.Item ?? ""), 36));

        var text = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        text.AddThemeConstantOverride("separation", 1);
        string title = alert.Kind switch
        {
            AlertKind.Stopped => $"{Subject(alert, content)} stopped",
            AlertKind.Jammed => $"{Subject(alert, content)} jammed",
            AlertKind.OrderEnding => "Order ending soon",
            _ => "Order ran out of time",
        };
        var name = Ui.Label(title, 15, alert.Resolved ? UiTheme.Muted : UiTheme.Text);
        name.AddThemeFontOverride("font", UiTheme.Bold);
        text.AddChild(name);

        string detail = building
            ? (alert.Detail.Length > 0 ? char.ToUpperInvariant(alert.Detail[0]) + alert.Detail[1..] : "Waiting")
            : Goods(alert.Order, content);
        Color color = alert.Resolved ? Palette.Ok
            : alert.Kind == AlertKind.Jammed || alert.Kind == AlertKind.OrderExpired ? Palette.Danger
            : Palette.Waiting;
        string ago = SimHost.FormatDuration(Math.Max(0, now - alert.Tick) / (double)Simulation.TicksPerSecond);
        string state = !alert.Resolved ? "" : building ? ", running again" : ", delivered";
        var line = Ui.Label($"{detail}{state} · {ago} ago", 13, color);
        line.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        line.CustomMinimumSize = new Vector2(220, 0);
        text.AddChild(line);
        row.AddChild(text);

        if (building || alert.Kind == AlertKind.OrderEnding)
        {
            var go = new Button
            {
                Text = building ? "Show" : "Orders",
                ThemeTypeVariation = "FlatButton",
                FocusMode = Control.FocusModeEnum.None,
                CustomMinimumSize = new Vector2(64, 34),
                SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            };
            go.AddThemeFontSizeOverride("font_size", 13);
            int id = alert.ExampleId;
            go.TooltipText = building ? "Select it and move the camera there" : "Open the orders";
            go.Pressed += building ? () => _show(id) : _orders;
            row.AddChild(go);
        }

        var dismiss = new Button { Text = "×", ThemeTypeVariation = "FlatButton", FocusMode = Control.FocusModeEnum.None, TooltipText = "Dismiss", SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        int alertId = alert.Id;
        dismiss.Pressed += () => log.Dismiss(alertId);
        row.AddChild(dismiss);
        return Ui.Pad(row, 16, 6);
    }

    private TextureRect Picture(string key, int size)
    {
        var rect = new TextureRect
        {
            CustomMinimumSize = new Vector2(size, size),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        _pictures.Add((rect, key));
        return rect;
    }
}
