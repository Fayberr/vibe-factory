using FactorySim.Content;

namespace FactorySim;

/// <summary>A command stamped with the tick it was applied at.</summary>
public sealed record LoggedCommand(long Tick, Command Command);

/// <summary>
/// Fixed-timestep driver around a <see cref="World"/>. The host (Godot, CLI, server)
/// feeds it real time via <see cref="Advance"/> or calls <see cref="Step()"/> directly;
/// the simulation itself never reads a clock, so identical inputs give identical results.
/// </summary>
public sealed class Simulation
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
            _ => CommandResult.Fail($"Unknown command {command.GetType().Name}"),
        };
        if (result.Ok) CommandLog?.Add(new LoggedCommand(World.Tick, command));
        return result;
    }

    private CommandResult Place(PlaceBuilding c)
    {
        if (!Content.Buildings.TryGetValue(c.DefId, out var def)) return CommandResult.Fail($"Unknown building '{c.DefId}'");
        var check = World.CanPlace(def, c.Pos, c.Facing);
        if (!check.Ok) return CommandResult.Fail(check.Reason!);
        if (!World.Sandbox)
        {
            if (World.Money < def.Cost) return CommandResult.Fail($"Need {def.Cost.Format()} (have {World.Money.Format()})");
            World.Money -= def.Cost;
        }
        var e = World.AddEntity(def, c.Pos, c.Facing);
        if (Events.Enabled) Events.Add(new EntityPlaced(World.Tick, e.Id, def.Id, e.Pos, e.Facing));
        return CommandResult.Success(e.Id);
    }

    private CommandResult Remove(RemoveBuilding c)
    {
        var e = World.EntityAt(c.Cell);
        if (e == null) return CommandResult.Fail($"Nothing at {c.Cell}");
        World.RemoveEntity(e);
        if (!World.Sandbox) World.Money += e.Def.Cost * RefundFraction;
        if (Events.Enabled) Events.Add(new EntityRemoved(World.Tick, e.Id, e.Def.Id, e.Pos));
        return CommandResult.Success(e.Id);
    }

    private CommandResult Rotate(RotateBuilding c)
    {
        var e = World.EntityAt(c.Cell);
        if (e == null) return CommandResult.Fail($"Nothing at {c.Cell}");
        var facing = c.Facing ?? e.Facing.RotateCW();
        var check = World.CanPlace(e.Def, e.Pos, facing, ignore: e);
        if (!check.Ok) return CommandResult.Fail(check.Reason!);
        World.Reorient(e, e.Pos, facing);
        if (Events.Enabled) Events.Add(new EntityReoriented(World.Tick, e.Id, e.Pos, e.Facing));
        return CommandResult.Success(e.Id);
    }

    private CommandResult Buy(BuyUpgrade c)
    {
        if (!Content.Upgrades.TryGetValue(c.UpgradeId, out var def)) return CommandResult.Fail($"Unknown upgrade '{c.UpgradeId}'");
        int level = World.UpgradeLevel(def.Id);
        if (def.MaxLevel is int max && level >= max) return CommandResult.Fail($"{def.Name} is maxed");
        var cost = def.CostForLevel(level);
        if (!World.Sandbox)
        {
            if (World.Money < cost) return CommandResult.Fail($"Need {cost.Format()} (have {World.Money.Format()})");
            World.Money -= cost;
        }
        World.SetUpgradeLevel(def.Id, level + 1);
        if (Events.Enabled) Events.Add(new UpgradePurchased(World.Tick, def.Id, level + 1, cost));
        return CommandResult.Success();
    }
}
