namespace FactorySim;

/// <summary>
/// Production/economy statistics: lifetime totals plus a rolling per-second income
/// window. This is the hook for throughput leaderboards and shared statistics —
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

    public void RecordSale(string item, long count, BigNum value)
    {
        TotalEarned += value;
        EarnedBuckets[BucketIndex] += value;
        Sold[item] = Sold.GetValueOrDefault(item) + count;
    }

    public void RecordProduced(string item, long count) =>
        Produced[item] = Produced.GetValueOrDefault(item) + count;

    /// <summary>Called by the simulation after each tick.</summary>
    internal void EndTick(long tick)
    {
        if ((tick + 1) % Simulation.TicksPerSecond != 0) return;
        BucketIndex = (BucketIndex + 1) % WindowSeconds;
        EarnedBuckets[BucketIndex] = BigNum.Zero;
        FilledBuckets = Math.Min(FilledBuckets + 1, WindowSeconds);
    }

    /// <summary>Average income over the completed seconds of the rolling window.</summary>
    public BigNum IncomePerSecond(int seconds = WindowSeconds)
    {
        int n = Math.Min(Math.Min(seconds, FilledBuckets), WindowSeconds - 1);
        if (n <= 0) return BigNum.Zero;
        BigNum sum = BigNum.Zero;
        for (int k = 1; k <= n; k++) sum += EarnedBuckets[(BucketIndex - k + WindowSeconds) % WindowSeconds];
        return sum / n;
    }

    public StatsSnapshot Snapshot(long tick) => new(
        tick,
        TotalEarned,
        IncomePerSecond(),
        new Dictionary<string, long>(Sold),
        new Dictionary<string, long>(Produced));
}

/// <summary>Immutable stats summary, e.g. for a leaderboard submission.</summary>
public sealed record StatsSnapshot(
    long Tick,
    BigNum TotalEarned,
    BigNum IncomePerSecond,
    IReadOnlyDictionary<string, long> Sold,
    IReadOnlyDictionary<string, long> Produced);
