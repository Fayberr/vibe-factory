using FactorySim.Editing;
using FactorySim.Persistence;

namespace FactorySim.Tests;

/// <summary>Production targets (idea F7): a rate to reach per item, a reading, and an alert when it is missed.</summary>
public class TargetTests
{
    /// <summary>A drill feeding a smelter feeding a depot, in column <paramref name="x"/>: iron ingots.</summary>
    private static void Line(Simulation sim, int x)
    {
        sim.Place("iron_miner", x, 0, 0, Dir.South);
        sim.Place("smelter", x, 1, 0, Dir.South);
        sim.Place("seller", x, 2, 0, Dir.South);
    }

    private static void Run(Simulation sim, AlertLog log, double seconds)
    {
        long end = sim.World.Tick + (long)(seconds * Simulation.TicksPerSecond);
        while (sim.World.Tick < end)
        {
            sim.Step(Math.Min(IdleSampler.Stride, end - sim.World.Tick));
            log.Observe(sim.World);
        }
    }

    [Fact]
    public void Targets_are_set_changed_and_removed()
    {
        var sim = TestUtil.NewSim();
        Assert.True(sim.Execute(new SetTarget("iron_ingot", 30)).Ok);
        Assert.Equal(30, sim.World.Targets["iron_ingot"]);
        Assert.True(sim.Execute(new SetTarget("iron_ingot", 60)).Ok);
        Assert.Equal(60, sim.World.Targets["iron_ingot"]);
        Assert.True(sim.Execute(new SetTarget("iron_ingot", null)).Ok);
        Assert.Empty(sim.World.Targets);

        Assert.False(sim.Execute(new SetTarget("no_such_item", 10)).Ok);
        Assert.False(sim.Execute(new SetTarget("iron_ingot", 0)).Ok);
        Assert.False(sim.Execute(new SetTarget("iron_ingot", -5)).Ok);
        Assert.False(sim.Execute(new SetTarget("iron_ingot", double.NaN)).Ok);
        Assert.Empty(sim.World.Targets);
    }

    [Fact]
    public void There_is_a_limit_on_how_many()
    {
        var sim = TestUtil.NewSim();
        var items = sim.World.Content.Items.Keys.Take(ProductionTargets.MaxTargets + 1).ToList();
        foreach (string item in items.Take(ProductionTargets.MaxTargets)) Assert.True(sim.Execute(new SetTarget(item, 10)).Ok);
        Assert.False(sim.Execute(new SetTarget(items[^1], 10)).Ok);
        Assert.True(sim.Execute(new SetTarget(items[0], 20)).Ok); // changing one that exists is fine
    }

    [Fact]
    public void Undo_brings_the_old_target_back()
    {
        var sim = TestUtil.NewSim();
        var history = new EditHistory(sim);
        history.Execute(new SetTarget("iron_ingot", 30));
        history.Execute(new SetTarget("iron_ingot", 90));
        history.Execute(new SetTarget("iron_ingot", null));

        history.Undo();
        Assert.Equal(90, sim.World.Targets["iron_ingot"]);
        history.Undo();
        Assert.Equal(30, sim.World.Targets["iron_ingot"]);
        history.Undo();
        Assert.False(sim.World.Targets.ContainsKey("iron_ingot"));
        history.Redo();
        Assert.Equal(30, sim.World.Targets["iron_ingot"]);
    }

    [Fact]
    public void Saves_keep_the_targets()
    {
        var sim = TestUtil.NewSim();
        sim.Execute(new SetTarget("iron_ingot", 45));
        sim.Execute(new SetTarget("copper_ingot", 12.5));
        var loaded = SaveSystem.Deserialize(SaveSystem.Serialize(sim), TestUtil.Content);
        Assert.Empty(loaded.Warnings);
        Assert.Equal(45, loaded.Simulation.World.Targets["iron_ingot"]);
        Assert.Equal(12.5, loaded.Simulation.World.Targets["copper_ingot"]);

        var empty = SaveSystem.Deserialize(SaveSystem.Serialize(TestUtil.NewSim()), TestUtil.Content);
        Assert.Empty(empty.Simulation.World.Targets);
    }

    [Fact]
    public void A_running_line_is_measured_against_its_target()
    {
        var sim = TestUtil.NewSim();
        var log = new AlertLog();
        Line(sim, 2);
        sim.Execute(new SetTarget("iron_ingot", 1));
        Run(sim, log, 2);
        Assert.Equal(TargetStatus.Measuring, ProductionTargets.Read(sim.World, "iron_ingot").Status);

        Run(sim, log, 60);
        var met = ProductionTargets.Read(sim.World, "iron_ingot");
        Assert.True(met.PerMinute > 1, $"made {met.PerMinute} a minute");
        Assert.Equal(TargetStatus.Met, met.Status);
        Assert.Equal(1, met.Share);

        sim.Execute(new SetTarget("iron_ingot", met.PerMinute * 10));
        var under = ProductionTargets.Read(sim.World, "iron_ingot");
        Assert.Equal(TargetStatus.Under, under.Status);
        Assert.InRange(under.Share, 0.05, 0.15);
        Assert.Single(ProductionTargets.ReadAll(sim.World));
    }

    [Fact]
    public void A_target_under_for_a_minute_raises_one_alert_that_resolves_when_met()
    {
        var sim = TestUtil.NewSim();
        var log = new AlertLog();
        Line(sim, 2);
        Run(sim, log, 30);
        double rate = ProductionTargets.Read(sim.World, "iron_ingot").PerMinute;
        sim.Execute(new SetTarget("iron_ingot", rate * 2));

        Run(sim, log, AlertLog.TargetSeconds - 10);
        Assert.DoesNotContain(log.Entries, a => a.Kind == AlertKind.TargetMissed);
        Run(sim, log, 20);
        var alert = Assert.Single(log.Entries, a => a.Kind == AlertKind.TargetMissed);
        Assert.Equal("iron_ingot", alert.Item);
        Assert.Contains("a minute", alert.Detail);
        Run(sim, log, 120);
        Assert.Single(log.Entries, a => a.Kind == AlertKind.TargetMissed); // still under, still one entry

        Line(sim, 4); // a second line doubles the rate
        Run(sim, log, 90);
        Assert.True(alert.Resolved);
    }

    [Fact]
    public void Removing_a_missed_target_resolves_its_alert()
    {
        var sim = TestUtil.NewSim();
        var log = new AlertLog();
        sim.Execute(new SetTarget("iron_ingot", 10));
        Run(sim, log, AlertLog.TargetSeconds + 20);
        var alert = Assert.Single(log.Entries, a => a.Kind == AlertKind.TargetMissed);
        sim.Execute(new SetTarget("iron_ingot", null));
        Run(sim, log, 2);
        Assert.True(alert.Resolved);
    }

    [Fact]
    public void Steps_and_suggestions_move_in_round_numbers()
    {
        Assert.Equal(15, ProductionTargets.Step(10, 1));
        Assert.Equal(5, ProductionTargets.Step(10, -1));
        Assert.Equal(15, ProductionTargets.Step(12, 1));
        Assert.Equal(10, ProductionTargets.Step(12, -1));
        Assert.Equal(1, ProductionTargets.Step(1, -1));
        Assert.Equal(0.5, ProductionTargets.Step(0.5, -1));
        Assert.Equal(20000, ProductionTargets.Step(20000, 1));

        var sim = TestUtil.NewSim();
        Assert.Equal(10, ProductionTargets.Suggest(sim.World, "iron_ingot"));
        Assert.Equal("7.5", ProductionTargets.Format(7.5));
        Assert.Equal("1,200", ProductionTargets.Format(1200));
    }
}
