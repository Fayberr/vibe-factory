using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using FactorySim.Balance;

namespace FactorySim.Client;

/// <summary>
/// The Planner window (idea F9): pick a product and a rate, and it lists every building the line needs,
/// from the ore up, what goes in, and what it earns. The arithmetic is the balance tool's
/// (<see cref="LinePlanner"/>), so this only lays it out.
///
/// To remove: this file, the window and its key in <c>Hud</c>, the <c>planner</c> keybind, and
/// <c>LinePlanner.cs</c> with its tests.
/// </summary>
public sealed class PlannerPanel
{
    public readonly Control Root;
    private readonly SearchablePicker _item;
    private readonly SpinBox _rate, _level;
    private readonly VBoxContainer _result = new();
    private readonly List<(TextureRect Rect, string Key)> _pictures = new();
    private string? _chosen;
    private string _shown = "";

    public PlannerPanel()
    {
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 6);

        var intro = Ui.Label("What a production line needs, from the ore up. Buildings at the level you pick, " +
                             "before research bonuses and polishing.", 12, UiTheme.Muted);
        intro.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        intro.CustomMinimumSize = new Vector2(400, 0);
        col.AddChild(intro);

        var grid = new GridContainer { Columns = 2 };
        grid.AddThemeConstantOverride("h_separation", 10);
        grid.AddChild(Ui.Label("Make", 14, UiTheme.Muted));
        _item = new SearchablePicker(key =>
        {
            if (key == null) return;
            _chosen = key;
            _item.SetSelected(key, false);
            _shown = "";
        });
        grid.AddChild(_item.Button);

        grid.AddChild(Ui.Label("Per second", 14, UiTheme.Muted));
        _rate = new SpinBox { MinValue = 0.05, MaxValue = 1000, Step = 0.05, Value = 1, CustomArrowStep = 0.5, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _rate.ValueChanged += _ => _shown = "";
        grid.AddChild(_rate);

        grid.AddChild(Ui.Label("Building level", 14, UiTheme.Muted));
        _level = new SpinBox { MinValue = 1, MaxValue = 9, Step = 1, Value = 1, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _level.ValueChanged += _ => _shown = "";
        grid.AddChild(_level);
        col.AddChild(grid);

        col.AddChild(new HSeparator());
        _result.AddThemeConstantOverride("separation", 2);
        col.AddChild(_result);
        Root = Ui.Pad(col, 16, 10);
    }

    /// <summary>Opens the planner on an item (from elsewhere in the HUD); ignored if the factory cannot make it.</summary>
    public void Choose(string item)
    {
        _chosen = item;
        _shown = "";
    }

    public void Refresh(Simulation sim, Thumbnails thumbs)
    {
        var world = sim.World;
        var book = LinePlanner.BookFor(world, (int)_level.Value);
        var items = LinePlanner.Plannable(book);
        if (_chosen == null || !items.Contains(_chosen)) _chosen = items.FirstOrDefault();

        string key = $"{world.UnlockedTier}/{world.Sandbox}/{_chosen}/{_rate.Value}/{_level.Value}";
        if (key != _shown)
        {
            _shown = key;
            _item.SetItems(items.Select(id => new SearchablePicker.Entry(id, book.NameOf(id), thumbs.GetItem(id))).ToList());
            _item.SetSelected(_chosen, false);
            Fill(book, _chosen);
        }

        _item.RefreshIcons(thumbs.GetItem);
        foreach (var (rect, pictureKey) in _pictures)
            if (GodotObject.IsInstanceValid(rect) && rect.Texture == null) rect.Texture = thumbs.GetItem(pictureKey);
    }

    private void Fill(RecipeBook book, string? item)
    {
        foreach (var child in _result.GetChildren()) child.QueueFree();
        _pictures.Clear();
        var plan = item == null ? null : LinePlanner.Plan(book, item, _rate.Value);
        if (plan == null)
        {
            _result.AddChild(Ui.Label("Nothing to plan yet.", 13, UiTheme.Muted));
            return;
        }

        foreach (var step in plan.Steps) _result.AddChild(StepRow(book, step));

        _result.AddChild(new HSeparator());
        string Rates(IReadOnlyDictionary<string, double> rates) =>
            string.Join(", ", rates.OrderByDescending(r => r.Value).Select(r => $"{Rate(r.Value)} {book.NameOf(r.Key)}"));
        AddNote($"Takes in {Rates(plan.RawPerSecond)}.");
        if (plan.Byproducts.Count > 0) AddNote($"Also makes {Rates(plan.Byproducts)}: sell it, use it, or burn it.");
        if (plan.IncomePerSecond > 0)
        {
            string payback = double.IsFinite(plan.PaybackSeconds) ? $", paid back in {SimHost.FormatDuration(plan.PaybackSeconds)}" : "";
            AddNote($"Sold at a depot it earns ${Money(plan.IncomePerSecond)}/s. " +
                    $"About ${Money(plan.TotalCost)} to build with belts and depots{payback}.", UiTheme.Money);
        }
    }

    private Control StepRow(RecipeBook book, ChainStep step)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        row.AddChild(Picture(step.Item, 26));

        var name = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        name.AddThemeConstantOverride("separation", 0);
        name.AddChild(Ui.Label(book.NameOf(step.Item), 14));
        string belt = step.BeltLoad > 1.0001 ? $", {Math.Ceiling(step.BeltLoad - 1e-9):0} belts" : "";
        name.AddChild(Ui.Label($"{Rate(step.Rate)}{belt}", 12, UiTheme.Muted));
        row.AddChild(name);

        bool overLimit = step.Allowed is int allowed && step.Built > allowed;
        var count = Ui.Label($"{step.Built}× {step.Building.Name}", 14, overLimit ? Palette.Danger : UiTheme.Text);
        count.HorizontalAlignment = HorizontalAlignment.Right;
        var right = new VBoxContainer { SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        right.AddThemeConstantOverride("separation", 0);
        right.AddChild(count);
        string detail = overLimit ? $"your tier allows {step.Allowed}"
            : step.Built - step.Buildings > 0.05 ? $"{step.Buildings:0.#} busy" : "";
        if (detail.Length > 0)
        {
            var d = Ui.Label(detail, 12, overLimit ? Palette.Danger : UiTheme.Muted);
            d.HorizontalAlignment = HorizontalAlignment.Right;
            right.AddChild(d);
        }
        row.AddChild(right);
        return row;
    }

    private void AddNote(string text, Color? color = null)
    {
        var note = Ui.Label(text, 13, color ?? UiTheme.Text);
        note.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        note.CustomMinimumSize = new Vector2(400, 0);
        _result.AddChild(note);
    }

    private TextureRect Picture(string item, int size)
    {
        var rect = new TextureRect
        {
            CustomMinimumSize = new Vector2(size, size),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        _pictures.Add((rect, item));
        return rect;
    }

    private static string Money(double value) => ((BigNum)value).Format();

    /// <summary>"2/s", "0.75/s", "1.2K/s".</summary>
    private static string Rate(double perSecond) =>
        perSecond >= 1000 ? $"{((BigNum)perSecond).Format()}/s"
        : Math.Abs(perSecond - Math.Round(perSecond)) < 0.005 ? $"{perSecond:0}/s"
        : $"{perSecond:0.##}/s";
}
