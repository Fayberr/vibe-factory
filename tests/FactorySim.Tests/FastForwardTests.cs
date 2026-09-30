namespace FactorySim.Tests;

/// <summary>Run until (idea H1): the goals a fast-forward stops at.</summary>
public class FastForwardTests
{
    /// <summary>A drill, a smelter and a depot, built for free, then a normal (not sandbox) game.</summary>
    private static Simulation IngotLine()
    {
        var sim = TestUtil.NewSim();
        sim.Place("iron_miner", 2, 0, 0, Dir.South);
        sim.Place("smelter", 2, 1, 0, Dir.South);
        sim.Place("seller", 2, 2, 0, Dir.South);
        sim.World.Sandbox = false;
        return sim;
    }

    /// <summary>Runs the way the client does, a second at a time, and returns why it stopped.</summary>
    private static string Run(Simulation sim, FastForward run)
    {
        for (int i = 0; i < FastForward.LimitSeconds + 10; i++)
        {
            sim.Step(Simulation.TicksPerSecond);
            if (run.Check(sim) is { } done) return done;
        }
        throw new Xunit.Sdk.XunitException("never stopped");
    }

    [Fact]
    public void It_runs_until_the_next_tier_can_be_unlocked()
    {
        var sim = IngotLine();
        var run = FastForward.Start(sim, RunUntil.NextTier, out var reason)!;
        Assert.Null(reason);
        Assert.NotNull(sim.NextTierBlocker());

        string done = Run(sim, run);
        Assert.Contains("Workshop", done);
        Assert.Null(sim.NextTierBlocker());
        Assert.True(sim.Execute(new UnlockTier()).Ok);
        Assert.InRange(run.Elapsed(sim.World), 60, FastForward.LimitSeconds - 1);
    }

    [Fact]
    public void Nothing_to_run_for_when_the_tier_is_ready_or_there_is_none()
    {
        var sim = IngotLine();
        sim.World.Sandbox = true;
        Assert.Null(FastForward.Start(sim, RunUntil.NextTier, out var ready));
        Assert.Contains("now", ready);

        sim.World.UnlockedTier = sim.Content.Tiers.Count - 1;
        Assert.Null(FastForward.Start(sim, RunUntil.NextTier, out var none));
        Assert.Contains("already", none);
    }

    [Fact]
    public void It_runs_until_money_doubles_with_a_floor_for_an_empty_bank()
    {
        var sim = IngotLine();
        var run = FastForward.Start(sim, RunUntil.MoneyDoubled, out _)!;
        Assert.Equal(FastForward.MoneyFloor, run.TargetMoney.ToDouble());
        Run(sim, run);
        Assert.True(sim.World.Money >= run.TargetMoney);

        var again = FastForward.Start(sim, RunUntil.MoneyDoubled, out _)!;
        Assert.Equal(sim.World.Money.ToDouble() * 2, again.TargetMoney.ToDouble(), 6);
    }

    [Fact]
    public void Ten_minutes_is_ten_minutes()
    {
        var sim = IngotLine();
        var run = FastForward.Start(sim, RunUntil.TenMinutes, out _)!;
        Assert.Equal("Ten minutes passed", Run(sim, run));
        Assert.Equal(FastForward.TenMinutesSeconds, run.Elapsed(sim.World));
    }

    [Fact]
    public void A_goal_the_factory_cannot_reach_gives_up_after_an_hour_and_says_why()
    {
        var sim = TestUtil.NewSim(sandbox: false); // nothing built, nothing earned
        var run = FastForward.Start(sim, RunUntil.NextTier, out _)!;
        string done = Run(sim, run);
        Assert.StartsWith("Stopped after an hour. Next tier: Earn", done);
        Assert.Equal(FastForward.LimitSeconds, run.Elapsed(sim.World));
    }

    [Fact]
    public void The_unlock_command_fails_with_the_same_reason_the_blocker_gives()
    {
        var sim = IngotLine();
        string? blocker = sim.NextTierBlocker();
        var result = sim.Execute(new UnlockTier());
        Assert.False(result.Ok);
        Assert.Equal(blocker, result.Error);
    }
}
