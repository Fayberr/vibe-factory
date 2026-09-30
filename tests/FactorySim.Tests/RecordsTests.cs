using FactorySim.Persistence;

namespace FactorySim.Tests;

/// <summary>Personal records for the statistics page (idea G6).</summary>
public class RecordsTests
{
    private static Simulation Line()
    {
        var sim = TestUtil.NewSim();
        sim.Place("iron_miner", 2, 0, 0, Dir.South);
        sim.Place("smelter", 2, 1, 0, Dir.South);
        sim.Place("seller", 2, 2, 0, Dir.South);
        return sim;
    }

    [Fact]
    public void Money_and_buildings_records_follow_the_peak_and_stay_there()
    {
        var sim = Line();
        sim.Step(5 * Simulation.TicksPerSecond + 1); // records are taken as a second starts
        var r = sim.World.Stats.Records;
        Assert.Equal(3, r.MostBuildings);
        Assert.Equal(sim.World.Money, r.MostMoney);

        var peak = sim.World.Money;
        Assert.True(sim.Execute(new RemoveBuildings(new[] { new GridPos(2, 2, 0) })).Ok);
        sim.World.Money = BigNum.Zero;
        sim.Step(2 * Simulation.TicksPerSecond);
        Assert.Equal(3, r.MostBuildings);
        Assert.True(r.MostMoney >= peak);
    }

    [Fact]
    public void Best_income_waits_for_a_full_minute()
    {
        var sim = Line();
        sim.Step(30 * Simulation.TicksPerSecond);
        Assert.True(sim.World.Stats.TotalEarned > BigNum.Zero);
        Assert.True(sim.World.Stats.Records.BestIncome.IsZero);
        sim.Step(40 * Simulation.TicksPerSecond);
        Assert.True(sim.World.Stats.Records.BestIncome > BigNum.Zero);
        Assert.True(sim.World.Stats.Records.BestIncomeTick >= 59 * Simulation.TicksPerSecond);
    }

    [Fact]
    public void A_tier_unlock_is_timed_once()
    {
        var sim = TestUtil.NewSim();
        sim.World.Sandbox = true;
        sim.Step(3 * Simulation.TicksPerSecond);
        var unlocked = sim.Execute(new UnlockTier());
        Assert.True(unlocked.Ok, unlocked.Error);
        Assert.Equal(3 * Simulation.TicksPerSecond, sim.World.Stats.Records.TierTicks[1]);
    }

    [Fact]
    public void Records_survive_a_save()
    {
        var sim = Line();
        sim.Step(70 * Simulation.TicksPerSecond);
        var before = sim.World.Stats.Records;
        var loaded = SaveSystem.Deserialize(SaveSystem.Serialize(sim), TestUtil.Content).Simulation!;
        var after = loaded.World.Stats.Records;
        Assert.Equal(before.BestIncome, after.BestIncome);
        Assert.Equal(before.MostMoneyTick, after.MostMoneyTick);
        Assert.Equal(before.MostBuildings, after.MostBuildings);
    }

    [Theory]
    [InlineData(45, "45 s")]
    [InlineData(750, "12 min 30 s")]
    [InlineData(3900, "1 h 5 min")]
    [InlineData(90000, "1 d 1 h")]
    public void Durations_read_short(long seconds, string text) =>
        Assert.Equal(text, PersonalRecords.Duration(seconds * Simulation.TicksPerSecond));
}
