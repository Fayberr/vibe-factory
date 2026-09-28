using FactorySim.Behaviors;
using FactorySim.Content;

namespace FactorySim.Balance;

/// <summary>Whether and where items run through an in-line upgrader (the polisher) before they count.</summary>
public enum PolishMode
{
    /// <summary>Nothing is polished.</summary>
    None,

    /// <summary>Only what gets sold is polished, right before the depot.</summary>
    Products,

    /// <summary>Every ore and every intermediate is polished too. Value carries into what is made
    /// from it, so this compounds with every stage.</summary>
    EveryStep,
}

/// <summary>What the balance numbers assume about the player's factory.</summary>
public sealed record BalanceAssumptions
{
    /// <summary>Every building at this level, capped at its own maximum. 1 = as built.</summary>
    public int Level { get; init; } = 1;

    public PolishMode Polish { get; init; } = PolishMode.Products;

    /// <summary>Belt tiles counted per extractor or machine when estimating what a factory costs to build.</summary>
    public int BeltTilesPerBuilding { get; init; } = 5;

    /// <summary>Count assumed for an extractor or depot that has no build limit.</summary>
    public int Unlimited { get; init; } = 10;

    public static BalanceAssumptions Default { get; } = new();
}

/// <summary>
/// How an item is obtained: extracted by a miner (Recipe null), or crafted by a recipe in a
/// building. Value is per unit as it leaves the building, levels and per-step polish included.
/// RawShare is the part of that value each raw resource accounts for (sums to 1).
/// </summary>
public sealed record ItemSource(
    string Item,
    int Tier,
    BuildingDef Building,
    RecipeDef? Recipe,
    double Value,
    IReadOnlyDictionary<string, double> RawShare)
{
    public bool IsExtracted => Recipe == null;

    /// <summary>Units of <see cref="Item"/> one craft (or one extraction cycle) yields.</summary>
    public int OutputPerCycle => Recipe?.Outputs.Where(o => o.Item == Item).Sum(o => o.Count)
                                 ?? ((MinerParams)Building.Params!).Amount;
}

/// <summary>A recipe that consumes an item, and the building that runs it.</summary>
public sealed record RecipeUse(RecipeDef Recipe, BuildingDef Building, int Tier);

/// <summary>
/// The content seen as an economy at one point of progression: how each item is made, what it
/// is worth, and what uses it, with every building at the assumed level. Pure calculation over
/// the content; the simulation never reads it. Rules match <see cref="ItemValues"/>: an item
/// comes from the earliest tier that can make it, and within a tier from its most valuable recipe.
/// </summary>
public sealed class RecipeBook
{
    public ContentRegistry Content { get; }
    public BalanceAssumptions Assumptions { get; }

    /// <summary>Highest unlocked tier: nothing from later tiers exists here.</summary>
    public int UnlockedTier { get; }

    /// <summary>How every obtainable item is made.</summary>
    public IReadOnlyDictionary<string, ItemSource> Sources { get; }

    /// <summary>Recipes that consume each item. Items used in nothing are absent.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<RecipeUse>> UsedBy { get; }

    /// <summary>Value multiplier of the best available in-line upgrader (1 = none).</summary>
    public double PolishMultiplier { get; }

    /// <summary>The plain belt, and what one carries per second at the assumed level.</summary>
    public BuildingDef? Belt { get; }
    public double BeltItemsPerSecond { get; }

    /// <summary>The first depot of the game: the reference for "sells for".</summary>
    public BuildingDef? Depot { get; }

    private RecipeBook(ContentRegistry content, BalanceAssumptions assumptions, int unlockedTier)
    {
        Content = content;
        Assumptions = assumptions;
        UnlockedTier = unlockedTier;

        Belt = content.BuildingList
            .Where(b => Available(b) && b.Params is ConveyorParams { Effect: null } && b.Footprint.Length == 1)
            .OrderBy(b => b.Tier).ThenBy(b => b.Cost.ToDouble()).FirstOrDefault();
        if (Belt?.Params is ConveyorParams belt)
            BeltItemsPerSecond = Math.Min(belt.Speed * Speed(Belt), belt.Spacing) * Simulation.TicksPerSecond / belt.Spacing;

        PolishMultiplier = content.BuildingList
            .Where(b => Available(b) && b.Params is ConveyorParams { Effect: { RequiresTag: null, Items: null } })
            .Select(b => ((ConveyorParams)b.Params!).Effect!.ValueMultiplier * ValueFactor(b))
            .DefaultIfEmpty(1).Max();

        Depot = content.BuildingList.Where(b => Available(b) && b.Params is SellerParams)
            .OrderBy(b => b.Tier).FirstOrDefault();

        Sources = ComputeSources();
        UsedBy = ComputeUses();
    }

    public static RecipeBook Create(ContentRegistry content, BalanceAssumptions? assumptions = null, int? unlockedTier = null) =>
        new(content, assumptions ?? BalanceAssumptions.Default, unlockedTier ?? content.Tiers.Count - 1);

    public bool Available(BuildingDef b) => b.Tier <= UnlockedTier;

    /// <summary>The assumed level of a building, within its own limits.</summary>
    public int LevelOf(BuildingDef b)
    {
        int level = Math.Max(1, Assumptions.Level);
        return b.Upgrade?.MaxLevel is int max ? Math.Min(level, max) : level;
    }

    public double Speed(BuildingDef b) => b.Upgrade?.SpeedFactor(LevelOf(b)) ?? 1;
    public double ValueFactor(BuildingDef b) => b.Upgrade?.ValueFactor(LevelOf(b)) ?? 1;

    /// <summary>Price of the building plus its upgrades to the assumed level (all refunded on removal).</summary>
    public double BuildCost(BuildingDef b) => (b.Upgrade?.Invested(b, LevelOf(b)) ?? b.Cost).ToDouble();

    /// <summary>Units per second one building of the item's source produces.</summary>
    public double RatePerBuilding(ItemSource s)
    {
        double cycles = s.Recipe != null
            ? Simulation.TicksPerSecond / (double)s.Recipe.Ticks
            : Simulation.TicksPerSecond / (double)((MinerParams)s.Building.Params!).Interval;
        return cycles * Speed(s.Building) * s.OutputPerCycle;
    }

    /// <summary>How many of an extractor or depot the unlocked tier allows.</summary>
    public int AllowedCount(BuildingDef b) => b.Limit?.At(b, UnlockedTier) ?? Assumptions.Unlimited;

    public bool IsRaw(string item) => Content.Items.TryGetValue(item, out var def) && def.Raw;

    /// <summary>
    /// What one unit fetches at a depot (default: <see cref="Depot"/>): its value, polished if the
    /// assumptions say so, times the depot's price bonus, and the raw-material cut for raw items.
    /// </summary>
    public double SaleValue(string item, BuildingDef? depot = null)
    {
        depot ??= Depot;
        if (!Sources.TryGetValue(item, out var source) || depot?.Params is not SellerParams sp) return 0;
        double polish = Assumptions.Polish == PolishMode.Products ? PolishMultiplier : 1;
        double raw = IsRaw(item) ? sp.RawMultiplier : 1;
        return source.Value * polish * raw * sp.Multiplier * ValueFactor(depot);
    }

    public string NameOf(string item) => Content.Items.TryGetValue(item, out var def) && def.Name.Length > 0 ? def.Name : item;

    private double StepPolish => Assumptions.Polish == PolishMode.EveryStep ? PolishMultiplier : 1;

    private Dictionary<string, ItemSource> ComputeSources()
    {
        var sources = new Dictionary<string, ItemSource>();

        foreach (var b in Content.BuildingList)
        {
            if (!Available(b) || b.Params is not MinerParams m) continue;
            if (sources.TryGetValue(m.Item, out var old) && old.Tier <= b.Tier) continue;
            double value = Content.Items[m.Item].BaseValue * StepPolish;
            sources[m.Item] = new ItemSource(m.Item, b.Tier, b, null, value, new Dictionary<string, double> { [m.Item] = 1 });
        }

        // Repeat until stable: a recipe counts once all its inputs can be obtained.
        for (int pass = 0, changed = 1; changed > 0 && pass < 10_000; pass++)
        {
            changed = 0;
            foreach (var b in Content.BuildingList)
            {
                if (!Available(b) || b.Params is not ProcessorParams p) continue;
                foreach (var r in p.Recipes.Select(id => Content.Recipes[id]))
                {
                    if (!r.Inputs.All(i => sources.ContainsKey(i.Item))) continue;
                    int tier = Math.Max(b.Tier, r.Inputs.Max(i => sources[i.Item].Tier));
                    double inputValue = r.Inputs.Sum(i => sources[i.Item].Value * i.Count);
                    double each = inputValue * r.ValueMultiplier * ValueFactor(b) / r.Outputs.Sum(o => o.Count) * StepPolish;

                    foreach (var o in r.Outputs)
                    {
                        // Earliest tier wins; within a tier, the most valuable way to make it.
                        if (sources.TryGetValue(o.Item, out var old) && (tier > old.Tier || (tier == old.Tier && each <= old.Value * (1 + 1e-12))))
                            continue;
                        // Never let an item be made from itself: sources always form a tree.
                        if (r.Inputs.Any(i => DependsOn(sources, i.Item, o.Item))) continue;

                        var share = new Dictionary<string, double>();
                        foreach (var i in r.Inputs)
                        {
                            double weight = inputValue > 0 ? sources[i.Item].Value * i.Count / inputValue : 1.0 / r.Inputs.Length;
                            foreach (var (raw, s) in sources[i.Item].RawShare)
                                share[raw] = share.GetValueOrDefault(raw) + weight * s;
                        }
                        sources[o.Item] = new ItemSource(o.Item, tier, b, r, each, share);
                        changed++;
                    }
                }
            }
        }
        return sources;
    }

    private static bool DependsOn(Dictionary<string, ItemSource> sources, string item, string target)
    {
        if (item == target) return true;
        return sources.TryGetValue(item, out var s) && s.Recipe != null && s.Recipe.Inputs.Any(i => DependsOn(sources, i.Item, target));
    }

    private Dictionary<string, IReadOnlyList<RecipeUse>> ComputeUses()
    {
        // Each makeable recipe once, at the earliest building that runs it.
        var recipes = new Dictionary<string, RecipeUse>();
        foreach (var b in Content.BuildingList)
        {
            if (!Available(b) || b.Params is not ProcessorParams p) continue;
            foreach (var r in p.Recipes.Select(id => Content.Recipes[id]))
            {
                if (!r.Inputs.All(i => Sources.ContainsKey(i.Item))) continue;
                int tier = Math.Max(b.Tier, r.Inputs.Max(i => Sources[i.Item].Tier));
                if (!recipes.TryGetValue(r.Id, out var old) || tier < old.Tier) recipes[r.Id] = new RecipeUse(r, b, tier);
            }
        }

        var uses = new Dictionary<string, List<RecipeUse>>();
        foreach (var use in recipes.Values.OrderBy(u => u.Tier))
            foreach (var input in use.Recipe.Inputs.Select(i => i.Item).Distinct())
            {
                if (!uses.TryGetValue(input, out var list)) uses[input] = list = new List<RecipeUse>();
                list.Add(use);
            }
        return uses.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<RecipeUse>)kv.Value);
    }
}
