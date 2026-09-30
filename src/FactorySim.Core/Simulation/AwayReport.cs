using FactorySim.View;

namespace FactorySim;

/// <summary>
/// One product in the away report. The rates are measured over the settled part of the catch-up (see
/// <see cref="OfflineReport.MeasuredSeconds"/>); <see cref="Earned"/> covers the whole time away, with the
/// extrapolated part at the measured rate, exactly as the money was credited.
/// </summary>
public sealed record AwayItem(string Item, double MadePerMinute, double SoldPerMinute, BigNum Earned);

/// <summary>
/// Buildings of one kind that spent much of the time away waiting for one reason ("3 Smelters: waiting
/// for Coal"). <see cref="IdleShare"/> is the part of the time they waited, averaged over them.
/// <see cref="ExampleId"/> is the one that waited longest, for a "show me" button.
/// </summary>
public sealed record AwayProblem(
    string Building,
    IdleReason Reason,
    string Detail,
    int Count,
    double IdleShare,
    int ExampleId,
    GridPos ExamplePos);

/// <summary>
/// Reads every building's status every <see cref="Stride"/> ticks and ranks what kept buildings waiting. It
/// only reads, so sampling never changes the simulation. Only statuses that name a reason count
/// (<see cref="IdleReason"/>): an empty belt or a depot with nothing to sell is not a problem.
/// </summary>
public sealed class IdleSampler
{
    /// <summary>
    /// Ticks between samples. A prime, so it never keeps step with a machine's rhythm: every 20 ticks would
    /// catch a smelter fed once a second at the same point of its cycle each time, always idle or never.
    /// </summary>
    public const int Stride = 13;

    /// <summary>A building counts only if it waited at least this part of the time for one reason, so a
    /// machine that is merely faster than its supply, or pauses now and then, does not fill the list.</summary>
    public const double MinShare = 0.5;

    private readonly Dictionary<(int Id, IdleReason Reason, string Detail), int> _counts = new();

    public int Samples { get; private set; }

    public void Sample(World world)
    {
        Samples++;
        foreach (var e in world.Entities)
        {
            var status = e.Behavior.GetStatus(e);
            if (status.Working || status.Idle == IdleReason.None) continue;
            var key = (e.Id, status.Idle, status.Detail ?? "");
            _counts[key] = _counts.GetValueOrDefault(key) + 1;
        }
    }

    /// <summary>The worst groups first: the most building-time lost, then by building and reason for a stable order.</summary>
    public List<AwayProblem> Problems(World world, int max = 6) => ProblemRanking.Rank(world, _counts, Samples, MinShare, max);
}

/// <summary>
/// Turns per building wait counts into ranked groups ("3 Smelters: waiting for Coal"). Shared by the away
/// report (<see cref="IdleSampler"/>) and the live bottleneck list (<see cref="BottleneckTracker"/>).
/// </summary>
public static class ProblemRanking
{
    public static List<AwayProblem> Rank(
        World world,
        IReadOnlyDictionary<(int Id, IdleReason Reason, string Detail), int> counts,
        int samples,
        double minShare,
        int max)
    {
        if (samples <= 0) return new List<AwayProblem>();

        var groups = new Dictionary<(string Def, IdleReason Reason, string Detail), List<(Entity E, double Share)>>();
        foreach (var ((id, reason, detail), n) in counts)
        {
            double share = (double)n / samples;
            if (share < minShare || world.GetEntity(id) is not { } e) continue;
            var key = (e.Def.Id, reason, detail);
            if (!groups.TryGetValue(key, out var list)) groups[key] = list = new();
            list.Add((e, share));
        }

        var result = new List<(AwayProblem Problem, double Lost)>();
        foreach (var ((def, reason, detail), list) in groups)
        {
            var worst = list.OrderByDescending(x => x.Share).ThenBy(x => x.E.Id).First();
            double lost = list.Sum(x => x.Share);
            result.Add((new AwayProblem(def, reason, detail, list.Count, lost / list.Count, worst.E.Id, worst.E.Pos), lost));
        }

        return result
            .OrderByDescending(r => r.Lost)
            .ThenBy(r => r.Problem.Building, StringComparer.Ordinal)
            .ThenBy(r => r.Problem.Detail, StringComparer.Ordinal)
            .Take(max)
            .Select(r => r.Problem)
            .ToList();
    }
}

/// <summary>The per item counters the report compares between two moments of the catch-up.</summary>
internal sealed record ItemCounters(
    Dictionary<string, long> Produced,
    Dictionary<string, long> Sold,
    Dictionary<string, BigNum> Earned,
    Dictionary<string, long> Science)
{
    public static ItemCounters Of(World world) => new(
        new Dictionary<string, long>(world.Stats.Produced),
        new Dictionary<string, long>(world.Stats.Sold),
        new Dictionary<string, BigNum>(world.Stats.EarnedByItem),
        new Dictionary<string, long>(world.ScienceBank));

    /// <summary>
    /// One line per item made or sold during the catch-up, the best earners first. Rates come from
    /// <paramref name="mid"/> to <paramref name="end"/>; earnings from <paramref name="start"/> to
    /// <paramref name="end"/> plus the measured rate over the <paramref name="remaining"/> ticks.
    /// </summary>
    public static List<AwayItem> Lines(ItemCounters start, ItemCounters mid, ItemCounters end, long measured, long remaining, double efficiency)
    {
        double minutes = measured / (double)Simulation.TicksPerSecond / 60.0;
        var items = end.Produced.Keys.Concat(end.Sold.Keys).Concat(end.Earned.Keys).Distinct();
        var lines = new List<AwayItem>();
        foreach (var item in items)
        {
            long made = end.Produced.GetValueOrDefault(item) - mid.Produced.GetValueOrDefault(item);
            long sold = end.Sold.GetValueOrDefault(item) - mid.Sold.GetValueOrDefault(item);
            BigNum earned = end.Earned.GetValueOrDefault(item) - start.Earned.GetValueOrDefault(item);
            if (remaining > 0 && measured > 0)
            {
                BigNum perTick = (end.Earned.GetValueOrDefault(item) - mid.Earned.GetValueOrDefault(item)) / measured;
                if (!perTick.IsZero) earned += perTick * remaining * efficiency;
            }
            if (made <= 0 && sold <= 0 && !(earned > BigNum.Zero)) continue;
            lines.Add(new AwayItem(item,
                minutes > 0 ? made / minutes : 0,
                minutes > 0 ? sold / minutes : 0,
                earned));
        }
        return lines
            .OrderByDescending(l => l.Earned)
            .ThenByDescending(l => l.MadePerMinute)
            .ThenBy(l => l.Item, StringComparer.Ordinal)
            .ToList();
    }
}
