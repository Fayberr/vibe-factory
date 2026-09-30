using FactorySim.Persistence;

namespace FactorySim.Tests;

/// <summary>History graphs (idea F2): the saved money, income and production series.</summary>
public class HistoryTests
{
    private static readonly DateTimeOffset At = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);
    private const int Recent = HistoryLog.RecentIntervalSeconds * Simulation.TicksPerSecond;

    /// <summary>A drill feeding a smelter feeding a depot: ingots made and sold at a steady rate.</summary>
    private static Simulation IngotLine()
    {
        var sim = TestUtil.NewSim();
        sim.Place("iron_miner", 2, 0, 0, Dir.South);
        sim.Place("smelter", 2, 1, 0, Dir.South);
        sim.Place("seller", 2, 2, 0, Dir.South);
        return sim;
    }

    [Fact]
    public void It_takes_a_point_every_interval_with_money_income_and_what_was_made()
    {
        var sim = IngotLine();
        sim.Step(Recent * 5);
        var s = sim.World.Stats.History.Recent;
        Assert.Equal(5, s.Count);
        Assert.Equal(Enumerable.Range(1, 5).Select(i => (long)i * Recent), s.Ticks);
        Assert.Equal(sim.World.Money, s.Money[^1]);
        Assert.Null(s.IncomeAt(0));

        // One ore a second in, one ingot a second out, sold at a depot.
        Assert.Equal(1.0, s.RateAt("iron_ingot", 4)!.Value, 1);
        double ingot = sim.World.Stats.EarnedByItem["iron_ingot"].ToDouble() / sim.World.Stats.Sold["iron_ingot"];
        Assert.Equal(ingot, s.IncomeAt(4)!.Value, 1);
        Assert.Equal("iron_ingot", s.Items()[0]);
        Assert.Equal(0, sim.World.Stats.History.Long.Count);
    }

    [Fact]
    public void It_keeps_only_its_capacity_and_every_list_stays_aligned()
    {
        var sim = IngotLine();
        sim.Step((long)Recent * (HistoryLog.RecentCapacity + 7));
        var s = sim.World.Stats.History.Recent;
        Assert.Equal(HistoryLog.RecentCapacity, s.Count);
        Assert.Equal((long)Recent * (HistoryLog.RecentCapacity + 7), s.Ticks[^1]);
        Assert.Equal(s.Count, s.Money.Count);
        Assert.Equal(s.Count, s.Earned.Count);
        Assert.All(s.Made.Values, list => Assert.Equal(s.Count, list.Count));
        long seconds = (long)HistoryLog.RecentIntervalSeconds * (HistoryLog.RecentCapacity + 7);
        Assert.Equal(seconds / HistoryLog.LongIntervalSeconds, sim.World.Stats.History.Long.Count);
    }

    [Fact]
    public void Income_across_an_offline_gap_is_the_average_over_the_gap()
    {
        var sim = IngotLine();
        sim.Step(Recent * 3);
        double before = sim.World.Stats.History.Recent.IncomeAt(2)!.Value;

        sim.CatchUp(3600);
        sim.Step(Recent);
        var s = sim.World.Stats.History.Recent;
        // The catch-up simulates the first 10 minutes and skips the rest, so one point spans the skip.
        int gap = Enumerable.Range(1, s.Count - 1).MaxBy(s.SecondsBefore);
        Assert.True(s.SecondsBefore(gap) > 2900);
        Assert.InRange(s.IncomeAt(gap)!.Value, before * 0.8, before * 1.2);
    }

    [Fact]
    public void Order_rewards_are_not_income()
    {
        var sim = IngotLine();
        sim.Step(Recent * 3);
        double before = sim.World.Stats.History.Recent.IncomeAt(2)!.Value;

        BigNum bonus = 1_000_000L;
        sim.World.AddMoney(bonus);
        sim.World.Stats.TotalEarned += bonus;
        sim.World.Stats.RewardsEarned += bonus;
        sim.Step(Recent);
        var s = sim.World.Stats.History.Recent;
        Assert.InRange(s.IncomeAt(3)!.Value, before * 0.9, before * 1.1);
        Assert.True(s.Money[3].ToDouble() > 1_000_000);
    }

    [Fact]
    public void It_survives_a_save_and_an_old_save_starts_empty()
    {
        var sim = IngotLine();
        sim.Step(Recent * 4);
        var json = SaveSystem.Serialize(sim, At);
        var loaded = SaveSystem.Deserialize(json, TestUtil.Content).Simulation;
        Assert.Equal(sim.World.Stats.History.Recent.Ticks, loaded.World.Stats.History.Recent.Ticks);
        Assert.Equal(sim.World.Stats.History.Recent.Made["iron_ingot"], loaded.World.Stats.History.Recent.Made["iron_ingot"]);
        Assert.Equal(json, SaveSystem.Serialize(loaded, At));

        // Continuing after a load gives the same points as never having saved.
        sim.Step(Recent * 2);
        loaded.Step(Recent * 2);
        Assert.Equal(SaveSystem.Serialize(sim, At), SaveSystem.Serialize(loaded, At));

        var old = System.Text.Json.Nodes.JsonNode.Parse(json)!;
        old["stats"]!.AsObject().Remove("history");
        var fromOld = SaveSystem.Deserialize(old.ToJsonString(), TestUtil.Content).Simulation;
        Assert.Equal(0, fromOld.World.Stats.History.Recent.Count);
    }
}
