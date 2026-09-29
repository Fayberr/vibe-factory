namespace FactorySim;

public sealed record OfflineOptions
{
    /// <summary>Real ticks simulated at most; the rest is extrapolated from measured income.</summary>
    public double MaxSimulatedSeconds { get; init; } = 600;

    /// <summary>Offline time beyond this is ignored.</summary>
    public double MaxOfflineSeconds { get; init; } = 7 * 24 * 3600;

    /// <summary>Multiplier on extrapolated income (a design lever, e.g. 0.5 for "half-speed offline").</summary>
    public double Efficiency { get; init; } = 1.0;
}

public sealed record OfflineReport(
    double ElapsedSeconds,
    long SimulatedTicks,
    long ExtrapolatedTicks,
    BigNum Earned,
    BigNum IncomePerSecond)
{
    /// <summary>Seconds of settled running the rates and problems were measured over (the second half of the simulated window).</summary>
    public double MeasuredSeconds { get; init; }

    /// <summary>What was made and sold, the best earners first. See <see cref="AwayItem"/>.</summary>
    public IReadOnlyList<AwayItem> Items { get; init; } = Array.Empty<AwayItem>();

    /// <summary>What kept buildings waiting, the worst first. See <see cref="AwayProblem"/>.</summary>
    public IReadOnlyList<AwayProblem> Problems { get; init; } = Array.Empty<AwayProblem>();

    /// <summary>
    /// The part of <see cref="Earned"/> no product line accounts for: order and goal rewards (paid while
    /// simulated, and at their measured rate for the rest, since the money is extrapolated as a whole).
    /// </summary>
    public BigNum Rewards { get; init; }

    /// <summary>Science packs banked while away, simulated plus extrapolated, per pack item.</summary>
    public IReadOnlyDictionary<string, long> Science { get; init; } = new Dictionary<string, long>();
}

/// <summary>
/// Catch-up for time the game was closed. Hybrid approach:
///  1. Really simulate a bounded window (belts fill, buffers settle, bottlenecks show).
///  2. Measure income over the second half of that window (steady state).
///  3. Extrapolate that rate over the remaining time.
/// Cost is bounded regardless of absence length, and short absences are exact.
/// Science packs banked by labs are extrapolated the same way as money. The report also says, per item,
/// what was made and earned, and samples every building during step 2 to rank what kept
/// them waiting (<see cref="IdleSampler"/>); sampling only reads, so the result is the same without it.
/// </summary>
public static class OfflineProgress
{
    public static OfflineReport CatchUp(this Simulation sim, double elapsedSeconds, OfflineOptions? options = null)
    {
        var o = options ?? new OfflineOptions();
        var world = sim.World;
        double elapsed = Math.Clamp(elapsedSeconds, 0, o.MaxOfflineSeconds);
        long totalTicks = (long)(elapsed * Simulation.TicksPerSecond);
        long simTicks = Math.Min(totalTicks, (long)(o.MaxSimulatedSeconds * Simulation.TicksPerSecond));

        bool eventsWere = sim.Events.Enabled;
        sim.Events.Enabled = false;
        try
        {
            var start = world.Stats.TotalEarned;
            var startItems = ItemCounters.Of(world);
            long warmup = simTicks / 2;
            sim.Step(warmup);
            var mid = world.Stats.TotalEarned;
            var midItems = ItemCounters.Of(world);

            long measured = simTicks - warmup;
            var sampler = new IdleSampler();
            for (long done = 0; done < measured;)
            {
                long n = Math.Min(IdleSampler.Stride, measured - done);
                sim.Step(n);
                done += n;
                sampler.Sample(world);
            }
            var end = world.Stats.TotalEarned;
            var endItems = ItemCounters.Of(world);

            long remaining = totalTicks - simTicks;
            BigNum perTick = measured > 0 ? (end - mid) / measured : BigNum.Zero;
            BigNum extrapolated = BigNum.Zero;

            if (remaining > 0 && !perTick.IsZero)
            {
                extrapolated = perTick * remaining * o.Efficiency;
                world.AddMoney(extrapolated);
                world.Stats.TotalEarned += extrapolated;
            }

            // Packs, like money: what the labs banked while simulated, plus the measured rate for the rest.
            var science = new Dictionary<string, long>();
            foreach (var (item, count) in endItems.Science.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            {
                long rate = count - midItems.Science.GetValueOrDefault(item);
                long extra = remaining > 0 && measured > 0 && rate > 0
                    ? (long)Math.Floor(rate * (double)remaining / measured * o.Efficiency)
                    : 0;
                world.AddScience(item, extra);
                long total = count - startItems.Science.GetValueOrDefault(item) + extra;
                if (total > 0) science[item] = total;
            }
            world.Tick += remaining; // time passes even for the extrapolated part

            var earned = end - start + extrapolated;
            var items = ItemCounters.Lines(startItems, midItems, endItems, measured, remaining, o.Efficiency);
            var rewards = earned - items.Aggregate(BigNum.Zero, (sum, i) => sum + i.Earned);
            return new OfflineReport(
                elapsed,
                simTicks,
                remaining,
                earned,
                perTick * Simulation.TicksPerSecond)
            {
                MeasuredSeconds = measured / (double)Simulation.TicksPerSecond,
                Items = items,
                // Rounding can leave a crumb either way when there were no rewards at all.
                Rewards = rewards > earned * 1e-9 ? rewards : BigNum.Zero,
                Problems = sampler.Problems(world),
                Science = science,
            };
        }
        finally
        {
            sim.Events.Enabled = eventsWere;
        }
    }
}
