using FactorySim.Persistence;
using FactorySim.View;

namespace FactorySim.Tests;

/// <summary>
/// The live bottleneck list and overlay (ideas F3 and F5): the away report's sampler on a rolling window.
/// </summary>
public class BottleneckTests
{
    private static readonly DateTimeOffset At = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);

    /// <summary>A furnace fed ingots and never coal, behind a smelter and a drill that back up into it.</summary>
    private static Simulation StarvedFurnace()
    {
        var sim = TestUtil.NewSim();
        sim.Place("iron_miner", 2, 2, 0, Dir.East);
        sim.Place("conveyor", 3, 2, 0, Dir.East);
        sim.Place("smelter", 4, 2, 0, Dir.East);
        sim.Place("conveyor", 5, 2, 0, Dir.East);
        sim.Place("blast_furnace", 6, 2, 0, Dir.East);
        return sim;
    }

    /// <summary>Steps one tick at a time and observes after each, like a client at a high frame rate.</summary>
    private static void Run(Simulation sim, BottleneckTracker tracker, int ticks)
    {
        for (int i = 0; i < ticks; i++)
        {
            sim.Step(1);
            tracker.Observe(sim.World);
        }
    }

    [Fact]
    public void It_ranks_what_waited_and_marks_each_building_with_its_reason()
    {
        var sim = StarvedFurnace();
        var tracker = new BottleneckTracker();
        Run(sim, tracker, 20 * 60);

        var problems = tracker.Problems(sim.World);
        var furnace = Assert.Single(problems, p => p.Building == "blast_furnace");
        Assert.Equal((IdleReason.Starved, "waiting for Coal"), (furnace.Reason, furnace.Detail));
        Assert.Equal(1.0, furnace.IdleShare, 6);
        Assert.Contains(problems, p => p.Building == "smelter" && p.Reason == IdleReason.Blocked);

        var waiting = tracker.Waiting(sim.World);
        int furnaceId = sim.World.EntityAt(new GridPos(6, 2, 0))!.Id;
        int smelterId = sim.World.EntityAt(new GridPos(4, 2, 0))!.Id;
        Assert.Equal(IdleReason.Starved, waiting[furnaceId]);
        Assert.Equal(IdleReason.Blocked, waiting[smelterId]);
    }

    [Fact]
    public void It_samples_once_a_stride_and_keeps_only_the_window()
    {
        var sim = StarvedFurnace();
        var tracker = new BottleneckTracker();
        Run(sim, tracker, IdleSampler.Stride * 3);
        Assert.Equal(3, tracker.Samples);

        Run(sim, tracker, 20 * 120);
        Assert.Equal(BottleneckTracker.WindowSamples, tracker.Samples);
    }

    [Fact]
    public void A_fixed_problem_leaves_the_list_once_the_window_moves_on()
    {
        var sim = StarvedFurnace();
        var tracker = new BottleneckTracker();
        Run(sim, tracker, 20 * 40);
        Assert.Contains(tracker.Problems(sim.World), p => p.Building == "blast_furnace");

        // Removing the furnace ends it at once; the list only names buildings that still exist.
        sim.Execute(new RemoveBuilding(new GridPos(6, 2, 0)));
        Assert.DoesNotContain(tracker.Problems(sim.World), p => p.Building == "blast_furnace");

        // A sink at the end: the smelter stops being blocked, and after one window it is off the list.
        sim.Place("seller", 6, 2, 0, Dir.East);
        Run(sim, tracker, 20 * (BottleneckTracker.WindowSeconds + 5));
        Assert.DoesNotContain(tracker.Problems(sim.World), p => p.Building == "smelter");
        Assert.Empty(tracker.Waiting(sim.World));
    }

    [Fact]
    public void Watching_never_changes_the_factory()
    {
        var plain = StarvedFurnace();
        var watched = StarvedFurnace();
        plain.Step(20 * 60);
        Run(watched, new BottleneckTracker(), 20 * 60);
        Assert.Equal(SaveSystem.Serialize(plain, At), SaveSystem.Serialize(watched, At));
    }

    [Fact]
    public void A_frame_that_skips_many_ticks_takes_one_sample_and_a_replaced_world_starts_over()
    {
        var sim = StarvedFurnace();
        var tracker = new BottleneckTracker();
        sim.Step(20 * 10);
        tracker.Observe(sim.World);
        Assert.Equal(1, tracker.Samples);

        // A new world restarts its tick count below the last sample; that must not stall the tracker.
        var fresh = StarvedFurnace();
        fresh.Step(1);
        tracker.Observe(fresh.World);
        Assert.Equal(2, tracker.Samples);

        tracker.Reset();
        Assert.Equal(0, tracker.Samples);
        Assert.Empty(tracker.Problems(fresh.World));
    }
}
