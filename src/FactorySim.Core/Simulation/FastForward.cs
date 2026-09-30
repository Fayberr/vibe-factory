namespace FactorySim;

/// <summary>What <see cref="FastForward"/> runs until.</summary>
public enum RunUntil
{
    /// <summary>The next tier can be unlocked (earned, delivered and affordable).</summary>
    NextTier,

    /// <summary>Money in the bank has doubled (or reached a floor for an empty bank).</summary>
    MoneyDoubled,

    /// <summary>A fixed stretch of game time.</summary>
    TenMinutes,
}

/// <summary>
/// "Run until" (idea H1): runs the simulation as fast as the machine allows until a goal is met, then hands
/// back to normal speed. The goal checks live here so they can be tested; the client decides how many ticks
/// to run per frame, stops early on an alert, and shows the reason this returns.
///
/// It gives up after <see cref="LimitSeconds"/> of game time, so a goal the factory cannot reach (a tier that
/// needs goods nothing makes) never runs forever.
///
/// To remove: this file, <c>FastForwardTests.cs</c>, and the Run until menu and <c>FastForward</c> handling in
/// the client's <c>SimHost</c> and <c>Hud</c>. <c>Simulation.NextTierBlocker</c> can stay; the tier unlock uses it.
/// </summary>
public sealed class FastForward
{
    /// <summary>Game time after which it stops whatever the goal.</summary>
    public const int LimitSeconds = 3600;

    /// <summary>The goal of <see cref="RunUntil.MoneyDoubled"/> is never below this.</summary>
    public const double MoneyFloor = 100;

    public const int TenMinutesSeconds = 600;

    public RunUntil Goal { get; }
    public long StartTick { get; }

    /// <summary>The money to reach, for <see cref="RunUntil.MoneyDoubled"/>.</summary>
    public BigNum TargetMoney { get; }

    private FastForward(RunUntil goal, long startTick, BigNum targetMoney)
    {
        Goal = goal;
        StartTick = startTick;
        TargetMoney = targetMoney;
    }

    /// <summary>Starts a run, or returns why there is nothing to run for (the goal is already met or cannot be).</summary>
    public static FastForward? Start(Simulation sim, RunUntil goal, out string? reason)
    {
        reason = null;
        var world = sim.World;
        if (goal == RunUntil.NextTier)
        {
            if (world.UnlockedTier + 1 >= sim.Content.Tiers.Count) { reason = "Every tier is already unlocked"; return null; }
            if (sim.NextTierBlocker() == null) { reason = "The next tier can be unlocked now"; return null; }
        }
        var target = BigNum.Max(world.Money * 2, MoneyFloor);
        return new FastForward(goal, world.Tick, target);
    }

    /// <summary>Game seconds run so far.</summary>
    public double Elapsed(World world) => (world.Tick - StartTick) / (double)Simulation.TicksPerSecond;

    /// <summary>Null while the run should go on; otherwise the line to show when it stops.</summary>
    public string? Check(Simulation sim)
    {
        var world = sim.World;
        double elapsed = Elapsed(world);
        switch (Goal)
        {
            case RunUntil.NextTier when sim.NextTierBlocker() == null:
                return $"Tier {world.UnlockedTier + 1} can be unlocked now ({sim.Content.Tiers[world.UnlockedTier + 1].Name})";
            case RunUntil.MoneyDoubled when world.Money >= TargetMoney:
                return $"Money reached ${TargetMoney.Format()}";
            case RunUntil.TenMinutes when elapsed >= TenMinutesSeconds:
                return "Ten minutes passed";
        }
        if (elapsed >= LimitSeconds)
            return Goal == RunUntil.NextTier && sim.NextTierBlocker() is { } blocker
                ? $"Stopped after an hour. Next tier: {blocker}"
                : "Stopped after an hour";
        return null;
    }

    /// <summary>What the run is waiting for, for a status line.</summary>
    public string Describe() => Goal switch
    {
        RunUntil.NextTier => "until the next tier is ready",
        RunUntil.MoneyDoubled => $"until money reaches ${TargetMoney.Format()}",
        _ => "for ten minutes",
    };
}
