using System.Text.Json.Serialization;

namespace FactorySim.Content;

// Content definitions are plain data. They can be written in code or loaded
// from JSON packs (see ContentRegistry), so new machines, items and recipes are
// mostly a data change, not a code change. `Meta` is an opaque bag for tools and
// renderers (colors, models, glyphs); the simulation never reads it.

public sealed class ItemDef
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";

    /// <summary>Value per unit when freshly mined. Processed items derive value from their inputs.</summary>
    public double BaseValue { get; init; }

    /// <summary>Unprocessed resource (ore, logs, sand, oil). Markets pay only a fraction for these.</summary>
    public bool Raw { get; init; }

    public Dictionary<string, string> Meta { get; init; } = new();
}

public enum PortKind : byte
{
    In,
    Out,
}

/// <summary>
/// An item connection point on one cell of a building's footprint.
/// An output on side S of cell C delivers into the neighbouring cell C+S, and
/// connects only if the building there has an input on the facing side of that cell.
/// Vertical logistics (ramps, lifts) are just buildings whose ports sit on different
/// footprint layers, so there are no special cases in the transport code.
/// </summary>
public sealed class PortDef
{
    public PortKind Kind { get; init; }
    public Side Side { get; init; }

    /// <summary>Local cell (relative to the building anchor, before rotation).</summary>
    public GridPos Cell { get; init; } = GridPos.Zero;
}

public sealed class BuildingDef
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";

    /// <summary>Name of the behavior (in the BehaviorRegistry) that runs this building.</summary>
    public string Behavior { get; init; } = "";

    /// <summary>Local cells occupied, relative to the anchor (0,0,0), facing north.</summary>
    public GridPos[] Footprint { get; init; } = { GridPos.Zero };

    public PortDef[] Ports { get; init; } = Array.Empty<PortDef>();

    public BigNum Cost { get; init; }

    public string Category { get; init; } = "misc";

    /// <summary>Progression tier that unlocks this building (index into the content's tiers).</summary>
    public int Tier { get; init; }

    /// <summary>How the building levels up. Filled from the behavior's default when omitted.</summary>
    public UpgradeTrack? Upgrade { get; set; }

    /// <summary>How many of this building may exist (null = unlimited). Grows with later tiers.</summary>
    public BuildLimit? Limit { get; init; }

    /// <summary>Replacement group this building belongs to (e.g. "belt", "machine").</summary>
    public string Group { get; init; } = "";

    /// <summary>
    /// Groups this building may be placed over: the occupants are removed (refunded) and this
    /// takes their place. A splitter lists "belt" so it can be dropped into a line; a belt does
    /// not list "machine", so dragging belts never destroys machines.
    /// </summary>
    public string[] Replaces { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Behavior-specific tuning. From JSON this arrives as a JsonElement and is bound to
    /// the behavior's params type when the registry is built; in code, assign the typed object.
    /// </summary>
    public object? Params { get; set; }

    public Dictionary<string, string> Meta { get; init; } = new();

    private int[]? _inputs, _outputs;

    /// <summary>Indices into <see cref="Ports"/> of all input ports.</summary>
    [JsonIgnore] public IReadOnlyList<int> InputPorts => _inputs ??= PortIndices(PortKind.In);

    /// <summary>Indices into <see cref="Ports"/> of all output ports.</summary>
    [JsonIgnore] public IReadOnlyList<int> OutputPorts => _outputs ??= PortIndices(PortKind.Out);

    private int[] PortIndices(PortKind kind) =>
        Enumerable.Range(0, Ports.Length).Where(i => Ports[i].Kind == kind).ToArray();

    public string MetaOr(string key, string fallback) => Meta.TryGetValue(key, out var v) ? v : fallback;
}

/// <summary>
/// Per-building upgrade levels (1 = as built). Each level multiplies speed and/or value;
/// costs grow geometrically from the building's price. MaxLevel null = uncapped.
/// </summary>
public sealed class UpgradeTrack
{
    public int? MaxLevel { get; init; } = 10;

    /// <summary>First upgrade costs Cost × CostFactor; each further one × CostGrowth.</summary>
    public double CostFactor { get; init; } = 1.5;
    public double CostGrowth { get; init; } = 1.8;

    /// <summary>Added to the speed multiplier per level above 1.</summary>
    public double SpeedPerLevel { get; init; }

    /// <summary>Added to the value multiplier per level above 1.</summary>
    public double ValuePerLevel { get; init; }

    public double SpeedFactor(int level) => 1 + SpeedPerLevel * (Math.Max(1, level) - 1);
    public double ValueFactor(int level) => 1 + ValuePerLevel * (Math.Max(1, level) - 1);

    public bool CanUpgrade(int level) => MaxLevel is not int max || level < max;

    /// <summary>Price of going from <paramref name="level"/> to level + 1.</summary>
    public BigNum UpgradeCost(BuildingDef def, int level) =>
        def.Cost * CostFactor * BigNum.Pow(CostGrowth, Math.Max(1, level) - 1);

    /// <summary>Total spent on a building at <paramref name="level"/> (price plus all upgrades); refunded on removal.</summary>
    public BigNum Invested(BuildingDef def, int level)
    {
        BigNum total = def.Cost;
        for (int l = 1; l < level; l++) total += UpgradeCost(def, l);
        return total;
    }
}

/// <summary>
/// Cap on a building's count: Base once its tier is unlocked, plus PerTier for every
/// <see cref="Every"/> later tiers (Every = 2: one more every second tier).
/// </summary>
public sealed class BuildLimit
{
    public int Base { get; init; } = 1;
    public int PerTier { get; init; }
    public int Every { get; init; } = 1;

    public int At(BuildingDef def, int unlockedTier) =>
        Base + PerTier * (Math.Max(0, unlockedTier - def.Tier) / Math.Max(1, Every));
}

/// <summary>A progression tier: unlocking it costs money, needs lifetime earnings, and grows the plot.</summary>
public sealed class TierDef
{
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public BigNum Cost { get; init; }
    public BigNum RequiredEarnings { get; init; }

    /// <summary>Side length of the square buildable plot once this tier is unlocked.</summary>
    public int PlotSize { get; init; } = 32;
}

public sealed record ItemAmount(string Item, int Count);

public sealed class RecipeDef
{
    public string Id { get; init; } = "";
    public ItemAmount[] Inputs { get; init; } = Array.Empty<ItemAmount>();
    public ItemAmount[] Outputs { get; init; } = Array.Empty<ItemAmount>();

    /// <summary>Work needed per craft, in ticks at speed 1.</summary>
    public int Ticks { get; init; } = 20;

    /// <summary>Output value = (sum of consumed input values) × this, split evenly over output units.</summary>
    public double ValueMultiplier { get; init; } = 1;
}

public enum UpgradeEffectKind : byte
{
    /// <summary>stat × PerLevel^level</summary>
    Multiply,

    /// <summary>stat + PerLevel × level</summary>
    Add,
}

public sealed class UpgradeDef
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";

    /// <summary>Stat key this upgrade modifies (see <see cref="StatIds"/>).</summary>
    public string Stat { get; init; } = "";

    public UpgradeEffectKind Effect { get; init; } = UpgradeEffectKind.Multiply;
    public double PerLevel { get; init; } = 1.1;

    public BigNum BaseCost { get; init; } = 100;
    public double CostGrowth { get; init; } = 1.5;

    /// <summary>Null = uncapped.</summary>
    public int? MaxLevel { get; init; }

    public BigNum CostForLevel(int currentLevel) => BaseCost * BigNum.Pow(CostGrowth, currentLevel);
}

/// <summary>Well-known stat keys read by the built-in behaviors. All default to 1.</summary>
public static class StatIds
{
    public const string ConveyorSpeed = "conveyor.speed";
    public const string MinerRate = "miner.rate";
    public const string MachineSpeed = "machine.speed";
    public const string SellMultiplier = "sell.multiplier";

    /// <summary>Max units per item bundle on belts. Raising it scales throughput without more simulated entities.</summary>
    public const string StackSize = "logistics.stackSize";
}

/// <summary>A set of definitions, e.g. one JSON file. Later packs override earlier ones by id.</summary>
/// <summary>
/// A long-term goal with a cash reward. <see cref="Kind"/> picks what is measured:
/// earned (lifetime $), sold / produced (units of <see cref="Item"/>, or of everything),
/// built (current count of <see cref="Building"/>, or of everything), level (highest
/// building level), contracts (completed), tier (unlocked tier).
/// </summary>
public sealed class MilestoneDef
{
    public static readonly string[] Kinds = { "earned", "sold", "produced", "built", "level", "contracts", "tier" };

    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public string Kind { get; init; } = "";
    public string? Item { get; init; }
    public string? Building { get; init; }
    public double Target { get; init; } = 1;
    public BigNum Reward { get; init; }
}

public sealed class ContentPack
{
    /// <summary>Money a new game starts with; the last pack that sets it wins.</summary>
    public BigNum? StartingMoney { get; init; }

    public List<ItemDef> Items { get; init; } = new();
    public List<BuildingDef> Buildings { get; init; } = new();
    public List<RecipeDef> Recipes { get; init; } = new();
    public List<UpgradeDef> Upgrades { get; init; } = new();

    /// <summary>Progression tiers in order; a pack that defines tiers replaces the whole list.</summary>
    public List<TierDef> Tiers { get; init; } = new();

    public List<MilestoneDef> Milestones { get; init; } = new();
}
