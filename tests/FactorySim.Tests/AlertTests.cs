namespace FactorySim.Tests;

/// <summary>Alerts (idea F4): buildings that stopped or jammed, and orders running out.</summary>
public class AlertTests
{
    /// <summary>A drill feeding a smelter feeding a depot, in column <paramref name="x"/>.</summary>
    private static int Line(Simulation sim, int x)
    {
        sim.Place("iron_miner", x, 0, 0, Dir.South);
        int smelter = sim.Place("smelter", x, 1, 0, Dir.South);
        sim.Place("seller", x, 2, 0, Dir.South);
        return smelter;
    }

    /// <summary>Runs the simulation the way the client does: a few ticks, then a look.</summary>
    private static void Run(Simulation sim, AlertLog log, double seconds)
    {
        long end = sim.World.Tick + (long)(seconds * Simulation.TicksPerSecond);
        while (sim.World.Tick < end)
        {
            sim.Step(Math.Min(IdleSampler.Stride, end - sim.World.Tick));
            log.Observe(sim.World);
        }
    }

    private static void Remove(Simulation sim, int x, int y) => Assert.True(sim.Execute(new RemoveBuilding(new GridPos(x, y, 0))).Ok);

    [Fact]
    public void A_machine_that_stops_working_is_reported_once_and_resolved_when_it_runs_again()
    {
        var sim = TestUtil.NewSim();
        var log = new AlertLog();
        int smelter = Line(sim, 2);
        Run(sim, log, 30);
        Assert.Empty(log.Entries);

        Remove(sim, 2, 0);
        Run(sim, log, AlertLog.StoppedSeconds - 5);
        Assert.Empty(log.Entries); // a pause is not news yet

        Run(sim, log, 15);
        var alert = Assert.Single(log.Entries);
        Assert.Equal(AlertKind.Stopped, alert.Kind);
        Assert.Equal("smelter", alert.Building);
        Assert.Equal(smelter, alert.ExampleId);
        Assert.NotEmpty(alert.Detail);
        Assert.False(alert.Resolved);
        Assert.Equal(1, log.Unseen);

        Run(sim, log, 60);
        Assert.Single(log.Entries); // still stopped, still one entry

        sim.Place("iron_miner", 2, 0, 0, Dir.South);
        Run(sim, log, 10);
        Assert.True(alert.Resolved);
        Assert.Single(log.Entries);
    }

    [Fact]
    public void A_machine_with_nowhere_to_put_its_output_is_jammed()
    {
        var sim = TestUtil.NewSim();
        var log = new AlertLog();
        int smelter = Line(sim, 2);
        Run(sim, log, 30);
        Remove(sim, 2, 2);
        Run(sim, log, 90);
        var alert = Assert.Single(log.Entries, a => a.Building == "smelter");
        Assert.Equal(AlertKind.Jammed, alert.Kind);
        Assert.Equal(smelter, alert.ExampleId);
    }

    [Fact]
    public void Buildings_that_never_worked_raise_nothing()
    {
        var sim = TestUtil.NewSim();
        var log = new AlertLog();
        sim.Place("smelter", 2, 1, 0, Dir.South);
        sim.Place("seller", 2, 2, 0, Dir.South);
        Run(sim, log, 120);
        Assert.Empty(log.Entries);
    }

    [Fact]
    public void Machines_of_one_type_that_stop_together_make_one_entry()
    {
        var sim = TestUtil.NewSim();
        var log = new AlertLog();
        Line(sim, 2);
        Line(sim, 5);
        Run(sim, log, 30);
        Remove(sim, 2, 0);
        Remove(sim, 5, 0);
        Run(sim, log, 40);
        var alert = Assert.Single(log.Entries);
        Assert.Equal(2, alert.Count);
    }

    [Fact]
    public void A_machine_that_stops_again_soon_after_recovering_is_not_reported_twice()
    {
        var sim = TestUtil.NewSim();
        var log = new AlertLog();
        Line(sim, 2);
        Run(sim, log, 30);
        Remove(sim, 2, 0);
        Run(sim, log, 40);
        sim.Place("iron_miner", 2, 0, 0, Dir.South);
        Run(sim, log, 10);
        Remove(sim, 2, 0);
        Run(sim, log, 40);
        Assert.Single(log.Entries);

        // After the quiet period it is news again.
        sim.Place("iron_miner", 2, 0, 0, Dir.South);
        Run(sim, log, AlertLog.RepeatSeconds + 10);
        Remove(sim, 2, 0);
        Run(sim, log, 40);
        Assert.Equal(2, log.Entries.Count);
    }

    [Fact]
    public void Orders_warn_before_they_run_out_and_say_when_they_did()
    {
        var sim = TestUtil.NewSim();
        var log = new AlertLog();
        var order = new Contract { Id = 5, Item = "iron_ingot", Quantity = 1000, OfferedAtTick = 0, ExpiresAtTick = 100 * Simulation.TicksPerSecond };
        sim.World.Contracts.Open.Add(order);
        Run(sim, log, 75);
        Assert.Empty(log.Entries);
        Run(sim, log, 10);
        var ending = Assert.Single(log.Entries);
        Assert.Equal(AlertKind.OrderEnding, ending.Kind);
        Assert.Same(order, ending.Order);
        Run(sim, log, 5);
        Assert.Single(log.Entries); // warned once

        log.OnEvent(new ContractExpired(sim.World.Tick, order));
        Assert.Equal(AlertKind.OrderExpired, log.Entries[0].Kind);

        log.OnEvent(new ContractCompleted(sim.World.Tick, order));
        Assert.True(ending.Resolved);
    }

    [Fact]
    public void Entries_can_be_dismissed_cleared_and_are_capped()
    {
        var log = new AlertLog();
        var order = new Contract { Id = 1, Item = "iron_ingot", Quantity = 1 };
        for (int i = 0; i < AlertLog.Capacity + 5; i++) log.OnEvent(new ContractExpired(i, order));
        Assert.Equal(AlertLog.Capacity, log.Entries.Count);
        Assert.Equal(AlertLog.Capacity + 4, log.Entries[0].Tick); // newest first
        Assert.Equal(AlertLog.Capacity + 5, log.Unseen);

        int version = log.Version;
        log.Dismiss(log.Entries[0].Id);
        Assert.Equal(AlertLog.Capacity - 1, log.Entries.Count);
        Assert.NotEqual(version, log.Version);

        log.MarkSeen();
        Assert.Equal(0, log.Unseen);
        log.Clear();
        Assert.Empty(log.Entries);
    }
}
