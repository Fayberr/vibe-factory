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
/// footprint layers — no special cases in the transport code.
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
public sealed class ContentPack
{
    public List<ItemDef> Items { get; init; } = new();
    public List<BuildingDef> Buildings { get; init; } = new();
    public List<RecipeDef> Recipes { get; init; } = new();
    public List<UpgradeDef> Upgrades { get; init; } = new();
}
