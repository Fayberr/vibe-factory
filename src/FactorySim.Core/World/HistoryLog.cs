namespace FactorySim;

/// <summary>
/// History graphs (idea F2): money, income and how fast each item was made, over time. Two series at
/// different resolutions: <see cref="Recent"/> for the last hour and <see cref="Long"/> for the last day.
/// It is saved with the stats, so the graphs survive a restart, and an old save simply starts empty.
///
/// Points keep the tick they were taken at and running totals, so a rate between two points is right
/// even across a gap (the offline catch-up skips ticks it only extrapolates): income is the earned total's
/// change over the time between them. Item counts are what was made since the point before.
///
/// The numbers to tune are the intervals and capacities below; a saved series with other values is
/// simply trimmed or spaced out, because every point carries its own tick.
///
/// To remove: this file, the <c>History</c> property on <see cref="StatsTracker"/>, its call in
/// <c>Simulation.Step</c>, <c>HistoryTests.cs</c> and the History window in the client.
/// </summary>
public sealed class HistoryLog
{
    public const int RecentIntervalSeconds = 30, RecentCapacity = 120; // an hour
    public const int LongIntervalSeconds = 15 * 60, LongCapacity = 96; // a day

    public HistorySeries Recent { get; set; } = new();
    public HistorySeries Long { get; set; } = new();

    /// <summary>Called by the simulation after each tick, with the tick that just ended.</summary>
    internal void EndTick(World world, long tick)
    {
        long ended = tick + 1;
        if (ended % (RecentIntervalSeconds * Simulation.TicksPerSecond) == 0) Recent.Add(world, ended, RecentCapacity);
        if (ended % (LongIntervalSeconds * Simulation.TicksPerSecond) == 0) Long.Add(world, ended, LongCapacity);
    }
}

/// <summary>One resolution of <see cref="HistoryLog"/>: parallel lists, oldest first.</summary>
public sealed class HistorySeries
{
    public List<long> Ticks { get; set; } = new();
    public List<BigNum> Money { get; set; } = new();
    public List<BigNum> Earned { get; set; } = new();

    /// <summary>Lifetime rewards at each point, taken out of the income so a paid order is not a spike.</summary>
    public List<BigNum> Rewards { get; set; } = new();

    /// <summary>Units made since the point before, per item, aligned with <see cref="Ticks"/>.</summary>
    public Dictionary<string, List<long>> Made { get; set; } = new();

    /// <summary>Lifetime production at the last point, to take the next difference from.</summary>
    public Dictionary<string, long> Seen { get; set; } = new();

    public int Count => Ticks.Count;

    internal void Add(World world, long tick, int capacity)
    {
        var stats = world.Stats;
        Ticks.Add(tick);
        Money.Add(world.Money);
        Earned.Add(stats.TotalEarned);
        Rewards.Add(stats.RewardsEarned);
        foreach (var (item, total) in stats.Produced.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            long made = total - Seen.GetValueOrDefault(item);
            if (made == 0 && !Made.ContainsKey(item)) continue;
            if (!Made.TryGetValue(item, out var list)) Made[item] = list = new List<long>();
            while (list.Count < Ticks.Count - 1) list.Add(0);
            list.Add(made);
            Seen[item] = total;
        }
        foreach (var list in Made.Values)
            while (list.Count < Ticks.Count) list.Add(0);

        while (Ticks.Count > capacity)
        {
            Ticks.RemoveAt(0);
            Money.RemoveAt(0);
            Earned.RemoveAt(0);
            if (Rewards.Count > 0) Rewards.RemoveAt(0);
            foreach (var list in Made.Values) if (list.Count > 0) list.RemoveAt(0);
        }
        foreach (var item in Made.Where(kv => kv.Value.All(n => n == 0)).Select(kv => kv.Key).ToList())
            Made.Remove(item);
    }

    /// <summary>Seconds between point <paramref name="i"/> and the one before it (0 for the first).</summary>
    public double SecondsBefore(int i) => i <= 0 || i >= Count ? 0 : (Ticks[i] - Ticks[i - 1]) / (double)Simulation.TicksPerSecond;

    /// <summary>
    /// Average income per second from sales between point <paramref name="i"/> and the one before (none for the
    /// first), the same thing the factory card shows: order and goal rewards are left out.
    /// </summary>
    public double? IncomeAt(int i)
    {
        double dt = SecondsBefore(i);
        if (dt <= 0) return null;
        double rewards = Rewards.Count == Count ? (Rewards[i] - Rewards[i - 1]).ToDouble() : 0;
        return Math.Max(0, (Earned[i] - Earned[i - 1]).ToDouble() - rewards) / dt;
    }

    /// <summary>Units of <paramref name="item"/> made per second between point <paramref name="i"/> and the one before.</summary>
    public double? RateAt(string item, int i)
    {
        double dt = SecondsBefore(i);
        if (dt <= 0) return null;
        return Made.TryGetValue(item, out var list) && i < list.Count ? list[i] / dt : 0;
    }

    /// <summary>Items made at any point in the series, most made first.</summary>
    public List<string> Items() =>
        Made.OrderByDescending(kv => kv.Value.Skip(1).Sum()).ThenBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Key).ToList();
}
