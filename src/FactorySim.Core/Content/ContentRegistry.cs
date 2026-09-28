using System.Text.Json;
using FactorySim.Behaviors;
using FactorySim.Persistence;

namespace FactorySim.Content;

public sealed class ContentException : Exception
{
    public ContentException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>
/// The validated, immutable set of definitions a world runs on. Built from one or more
/// <see cref="ContentPack"/>s; later packs override earlier ones by id, so experiments,
/// balance patches and mods can layer on top of the base pack without editing it.
/// </summary>
public sealed class ContentRegistry
{
    private const string BaseResource = "FactorySim.Content.base.json";

    public BehaviorRegistry Behaviors { get; }
    public IReadOnlyDictionary<string, ItemDef> Items { get; }
    public IReadOnlyDictionary<string, BuildingDef> Buildings { get; }
    public IReadOnlyDictionary<string, RecipeDef> Recipes { get; }
    public IReadOnlyDictionary<string, UpgradeDef> Upgrades { get; }

    /// <summary>Buildings in declaration order (for toolbars and hotkeys).</summary>
    public IReadOnlyList<BuildingDef> BuildingList { get; }

    /// <summary>Upgrades in declaration order.</summary>
    public IReadOnlyList<UpgradeDef> UpgradeList { get; }

    /// <summary>Progression tiers; index 0 is available from the start.</summary>
    public IReadOnlyList<TierDef> Tiers { get; }

    private ContentRegistry(
        BehaviorRegistry behaviors,
        List<ItemDef> items,
        List<BuildingDef> buildings,
        List<RecipeDef> recipes,
        List<UpgradeDef> upgrades,
        List<TierDef> tiers)
    {
        Behaviors = behaviors;
        Items = items.ToDictionary(x => x.Id);
        Buildings = buildings.ToDictionary(x => x.Id);
        Recipes = recipes.ToDictionary(x => x.Id);
        Upgrades = upgrades.ToDictionary(x => x.Id);
        BuildingList = buildings;
        UpgradeList = upgrades;
        Tiers = tiers.Count > 0 ? tiers : new List<TierDef> { new() { Name = "Start", PlotSize = 32 } };
    }

    /// <summary>The built-in base pack, optionally with extra packs layered on top.</summary>
    public static ContentRegistry LoadDefault(BehaviorRegistry? behaviors = null, params ContentPack[] extraPacks)
    {
        var packs = new List<ContentPack> { ParsePack(ReadEmbedded(BaseResource)) };
        packs.AddRange(extraPacks);
        return Build(behaviors ?? BehaviorRegistry.CreateDefault(), packs);
    }

    public static ContentPack ParsePack(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<ContentPack>(json, Json.Options)
                   ?? throw new ContentException("Content pack is empty.");
        }
        catch (JsonException ex)
        {
            throw new ContentException($"Invalid content JSON: {ex.Message}", ex);
        }
    }

    public static ContentRegistry Build(BehaviorRegistry behaviors, IEnumerable<ContentPack> packs)
    {
        var items = new OrderedById<ItemDef>(x => x.Id);
        var buildings = new OrderedById<BuildingDef>(x => x.Id);
        var recipes = new OrderedById<RecipeDef>(x => x.Id);
        var upgrades = new OrderedById<UpgradeDef>(x => x.Id);
        var tiers = new List<TierDef>();
        foreach (var pack in packs)
        {
            if (pack.Tiers.Count > 0)
            {
                tiers.Clear();
                tiers.AddRange(pack.Tiers);
            }
            pack.Items.ForEach(items.Put);
            pack.Buildings.ForEach(buildings.Put);
            pack.Recipes.ForEach(recipes.Put);
            pack.Upgrades.ForEach(upgrades.Put);
        }

        var registry = new ContentRegistry(behaviors, items.List, buildings.List, recipes.List, upgrades.List, tiers);
        registry.Validate();
        return registry;
    }

    public ItemDef Item(string id) => Items.TryGetValue(id, out var d) ? d : throw new KeyNotFoundException($"Unknown item '{id}'.");
    public BuildingDef Building(string id) => Buildings.TryGetValue(id, out var d) ? d : throw new KeyNotFoundException($"Unknown building '{id}'.");

    private void Validate()
    {
        foreach (var r in Recipes.Values)
        {
            if (r.Ticks <= 0) throw new ContentException($"Recipe '{r.Id}': ticks must be > 0.");
            if (r.Inputs.Length == 0 || r.Outputs.Length == 0) throw new ContentException($"Recipe '{r.Id}' needs inputs and outputs.");
            foreach (var a in r.Inputs.Concat(r.Outputs))
            {
                if (!Items.ContainsKey(a.Item)) throw new ContentException($"Recipe '{r.Id}': unknown item '{a.Item}'.");
                if (a.Count <= 0) throw new ContentException($"Recipe '{r.Id}': counts must be > 0.");
            }
        }

        foreach (var u in Upgrades.Values)
        {
            if (string.IsNullOrEmpty(u.Stat)) throw new ContentException($"Upgrade '{u.Id}' has no stat.");
            if (u.CostGrowth < 1) throw new ContentException($"Upgrade '{u.Id}': costGrowth must be ≥ 1.");
        }

        foreach (var b in Buildings.Values)
        {
            if (!Behaviors.TryGet(b.Behavior, out var behavior))
                throw new ContentException($"Building '{b.Id}': unknown behavior '{b.Behavior}'. Known: {string.Join(", ", Behaviors.Names)}.");
            if (!b.Footprint.Contains(GridPos.Zero))
                throw new ContentException($"Building '{b.Id}': footprint must include the anchor [0,0,0].");
            if (b.Footprint.Distinct().Count() != b.Footprint.Length)
                throw new ContentException($"Building '{b.Id}': footprint has duplicate cells.");
            foreach (var port in b.Ports)
                if (!b.Footprint.Contains(port.Cell))
                    throw new ContentException($"Building '{b.Id}': port cell {port.Cell} is not in the footprint.");

            if (b.Tier < 0 || b.Tier >= Tiers.Count)
                throw new ContentException($"Building '{b.Id}': tier {b.Tier} does not exist (tiers 0..{Tiers.Count - 1}).");

            b.Params = BindParams(b, behavior);
            behavior.Bind(b, this);
            b.Upgrade ??= behavior.DefaultUpgrade(b);
            if (b.Upgrade.CostGrowth < 1 || b.Upgrade.MaxLevel < 1)
                throw new ContentException($"Building '{b.Id}': invalid upgrade track.");
        }

        for (int i = 1; i < Tiers.Count; i++)
            if (Tiers[i].PlotSize < Tiers[i - 1].PlotSize)
                throw new ContentException($"Tier '{Tiers[i].Name}': plot size must not shrink.");
    }

    private static object BindParams(BuildingDef def, IBehavior behavior)
    {
        try
        {
            return def.Params switch
            {
                null => Activator.CreateInstance(behavior.ParamsType)!,
                JsonElement je => je.Deserialize(behavior.ParamsType, Json.Options)!,
                var typed when behavior.ParamsType.IsInstanceOfType(typed) => typed,
                var other => throw new ContentException(
                    $"Building '{def.Id}': params of type {other.GetType().Name}, expected {behavior.ParamsType.Name}."),
            };
        }
        catch (JsonException ex)
        {
            throw new ContentException($"Building '{def.Id}': invalid params: {ex.Message}", ex);
        }
    }

    private static string ReadEmbedded(string name)
    {
        using var stream = typeof(ContentRegistry).Assembly.GetManifestResourceStream(name)
                           ?? throw new ContentException($"Embedded content '{name}' not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>Id-keyed list that keeps first-declaration order while letting later entries override.</summary>
    private sealed class OrderedById<T>
    {
        private readonly Func<T, string> _id;
        private readonly Dictionary<string, int> _index = new();
        public readonly List<T> List = new();

        public OrderedById(Func<T, string> id) => _id = id;

        public void Put(T item)
        {
            var id = _id(item);
            if (string.IsNullOrWhiteSpace(id)) throw new ContentException($"{typeof(T).Name} without an id.");
            if (_index.TryGetValue(id, out int i)) List[i] = item;
            else
            {
                _index[id] = List.Count;
                List.Add(item);
            }
        }
    }
}
