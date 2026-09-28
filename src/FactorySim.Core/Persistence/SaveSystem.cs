using System.Text.Json;
using FactorySim.Content;

namespace FactorySim.Persistence;

public sealed class SaveData
{
    public int Version { get; set; } = SaveSystem.CurrentVersion;

    /// <summary>Wall-clock time of saving, supplied by the host. Used for offline catch-up only.</summary>
    public DateTimeOffset? SavedAtUtc { get; set; }

    public long Tick { get; set; }
    public BigNum Money { get; set; }
    public uint RngState { get; set; }
    public int NextEntityId { get; set; }
    public long NextItemUid { get; set; }
    public GridBounds Bounds { get; set; }
    public bool Sandbox { get; set; }
    public Dictionary<string, int> Upgrades { get; set; } = new();
    public StatsTracker Stats { get; set; } = new();
    public List<EntitySave> Entities { get; set; } = new();
}

public sealed class EntitySave
{
    public int Id { get; set; }
    public string Def { get; set; } = "";
    public GridPos Pos { get; set; }
    public Dir Facing { get; set; }

    /// <summary>Behavior state, serialized via the behavior's own state type (open to new behaviors).</summary>
    public JsonElement? State { get; set; }
}

public sealed record LoadResult(Simulation Simulation, DateTimeOffset? SavedAtUtc, IReadOnlyList<string> Warnings);

/// <summary>
/// JSON save/load. The save is the world's data only; caches (grid index, links,
/// update order, stat cache) are rebuilt on load. Saving mid-run and loading gives a
/// simulation that continues bit-identically (covered by tests).
/// </summary>
public static class SaveSystem
{
    public const int CurrentVersion = 1;

    public static SaveData Capture(World world, DateTimeOffset? savedAtUtc = null) => new()
    {
        SavedAtUtc = savedAtUtc,
        Tick = world.Tick,
        Money = world.Money,
        RngState = world.Rng.State,
        NextEntityId = world.NextEntityId,
        NextItemUid = world.NextItemUid,
        Bounds = world.Bounds,
        Sandbox = world.Sandbox,
        Upgrades = new Dictionary<string, int>(world.UpgradeLevels),
        Stats = world.Stats,
        Entities = world.Entities.OrderBy(e => e.Id).Select(e => new EntitySave
        {
            Id = e.Id,
            Def = e.Def.Id,
            Pos = e.Pos,
            Facing = e.Facing,
            State = JsonSerializer.SerializeToElement(e.State, e.Behavior.StateType, Json.Options),
        }).ToList(),
    };

    public static string Serialize(Simulation sim, DateTimeOffset? savedAtUtc = null, bool pretty = false) =>
        JsonSerializer.Serialize(Capture(sim.World, savedAtUtc), pretty ? Json.Pretty : Json.Options);

    public static LoadResult Deserialize(string json, ContentRegistry content)
    {
        var data = JsonSerializer.Deserialize<SaveData>(json, Json.Options) ?? throw new JsonException("Empty save.");
        if (data.Version > CurrentVersion)
            throw new NotSupportedException($"Save version {data.Version} is newer than supported ({CurrentVersion}).");
        Migrate(data);

        var warnings = new List<string>();
        var world = new World(content, data.RngState)
        {
            Tick = data.Tick,
            Money = data.Money,
            Bounds = data.Bounds,
            Sandbox = data.Sandbox,
            Stats = data.Stats,
        };
        foreach (var (id, level) in data.Upgrades)
        {
            if (content.Upgrades.ContainsKey(id)) world.SetUpgradeLevel(id, level);
            else warnings.Add($"Dropped unknown upgrade '{id}'.");
        }

        foreach (var es in data.Entities)
        {
            if (!content.Buildings.TryGetValue(es.Def, out var def))
            {
                warnings.Add($"Dropped entity #{es.Id}: unknown building '{es.Def}'.");
                continue;
            }
            if (!world.CanPlace(def, es.Pos, es.Facing).Ok)
            {
                warnings.Add($"Dropped entity #{es.Id} ({es.Def}): footprint no longer fits.");
                continue;
            }
            content.Behaviors.TryGet(def.Behavior, out var behavior);
            object? state = null;
            try
            {
                state = es.State?.Deserialize(behavior.StateType, Json.Options);
            }
            catch (JsonException ex)
            {
                warnings.Add($"Reset state of entity #{es.Id} ({es.Def}): {ex.Message}");
            }
            world.AddEntity(def, es.Pos, es.Facing, es.Id, state);
        }

        // Restore counters last: AddEntity bumps NextEntityId past loaded ids.
        world.NextEntityId = Math.Max(world.NextEntityId, data.NextEntityId);
        world.NextItemUid = data.NextItemUid;
        return new LoadResult(new Simulation(world), data.SavedAtUtc, warnings);
    }

    /// <summary>Upgrade older saves in place. Add a case per version bump.</summary>
    private static void Migrate(SaveData data)
    {
        // Example for the future:
        // if (data.Version == 1) { ...transform...; data.Version = 2; }
        data.Version = CurrentVersion;
    }
}
