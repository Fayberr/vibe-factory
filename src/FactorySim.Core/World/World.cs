using FactorySim.Content;

namespace FactorySim;

/// <summary>
/// Inclusive cell bounds of the whole map. Height 0 is the base plate: nothing is built below it,
/// and there are 4 levels above. Which part of the map may be built on is the <see cref="Land"/>.
/// </summary>
public readonly record struct GridBounds(GridPos Min, GridPos Max)
{
    public bool Contains(GridPos p) =>
        p.X >= Min.X && p.X <= Max.X && p.Y >= Min.Y && p.Y <= Max.Y && p.Z >= Min.Z && p.Z <= Max.Z;
}

public readonly record struct PlacementCheck(bool Ok, string? Reason = null)
{
    public static readonly PlacementCheck Success = new(true);
    public static PlacementCheck Fail(string reason) => new(false, reason);
}

/// <summary>
/// All simulation state: grid, entities, economy, upgrades, stats, RNG. Pure data plus
/// queries; mutate it through <see cref="Simulation.Execute"/> so every change is a
/// validated, loggable, replayable command.
/// </summary>
public sealed class World
{
    private readonly Dictionary<int, Entity> _entities = new();
    private readonly Dictionary<GridPos, Entity> _grid = new();
    private readonly Dictionary<string, double> _statCache = new();
    private readonly Dictionary<string, int> _counts = new();
    private List<Entity> _updateOrder = new();
    private bool _topologyDirty = true;

    public ContentRegistry Content { get; }

    /// <summary>Ticks simulated since the world was created.</summary>
    public long Tick { get; internal set; }

    public BigNum Money { get; internal set; }

    /// <summary>The whole map. What may be built on is <see cref="Land"/>: the plots the player owns.</summary>
    public GridBounds Bounds { get; set; }

    /// <summary>The plots of the map and which of them are owned.</summary>
    public Land Land { get; internal set; }

    /// <summary>Free building and upgrades, for prototyping and level design.</summary>
    public bool Sandbox { get; set; }

    /// <summary>Highest unlocked progression tier (index into <see cref="ContentRegistry.Tiers"/>).</summary>
    public int UnlockedTier { get; internal set; }

    public Rng Rng { get; }
    public StatsTracker Stats { get; internal set; } = new();

    /// <summary>Contracts and milestones run (off in scripted tests that check exact money).</summary>
    public bool Goals { get; set; } = true;

    /// <summary>Production targets (idea F7): items made per minute the player wants to keep up, per item id. See <see cref="ProductionTargets"/>.</summary>
    public Dictionary<string, double> Targets { get; set; } = new();

    public ContractBoard Contracts { get; internal set; } = new();

    /// <summary>Ids of reached milestones.</summary>
    public HashSet<string> Milestones { get; internal set; } = new();

    /// <summary>Running samples for sustained production-rate milestones, keyed by milestone id.</summary>
    public Dictionary<string, RateMilestoneState> RateMilestones { get; internal set; } = new();

    internal Dictionary<string, int> UpgradeLevels { get; } = new();
    internal Dictionary<string, long> ScienceBank { get; } = new();
    internal int NextEntityId { get; set; } = 1;
    internal long NextItemUid { get; set; } = 1;

    public World(ContentRegistry content, uint seed = 1)
    {
        Content = content;
        Rng = new Rng(seed);
        Land = new Land(content.Map);
        Bounds = Land.Bounds();
    }

    public IReadOnlyCollection<Entity> Entities => _entities.Values;
    public int EntityCount => _entities.Count;

    public Entity? GetEntity(int id) => _entities.GetValueOrDefault(id);
    public Entity? EntityAt(GridPos cell) => _grid.GetValueOrDefault(cell);

    // ---- Stats & upgrades -------------------------------------------------

    public int UpgradeLevel(string upgradeId) => UpgradeLevels.GetValueOrDefault(upgradeId);

    public IReadOnlyDictionary<string, int> AllUpgradeLevels => UpgradeLevels;

    /// <summary>
    /// Current value of a stat: 1 × all Multiply upgrades + all Add upgrades targeting it.
    /// Cached until an upgrade level changes.
    /// </summary>
    public double Stat(string stat)
    {
        if (_statCache.TryGetValue(stat, out var cached)) return cached;
        double mult = 1, add = 0;
        foreach (var u in Content.Upgrades.Values)
        {
            if (u.Stat != stat) continue;
            int level = UpgradeLevel(u.Id);
            if (level == 0) continue;
            if (u.Effect == UpgradeEffectKind.Multiply) mult *= PowInt(u.PerLevel, level);
            else add += u.PerLevel * level;
        }
        return _statCache[stat] = mult + add;
    }

    internal void SetUpgradeLevel(string upgradeId, int level)
    {
        UpgradeLevels[upgradeId] = level;
        _statCache.Clear();
    }

    // ---- Research bank ---------------------------------------------------

    /// <summary>
    /// Research packs banked by labs, per item id, waiting to be spent on upgrades priced in packs
    /// (see <see cref="UpgradeDef.Packs"/>).
    /// </summary>
    public IReadOnlyDictionary<string, long> Science => ScienceBank;

    public long ScienceOf(string item) => ScienceBank.GetValueOrDefault(item);

    internal void AddScience(string item, long count)
    {
        if (count > 0) ScienceBank[item] = ScienceOf(item) + count;
    }

    /// <summary>Whether the bank holds every pack in <paramref name="price"/>.</summary>
    public bool CanPay(IReadOnlyList<ItemAmount> price)
    {
        foreach (var p in price)
            if (ScienceOf(p.Item) < p.Count) return false;
        return true;
    }

    internal void PayScience(IReadOnlyList<ItemAmount> price)
    {
        foreach (var p in price) ScienceBank[p.Item] = ScienceOf(p.Item) - p.Count;
    }

    /// <summary>Deterministic integer power (no Math.Pow).</summary>
    private static double PowInt(double b, int n)
    {
        double r = 1;
        while (n > 0)
        {
            if ((n & 1) != 0) r *= b;
            b *= b;
            n >>= 1;
        }
        return r;
    }

    // ---- Placement ---------------------------------------------------------

    /// <summary>
    /// Placement that may replace occupants: every footprint cell must be free or hold a building
    /// whose group <paramref name="def"/> lists in <see cref="BuildingDef.Replaces"/>.
    /// <paramref name="replaced"/> receives the buildings that would be removed.
    /// </summary>
    public PlacementCheck CanPlaceReplacing(BuildingDef def, GridPos pos, Dir facing, List<Entity> replaced, bool rules = true)
    {
        if (rules && CheckPlacement(def, pos, facing) is { Ok: false } rule) return rule;
        replaced.Clear();
        foreach (var cell in Entity.CellsFor(def, pos, facing))
        {
            if (CellProblem(cell) is { } problem) return PlacementCheck.Fail(problem);
            var occupant = EntityAt(cell);
            if (occupant == null || replaced.Contains(occupant)) continue;
            if (Array.IndexOf(def.Replaces, occupant.Def.Group) < 0 || occupant.Def.Group.Length == 0)
                return PlacementCheck.Fail($"{cell} is occupied by {occupant.Def.Name}");
            if (occupant.Def == def && occupant.Pos == pos && occupant.Facing == facing)
                return PlacementCheck.Fail($"{occupant.Def.Name} is already here");
            replaced.Add(occupant);
        }
        // Everything replaced must fit inside the new footprint, or parts of it would be left floating.
        var cells = Entity.CellsFor(def, pos, facing).ToHashSet();
        foreach (var e in replaced)
            if (!e.Cells().All(cells.Contains))
                return PlacementCheck.Fail($"{e.Def.Name} only partly fits under {def.Name}");
        return PlacementCheck.Success;
    }

    /// <summary>
    /// Whether <paramref name="def"/> fits at <paramref name="pos"/>. With <paramref name="rules"/>
    /// off it checks only the ground, the plot and what already stands there: loading a save does
    /// that, so a factory built before a rule existed is never thrown away.
    /// </summary>
    public PlacementCheck CanPlace(BuildingDef def, GridPos pos, Dir facing, Entity? ignore = null, bool rules = true)
    {
        if (rules && CheckPlacement(def, pos, facing) is { Ok: false } rule) return rule;
        foreach (var cell in Entity.CellsFor(def, pos, facing))
        {
            if (CellProblem(cell) is { } problem) return PlacementCheck.Fail(problem);
            var occupant = EntityAt(cell);
            if (occupant != null && occupant != ignore) return PlacementCheck.Fail($"{cell} is occupied by {occupant.Def.Name}");
        }
        return PlacementCheck.Success;
    }

    /// <summary>True when a cell lies off the map sideways. Height is ground and sky, not an edge.</summary>
    public bool OffMap(GridPos cell) =>
        cell.X < Bounds.Min.X || cell.X > Bounds.Max.X || cell.Y < Bounds.Min.Y || cell.Y > Bounds.Max.Y;

    /// <summary>
    /// Whether the player may build at a cell's column: it is on the map and its plot is owned.
    /// Sandbox is free building, so all of the map counts. Height is not looked at here.
    /// </summary>
    public bool OwnsCell(GridPos cell) =>
        !OffMap(cell) && (Sandbox || Land.Owns(cell.X, cell.Y));

    /// <summary>Why a building may not stand on a cell (off the map, too high or low, or land not bought), or null.</summary>
    public string? CellProblem(GridPos cell) =>
        !Bounds.Contains(cell) ? OutsideReason(cell)
        : !OwnsCell(cell) ? "This land is not yours yet: buy the plot first"
        : null;

    /// <summary>
    /// The extra placement rule of a definition (<see cref="BuildingDef.Placement"/>). "mapEdge"
    /// wants the building against the outer border of the whole map with every input facing inward,
    /// so what it does looks off the map. Depots use it: goods leave at the edge of the map, and the
    /// belts that feed them come from inside. Free building in sandbox ignores it.
    /// </summary>
    public PlacementCheck CheckPlacement(BuildingDef def, GridPos pos, Dir facing)
    {
        if (Sandbox || def.Placement.Length == 0) return PlacementCheck.Success;
        if (def.InputPorts.Count == 0) return PlacementCheck.Success;
        foreach (int port in def.InputPorts)
        {
            var p = def.Ports[port];
            var outward = p.Side.ToWorld(facing).Opposite();
            if (!OffMap(pos + p.Cell.Rotate(facing) + outward.Offset()))
                return PlacementCheck.Fail($"{def.Name} belongs on the edge of the map, taking items from the inside");
        }
        return PlacementCheck.Success;
    }

    /// <summary>Whether this building already obeys its placement rule. One that does not (an old
    /// save, or one built in sandbox) may still be moved and turned, but must not get worse.</summary>
    public bool ObeysPlacement(Entity e) => CheckPlacement(e.Def, e.Pos, e.Facing).Ok;

    private string OutsideReason(GridPos cell) =>
        cell.Z < Bounds.Min.Z ? "Can't build below the ground"
        : cell.Z > Bounds.Max.Z ? $"Too high (max height {Bounds.Max.Z})"
        : "Outside the map";

    /// <summary>Number of buildings of a def currently placed.</summary>
    public int CountOf(string defId) => _counts.GetValueOrDefault(defId);

    /// <summary>Current cap for a def (null = unlimited).</summary>
    public int? LimitOf(BuildingDef def) => def.Limit?.At(def, UnlockedTier);

    internal Entity AddEntity(BuildingDef def, GridPos pos, Dir facing, int? id = null, object? state = null, int level = 1)
    {
        if (!Content.Behaviors.TryGet(def.Behavior, out var behavior))
            throw new InvalidOperationException($"No behavior '{def.Behavior}'.");
        var e = new Entity(id ?? NextEntityId, def, behavior, pos, facing, state ?? behavior.CreateState(def))
        {
            Level = Math.Clamp(level, 1, def.Upgrade?.MaxLevel ?? int.MaxValue),
        };
        NextEntityId = Math.Max(NextEntityId, e.Id + 1);
        _entities.Add(e.Id, e);
        _counts[def.Id] = CountOf(def.Id) + 1;
        foreach (var cell in e.Cells()) _grid[cell] = e;
        _topologyDirty = true;
        return e;
    }

    internal void RemoveEntity(Entity e)
    {
        foreach (var cell in e.Cells()) _grid.Remove(cell);
        _entities.Remove(e.Id);
        _counts[e.Def.Id] = CountOf(e.Def.Id) - 1;
        _topologyDirty = true;
    }

    internal void Reorient(Entity e, GridPos pos, Dir facing)
    {
        foreach (var cell in e.Cells()) _grid.Remove(cell);
        e.Pos = pos;
        e.Facing = facing;
        foreach (var cell in e.Cells()) _grid[cell] = e;
        _topologyDirty = true;
    }

    /// <summary>Repositions several entities at once (callers validate first).</summary>
    internal void MoveEntities(IReadOnlyList<(Entity Entity, GridPos Pos, Dir Facing)> plan)
    {
        foreach (var (e, _, _) in plan)
            foreach (var cell in e.Cells()) _grid.Remove(cell);
        foreach (var (e, pos, facing) in plan)
        {
            e.Pos = pos;
            e.Facing = facing;
            foreach (var cell in e.Cells()) _grid[cell] = e;
        }
        _topologyDirty = true;
    }

    // ---- Topology ----------------------------------------------------------

    /// <summary>Rebuilds port links now if the layout changed (renderers call this before reading links).</summary>
    public void EnsureTopology() => _ = UpdateOrder;

    /// <summary>Entities in tick order (downstream first). Rebuilds links if the layout changed.</summary>
    public IReadOnlyList<Entity> UpdateOrder
    {
        get
        {
            if (_topologyDirty)
            {
                _updateOrder = Topology.Rebuild(this);
                _topologyDirty = false;
            }
            return _updateOrder;
        }
    }

    internal ItemStack CreateItem(string type, long count, BigNum unitValue) =>
        new() { Uid = NextItemUid++, Type = type, Count = count, UnitValue = unitValue };

    internal void AddMoney(BigNum amount) => Money += amount;

    /// <summary>Money a building returns when removed (price plus upgrades).</summary>
    public BigNum InvestedIn(Entity e) => e.Def.Upgrade?.Invested(e.Def, e.Level) ?? e.Def.Cost;

}
