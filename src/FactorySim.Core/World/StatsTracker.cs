namespace FactorySim;

/// <summary>
/// Production/economy statistics: lifetime totals plus a rolling per-second income
/// window. This is the hook for throughput leaderboards and shared statistics:
/// <see cref="Snapshot"/> is a deterministic, serializable summary.
/// </summary>
public sealed class StatsTracker
{
    public const int WindowSeconds = 60;

    public BigNum TotalEarned { get; set; }
    public Dictionary<string, long> Sold { get; set; } = new();
    public Dictionary<string, long> Produced { get; set; } = new();

    /// <summary>Money earned per second, ring buffer; index <see cref="BucketIndex"/> is the second in progress.</summary>
    public BigNum[] EarnedBuckets { get; set; } = new BigNum[WindowSeconds];
    public int BucketIndex { get; set; }

    /// <summary>Number of completed seconds in the window (≤ WindowSeconds).</summary>
    public int FilledBuckets { get; set; }

    /// <summary>Lifetime money from sales per item, exactly as paid. Missing in old saves, which then start at zero.</summary>
    public Dictionary<string, BigNum> EarnedByItem { get; set; } = new();

    /// <summary>
    /// Money earned per second per item, ring buffers aligned with <see cref="EarnedBuckets"/> (same
    /// <see cref="BucketIndex"/> and <see cref="FilledBuckets"/>). Missing in old saves.
    /// </summary>
    public Dictionary<string, BigNum[]> EarnedBucketsByItem { get; set; } = new();

    public void RecordSale(string item, long count, BigNum value)
    {
        TotalEarned += value;
        EarnedBuckets[BucketIndex] += value;
        Sold[item] = Sold.GetValueOrDefault(item) + count;
        EarnedByItem[item] = EarnedByItem.GetValueOrDefault(item) + value;
        BucketsOf(item)[BucketIndex] += value;
    }

    private BigNum[] BucketsOf(string item)
    {
        if (EarnedBucketsByItem.TryGetValue(item, out var buckets) && buckets?.Length == WindowSeconds) return buckets;
        return EarnedBucketsByItem[item] = new BigNum[WindowSeconds];
    }

    public void RecordProduced(string item, long count) =>
        Produced[item] = Produced.GetValueOrDefault(item) + count;

    /// <summary>Called by the simulation after each tick.</summary>
    internal void EndTick(long tick)
    {
        if ((tick + 1) % Simulation.TicksPerSecond != 0) return;
        BucketIndex = (BucketIndex + 1) % WindowSeconds;
        EarnedBuckets[BucketIndex] = BigNum.Zero;
        foreach (var buckets in EarnedBucketsByItem.Values)
            if (buckets?.Length == WindowSeconds) buckets[BucketIndex] = BigNum.Zero;
        FilledBuckets = Math.Min(FilledBuckets + 1, WindowSeconds);
    }

    /// <summary>Average income over the completed seconds of the rolling window.</summary>
    public BigNum IncomePerSecond(int seconds = WindowSeconds) => Average(EarnedBuckets, seconds);

    /// <summary>Average income of one item over the same window as <see cref="IncomePerSecond"/>; zero if it sold nothing.</summary>
    public BigNum IncomePerSecondOf(string item, int seconds = WindowSeconds) =>
        EarnedBucketsByItem.TryGetValue(item, out var buckets) ? Average(buckets, seconds) : BigNum.Zero;

    /// <summary>Average income per item over the window, for every item that earned something in it.</summary>
    public Dictionary<string, BigNum> IncomePerSecondByItem(int seconds = WindowSeconds)
    {
        var rates = new Dictionary<string, BigNum>();
        foreach (var (item, buckets) in EarnedBucketsByItem)
        {
            var rate = Average(buckets, seconds);
            if (rate > BigNum.Zero) rates[item] = rate;
        }
        return rates;
    }

    /// <summary>An item's part of the income over the window (0..1): its rate over the total rate, zero when nothing was earned.</summary>
    public BigNum IncomeShareOf(string item, int seconds = WindowSeconds)
    {
        var total = IncomePerSecond(seconds);
        return total > BigNum.Zero ? IncomePerSecondOf(item, seconds) / total : BigNum.Zero;
    }

    private BigNum Average(BigNum[]? buckets, int seconds)
    {
        int n = Math.Min(Math.Min(seconds, FilledBuckets), WindowSeconds - 1);
        if (n <= 0 || buckets?.Length != WindowSeconds) return BigNum.Zero;
        BigNum sum = BigNum.Zero;
        for (int k = 1; k <= n; k++) sum += buckets[(BucketIndex - k + WindowSeconds) % WindowSeconds];
        return sum / n;
    }

    public StatsSnapshot Snapshot(long tick) => new(
        tick,
        TotalEarned,
        IncomePerSecond(),
        new Dictionary<string, long>(Sold),
        new Dictionary<string, long>(Produced),
        new Dictionary<string, BigNum>(EarnedByItem));
}

/// <summary>Immutable stats summary, e.g. for a leaderboard submission.</summary>
public sealed record StatsSnapshot(
    long Tick,
    BigNum TotalEarned,
    BigNum IncomePerSecond,
    IReadOnlyDictionary<string, long> Sold,
    IReadOnlyDictionary<string, long> Produced,
    IReadOnlyDictionary<string, BigNum> EarnedByItem);
