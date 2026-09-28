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
    BigNum IncomePerSecond);

/// <summary>
/// Catch-up for time the game was closed. Hybrid approach:
///  1. Really simulate a bounded window (belts fill, buffers settle, bottlenecks show).
///  2. Measure income over the second half of that window (steady state).
///  3. Extrapolate that rate over the remaining time.
/// Cost is bounded regardless of absence length, and short absences are exact.
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
            long warmup = simTicks / 2;
            sim.Step(warmup);
            var mid = world.Stats.TotalEarned;
            sim.Step(simTicks - warmup);
            var end = world.Stats.TotalEarned;

            long measured = simTicks - warmup;
            long remaining = totalTicks - simTicks;
            BigNum perTick = measured > 0 ? (end - mid) / measured : BigNum.Zero;
            BigNum extrapolated = BigNum.Zero;

            if (remaining > 0 && !perTick.IsZero)
            {
                extrapolated = perTick * remaining * o.Efficiency;
                world.AddMoney(extrapolated);
                world.Stats.TotalEarned += extrapolated;
            }
            world.Tick += remaining; // time passes even for the extrapolated part

            return new OfflineReport(
                elapsed,
                simTicks,
                remaining,
                end - start + extrapolated,
                perTick * Simulation.TicksPerSecond);
        }
        finally
        {
            sim.Events.Enabled = eventsWere;
        }
    }
}
