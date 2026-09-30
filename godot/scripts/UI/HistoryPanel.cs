using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace FactorySim.Client;

/// <summary>
/// The History window (idea F2): money, income or one item's production over the last hour or day,
/// drawn from the saved <see cref="HistoryLog"/>. It only draws; what is recorded, and how often,
/// lives in the core.
///
/// To remove: this file, the window and its key in <c>Hud</c>, the <c>history</c> keybind, and
/// <c>HistoryLog.cs</c> (see there).
/// </summary>
public sealed class HistoryPanel
{
    private const string MoneyKey = "$money", IncomeKey = "$income";

    public readonly Control Root;
    private readonly SearchablePicker _what;
    private readonly Button _hour, _day;
    private readonly LineGraph _graph = new();
    private readonly Label _summary;
    private string _chosen = IncomeKey;
    private bool _long;
    private string _shown = "";

    public HistoryPanel()
    {
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 6);

        var top = new HBoxContainer();
        top.AddThemeConstantOverride("separation", 6);
        _what = new SearchablePicker(key =>
        {
            if (key == null) return;
            _chosen = key;
            _shown = "";
        });
        top.AddChild(_what.Button);
        var range = new ButtonGroup();
        _hour = new Button { Text = "Hour", ToggleMode = true, ButtonGroup = range, ButtonPressed = true, FocusMode = Control.FocusModeEnum.None };
        _day = new Button { Text = "Day", ToggleMode = true, ButtonGroup = range, FocusMode = Control.FocusModeEnum.None };
        _hour.Pressed += () => { _long = false; _shown = ""; };
        _day.Pressed += () => { _long = true; _shown = ""; };
        top.AddChild(_hour);
        top.AddChild(_day);
        col.AddChild(top);

        _graph.CustomMinimumSize = new Vector2(420, 190);
        col.AddChild(_graph);
        _summary = Ui.Label("", 12, UiTheme.Muted);
        _summary.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _summary.CustomMinimumSize = new Vector2(420, 0);
        col.AddChild(_summary);
        Root = Ui.Pad(col, 14, 10);
    }

    public void Refresh(Simulation sim, Thumbnails thumbs)
    {
        var log = sim.World.Stats.History;
        var series = _long ? log.Long : log.Recent;
        string key = $"{_chosen}/{_long}/{series.Count}/{(series.Count > 0 ? series.Ticks[^1] : 0)}";
        if (key == _shown) return;
        _shown = key;

        var items = log.Recent.Items().Union(log.Long.Items()).ToList();
        _what.SetPinned(new[]
        {
            new SearchablePicker.Entry(IncomeKey, "Income", null),
            new SearchablePicker.Entry(MoneyKey, "Money", null),
        });
        _what.SetItems(items.Select(id => new SearchablePicker.Entry(id, $"{Ui.ItemName(sim.Content, id)} made", thumbs.GetItem(id))).ToList());
        _what.SetSelected(_chosen, false);
        _what.RefreshIcons(thumbs.GetItem);

        var points = new List<(double Seconds, double Value)>();
        long now = sim.World.Tick;
        for (int i = 0; i < series.Count; i++)
        {
            double? value = _chosen switch
            {
                MoneyKey => series.Money[i].ToDouble(),
                IncomeKey => series.IncomeAt(i),
                _ => series.RateAt(_chosen, i),
            };
            if (value is double v) points.Add(((series.Ticks[i] - now) / (double)Simulation.TicksPerSecond, v));
        }

        double span = (_long ? HistoryLog.LongIntervalSeconds * HistoryLog.LongCapacity : HistoryLog.RecentIntervalSeconds * HistoryLog.RecentCapacity);
        Func<double, string> format = _chosen switch
        {
            MoneyKey => v => $"${((BigNum)v).Format()}",
            IncomeKey => v => $"${((BigNum)v).Format()}/s",
            _ => v => v >= 100 ? $"{((BigNum)v).Format()}/s" : $"{v:0.##}/s",
        };
        Color color = _chosen switch { MoneyKey or IncomeKey => UiTheme.Money, _ => UiTheme.Accent };
        _graph.Show(points, span, format, color);

        string what = _chosen switch { MoneyKey => "Money in the bank", IncomeKey => "Income per second", _ => $"{Ui.ItemName(sim.Content, _chosen)} made per second" };
        int interval = _long ? HistoryLog.LongIntervalSeconds : HistoryLog.RecentIntervalSeconds;
        _summary.Text = points.Count < 2
            ? $"{what}. The first points come in every {SimHost.FormatDuration(interval)} of play; this factory has not run long enough yet."
            : $"{what}, a point every {SimHost.FormatDuration(interval)}. Time away shows as one long step, averaged over the break.";
    }
}
