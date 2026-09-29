using FactorySim.Behaviors;

namespace FactorySim.Tests;

public class ConveyorTests
{
    [Fact]
    public void Items_travel_exactly_one_tile_per_twenty_ticks()
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
        // speed 50 units/tick, 1000 units/tile → 20 ticks per tile, no loss at tile edges.
        Assert.Equal(produced + 3 * 20, sold);
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

        // speed 50 / spacing 250 = 0.2 items per tick (4 per second).
        Assert.InRange(sold, 199, 201);
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
    public void A_belt_does_not_merge_into_the_side_of_another_belt()
    {
        var sim = TestUtil.NewSim();
        sim.Place("iron_miner", 0, 0, 0, Dir.East);
        sim.Line(new GridPos(1, 0, 0), Dir.East, 4);
        sim.Place("seller", 5, 0, 0, Dir.East);
        sim.Place("copper_miner", 3, 3, 0, Dir.North);
        sim.Line(new GridPos(3, 2, 0), Dir.North, 2); // ends against the side of (3,0)

        sim.Step(20 * 60);
        Assert.InRange(sim.Sold("iron_ore"), 55, 60); // the main line is untouched
        Assert.Equal(0, sim.Sold("copper_ore"));      // the side line only backs up: that takes a merger
        var side = sim.World.EntityAt(new GridPos(3, 1, 0))!;
        Assert.Null(side.Link(side.Def.OutputPorts[0]).Target);
        Assert.Null(ConveyorBehavior.CurveSide(sim.World.EntityAt(new GridPos(3, 0, 0))!)); // and it stays straight
    }

    [Fact]
    public void A_machine_can_still_load_a_belt_from_the_side()
    {
        var sim = TestUtil.NewSim();
        sim.Place("iron_miner", 0, 0, 0, Dir.East);
        sim.Line(new GridPos(1, 0, 0), Dir.East, 4);
        sim.Place("seller", 5, 0, 0, Dir.East);
        sim.Place("copper_miner", 3, 1, 0, Dir.North); // points straight into the side of (3,0)

        sim.Step(20 * 60);
        Assert.InRange(sim.Sold("iron_ore"), 55, 60);
        Assert.InRange(sim.Sold("copper_ore"), 36, 40);
    }

    [Fact]
    public void Two_side_belts_into_a_belt_with_nothing_behind_it_are_not_a_merger_either()
    {
        var sim = TestUtil.NewSim();
        sim.Place("iron_miner", 2, 0, 0, Dir.South);
        sim.Place("conveyor", 2, 1, 0, Dir.South);   // from the north side
        sim.Place("copper_miner", 2, 4, 0, Dir.North);
        sim.Place("conveyor", 2, 3, 0, Dir.North);   // from the south side
        sim.Place("conveyor", 2, 2, 0, Dir.East);    // nothing behind it, so neither side is a curve
        sim.Place("seller", 3, 2, 0, Dir.East);

        sim.Step(20 * 30);
        Assert.Equal(0, sim.Sold("iron_ore"));
        Assert.Equal(0, sim.Sold("copper_ore"));
    }

    [Fact]
    public void Bridge_lets_lines_cross_without_mixing()
    {
        var sim = TestUtil.NewSim();
        Samples.DemoLayout.Build(sim);
        sim.Step(20 * 60);

        var stats = sim.World.Stats;
        string sold = string.Join(", ", stats.Sold.Select(kv => $"{kv.Key}={kv.Value}"));
        // Each line now ends with a belt run out to the rim of the plot, where its depot stands,
        // so the first sale is ~15 tiles in (level 1 belts move 1 tile/s).
        Assert.True(stats.Sold.GetValueOrDefault("iron_ingot") > 35, sold);  // line A went over the bridge
        Assert.True(stats.Sold.GetValueOrDefault("copper_wire") > 35, sold); // line B went under it
        Assert.True(stats.Sold.GetValueOrDefault("crate") > 10, sold);       // line C merged planks and plates
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
