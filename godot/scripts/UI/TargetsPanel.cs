using System;
using System.Collections.Generic;
using System.Linq;
using FactorySim.Content;
using Godot;

namespace FactorySim.Client;

/// <summary>
/// The Targets window (idea F7): one row per production target with what the factory makes now, a bar,
/// − and + to move the target a round step, and a cross to drop it; a picker below adds one. Every change
/// is a <see cref="SetTarget"/> command through the edit history, so undo covers it.
///
/// To remove: this file, the window, its key and badge in <c>Hud</c>, the <c>targets</c> keybind, the
/// <c>Target</c> icon, the Targets button in <c>AlertPanel</c>, and <c>ProductionTargets.cs</c> in the core
/// (see there).
/// </summary>
public sealed class TargetsPanel
{
    public readonly Control Root;
    private readonly Func<Command, CommandResult> _execute;
    private readonly Action<string> _report;
    private readonly VBoxContainer _list = new();
    private readonly SearchablePicker _add;
    private readonly Label _full;
    private readonly List<Row> _rows = new();
    private string _shown = "", _addShown = "";
    private World? _world;

    private sealed record Row(string Item, TextureRect Picture, Label Rate, ProgressBar Bar);

    private static readonly StyleBoxFlat MetFill = Fill(Palette.Ok), UnderFill = Fill(Palette.Waiting), MeasuringFill = Fill(UiTheme.Muted);

    private static StyleBoxFlat Fill(Color color) => new() { BgColor = color, CornerRadiusTopLeft = 3, CornerRadiusTopRight = 3, CornerRadiusBottomLeft = 3, CornerRadiusBottomRight = 3 };

    public TargetsPanel(Func<Command, CommandResult> execute, Action<string> report)
    {
        _execute = execute;
        _report = report;
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 6);

        var intro = Ui.Label($"Items a minute to aim for, counted over the last minute. A target that stays under for " +
                             $"{AlertLog.TargetSeconds} seconds shows up in Alerts.", 12, UiTheme.Muted);
        intro.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        intro.CustomMinimumSize = new Vector2(380, 0);
        col.AddChild(intro);

        _list.AddThemeConstantOverride("separation", 4);
        col.AddChild(_list);
        col.AddChild(new HSeparator());

        var add = new HBoxContainer();
        add.AddThemeConstantOverride("separation", 10);
        add.AddChild(Ui.Label("Add a target for", 14, UiTheme.Muted));
        _add = new SearchablePicker(key =>
        {
            if (key == null || _world == null) return;
            Do(new SetTarget(key, ProductionTargets.Suggest(_world, key)));
        });
        add.AddChild(_add.Button);
        col.AddChild(add);
        _full = Ui.Label($"That is the most targets at once ({ProductionTargets.MaxTargets}). Drop one to add another.", 12, UiTheme.Muted);
        _full.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        col.AddChild(_full);
        Root = Ui.Pad(col, 16, 10);
    }

    /// <summary>Targets the factory is short of now, for the sidebar badge.</summary>
    public static int UnderCount(World world) => ProductionTargets.ReadAll(world).Count(r => r.Status == TargetStatus.Under);

    public void Refresh(Simulation sim, Thumbnails thumbs)
    {
        var world = _world = sim.World;
        var readings = ProductionTargets.ReadAll(world);

        string key = string.Join(",", readings.Select(r => r.Item));
        if (key != _shown)
        {
            _shown = key;
            foreach (var child in _list.GetChildren()) child.QueueFree();
            _rows.Clear();
            if (readings.Count == 0)
                _list.AddChild(Ui.Label("No targets yet. Pick an item below.", 13, UiTheme.Muted));
            foreach (var r in readings) _list.AddChild(BuildRow(r.Item, sim.Content));
        }

        foreach (var (row, r) in _rows.Zip(readings))
        {
            string made = ProductionTargets.Format(r.PerMinute), target = ProductionTargets.Format(r.Target);
            row.Rate.Text = r.Status == TargetStatus.Measuring ? $"Measuring... / {target} a minute" : $"{made} / {target} a minute";
            row.Rate.AddThemeColorOverride("font_color", r.Status switch
            {
                TargetStatus.Met => Palette.Ok,
                TargetStatus.Under => Palette.Waiting,
                _ => UiTheme.Muted,
            });
            row.Bar.Value = r.Share;
            row.Bar.AddThemeStyleboxOverride("fill", r.Status switch
            {
                TargetStatus.Met => MetFill,
                TargetStatus.Under => UnderFill,
                _ => MeasuringFill,
            });
            if (row.Picture.Texture == null) row.Picture.Texture = thumbs.GetItem(row.Item);
        }

        // The picker lists what the factory can make by now, less what already has a target.
        var content = sim.Content;
        var items = content.ItemValue.Where(kv => (world.Sandbox || kv.Value.Tier <= world.UnlockedTier) && !world.Targets.ContainsKey(kv.Key))
            .OrderBy(kv => kv.Value.Tier).ThenBy(kv => content.Items[kv.Key].Name, StringComparer.Ordinal)
            .Select(kv => kv.Key).ToList();
        string addKey = string.Join(",", items);
        if (addKey != _addShown)
        {
            _addShown = addKey;
            _add.SetItems(items.Select(id => new SearchablePicker.Entry(id, content.Items[id].Name, thumbs.GetItem(id))).ToList());
            _add.SetSelected(null, false);
        }
        _add.RefreshIcons(thumbs.GetItem);
        bool full = world.Targets.Count >= ProductionTargets.MaxTargets;
        _add.Button.Disabled = full;
        _full.Visible = full;
    }

    /// <summary>For scripted tests: the rows' rate lines, top to bottom.</summary>
    public IEnumerable<string> RateLines => _rows.Select(r => r.Rate.Text);

    private Control BuildRow(string item, ContentRegistry content)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        var picture = new TextureRect
        {
            CustomMinimumSize = new Vector2(30, 30),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        row.AddChild(picture);

        var text = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        text.AddThemeConstantOverride("separation", 1);
        var name = Ui.Label(Ui.ItemName(content, item), 14);
        name.AddThemeFontOverride("font", UiTheme.Bold);
        text.AddChild(name);
        var rate = Ui.Label("", 12, UiTheme.Muted);
        text.AddChild(rate);
        var bar = new ProgressBar { CustomMinimumSize = new Vector2(0, 6), ShowPercentage = false, MaxValue = 1 };
        text.AddChild(bar);
        row.AddChild(text);

        row.AddChild(Small("−", "Lower the target a step", () => StepTarget(item, -1)));
        row.AddChild(Small("+", "Raise the target a step", () => StepTarget(item, 1)));
        row.AddChild(Small("×", "Drop this target", () => Do(new SetTarget(item, null))));
        _rows.Add(new Row(item, picture, rate, bar));
        return row;
    }

    private void StepTarget(string item, int direction)
    {
        if (_world == null || !_world.Targets.TryGetValue(item, out double now)) return;
        double next = ProductionTargets.Step(now, direction);
        if (Math.Abs(next - now) > 1e-9) Do(new SetTarget(item, next));
    }

    private void Do(Command command)
    {
        var result = _execute(command);
        if (!result.Ok && result.Error != null) _report(result.Error);
    }

    private static Button Small(string text, string tip, Action pressed)
    {
        var button = new Button
        {
            Text = text,
            TooltipText = tip,
            ThemeTypeVariation = "FlatButton",
            FocusMode = Control.FocusModeEnum.None,
            CustomMinimumSize = new Vector2(30, 30),
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        button.Pressed += pressed;
        return button;
    }
}
