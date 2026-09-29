using FactorySim.Content;
using FactorySim.Persistence;

namespace FactorySim.Tests;

/// <summary>
/// The polisher multiplies the value of what passes it, and the depot is the only place that
/// bonus is paid. A machine values its inputs at their plain value, so polishing an ingredient
/// is wasted: the bonus has to be applied to what you actually sell.
/// </summary>
public class PolishTests
{
    private const int Warmup = 1200, Window = 2000;

    /// <summary>Drill, smelter and depot with belts between them, a polisher where asked.</summary>
    private static Simulation Line(params int[] polishers)
    {
        var sim = TestUtil.NewSim();
        sim.Place("iron_miner", 0, 0, 0, Dir.East);
        for (int x = 1; x <= 5; x++)
            sim.Place(polishers.Contains(x) ? "polisher" : "conveyor", x, 0, 0, Dir.East);
        sim.Place("smelter", 6, 0, 0, Dir.East);
        for (int x = 7; x <= 8; x++)
            sim.Place(polishers.Contains(x) ? "polisher" : "conveyor", x, 0, 0, Dir.East);
        sim.Place("seller", 9, 0, 0, Dir.East);
        return sim;
    }

    /// <summary>Money per second once the line has settled.</summary>
    private static double Income(Simulation sim)
    {
        sim.Step(Warmup);
        double before = sim.World.Stats.TotalEarned.ToDouble();
        sim.Step(Window);
        return (sim.World.Stats.TotalEarned.ToDouble() - before) / (Window / (double)Simulation.TicksPerSecond);
    }

    [Fact]
    public void Polishing_an_ingredient_pays_nothing()
    {
        // One drill making 1 iron ore/s: $1 of ore becomes a $2 ingot, sold at par by the
        // base depot, so the line earns $2/s until something multiplies the price.
        double plain = Income(Line());
        Assert.Equal(2, plain, 1);

        // A polisher on the ore, before the smelter, changes nothing at all.
        Assert.Equal(plain, Income(Line(3)), 1);
    }

    [Fact]
    public void Polishing_the_product_pays_once()
    {
        double plain = Income(Line());
        double polished = Income(Line(8));
        Assert.Equal(1.5, polished / plain, 2); // $2 per ingot becomes $3
    }

    [Fact]
    public void The_bonus_never_compounds_through_the_chain()
    {
        double plain = Income(Line());

        // Polishing ore, ingot and product: the two upstream ones are wasted, the result is
        // exactly the line with a single polisher before the depot.
        Assert.Equal(Income(Line(8)), Income(Line(3, 7, 8)), 2);
        Assert.Equal(Income(Line(8)), Income(Line(3, 7)), 2);

        // And a line with polishers all the way up to the smelter earns the plain income.
        Assert.Equal(plain, Income(Line(1, 2, 3, 4, 5)), 1);
    }

    [Fact]
    public void A_polished_item_keeps_its_bonus_across_belts()
    {
        // The bonus rides on the item, so a long run to the depot does not lose it.
        var sim = TestUtil.NewSim();
        sim.Place("iron_miner", 0, 0, 0, Dir.East);
        sim.Place("conveyor", 1, 0, 0, Dir.East);
        sim.Place("smelter", 2, 0, 0, Dir.East);
        sim.Place("polisher", 3, 0, 0, Dir.East);
        sim.Line(new GridPos(4, 0, 0), Dir.East, 8);
        sim.Place("seller", 12, 0, 0, Dir.East);

        Assert.Equal(3, Income(sim), 1); // 1 ingot/s at $3
    }

    [Fact]
    public void Polished_ore_still_earns_the_bonus_on_the_raw_price()
    {
        var plain = TestUtil.NewSim();
        plain.Place("iron_miner", 0, 0, 0, Dir.East);
        plain.Place("conveyor", 1, 0, 0, Dir.East);
        plain.Place("seller", 2, 0, 0, Dir.East);

        var polished = TestUtil.NewSim();
        polished.Place("iron_miner", 0, 0, 0, Dir.East);
        polished.Place("polisher", 1, 0, 0, Dir.East);
        polished.Place("seller", 2, 0, 0, Dir.East);

        // Raw ore sells for a quarter of $1, and the polisher's 1.5 applies to that price.
        Assert.Equal(0.25, Income(plain), 6);
        Assert.Equal(0.375, Income(polished), 6);
    }

    [Fact]
    public void The_bonus_survives_a_save_and_load()
    {
        var sim = Line(8);
        sim.Step(Warmup);

        var loaded = SaveSystem.Deserialize(SaveSystem.Serialize(sim), TestUtil.Content).Simulation;
        double before = loaded.World.Stats.TotalEarned.ToDouble();
        loaded.Step(Window);
        double perSecond = (loaded.World.Stats.TotalEarned.ToDouble() - before) / (Window / (double)Simulation.TicksPerSecond);

        Assert.Equal(3, perSecond, 1); // the bonus was in the save, not recomputed
    }
}
