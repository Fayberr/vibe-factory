using FactorySim.Behaviors;

namespace FactorySim.Tests;

public class ConveyorTests
{
    [Fact]
    public void Items_travel_exactly_one_tile_per_ten_ticks()
    {
        var sim = TestUtil.NewSim();
        sim.Place("iron_miner", 0, 0, 0, Dir.East);
        sim.Line(new GridPos(1, 0, 0), Dir.East, 3);
        sim.Place("seller", 4, 0, 0, Dir.East);

        long? produced = null, sold = null;
        for (int t = 0; t < 200 && sold == null; t++)
        {
            sim.Step();
            foreach (var ev in sim.DrainEvents())
            {
                if (ev is ItemProduced p && produced == null) produced = p.Tick;
                if (ev is ItemSold s) sold = s.Tick;
            }
        }

        Assert.Equal(19, produced); // 20 ticks of work at rate 1
        // speed 100 units/tick, 1000 units/tile → 10 ticks per tile, no loss at tile edges.
        Assert.Equal(produced + 3 * 10, sold);
    }

    [Fact]
    public void Saturated_belt_throughput_is_speed_over_spacing()
    {
        var sim = TestUtil.NewSim(TestUtil.FastContent);
        sim.Place("fast_miner", 0, 0, 0, Dir.East);
        sim.Line(new GridPos(1, 0, 0), Dir.East, 6);
        sim.Place("seller", 7, 0, 0, Dir.East);

        sim.Step(200); // warm up
        long before = sim.Sold("iron_ore");
        sim.Step(1000);
        long sold = sim.Sold("iron_ore") - before;

        // speed 100 / spacing 250 = 0.4 items per tick.
        Assert.InRange(sold, 399, 401);
    }

    [Fact]
    public void Spacing_is_never_violated_on_a_tile()
    {
        var sim = TestUtil.NewSim(TestUtil.FastContent);
        sim.Place("fast_miner", 0, 0, 0, Dir.East);
        sim.Line(new GridPos(1, 0, 0), Dir.East, 4);
        // No sink: the line backs up completely.
        for (int t = 0; t < 300; t++)
        {
            sim.Step();
            for (int x = 1; x <= 4; x++)
            {
                var items = sim.Belt(x, 0, 0).Items;
                for (int i = 1; i < items.Count; i++) Assert.True(items[i - 1].Pos - items[i].Pos >= 250);
                Assert.All(items, it => Assert.InRange(it.Pos, 0, ConveyorBehavior.Length));
            }
        }

        // Fully backed up: 5 items per tile (1000, 750, 500, 250, 0).
        for (int x = 1; x <= 4; x++) Assert.Equal(5, sim.Belt(x, 0, 0).Items.Count);
        var miner = sim.World.EntityAt(new GridPos(0, 0, 0))!;
        Assert.False(miner.Behavior.GetStatus(miner).Working);
    }

    [Fact]
    public void Side_loading_merges_two_lines()
    {
        var sim = TestUtil.NewSim();
        sim.Place("iron_miner", 0, 0, 0, Dir.East);
        sim.Line(new GridPos(1, 0, 0), Dir.East, 4);
        sim.Place("seller", 5, 0, 0, Dir.East);
        sim.Place("copper_miner", 3, 3, 0, Dir.North);
        sim.Line(new GridPos(3, 2, 0), Dir.North, 2); // ends into the side of (3,0)

        sim.Step(20 * 60);
        Assert.InRange(sim.Sold("iron_ore"), 55, 60);   // 1/s
        Assert.InRange(sim.Sold("copper_ore"), 36, 40); // 1 per 1.5 s
    }

    [Fact]
    public void Bridge_lets_lines_cross_without_mixing()
    {
        var sim = TestUtil.NewSim();
        Samples.DemoLayout.Build(sim);
        sim.Step(20 * 60);

        var stats = sim.World.Stats;
        Assert.True(stats.Sold.GetValueOrDefault("iron_ingot") > 50);  // line A went over the bridge
        Assert.True(stats.Sold.GetValueOrDefault("copper_wire") > 50); // line B went under it
        Assert.True(stats.Sold.GetValueOrDefault("crate") > 10);       // line C merged planks and plates
        Assert.Equal(0, stats.Sold.GetValueOrDefault("iron_ore"));      // nothing leaked across lines
        Assert.Equal(0, stats.Sold.GetValueOrDefault("copper_ore"));
    }

    [Fact]
    public void Inline_effect_applies_once_per_item()
    {
        var sim = TestUtil.NewSim();
        sim.Place("iron_miner", 0, 0, 0, Dir.East);
        sim.Place("smelter", 1, 0, 0, Dir.East);
        sim.Line(new GridPos(2, 0, 0), Dir.East, 2, "polisher"); // two polishers in a row
        sim.Place("seller", 4, 0, 0, Dir.East);

        sim.Step(20 * 10);
        var sale = sim.DrainEvents().OfType<ItemSold>().First();
        Assert.Equal((BigNum)3, sale.Payout); // ingot 2 × 1.5, not × 2.25
    }
}
