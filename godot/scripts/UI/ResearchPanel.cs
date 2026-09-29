using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using FactorySim.Behaviors;
using FactorySim.Content;

namespace FactorySim.Client;

/// <summary>
/// Research: the bank of packs that labs fill, and the upgrades it buys. Everything shown comes from
/// the content (<see cref="ContentRegistry.UpgradeList"/>, items marked <c>science</c>), so a content
/// pack without research leaves this window with nothing to show and the sidebar hides its button.
/// </summary>
public sealed class ResearchPanel
{
    public readonly Control Root;
    private readonly HFlowContainer _bank = new();
    private readonly Label _hint = Ui.Label("", 13, UiTheme.Muted);
    private readonly VBoxContainer _cards = new();
    private readonly Label _later = Ui.Label("", 12, UiTheme.Muted);
    private readonly Action<string> _buy;
    private readonly Dictionary<string, Card> _byId = new();
    private readonly List<(string Item, TextureRect Icon, Label Text)> _bankRows = new();
    private ContentRegistry? _content;

    private sealed class Card
    {
        public required Control Root;
        public required Label Level, Effect, Price;
        public required Button Buy;
    }

    public ResearchPanel(Action<string> buy)
    {
        _buy = buy;
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 0);
        _bank.AddThemeConstantOverride("h_separation", 16);
        col.AddChild(Ui.Pad(_bank, 16, 10));
        _hint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _hint.CustomMinimumSize = new Vector2(360, 0);
        col.AddChild(Ui.Pad(_hint, 16, 4));
        _cards.AddThemeConstantOverride("separation", 0);
        col.AddChild(_cards);
        col.AddChild(Ui.Pad(_later, 16, 8));
        Root = col;
    }

    /// <summary>Whether the content has anything to research at all (the sidebar button hides otherwise).</summary>
    public static bool HasResearch(ContentRegistry content) => content.UpgradeList.Count > 0;

    /// <summary>Whether research is open yet: some upgrade's tier is unlocked.</summary>
    public static bool IsOpen(World world) =>
        world.Sandbox || world.Content.UpgradeList.Any(u => u.Tier <= world.UnlockedTier);

    public void Refresh(Simulation sim, Thumbnails thumbs)
    {
        var world = sim.World;
        if (_content != sim.Content) Rebuild(sim.Content);

        foreach (var (item, icon, text) in _bankRows)
        {
            icon.Texture = thumbs.GetItem(item);
            text.Text = $"{world.ScienceOf(item)} {sim.Content.Items[item].Name}";
        }

        bool anyLab = world.Entities.Any(e => e.Behavior is LabBehavior);
        bool empty = world.Science.Values.All(v => v == 0);
        _hint.Visible = !anyLab && empty;
        _hint.Text = Hint(sim.Content);

        int hidden = 0;
        foreach (var u in sim.Content.UpgradeList)
        {
            var card = _byId[u.Id];
            bool shown = world.Sandbox || u.Tier <= world.UnlockedTier;
            card.Root.Visible = shown;
            if (!shown)
            {
                hidden++;
                continue;
            }
            Fill(card, u, world);
        }
        _later.Visible = hidden > 0;
        _later.Text = hidden == 1 ? "1 more opens with a later tier." : $"{hidden} more open with later tiers.";
    }

    private void Rebuild(ContentRegistry content)
    {
        _content = content;
        foreach (var child in _bank.GetChildren()) child.QueueFree();
        foreach (var child in _cards.GetChildren()) child.QueueFree();
        _bankRows.Clear();
        _byId.Clear();

        foreach (var item in content.Items.Values.Where(i => i.Science))
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 6);
            var icon = new TextureRect { CustomMinimumSize = new Vector2(32, 32), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered };
            var text = Ui.Label("", 16);
            text.AddThemeFontOverride("font", UiTheme.Bold);
            row.AddChild(icon);
            row.AddChild(text);
            _bank.AddChild(row);
            _bankRows.Add((item.Id, icon, text));
        }

        foreach (var u in content.UpgradeList)
        {
            var card = NewCard(u);
            _cards.AddChild(card.Root);
            _byId[u.Id] = card;
        }
    }

    private Card NewCard(UpgradeDef u)
    {
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 0);
        box.AddChild(new HSeparator());
        var text = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        text.AddThemeConstantOverride("separation", 3);

        var head = new HBoxContainer();
        var name = Ui.Label(u.Name, 16);
        name.AddThemeFontOverride("font", UiTheme.Bold);
        head.AddChild(name);
        head.AddChild(Ui.Spacer());
        var level = Ui.Label("", 12, UiTheme.Muted);
        head.AddChild(level);
        text.AddChild(head);

        if (u.Description.Length > 0)
        {
            var description = Ui.Label(u.Description, 13);
            description.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            description.CustomMinimumSize = new Vector2(250, 0);
            text.AddChild(description);
        }
        var effect = Ui.Label("", 12, UiTheme.Muted);
        text.AddChild(effect);
        var price = Ui.Label("", 13, UiTheme.Money);
        text.AddChild(price);

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);
        row.AddChild(text);
        var buy = new Button { ThemeTypeVariation = "FlatButton", FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(92, 40), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        buy.AddThemeFontSizeOverride("font_size", 13);
        buy.Pressed += () => _buy(u.Id);
        row.AddChild(buy);
        box.AddChild(Ui.Pad(row, 14, 10));
        return new Card { Root = box, Level = level, Effect = effect, Price = price, Buy = buy };
    }

    private static void Fill(Card card, UpgradeDef u, World world)
    {
        int level = world.UpgradeLevel(u.Id);
        bool maxed = u.MaxLevel is int max && level >= max;
        card.Level.Text = u.MaxLevel is int cap ? $"Level {level} / {cap}" : $"Level {level}";
        card.Effect.Text = level == 0 ? $"First level: {Bonus(u, 1)}"
            : maxed ? $"Bonus: {Bonus(u, level)}, fully researched"
            : $"Bonus: {Bonus(u, level)}, next level {Bonus(u, level + 1)}";

        if (maxed)
        {
            card.Price.Visible = false;
            card.Buy.Text = "Done";
            card.Buy.Disabled = true;
            card.Buy.TooltipText = "";
            return;
        }
        var packs = u.PacksForLevel(level);
        var money = u.CostForLevel(level);
        var parts = packs.Select(p => $"{p.Count} {world.Content.Items[p.Item].Name}").ToList();
        if (!money.IsZero || parts.Count == 0) parts.Add("$" + money.Format());
        bool afford = world.Sandbox || (world.CanPay(packs) && world.Money >= money);
        card.Price.Visible = true;
        card.Price.Text = "Costs " + string.Join(" + ", parts);
        card.Price.AddThemeColorOverride("font_color", afford ? UiTheme.Money : UiTheme.Muted);
        card.Buy.Text = "Research";
        card.Buy.Disabled = !afford;
        card.Buy.TooltipText = afford ? "" : "The lab has not banked enough packs yet";
    }

    /// <summary>The total effect at <paramref name="level"/>, in words ("+10%", "+2").</summary>
    private static string Bonus(UpgradeDef u, int level) => u.Effect == UpgradeEffectKind.Multiply
        ? $"+{(Math.Pow(u.PerLevel, level) - 1) * 100:0.#}%"
        : $"+{u.PerLevel * level:0.##}";

    /// <summary>How to start researching, named from the content: the building that makes packs and the lab.</summary>
    private static string Hint(ContentRegistry content)
    {
        var packRecipes = content.Recipes.Values.Where(r => r.Outputs.Any(o => content.Items[o.Item].Science)).Select(r => r.Id).ToHashSet();
        string bench = content.BuildingList.FirstOrDefault(b => b.Params is ProcessorParams p && p.Recipes.Any(packRecipes.Contains))?.Name ?? "machine";
        string lab = content.BuildingList.FirstOrDefault(b => b.Behavior == "lab")?.Name ?? "lab";
        return $"Research is optional. Make packs in a {bench}, belt them into a {lab}, and it banks them here " +
               "to spend on permanent bonuses. A lab takes packs whether or not you are buying anything.";
    }
}
