using FactorySim.Behaviors;
using FactorySim.Content;

namespace FactorySim.Tests;

public class MachineAndEconomyTests
{
    [Fact]
    public void Smelter_converts_ore_and_multiplies_value()
    {
        var sim = TestUtil.NewSim();
        sim.Place("iron_miner", 0, 0, 0, Dir.East);
        sim.Place("conveyor", 1, 0, 0, Dir.East);
        sim.Place("smelter", 2, 0, 0, Dir.East);
        sim.Place("conveyor", 3, 0, 0, Dir.East);
        sim.Place("seller", 4, 0, 0, Dir.East);

        sim.Step(20 * 30);
        var sales = sim.DrainEvents().OfType<ItemSold>().ToList();
        Assert.NotEmpty(sales);
        Assert.All(sales, s => Assert.Equal("iron_ingot", s.Item));
        Assert.All(sales, s => Assert.Equal((BigNum)2, s.Payout)); // ore 1 × smelt 2
        Assert.InRange(sales.Count, 27, 30);                        // miner-bound: 1/s
    }

    [Fact]
    public void Workshop_merges_ingredients_and_combines_value()
    {
        var sim = TestUtil.NewSim();
        Samples.DemoLayout.Build(sim);
        sim.Step(20 * 60);

        var crates = sim.DrainEvents().OfType<ItemSold>().Where(s => s.Item == "crate").ToList();
        Assert.NotEmpty(crates);
        // plank = log 1.2 × 1.8 / 2 = 1.08, plate = ore 1 × 2 × 1.5 = 3 → crate = (2 × 1.08 + 3) × 2.
        Assert.All(crates, s => Assert.Equal(10.32, s.Payout.ToDouble() / s.Count, 9));
    }

    [Fact]
    public void Processor_rejects_items_it_has_no_recipe_for()
    {
        var sim = TestUtil.NewSim();
        sim.Place("copper_miner", 0, 0, 0, Dir.East);
        sim.Place("conveyor", 1, 0, 0, Dir.East);
        sim.Place("press", 2, 0, 0, Dir.East); // takes ingots, not ore

        sim.Step(20 * 10);
        var press = sim.World.EntityAt(new GridPos(2, 0, 0))!;
        Assert.Empty(((ProcessorState)press.State).Inputs);
        Assert.NotEmpty(sim.Belt(1, 0, 0).Items); // copper waits on the belt
    }

    [Fact]
    public void Upgrades_cost_money_scale_stats_and_respect_caps()
    {
        var sim = TestUtil.NewSim(sandbox: false, money: 1_000_000);
        var def = sim.Content.Upgrades["belt_speed"];

        Assert.True(sim.Execute(new BuyUpgrade("belt_speed")).Ok);
        Assert.True(sim.Execute(new BuyUpgrade("belt_speed")).Ok);
        Assert.Equal(1.1 * 1.1, sim.World.Stat(StatIds.ConveyorSpeed), 12);
        Assert.Equal((BigNum)1_000_000 - def.CostForLevel(0) - def.CostForLevel(1), sim.World.Money);

        for (int i = 2; i < def.MaxLevel; i++) Assert.True(sim.Execute(new BuyUpgrade("belt_speed")).Ok);
        Assert.False(sim.Execute(new BuyUpgrade("belt_speed")).Ok); // capped

        Assert.True(sim.Execute(new BuyUpgrade("stack_size")).Ok);
        Assert.Equal(2, sim.World.Stat(StatIds.StackSize)); // additive: 1 + 1
    }

    [Fact]
    public void Faster_miners_emit_bundles_once_stack_size_allows()
    {
        var sim = TestUtil.NewSim(TestUtil.FastContent);
        for (int i = 0; i < 3; i++) sim.Execute(new BuyUpgrade("stack_size")); // stack 4
        for (int i = 0; i < 20; i++) sim.Execute(new BuyUpgrade("miner_rate"));  // ×16.4

        sim.Place("fast_miner", 0, 0, 0, Dir.East);
        sim.Place("conveyor", 1, 0, 0, Dir.East);
        sim.Place("seller", 2, 0, 0, Dir.East);
        sim.Step(400);

        var sales = sim.DrainEvents().OfType<ItemSold>().ToList();
        Assert.All(sales, s => Assert.InRange(s.Count, 1, 4));
        Assert.Contains(sales, s => s.Count == 4);
        // Belt still moves ≤ 0.4 bundles/tick, but each bundle now carries 4 units.
        Assert.True(sim.Sold("iron_ore") > 0.4 * 300 * 3);
    }

    [Fact]
    public void Seller_pays_value_times_multipliers()
    {
        var sim = TestUtil.NewSim();
        sim.Execute(new BuyUpgrade("sell_price")); // ×1.25
        sim.Place("iron_miner", 0, 0, 0, Dir.East);
        sim.Place("seller", 1, 0, 0, Dir.East);
        sim.Step(20 * 10);

        Assert.Equal(10, sim.Sold("iron_ore"));
        Assert.Equal(12.5, sim.World.Money.ToDouble(), 9);
        Assert.Equal(sim.World.Money, sim.World.Stats.TotalEarned);
    }

    [Fact]
    public void Income_window_reports_steady_state_rate()
    {
        var sim = TestUtil.NewSim();
        sim.Place("iron_miner", 0, 0, 0, Dir.East);
        sim.Place("seller", 1, 0, 0, Dir.East);
        sim.Step(20 * 30);
        Assert.Equal(1.0, sim.World.Stats.IncomePerSecond(10).ToDouble(), 9);
    }
}
