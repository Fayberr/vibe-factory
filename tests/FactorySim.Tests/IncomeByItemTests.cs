using System.Text.Json.Nodes;
using FactorySim.Persistence;
using FactorySim.Samples;

namespace FactorySim.Tests;

/// <summary>Income broken down by product: which items make the money.</summary>
public class IncomeByItemTests
{
    /// <summary>Two products: raw iron ore sold straight from a drill, and smelted ingots.</summary>
    private static Simulation TwoProducts()
    {
        var sim = TestUtil.NewSim();
        sim.Place("iron_miner", 0, 0, 0, Dir.East);
        sim.Place("seller", 1, 0, 0, Dir.East);
        sim.Place("iron_miner", 0, 2, 0, Dir.East);
        sim.Place("smelter", 1, 2, 0, Dir.East);
        sim.Place("seller", 2, 2, 0, Dir.East);
        return sim;
    }

    private static BigNum Paid(IEnumerable<ItemSold> sales, string item)
    {
        BigNum sum = BigNum.Zero;
        foreach (var s in sales.Where(s => s.Item == item)) sum += s.Payout;
        return sum;
    }

    [Fact]
    public void Per_item_rate_matches_what_was_sold_over_the_window()
    {
        var sim = TwoProducts();
        sim.Step(20 * 30);
        var sales = sim.DrainEvents().OfType<ItemSold>().ToList();
        var stats = sim.World.Stats;

        foreach (var item in new[] { "iron_ore", "iron_ingot" })
        {
            // The last 10 completed seconds are ticks 400..599.
            var recent = Paid(sales.Where(s => s.Tick >= 20 * 20), item);
            Assert.True(recent > BigNum.Zero, item);
            Assert.Equal((recent / 10).ToDouble(), stats.IncomePerSecondOf(item, 10).ToDouble(), 9);
            Assert.Equal((Paid(sales, item) / 30).ToDouble(), stats.IncomePerSecondOf(item).ToDouble(), 9);
        }
        Assert.Equal(0.25, stats.IncomePerSecondOf("iron_ore", 10).ToDouble(), 9); // 1 raw ore/s at 25%
        Assert.Equal(new[] { "iron_ingot", "iron_ore" }, stats.IncomePerSecondByItem().Keys.OrderBy(k => k, StringComparer.Ordinal));
        Assert.Equal(BigNum.Zero, stats.IncomePerSecondOf("copper_wire"));
    }

    [Fact]
    public void Per_item_window_rolls_like_the_total_window()
    {
        var stats = new StatsTracker();
        long tick = 0;
        void Second(params (string Item, double Value)[] sales)
        {
            foreach (var (item, value) in sales) stats.RecordSale(item, 1, value);
            for (int t = 0; t < Simulation.TicksPerSecond; t++) stats.EndTick(tick++);
        }

        for (int i = 0; i < 5; i++) Second(("a", 4), ("b", 1));
        Assert.Equal(4.0, stats.IncomePerSecondOf("a").ToDouble(), 12);
        Assert.Equal(1.0, stats.IncomePerSecondOf("b").ToDouble(), 12);

        // b stops selling: its rate falls as its seconds leave the window, and then it earns nothing.
        for (int i = 0; i < 5; i++) Second(("a", 4));
        Assert.Equal(0.5, stats.IncomePerSecondOf("b", 10).ToDouble(), 12);
        for (int i = 0; i < StatsTracker.WindowSeconds; i++) Second(("a", 4));
        Assert.Equal(BigNum.Zero, stats.IncomePerSecondOf("b"));
        Assert.Equal(BigNum.Zero, stats.IncomeShareOf("b"));
        Assert.Equal(new[] { "a" }, stats.IncomePerSecondByItem().Keys);
        Assert.Equal(stats.IncomePerSecond(), stats.IncomePerSecondOf("a"));
        Assert.Equal(new BigNum(5, 0), stats.EarnedByItem["b"]); // lifetime stays
    }

    [Fact]
    public void Lifetime_earned_per_item_equals_the_sales_that_item_saw()
    {
        var sim = TwoProducts();
        sim.Step(20 * 45);
        var sales = sim.DrainEvents().OfType<ItemSold>().ToList();
        var stats = sim.World.Stats;

        Assert.Equal(new[] { "iron_ingot", "iron_ore" }, stats.EarnedByItem.Keys.OrderBy(k => k, StringComparer.Ordinal));
        foreach (var (item, earned) in stats.EarnedByItem)
            Assert.Equal(Paid(sales, item), earned); // summed in the same order, so exact
        Assert.Equal(stats.TotalEarned.ToDouble(), stats.EarnedByItem.Values.Sum(v => v.ToDouble()), 9);
    }

    [Fact]
    public void Lifetime_earned_is_the_value_handed_to_RecordSale()
    {
        var stats = new StatsTracker();
        stats.RecordSale("gear", 3, new BigNum(7.5, 0));
        stats.RecordSale("gear", 1, new BigNum(1, 30));
        stats.RecordSale("wire", 100, BigNum.Zero);
        Assert.Equal(new BigNum(7.5, 0) + new BigNum(1, 30), stats.EarnedByItem["gear"]);
        Assert.Equal(BigNum.Zero, stats.EarnedByItem["wire"]);
    }

    [Fact]
    public void Shares_add_up_to_about_100_percent_whenever_there_is_income()
    {
        var sim = TwoProducts();
        var stats = sim.World.Stats;
        for (int second = 1; second <= 90; second++)
        {
            sim.Step(20);
            var rates = stats.IncomePerSecondByItem();
            if (stats.IncomePerSecond().IsZero)
            {
                Assert.Empty(rates);
                continue;
            }
            double shares = rates.Keys.Sum(item => stats.IncomeShareOf(item).ToDouble());
            Assert.Equal(1.0, shares, 9);
            Assert.All(rates.Keys, item => Assert.InRange(stats.IncomeShareOf(item).ToDouble(), 0, 1 + 1e-9));
        }
        Assert.Equal(2, stats.IncomePerSecondByItem().Count);
    }

    [Fact]
    public void Nothing_sold_means_no_income_and_no_division_by_zero()
    {
        var sim = TwoProducts();
        var stats = sim.World.Stats;
        Assert.Empty(stats.IncomePerSecondByItem());
        Assert.Equal(BigNum.Zero, stats.IncomeShareOf("iron_ore"));
        sim.Step(1); // a sale before any second completes has no rate yet
        Assert.Equal(BigNum.Zero, stats.IncomeShareOf("iron_ore"));
        Assert.Empty(stats.Snapshot(sim.World.Tick).EarnedByItem);
    }

    [Fact]
    public void Save_and_load_keep_the_per_item_numbers()
    {
        var sim = TwoProducts();
        sim.Step(20 * 40 + 7);
        var loaded = SaveSystem.Deserialize(SaveSystem.Serialize(sim), TestUtil.Content);
        Assert.Empty(loaded.Warnings);
        var before = sim.World.Stats;
        var after = loaded.Simulation.World.Stats;

        Assert.Equal(before.EarnedByItem, after.EarnedByItem);
        Assert.Equal(before.IncomePerSecondByItem(), after.IncomePerSecondByItem());
        Assert.Equal(before.IncomePerSecondByItem(10), after.IncomePerSecondByItem(10));
        foreach (var item in before.EarnedByItem.Keys)
            Assert.Equal(before.IncomeShareOf(item), after.IncomeShareOf(item));
        Assert.Equal(before.Snapshot(sim.World.Tick).EarnedByItem, after.Snapshot(loaded.Simulation.World.Tick).EarnedByItem);

        // The window keeps rolling the same way after loading.
        sim.Step(20 * 30);
        loaded.Simulation.Step(20 * 30);
        Assert.Equal(before.IncomePerSecondByItem(), after.IncomePerSecondByItem());
        Assert.Equal(before.EarnedByItem, after.EarnedByItem);
    }

    [Fact]
    public void An_old_save_without_per_item_income_loads_whole_and_reports_none()
    {
        var sim = TestUtil.NewSim(sandbox: false, money: 10_000);
        DemoLayout.Build(sim);
        sim.Step(20 * 60);
        Assert.NotEmpty(sim.World.Stats.EarnedByItem);

        var json = JsonNode.Parse(SaveSystem.Serialize(sim))!;
        var saved = json["stats"]!.AsObject();
        Assert.True(saved.Remove("earnedByItem"));
        Assert.True(saved.Remove("earnedBucketsByItem"));
        var loaded = SaveSystem.Deserialize(json.ToJsonString(), TestUtil.Content);

        Assert.Empty(loaded.Warnings);
        var world = loaded.Simulation.World;
        Assert.Equal(sim.World.EntityCount, world.EntityCount);
        Assert.Equal(
            sim.World.Entities.OrderBy(e => e.Id).Select(e => (e.Id, e.Def.Id, e.Pos, e.Facing, e.Level)),
            world.Entities.OrderBy(e => e.Id).Select(e => (e.Id, e.Def.Id, e.Pos, e.Facing, e.Level)));
        Assert.Equal(sim.World.Stats.TotalEarned, world.Stats.TotalEarned);
        Assert.Equal(sim.World.Stats.IncomePerSecond(), world.Stats.IncomePerSecond());

        Assert.Empty(world.Stats.EarnedByItem);
        Assert.Empty(world.Stats.IncomePerSecondByItem());
        Assert.All(sim.World.Stats.Sold.Keys, item => Assert.Equal(BigNum.Zero, world.Stats.IncomeShareOf(item)));
        Assert.Empty(world.Stats.Snapshot(world.Tick).EarnedByItem);

        // From then on it counts as usual.
        loaded.Simulation.Step(20 * 5);
        Assert.NotEmpty(world.Stats.EarnedByItem);
        Assert.NotEmpty(world.Stats.IncomePerSecondByItem());
    }

    [Fact]
    public void Snapshot_carries_lifetime_earned_per_item_and_keeps_its_other_fields()
    {
        var sim = TwoProducts();
        sim.Step(20 * 20);
        var stats = sim.World.Stats;
        var snap = stats.Snapshot(sim.World.Tick);

        Assert.Equal(stats.EarnedByItem, snap.EarnedByItem);
        Assert.Equal(stats.TotalEarned, snap.TotalEarned);
        Assert.Equal(stats.IncomePerSecond(), snap.IncomePerSecond);
        Assert.Equal(stats.Sold, snap.Sold);
        Assert.Equal(stats.Produced, snap.Produced);

        sim.Step(20); // a copy, not a live view
        Assert.NotEqual(stats.EarnedByItem["iron_ore"], snap.EarnedByItem["iron_ore"]);
    }
}
