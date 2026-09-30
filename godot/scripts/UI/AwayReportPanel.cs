using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using FactorySim.Content;
using FactorySim.View;

namespace FactorySim.Client;

/// <summary>
/// "While you were away": what the offline catch-up found (<see cref="OfflineReport"/>). The money, the
/// products that earned it, and the buildings that sat waiting, each with a button that shows one of them.
/// It only formats the report, so how it is measured lives in the core (<c>AwayReport.cs</c>).
/// </summary>
public sealed class AwayReportPanel
{
    /// <summary>Products listed before the rest are summed up as "and N more".</summary>
    private const int MaxItems = 6;

    public readonly Control Root;
    private readonly VBoxContainer _col = new();
    private readonly Action<int> _show;
    private readonly List<(TextureRect Rect, string Key)> _pictures = new();

    public AwayReportPanel(Action<int> show)
    {
        _show = show;
        _col.AddThemeConstantOverride("separation", 0);
        Root = _col;
    }

    public void Fill(OfflineReport report, ContentRegistry content, Thumbnails thumbs)
    {
        foreach (var child in _col.GetChildren()) child.QueueFree();
        _pictures.Clear();

        var head = new VBoxContainer();
        head.AddThemeConstantOverride("separation", 2);
        head.AddChild(Ui.Label($"You were away for {SimHost.FormatDuration(report.ElapsedSeconds)}.", 14, UiTheme.Muted));
        var money = Ui.Label(report.Earned.IsZero ? "The factory earned nothing." : $"The factory earned ${report.Earned.Format()}", 22,
            report.Earned.IsZero ? UiTheme.Text : UiTheme.Money);
        money.AddThemeFontOverride("font", UiTheme.Bold);
        head.AddChild(money);
        if (!report.IncomePerSecond.IsZero) head.AddChild(Ui.Label($"at ${report.IncomePerSecond.Format()}/s", 13, UiTheme.Muted));
        foreach (var (item, count) in report.Science)
            head.AddChild(Ui.Label($"Labs banked {count} {Name(content, item)}.", 14));
        if (report.ExtrapolatedTicks > 0)
        {
            var note = Ui.Label($"The first {SimHost.FormatDuration(report.SimulatedTicks / (double)Simulation.TicksPerSecond)} ran in full; " +
                                "the rest is worked out from how they went.", 12, UiTheme.Muted);
            note.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            note.CustomMinimumSize = new Vector2(380, 0);
            head.AddChild(note);
        }
        _col.AddChild(Ui.Pad(head, 16, 10));

        AddSection("PRODUCTS");
        var items = report.Items.Where(i => content.Items.ContainsKey(i.Item)).ToList();
        if (items.Count == 0) _col.AddChild(Ui.Pad(Ui.Label("Nothing was made.", 13, UiTheme.Muted), 16, 6));
        foreach (var item in items.Take(MaxItems)) _col.AddChild(ItemRow(item, report, content, thumbs));
        if (items.Count > MaxItems) _col.AddChild(Ui.Pad(Ui.Label($"and {items.Count - MaxItems} more", 12, UiTheme.Muted), 16, 4));
        if (report.Rewards > BigNum.Zero) _col.AddChild(RewardsRow(report));

        AddSection("WHAT HELD IT BACK");
        if (report.Problems.Count == 0)
            _col.AddChild(Ui.Pad(Ui.Label("Nothing sat waiting for long.", 13, Palette.Ok), 16, 6));
        foreach (var problem in report.Problems) _col.AddChild(ProblemRow(problem, content, Picture, _show));
        _col.AddChild(Ui.Pad(new Control(), 0, 4));
        Refresh(thumbs);
    }

    /// <summary>Thumbnails render in the background, so pictures that were not ready yet are filled in later.</summary>
    public void Refresh(Thumbnails thumbs)
    {
        foreach (var (rect, key) in _pictures)
            if (GodotObject.IsInstanceValid(rect) && rect.Texture == null)
                rect.Texture = key.StartsWith("item:", StringComparison.Ordinal) ? thumbs.GetItem(key[5..]) : thumbs.Get(key);
    }

    private void AddSection(string title)
    {
        _col.AddChild(new HSeparator());
        var label = Ui.Label(title, 12, UiTheme.Muted);
        label.AddThemeFontOverride("font", UiTheme.Bold);
        _col.AddChild(Ui.Pad(label, 16, 6));
    }

    private Control ItemRow(AwayItem item, OfflineReport report, ContentRegistry content, Thumbnails thumbs)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        row.AddChild(Picture("item:" + item.Item, 32));

        var text = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        text.AddThemeConstantOverride("separation", 0);
        var name = Ui.Label(Name(content, item.Item), 15);
        name.AddThemeFontOverride("font", UiTheme.Bold);
        text.AddChild(name);
        string rates = item.SoldPerMinute > 0 && Math.Abs(item.SoldPerMinute - item.MadePerMinute) > 0.05
            ? $"{Rate(item.MadePerMinute)} made, {Rate(item.SoldPerMinute)} sold"
            : item.SoldPerMinute > 0 ? $"{Rate(item.SoldPerMinute)} made and sold"
            : item.MadePerMinute > 0 ? $"{Rate(item.MadePerMinute)} made, all used in the factory"
            : "sold early on";
        text.AddChild(Ui.Label(rates, 12, UiTheme.Muted));
        row.AddChild(text);

        if (item.Earned > BigNum.Zero) row.AddChild(Earnings(item.Earned, report.Earned));
        return Ui.Pad(row, 16, 5);
    }

    /// <summary>Money no product accounts for: order and goal rewards, as a line of its own so the list adds up.</summary>
    private static Control RewardsRow(OfflineReport report)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        row.AddChild(new Control { CustomMinimumSize = new Vector2(32, 32) });
        var text = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        text.AddThemeConstantOverride("separation", 0);
        var name = Ui.Label("Orders and goals", 15);
        name.AddThemeFontOverride("font", UiTheme.Bold);
        text.AddChild(name);
        text.AddChild(Ui.Label("rewards, on top of the sales", 12, UiTheme.Muted));
        row.AddChild(text);
        row.AddChild(Earnings(item: report.Rewards, total: report.Earned));
        return Ui.Pad(row, 16, 5);
    }

    /// <summary>"$1.2K" over "30% of it", right aligned.</summary>
    private static Control Earnings(BigNum item, BigNum total)
    {
        var right = new VBoxContainer { SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        right.AddThemeConstantOverride("separation", 0);
        var earned = Ui.Label("$" + item.Format(), 15, UiTheme.Money);
        earned.HorizontalAlignment = HorizontalAlignment.Right;
        right.AddChild(earned);
        if (total > BigNum.Zero)
        {
            var share = Ui.Label($"{Math.Min(1, (item / total).ToDouble()) * 100:0}% of it", 12, UiTheme.Muted);
            share.HorizontalAlignment = HorizontalAlignment.Right;
            right.AddChild(share);
        }
        return right;
    }

    /// <summary>
    /// One group of waiting buildings: picture, "3× Smelter", the reason and how long, a hint at the fix, and a
    /// Show button. Shared with the live Bottlenecks window so both read the same.
    /// </summary>
    internal static Control ProblemRow(AwayProblem problem, ContentRegistry content, Func<string, int, TextureRect> picture, Action<int> showEntity)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        row.AddChild(picture(problem.Building, 36));

        var text = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        text.AddThemeConstantOverride("separation", 1);
        string building = content.Buildings.TryGetValue(problem.Building, out var def) ? def.Name : problem.Building;
        var name = Ui.Label(problem.Count == 1 ? building : $"{problem.Count}× {building}", 15);
        name.AddThemeFontOverride("font", UiTheme.Bold);
        text.AddChild(name);
        string detail = problem.Detail.Length > 0 ? char.ToUpperInvariant(problem.Detail[0]) + problem.Detail[1..] : "Waiting";
        var status = new EntityStatus(false, 0, problem.Detail, problem.Reason);
        text.AddChild(Ui.Label($"{detail}, {problem.IdleShare * 100:0}% of the time", 13, Ui.StatusColor(status)));
        var hint = Ui.Label(problem.Reason == IdleReason.Blocked
            ? "Its output has nowhere to go: more depots, a faster belt or another machine after it."
            : "It is faster than its supply: more drills or machines before it.", 12, UiTheme.Muted);
        hint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        hint.CustomMinimumSize = new Vector2(250, 0);
        text.AddChild(hint);
        row.AddChild(text);

        var show = new Button { Text = "Show", ThemeTypeVariation = "FlatButton", FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(64, 34), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        show.AddThemeFontSizeOverride("font_size", 13);
        show.TooltipText = problem.Count == 1 ? "Select it and move the camera there" : "Select the one that waited longest and move the camera there";
        int id = problem.ExampleId;
        show.Pressed += () => showEntity(id);
        row.AddChild(show);
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

    private static string Name(ContentRegistry content, string item) =>
        content.Items.TryGetValue(item, out var def) ? def.Name : item;

    /// <summary>"60/min", "7.5/min", "1.2K/min".</summary>
    private static string Rate(double perMinute) =>
        perMinute >= 1000 ? $"{((BigNum)perMinute).Format()}/min"
        : perMinute >= 10 ? $"{perMinute:0}/min"
        : $"{perMinute:0.#}/min";
}
