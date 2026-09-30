namespace FactorySim;

/// <summary>How a production target is doing.</summary>
public enum TargetStatus
{
    /// <summary>Too little of the window has passed to say (just loaded or just started).</summary>
    Measuring,

    /// <summary>The factory makes at least the target.</summary>
    Met,

    /// <summary>The factory makes less than the target.</summary>
    Under,
}

/// <summary>A target and how it is doing now.</summary>
public sealed record TargetReading(string Item, double Target, double PerMinute, TargetStatus Status)
{
    /// <summary>How far along the target is, 0 to 1.</summary>
    public double Share => Target <= 0 ? 1 : Math.Clamp(PerMinute / Target, 0, 1);
}

/// <summary>
/// Production targets (idea F7): "make 60 iron plates a minute", with a reading of how the factory is doing
/// against it. A target is a player setting kept in <see cref="World.Targets"/> and saved; the rate is items
/// made over the last minute (<see cref="StatsTracker.ProducedPerSecondOf"/>). The Alerts window raises an
/// entry for a target that stays under for a minute.
///
/// To remove: this file and <c>TargetTests.cs</c>, the <c>SetTarget</c> command (its line in
/// <c>Simulation.Execute</c> and in <c>EditHistory.Inverse</c>), <c>World.Targets</c> and <c>SaveData.Targets</c>,
/// the target part of <c>Alerts.cs</c>, and the client's Targets window. The per-item production window in
/// <c>StatsTracker</c> can stay.
/// </summary>
public static class ProductionTargets
{
    /// <summary>At most this many targets at once, so the window stays one short list.</summary>
    public const int MaxTargets = 12;

    /// <summary>Seconds measured before a reading says Met or Under.</summary>
    public const int MeasureSeconds = 10;

    /// <summary>The highest target, items per minute.</summary>
    public const double MaxPerMinute = 1_000_000;

    public static CommandResult Set(World world, SetTarget c)
    {
        if (!world.Content.Items.ContainsKey(c.Item)) return CommandResult.Fail($"Unknown item '{c.Item}'");
        if (c.PerMinute is not { } rate)
        {
            world.Targets.Remove(c.Item);
            return CommandResult.Success(0);
        }
        if (!(rate > 0) || rate > MaxPerMinute) return CommandResult.Fail("A target is a number of items a minute above zero");
        if (!world.Targets.ContainsKey(c.Item) && world.Targets.Count >= MaxTargets) return CommandResult.Fail($"At most {MaxTargets} targets");
        world.Targets[c.Item] = rate;
        return CommandResult.Success(0);
    }

    public static TargetReading Read(World world, string item)
    {
        double target = world.Targets.GetValueOrDefault(item);
        double perMinute = world.Stats.ProducedPerSecondOf(item) * 60;
        var status = world.Stats.MeasuredSeconds < MeasureSeconds ? TargetStatus.Measuring
            : perMinute >= target - 1e-9 ? TargetStatus.Met
            : TargetStatus.Under;
        return new TargetReading(item, target, perMinute, status);
    }

    /// <summary>Every target, in the order of the item list.</summary>
    public static List<TargetReading> ReadAll(World world) =>
        world.Content.Items.Values.Where(i => world.Targets.ContainsKey(i.Id)).Select(i => Read(world, i.Id)).ToList();

    /// <summary>
    /// A first target for an item: the next step above what the factory makes now (a little more than
    /// today, so it is something to work towards), or 10 a minute when it makes none.
    /// </summary>
    public static double Suggest(World world, string item)
    {
        double now = world.Stats.ProducedPerSecondOf(item) * 60;
        return now > 0 ? Step(now, 1) : 10;
    }

    /// <summary>A rate for display: one decimal below 10 a minute, whole numbers above, thousands grouped.</summary>
    public static string Format(double perMinute) =>
        perMinute.ToString(perMinute < 10 ? "0.#" : "#,0", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The steps the client's − and + move a target through, items per minute.</summary>
    public static readonly double[] Steps =
    {
        1, 2, 3, 5, 10, 15, 20, 30, 45, 60, 90, 120, 180, 240, 300, 450, 600, 900, 1200, 1800, 2400, 3600, 5400, 7200, 10800,
    };

    /// <summary>The step after (+1) or before (-1) <paramref name="current"/>.</summary>
    public static double Step(double current, int direction) => direction > 0
        ? Steps.FirstOrDefault(s => s > current + 1e-9, Math.Max(current, Steps[^1]))
        : Steps.LastOrDefault(s => s < current - 1e-9, Math.Min(current, Steps[0]));
}
