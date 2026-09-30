namespace FactorySim;

/// <summary>
/// Personal records for the statistics page (idea G6): the best a factory has done so far and when,
/// plus the tick each tier was reached. Saved with the stats, so an old save starts with empty records
/// and fills them from its next second on.
///
/// Updated once a simulated second (and on a tier unlock), so it costs nothing per tick. Income only
/// counts once a full minute is measured, so one early sale does not set a record that lasts forever.
///
/// To remove: this file, the <c>Records</c> property on <see cref="StatsTracker"/>, its two calls in
/// <c>Simulation</c>, <c>RecordsTests.cs</c>, and the records part of the client's Statistics window.
/// </summary>
public sealed class PersonalRecords
{
    public BigNum BestIncome { get; set; }
    public long BestIncomeTick { get; set; }
    public BigNum MostMoney { get; set; }
    public long MostMoneyTick { get; set; }
    public int MostBuildings { get; set; }
    public long MostBuildingsTick { get; set; }

    /// <summary>Tier number to the tick it was unlocked (tier 0 is where every factory starts).</summary>
    public Dictionary<int, long> TierTicks { get; set; } = new();

    /// <summary>Called by the simulation once a second.</summary>
    internal void Observe(World world, long tick)
    {
        var stats = world.Stats;
        if (stats.MeasuredSeconds >= StatsTracker.WindowSeconds - 1)
        {
            var income = stats.IncomePerSecond();
            if (income > BestIncome) (BestIncome, BestIncomeTick) = (income, tick);
        }
        if (world.Money > MostMoney) (MostMoney, MostMoneyTick) = (world.Money, tick);
        if (world.EntityCount > MostBuildings) (MostBuildings, MostBuildingsTick) = (world.EntityCount, tick);
    }

    /// <summary>Called by the simulation when a tier unlocks. The first unlock of a tier counts.</summary>
    internal void TierReached(int tier, long tick) => TierTicks.TryAdd(tier, tick);

    /// <summary>"1 h 5 min", "12 min 30 s", "45 s": a span of simulated time, short.</summary>
    public static string Duration(long ticks)
    {
        long s = Math.Max(0, ticks / Simulation.TicksPerSecond);
        if (s >= 86400) return $"{s / 86400} d {s % 86400 / 3600} h";
        if (s >= 3600) return $"{s / 3600} h {s % 3600 / 60} min";
        if (s >= 60) return $"{s / 60} min {s % 60} s";
        return $"{s} s";
    }
}
