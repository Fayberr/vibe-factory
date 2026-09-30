using FactorySim.Cli;
using FactorySim.Persistence;

namespace FactorySim.Tests;

public class InspectCommandTests
{
    [Fact]
    public void Report_reads_buildings_levels_and_remaining_tier_requirements_from_the_save()
    {
        var sim = TestUtil.NewSim();
        sim.Place("iron_miner", 0, 0, 0, Dir.East);
        sim.Place("iron_miner", 0, 2, 0, Dir.East);
        Assert.True(sim.Execute(new SetBuildingLevels(new[] { new LevelChange(new GridPos(0, 2, 0), 3) })).Ok);
        sim.World.UnlockedTier = 1;

        var loaded = SaveSystem.Deserialize(SaveSystem.Serialize(sim), TestUtil.Content).Simulation;
        string report = InspectCommand.Report(loaded, sampleSeconds: 1);

        Assert.Contains("Workshop (tier 1)", report);
        Assert.Contains("Iron Drill", report);
        Assert.Contains("L1: 1, L3: 1", report);
        Assert.Contains("Industry", report);
        Assert.Contains("Steel Beam", report);
    }

    [Fact]
    public void Report_measures_a_factory_that_actually_runs_and_names_its_blockage()
    {
        var sim = TestUtil.NewSim();
        sim.Place("iron_miner", 0, 0, 0, Dir.East);
        sim.Line(new GridPos(1, 0, 0), Dir.East, 2);
        sim.Place("smelter", 3, 0, 0, Dir.East);
        sim.Line(new GridPos(4, 0, 0), Dir.East, 2);
        sim.Place("seller", 6, 0, 0, Dir.East);
        sim.Place("iron_miner", 0, 3, 0, Dir.East); // no output belt

        var loaded = SaveSystem.Deserialize(SaveSystem.Serialize(sim), TestUtil.Content).Simulation;
        string report = InspectCommand.Report(loaded, sampleSeconds: 90);

        Assert.Contains("Iron Ingot", report);
        Assert.Contains("$2", report);
        Assert.Contains("output blocked", report);
        Assert.True(loaded.World.Stats.Sold.GetValueOrDefault("iron_ingot") > 0);
    }
}
