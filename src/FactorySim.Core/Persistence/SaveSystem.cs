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
    /// <summary>The map at the time of saving. Only saves from before the land existed (version 1) rely on it: it was then the buildable plot.</summary>
    public GridBounds Bounds { get; set; }

    /// <summary>The plots the player owns (missing in version 1 saves).</summary>
    public LandSave? Land { get; set; }
    public bool Sandbox { get; set; }
    public int UnlockedTier { get; set; }
    public Dictionary<string, int> Upgrades { get; set; } = new();
    public StatsTracker Stats { get; set; } = new();
    public bool Goals { get; set; } = true;
    public ContractBoard Contracts { get; set; } = new();
    public List<string> Milestones { get; set; } = new();
    public List<EntitySave> Entities { get; set; } = new();
}

/// <summary>
/// Owned plots as [column, row] pairs, with the plot size they were counted in. The size is saved so a
/// game whose plots later change size keeps the land it had: every new plot that overlaps an old one is owned.
/// </summary>
public sealed class LandSave
{
    public int PlotSize { get; set; }
    public List<int[]> Owned { get; set; } = new();
}

public sealed class EntitySave
{
    public int Id { get; set; }
    public string Def { get; set; } = "";
    public GridPos Pos { get; set; }
    public Dir Facing { get; set; }

    /// <summary>Upgrade level (missing in old saves → 1).</summary>
    public int Level { get; set; } = 1;

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
    public const int CurrentVersion = 2;

    public static SaveData Capture(World world, DateTimeOffset? savedAtUtc = null) => new()
    {
        SavedAtUtc = savedAtUtc,
        Tick = world.Tick,
        Money = world.Money,
        RngState = world.Rng.State,
        NextEntityId = world.NextEntityId,
        NextItemUid = world.NextItemUid,
        Bounds = world.Bounds,
        Land = new LandSave { PlotSize = world.Land.PlotSize, Owned = world.Land.Owned.Select(p => new[] { p.Column, p.Row }).ToList() },
        Sandbox = world.Sandbox,
        UnlockedTier = world.UnlockedTier,
        Upgrades = new Dictionary<string, int>(world.UpgradeLevels),
        Stats = world.Stats,
        Goals = world.Goals,
        Contracts = world.Contracts,
        Milestones = world.Milestones.OrderBy(id => id, StringComparer.Ordinal).ToList(),
        Entities = world.Entities.OrderBy(e => e.Id).Select(e => new EntitySave
        {
            Id = e.Id,
            Def = e.Def.Id,
            Pos = e.Pos,
            Facing = e.Facing,
            Level = e.Level,
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
            Sandbox = data.Sandbox,
            UnlockedTier = Math.Clamp(data.UnlockedTier, 0, content.Tiers.Count - 1),
            Stats = data.Stats,
            Goals = data.Goals,
            Contracts = data.Contracts,
            Milestones = data.Milestones.ToHashSet(),
        };
        RestoreLand(world, data, content);

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
            // Placement rules are off here on purpose: a depot built before the rule existed
            // keeps its spot, and one the player placed in sandbox stays where they put it.
            if (!world.CanPlace(def, es.Pos, es.Facing, rules: false).Ok)
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
            world.AddEntity(def, es.Pos, es.Facing, es.Id, state, Math.Max(1, es.Level));
        }

        // Restore counters last: AddEntity bumps NextEntityId past loaded ids.
        world.NextEntityId = Math.Max(world.NextEntityId, data.NextEntityId);
        world.NextItemUid = data.NextItemUid;
        return new LoadResult(new Simulation(world), data.SavedAtUtc, warnings);
    }

    /// <summary>
    /// Owns the land the save had, then makes sure every building stands on land that is owned and on the
    /// map. Loading never throws a factory away: land bought under other sizes is mapped onto today's plots
    /// (anything overlapping counts), the plots under any building are owned, and a map too small for a
    /// building (an old, very large plot) grows. The starting plot is always owned.
    /// </summary>
    private static void RestoreLand(World world, SaveData data, ContentRegistry content)
    {
        var land = world.Land;

        // Rectangles of cells that were the player's: the plots of a version 2 save, or, before the
        // land existed, the one square plot that grew with the tiers.
        var rects = new List<(int MinX, int MinY, int MaxX, int MaxY)>();
        if (data.Land is { PlotSize: > 0 } saved)
        {
            foreach (var pair in saved.Owned)
                if (pair.Length >= 2)
                    rects.Add((pair[0] * saved.PlotSize, pair[1] * saved.PlotSize, (pair[0] + 1) * saved.PlotSize - 1, (pair[1] + 1) * saved.PlotSize - 1));
        }
        else
            rects.Add((data.Bounds.Min.X, data.Bounds.Min.Y, data.Bounds.Max.X, data.Bounds.Max.Y));

        foreach (var es in data.Entities)
            if (content.Buildings.TryGetValue(es.Def, out var def))
                foreach (var cell in Entity.CellsFor(def, es.Pos, es.Facing))
                    rects.Add((cell.X, cell.Y, cell.X, cell.Y));

        int size = land.PlotSize;
        int columns = land.Columns, rows = land.Rows;
        foreach (var r in rects)
        {
            if (r.MaxX < 0 || r.MaxY < 0 || r.MaxX < r.MinX || r.MaxY < r.MinY) continue;
            columns = Math.Max(columns, r.MaxX / size + 1);
            rows = Math.Max(rows, r.MaxY / size + 1);
        }
        var plots = new List<PlotId>();
        foreach (var r in rects)
        {
            if (r.MaxX < 0 || r.MaxY < 0 || r.MaxX < r.MinX || r.MaxY < r.MinY) continue;
            for (int row = Math.Max(0, r.MinY) / size; row <= r.MaxY / size; row++)
                for (int col = Math.Max(0, r.MinX) / size; col <= r.MaxX / size; col++)
                    plots.Add(new PlotId(col, row));
        }
        land.Fit(columns, rows, plots);

        // The map is the land's grid; older saves allowed underground layers, but the base plate is the floor.
        world.Bounds = land.Bounds(Math.Max(0, data.Bounds.Min.Z), data.Bounds.Max.Z > 0 ? data.Bounds.Max.Z : 4);
    }

    /// <summary>Upgrade older saves in place. Add a case per version bump.</summary>
    private static void Migrate(SaveData data)
    {
        // Version 2 added the land. A version 1 save has none: RestoreLand reads its old Bounds and
        // gives the player every plot that square covered, so there is nothing to transform here.
        data.Version = CurrentVersion;
    }
}
