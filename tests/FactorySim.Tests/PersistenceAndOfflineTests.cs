using FactorySim.Persistence;
using FactorySim.Samples;

namespace FactorySim.Tests;

public class PersistenceAndOfflineTests
{
    private static Simulation Demo()
    {
        var sim = TestUtil.NewSim(sandbox: false, money: 10_000);
        DemoLayout.Build(sim);
        sim.Execute(new BuyUpgrade("miner_rate"));
        sim.Execute(new BuyUpgrade("stack_size"));
        return sim;
    }

    [Fact]
    public void Save_then_load_continues_bit_identically()
    {
        var uninterrupted = Demo();
        uninterrupted.Step(1500);

        var first = Demo();
        first.Step(700);
        var json = SaveSystem.Serialize(first);
        var loaded = SaveSystem.Deserialize(json, TestUtil.Content);
        Assert.Empty(loaded.Warnings);
        loaded.Simulation.Step(800);

        Assert.Equal(SaveSystem.Serialize(uninterrupted), SaveSystem.Serialize(loaded.Simulation));
    }

    [Fact]
    public void Same_inputs_same_outputs()
    {
        var a = Demo();
        var b = Demo();
        a.Step(2000);
        b.Step(2000);
        Assert.Equal(SaveSystem.Serialize(a), SaveSystem.Serialize(b));
    }

    [Fact]
    public void Load_drops_unknown_content_with_warnings()
    {
        var sim = Demo();
        var json = SaveSystem.Serialize(sim).Replace("\"def\":\"polisher\"", "\"def\":\"removed_machine\"");
        var loaded = SaveSystem.Deserialize(json, TestUtil.Content);
        Assert.Single(loaded.Warnings);
        Assert.Equal(sim.World.EntityCount - 1, loaded.Simulation.World.EntityCount);
    }

    [Fact]
    public void Saves_carry_the_host_supplied_timestamp()
    {
        var at = new DateTimeOffset(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);
        var loaded = SaveSystem.Deserialize(SaveSystem.Serialize(Demo(), at), TestUtil.Content);
        Assert.Equal(at, loaded.SavedAtUtc);
    }

    [Fact]
    public void Short_offline_period_is_simulated_exactly()
    {
        var a = Demo();
        var b = Demo();
        a.Step(20 * 90);
        var report = b.CatchUp(90);

        Assert.Equal(0, report.ExtrapolatedTicks);
        Assert.Equal(a.World.Money, b.World.Money);
        Assert.Equal(a.World.Tick, b.World.Tick);
    }

    [Fact]
    public void Long_offline_period_is_extrapolated_close_to_full_simulation()
    {
        const double hour = 3600;
        var full = Demo();
        full.Step((long)(hour * Simulation.TicksPerSecond));

        var fast = Demo();
        var report = fast.CatchUp(hour, new OfflineOptions { MaxSimulatedSeconds = 120 });

        Assert.Equal(20 * 120, report.SimulatedTicks);
        Assert.True(report.ExtrapolatedTicks > 0);
        Assert.Equal(full.World.Tick, fast.World.Tick);
        double ratio = fast.World.Money.ToDouble() / full.World.Money.ToDouble();
        Assert.InRange(ratio, 0.97, 1.03);
    }
}
