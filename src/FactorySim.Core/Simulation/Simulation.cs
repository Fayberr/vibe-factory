using FactorySim.Content;

namespace FactorySim;

/// <summary>A command stamped with the tick it was applied at.</summary>
public sealed record LoggedCommand(long Tick, Command Command);

/// <summary>
/// Fixed-timestep driver around a <see cref="World"/>. The host (Godot, CLI, server)
/// feeds it real time via <see cref="Advance"/> or calls <see cref="Step()"/> directly;
/// the simulation itself never reads a clock, so identical inputs give identical results.
/// </summary>
public sealed partial class Simulation
{
    public const int TicksPerSecond = 20;
    public const double SecondsPerTick = 1.0 / TicksPerSecond;

    private readonly TickContext _ctx;
    private double _accumulator;

    public World World { get; }
    public ContentRegistry Content => World.Content;
    public EventQueue Events { get; } = new();

    /// <summary>When set, successful commands are appended here (replays, undo, anti-cheat).</summary>
    public List<LoggedCommand>? CommandLog { get; set; }

    /// <summary>Share of a building's cost returned on removal.</summary>
    public double RefundFraction { get; set; } = 1.0;

    public Simulation(World world)
    {
        World = world;
        _ctx = new TickContext(this);
    }

    public static Simulation CreateNew(ContentRegistry content, BigNum startingMoney, uint seed = 1)
    {
        var world = new World(content, seed) { Money = startingMoney };
        return new Simulation(world);
    }

    /// <summary>Fraction (0..1) of the way to the next tick; use it to interpolate visuals.</summary>
    public float Alpha => (float)Math.Clamp(_accumulator / SecondsPerTick, 0, 1);

    /// <summary>Runs exactly one tick.</summary>
    public void Step()
    {
        var order = World.UpdateOrder;
        for (int i = 0; i < order.Count; i++)
        {
            var e = order[i];
            e.Behavior.Tick(_ctx, e);
        }
        World.Stats.EndTick(World.Tick);
        World.Stats.History.EndTick(World, World.Tick);
        if (World.Goals && World.Tick % TicksPerSecond == 0) UpdateGoals();
        World.Tick++;
    }

    public void Step(long ticks)
    {
        for (long i = 0; i < ticks; i++) Step();
    }

    /// <summary>
    /// Accumulates real time and runs as many whole ticks as fit, at most
    /// <paramref name="maxTicks"/> (excess backlog is dropped so a slow frame can't snowball).
    /// Returns the number of ticks run.
    /// </summary>
    public int Advance(double seconds, int maxTicks = 20)
    {
        _accumulator += Math.Max(0, seconds);
        int n = 0;
        while (_accumulator >= SecondsPerTick && n < maxTicks)
        {
            Step();
            _accumulator -= SecondsPerTick;
            n++;
        }
        if (_accumulator >= SecondsPerTick) _accumulator = SecondsPerTick * 0.999;
        return n;
    }

    // ---- Commands ----------------------------------------------------------

    public CommandResult Execute(Command command)
    {
        var result = command switch
        {
            PlaceBuilding c => Place(c),
            RemoveBuilding c => Remove(c),
            RotateBuilding c => Rotate(c),
            BuyUpgrade c => Buy(c),
            PlaceBlueprint c => PlaceMany(c),
            RemoveBuildings c => RemoveMany(c),
            MoveBuildings c => Move(c),
            SetBuildingLevels c => SetLevels(c),
            SelectRecipe c => Choose(c),
            SetFilter c => Filter(c),
            RerollContract c => Reroll(c),
            UnlockTier => Unlock(),
            BuyPlot c => BuyLand(c),
            _ => CommandResult.Fail($"Unknown command {command.GetType().Name}"),
        };
        if (result.Ok) CommandLog?.Add(new LoggedCommand(World.Tick, command));
        return result;
    }

    /// <summary>Build-limit check for adding <paramref name="adding"/> more of <paramref name="def"/> (after removing <paramref name="freed"/>); null when allowed.</summary>
    public string? LimitReason(BuildingDef def, int adding = 1, int freed = 0)
    {
        if (World.Sandbox || World.LimitOf(def) is not int max) return null;
        if (World.CountOf(def.Id) - freed + adding <= max) return null;
        return $"{def.Name} limit reached ({World.CountOf(def.Id)}/{max}). Later tiers allow more.";
    }

    /// <summary>Tier gate for placing <paramref name="def"/>; null when allowed.</summary>
    public string? LockReason(BuildingDef def) =>
        World.Sandbox || def.Tier <= World.UnlockedTier
            ? null
            : $"{def.Name} unlocks at tier {def.Tier} ({Content.Tiers[def.Tier].Name})";

    private CommandResult Place(PlaceBuilding c)
    {
        if (!Content.Buildings.TryGetValue(c.DefId, out var def)) return CommandResult.Fail($"Unknown building '{c.DefId}'");
        if (LockReason(def) is { } locked) return CommandResult.Fail(locked);

        var replaced = new List<Entity>();
        var check = c.Replace ? World.CanPlaceReplacing(def, c.Pos, c.Facing, replaced) : World.CanPlace(def, c.Pos, c.Facing);
        if (!check.Ok) return CommandResult.Fail(check.Reason!);
        if (LimitReason(def, 1, replaced.Count(r => r.Def == def)) is { } limit) return CommandResult.Fail(limit);

        if (!World.Sandbox)
        {
            BigNum refund = BigNum.Zero;
            foreach (var r in replaced) refund += World.InvestedIn(r) * RefundFraction;
            var net = def.Cost - refund;
            if (World.Money < net) return CommandResult.Fail($"Need {net.Format()} (have {World.Money.Format()})");
            World.Money -= net;
        }

        // Items on a belt survive a swap between belt pieces (conveyor ⇄ polisher ⇄ bridge belt).
        object? state = null;
        if (replaced.Count == 1 && replaced[0].State is Behaviors.ConveyorState && World.Content.Behaviors.Get(def.Behavior) is Behaviors.ConveyorBehavior)
            state = replaced[0].State;
        foreach (var r in replaced)
        {
            World.RemoveEntity(r);
            if (Events.Enabled) Events.Add(new EntityRemoved(World.Tick, r.Id, r.Def.Id, r.Pos));
        }

        var e = World.AddEntity(def, c.Pos, c.Facing, state: state);
        if (Events.Enabled) Events.Add(new EntityPlaced(World.Tick, e.Id, def.Id, e.Pos, e.Facing));
        return CommandResult.Success(e.Id);
    }

    private CommandResult Remove(RemoveBuilding c)
    {
        var e = World.EntityAt(c.Cell);
        if (e == null) return CommandResult.Fail($"Nothing at {c.Cell}");
        World.RemoveEntity(e);
        if (!World.Sandbox) World.Money += World.InvestedIn(e) * RefundFraction;
        if (Events.Enabled) Events.Add(new EntityRemoved(World.Tick, e.Id, e.Def.Id, e.Pos));
        return CommandResult.Success(e.Id);
    }

    private CommandResult Rotate(RotateBuilding c)
    {
        var e = World.EntityAt(c.Cell);
        if (e == null) return CommandResult.Fail($"Nothing at {c.Cell}");
        var facing = c.Facing ?? e.Facing.RotateCW();
        // A building that already breaks its placement rule (an old save) may still be turned;
        // one that obeys it must keep obeying it.
        var check = World.CanPlace(e.Def, e.Pos, facing, ignore: e, rules: World.ObeysPlacement(e));
        if (!check.Ok) return CommandResult.Fail(check.Reason!);
        World.Reorient(e, e.Pos, facing);
        if (Events.Enabled) Events.Add(new EntityReoriented(World.Tick, e.Id, e.Pos, e.Facing));
        return CommandResult.Success(e.Id);
    }

    private CommandResult PlaceMany(PlaceBlueprint c)
    {
        var plan = new List<(BuildingDef Def, GridPos Pos, Dir Facing, int Level, string? Recipe, IReadOnlyList<string?>? Filters)>();
        var claimed = new HashSet<GridPos>();
        BigNum cost = BigNum.Zero;
        foreach (var (defId, pos, facing, level, recipe, filters) in c.Blueprint.Placements(c.At, c.QuarterTurns))
        {
            if (!Content.Buildings.TryGetValue(defId, out var def)) return CommandResult.Fail($"Unknown building '{defId}'");
            if (LockReason(def) is { } locked) return CommandResult.Fail(locked);
            var check = World.CanPlace(def, pos, facing);
            if (!check.Ok) return CommandResult.Fail(check.Reason!);
            foreach (var cell in Entity.CellsFor(def, pos, facing))
                if (!claimed.Add(cell)) return CommandResult.Fail($"Blueprint overlaps itself at {cell}");
            int lv = Math.Clamp(level, 1, def.Upgrade?.MaxLevel ?? int.MaxValue);
            cost += def.Upgrade?.Invested(def, lv) ?? def.Cost;
            plan.Add((def, pos, facing, lv, recipe, filters));
        }
        if (plan.Count == 0) return CommandResult.Fail("Nothing to place");
        foreach (var g in plan.GroupBy(x => x.Def))
            if (LimitReason(g.Key, g.Count()) is { } limit) return CommandResult.Fail(limit);
        if (!World.Sandbox)
        {
            if (World.Money < cost) return CommandResult.Fail($"Need {cost.Format()} (have {World.Money.Format()})");
            World.Money -= cost;
        }

        var ids = new List<int>(plan.Count);
        foreach (var (def, pos, facing, level, recipe, filters) in plan)
        {
            var e = World.AddEntity(def, pos, facing, level: level);
            if (recipe != null) e.Behavior.Select(e, recipe); // copies keep their chosen recipe
            if (filters != null) // and their filters; one for an item this game doesn't have is left open
                for (int i = 0; i < filters.Count; i++)
                    if (filters[i] != null) e.Behavior.SetFilter(e, i, filters[i]);
            ids.Add(e.Id);
            if (Events.Enabled) Events.Add(new EntityPlaced(World.Tick, e.Id, def.Id, e.Pos, e.Facing));
        }
        return CommandResult.Success(ids);
    }

    private CommandResult RemoveMany(RemoveBuildings c)
    {
        var entities = c.Cells.Select(World.EntityAt).OfType<Entity>().Distinct().ToList();
        if (entities.Count == 0) return CommandResult.Fail("Nothing to remove");
        foreach (var e in entities)
        {
            World.RemoveEntity(e);
            if (!World.Sandbox) World.Money += World.InvestedIn(e) * RefundFraction;
            if (Events.Enabled) Events.Add(new EntityRemoved(World.Tick, e.Id, e.Def.Id, e.Pos));
        }
        return CommandResult.Success(entities.Select(e => e.Id).ToList());
    }

    private CommandResult Move(MoveBuildings c)
    {
        var entities = new List<Entity>();
        foreach (var cell in c.Cells)
        {
            var e = World.EntityAt(cell);
            if (e == null) return CommandResult.Fail($"Nothing at {cell}");
            if (!entities.Contains(e)) entities.Add(e);
        }
        var moving = entities.ToHashSet();
        var plan = entities
            .Select(e => (Entity: e, Pos: (e.Pos - c.Pivot).Rotate(c.QuarterTurns) + c.Pivot + c.Delta, Facing: e.Facing.RotateCW(c.QuarterTurns)))
            .ToList();

        var claimed = new HashSet<GridPos>();
        foreach (var (e, pos, facing) in plan)
        {
            if (World.ObeysPlacement(e) && World.CheckPlacement(e.Def, pos, facing) is { Ok: false } rule)
                return CommandResult.Fail(rule.Reason!);
            foreach (var cell in Entity.CellsFor(e.Def, pos, facing))
            {
                if (World.CellProblem(cell) is { } problem) return CommandResult.Fail($"{cell}: {problem}");
                var occupant = World.EntityAt(cell);
                if (occupant != null && !moving.Contains(occupant)) return CommandResult.Fail($"{cell} is occupied by {occupant.Def.Name}");
                if (!claimed.Add(cell)) return CommandResult.Fail($"Moved buildings overlap at {cell}");
            }
        }

        World.MoveEntities(plan);
        if (Events.Enabled)
            foreach (var (e, _, _) in plan) Events.Add(new EntityReoriented(World.Tick, e.Id, e.Pos, e.Facing));
        return CommandResult.Success(entities.Select(e => e.Id).ToList());
    }

    private CommandResult Choose(SelectRecipe c)
    {
        if (World.EntityAt(c.Cell) is not { } e) return CommandResult.Fail("Nothing here");
        if (e.Behavior.Selection(e) == c.Recipe) return CommandResult.Success(e.Id);
        if (e.Behavior.Select(e, c.Recipe) is { } error) return CommandResult.Fail(error);
        if (Events.Enabled) Events.Add(new EntitySelectionChanged(World.Tick, e.Id, c.Recipe));
        return CommandResult.Success(e.Id);
    }

    private CommandResult Filter(SetFilter c)
    {
        if (World.EntityAt(c.Cell) is not { } e) return CommandResult.Fail("Nothing here");
        var now = e.Behavior.Filters(e);
        if (c.Filter == null && now == null) return CommandResult.Success(e.Id);
        if (now != null && c.Output >= 0 && c.Output < now.Count && now[c.Output] == c.Filter) return CommandResult.Success(e.Id);
        if (e.Behavior.SetFilter(e, c.Output, c.Filter) is { } error) return CommandResult.Fail(error);
        if (Events.Enabled) Events.Add(new EntityFilterChanged(World.Tick, e.Id, c.Output, c.Filter));
        return CommandResult.Success(e.Id);
    }

    private CommandResult SetLevels(SetBuildingLevels c)
    {
        var plan = new List<(Entity Entity, int Level)>();
        BigNum cost = BigNum.Zero;
        foreach (var change in c.Changes)
        {
            var e = World.EntityAt(change.Cell);
            if (e == null) return CommandResult.Fail($"Nothing at {change.Cell}");
            if (plan.Exists(p => p.Entity == e)) continue;
            var track = e.Def.Upgrade;
            if (track == null) return CommandResult.Fail($"{e.Def.Name} can't be upgraded");
            if (change.Level < 1) return CommandResult.Fail("Level must be at least 1");
            if (track.MaxLevel is int max && change.Level > max) return CommandResult.Fail($"{e.Def.Name} is maxed at level {max}");
            cost += track.Invested(e.Def, change.Level) - track.Invested(e.Def, e.Level);
            plan.Add((e, change.Level));
        }
        if (plan.Count == 0) return CommandResult.Fail("Nothing to upgrade");
        if (!World.Sandbox)
        {
            // Upgrades cost the full difference; downgrades (undo) refund it like removal would.
            var charge = cost.Sign > 0 ? cost : cost * RefundFraction;
            if (World.Money < charge) return CommandResult.Fail($"Need {charge.Format()} (have {World.Money.Format()})");
            World.Money -= charge;
        }
        foreach (var (e, level) in plan)
        {
            e.Level = level;
            if (Events.Enabled) Events.Add(new EntityLevelChanged(World.Tick, e.Id, level));
        }
        return CommandResult.Success(plan.Select(p => p.Entity.Id).ToList());
    }

    /// <summary>What stops the next tier from being unlocked right now, or null when it can be.</summary>
    public string? NextTierBlocker()
    {
        int next = World.UnlockedTier + 1;
        if (next >= Content.Tiers.Count) return "Every tier is already unlocked";
        if (World.Sandbox) return null;
        var tier = Content.Tiers[next];
        if (World.Stats.TotalEarned < tier.RequiredEarnings)
            return $"Earn {tier.RequiredEarnings.Format()} in total first ({World.Stats.TotalEarned.Format()} so far)";
        foreach (var need in tier.Deliver)
        {
            long sold = TierDef.SoldOf(World.Stats, need);
            if (sold < need.Count)
                return $"Sell {need.Count} {Content.Items[need.Item].Name} first ({sold} so far)";
        }
        if (World.Money < tier.Cost) return $"Need {tier.Cost.Format()} (have {World.Money.Format()})";
        return null;
    }

    private CommandResult Unlock()
    {
        if (NextTierBlocker() is { } blocker) return CommandResult.Fail(blocker);
        int next = World.UnlockedTier + 1;
        var tier = Content.Tiers[next];
        if (!World.Sandbox) World.Money -= tier.Cost;
        World.UnlockedTier = next;
        if (Events.Enabled) Events.Add(new TierUnlocked(World.Tick, next, tier.Name));
        return CommandResult.Success();
    }

    /// <summary>
    /// Buys a plot of land: it must share an edge with land already owned, and costs
    /// <see cref="Land.PriceOf"/> (nothing in sandbox).
    /// </summary>
    private CommandResult BuyLand(BuyPlot c)
    {
        var plot = new PlotId(c.Column, c.Row);
        if (World.Land.WhyNot(plot) is { } why) return CommandResult.Fail(why);
        var price = World.Land.PriceOf(plot);
        if (!World.Sandbox)
        {
            if (World.Money < price) return CommandResult.Fail($"Need {price.Format()} (have {World.Money.Format()})");
            World.Money -= price;
        }
        World.Land.Add(plot);
        if (Events.Enabled) Events.Add(new PlotBought(World.Tick, plot.Column, plot.Row, price));
        return CommandResult.Success();
    }

    private CommandResult Buy(BuyUpgrade c)
    {
        if (!Content.Upgrades.TryGetValue(c.UpgradeId, out var def)) return CommandResult.Fail($"Unknown upgrade '{c.UpgradeId}'");
        int level = World.UpgradeLevel(def.Id);
        if (def.MaxLevel is int max && level >= max) return CommandResult.Fail($"{def.Name} is maxed");
        var cost = def.CostForLevel(level);
        var packs = def.PacksForLevel(level);
        if (!World.Sandbox)
        {
            if (def.Tier > World.UnlockedTier)
                return CommandResult.Fail($"{def.Name} opens with tier {def.Tier} ({Content.Tiers[def.Tier].Name})");
            if (World.Money < cost) return CommandResult.Fail($"Need {cost.Format()} (have {World.Money.Format()})");
            foreach (var p in packs)
                if (World.ScienceOf(p.Item) < p.Count)
                    return CommandResult.Fail($"Need {p.Count} {Content.Items[p.Item].Name} (have {World.ScienceOf(p.Item)})");
            World.Money -= cost;
            World.PayScience(packs);
        }
        World.SetUpgradeLevel(def.Id, level + 1);
        if (Events.Enabled) Events.Add(new UpgradePurchased(World.Tick, def.Id, level + 1, cost));
        return CommandResult.Success();
    }
}
