using FactorySim.Behaviors;
using FactorySim.Content;

namespace FactorySim.Tests;

internal static class TestUtil
{
    public static readonly ContentRegistry Content = ContentRegistry.LoadDefault();

    /// <summary>
    /// Base content plus a very fast source for saturation tests and the kind of global
    /// "research" upgrades a mod pack can add (the base game only has per-building levels).
    /// </summary>
    public static readonly ContentRegistry FastContent = ContentRegistry.LoadDefault(null, new ContentPack
    {
        Upgrades =
        {
            new UpgradeDef { Id = "belt_speed", Stat = StatIds.ConveyorSpeed, PerLevel = 1.1, BaseCost = 100, CostGrowth = 1.6, MaxLevel = 9 },
            new UpgradeDef { Id = "miner_rate", Stat = StatIds.MinerRate, PerLevel = 1.15, BaseCost = 50 },
            new UpgradeDef { Id = "sell_price", Stat = StatIds.SellMultiplier, PerLevel = 1.25, BaseCost = 250 },
            new UpgradeDef { Id = "stack_size", Stat = StatIds.StackSize, Effect = UpgradeEffectKind.Add, PerLevel = 1, BaseCost = 500 },
        },
        Buildings =
        {
            new BuildingDef
            {
                Id = "fast_miner",
                Name = "Fast Miner",
                Behavior = "miner",
                Ports = new[] { new PortDef { Kind = PortKind.Out, Side = Side.Front } },
                Params = new MinerParams { Item = "iron_ore", Interval = 1, Amount = 1 },
            },
        },
    });

    /// <summary>
    /// A fresh world. Contracts and milestones are off unless <paramref name="goals"/>, so money stays exact.
    /// Every plot is owned unless <paramref name="allLand"/> is off, so tests about money, tiers or machines
    /// can build anywhere on the map; the land tests ask for the real thing (only the starting plot).
    /// </summary>
    public static Simulation NewSim(ContentRegistry? content = null, bool sandbox = true, BigNum? money = null, bool goals = false, bool allLand = true)
    {
        var sim = Simulation.CreateNew(content ?? Content, money ?? BigNum.Zero);
        sim.World.Sandbox = sandbox;
        sim.World.Goals = goals;
        if (allLand)
            foreach (var plot in sim.World.Land.All().ToList()) sim.World.Land.Add(plot);
        return sim;
    }

    public static int Place(this Simulation sim, string def, int x, int y, int z, Dir facing)
    {
        var r = sim.Execute(new PlaceBuilding(def, new GridPos(x, y, z), facing));
        Assert.True(r.Ok, r.Error);
        return r.EntityId;
    }

    /// <summary>Places <paramref name="length"/> conveyors starting at <paramref name="start"/> heading <paramref name="dir"/>.</summary>
    public static GridPos Line(this Simulation sim, GridPos start, Dir dir, int length, string def = "conveyor")
    {
        var p = start;
        for (int i = 0; i < length; i++)
        {
            sim.Place(def, p.X, p.Y, p.Z, dir);
            p = p.Step(dir);
        }
        return p; // first cell after the line
    }

    public static ConveyorState Belt(this Simulation sim, int x, int y, int z) =>
        (ConveyorState)sim.World.EntityAt(new GridPos(x, y, z))!.State;

    public static List<SimEvent> DrainEvents(this Simulation sim)
    {
        var list = new List<SimEvent>();
        sim.Events.Drain(list);
        return list;
    }

    public static long Sold(this Simulation sim, string item) => sim.World.Stats.Sold.GetValueOrDefault(item);
}
