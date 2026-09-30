using FactorySim.View;

namespace FactorySim;

/// <summary>
/// The live bottleneck list (idea F3) and the diagnostics overlay behind it (idea F5): which buildings kept
/// waiting over the last <see cref="WindowSeconds"/> seconds, and why. It is the away report's sampler on a
/// rolling window, so the two always agree on what counts as a problem.
///
/// It only reads the world, so it is never saved and never changes the simulation. A client calls
/// <see cref="Observe"/> after advancing the simulation; it samples at most once per
/// <see cref="IdleSampler.Stride"/> ticks, whatever the frame rate or game speed. Throw it away (or call
/// <see cref="Reset"/>) when the world is replaced.
///
/// The numbers to tune are <see cref="WindowSeconds"/> and <see cref="MinShare"/>.
/// </summary>
public sealed class BottleneckTracker
{
    /// <summary>How far back the list looks. Long enough to ride out a machine's rhythm, short enough to show a fix quickly.</summary>
    public const int WindowSeconds = 30;

    /// <summary>A building is listed once it waited at least this part of the window, the same bar as the away report.</summary>
    public const double MinShare = IdleSampler.MinShare;

    /// <summary>Samples kept: the window in ticks over the sampling stride.</summary>
    public static readonly int WindowSamples = WindowSeconds * Simulation.TicksPerSecond / IdleSampler.Stride;

    private readonly Queue<List<(int Id, IdleReason Reason, string Detail)>> _samples = new();
    private readonly Dictionary<(int Id, IdleReason Reason, string Detail), int> _counts = new();
    private long _lastTick = long.MinValue;

    /// <summary>Samples in the window right now (grows to <see cref="WindowSamples"/>).</summary>
    public int Samples => _samples.Count;

    /// <summary>Takes a sample if at least a stride of ticks passed since the last one.</summary>
    public void Observe(World world)
    {
        if (_lastTick != long.MinValue && world.Tick >= _lastTick && world.Tick - _lastTick < IdleSampler.Stride) return;
        _lastTick = world.Tick;

        var sample = new List<(int, IdleReason, string)>();
        foreach (var e in world.Entities)
        {
            var status = e.Behavior.GetStatus(e);
            if (status.Working || status.Idle == IdleReason.None) continue;
            var key = (e.Id, status.Idle, status.Detail ?? "");
            sample.Add(key);
            _counts[key] = _counts.GetValueOrDefault(key) + 1;
        }
        _samples.Enqueue(sample);

        while (_samples.Count > WindowSamples)
            foreach (var key in _samples.Dequeue())
                if (--_counts[key] <= 0) _counts.Remove(key);
    }

    public void Reset()
    {
        _samples.Clear();
        _counts.Clear();
        _lastTick = long.MinValue;
    }

    /// <summary>The worst groups first, by building-time lost over the window.</summary>
    public List<AwayProblem> Problems(World world, int max = 8) => ProblemRanking.Rank(world, _counts, Samples, MinShare, max);

    /// <summary>
    /// Every building that waited at least <see cref="MinShare"/> of the window, with its main reason (the one
    /// it waited for most). This is what the overlay marks.
    /// </summary>
    public Dictionary<int, IdleReason> Waiting(World world)
    {
        var best = new Dictionary<int, (IdleReason Reason, int Count)>();
        var total = new Dictionary<int, int>();
        foreach (var ((id, reason, _), n) in _counts)
        {
            total[id] = total.GetValueOrDefault(id) + n;
            if (!best.TryGetValue(id, out var b) || n > b.Count || (n == b.Count && reason > b.Reason)) best[id] = (reason, n);
        }

        var result = new Dictionary<int, IdleReason>();
        if (Samples == 0) return result;
        foreach (var (id, n) in total)
            if ((double)n / Samples >= MinShare && world.GetEntity(id) != null) result[id] = best[id].Reason;
        return result;
    }
}
