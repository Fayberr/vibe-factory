using FactorySim.Balance;
using FactorySim.Content;

namespace FactorySim.Tests;

/// <summary>
/// How long the ten tiers take, and the level tracks that set it (4.29.0): machine and drill upgrades
/// cost enough that upgrading cannot skip the game, and depot levels get steeper the higher they go.
/// </summary>
public class GameLengthTests
{
    private static readonly ContentRegistry C = TestUtil.Content;

    private static ContentRegistry With(string json) => ContentRegistry.LoadDefault(null, ContentRegistry.ParsePack(json));

    private static double Hours(IReadOnlyList<TierEstimate> pacing) => pacing[^1].CumulativeSeconds / 3600;

    [Fact]
    public void Even_the_best_player_needs_ten_hours_for_the_ten_tiers()
    {
        // Waiting for money only: the factory is built the moment a tier opens, so real play is longer.
        var reference = TierPacing.Estimate(C, new BalanceAssumptions { UpgradePaybackSeconds = 1800 });
        var patient = TierPacing.Estimate(C, new BalanceAssumptions { UpgradePaybackSeconds = 1e7 });
        // Another way to play: leave machines at level 1, build more of them, and upgrade only the rest.
        var drillsOnly = TierPacing.Estimate(With("""{ "levelTracks": { "processor": { "maxLevel": 1 } } }"""),
            new BalanceAssumptions { UpgradePaybackSeconds = 1e7 });

        Assert.InRange(Hours(reference), 10, 14);
        Assert.InRange(Hours(patient), 10, Hours(reference) + 1e-9);
        Assert.True(Hours(drillsOnly) >= 10, $"drills only: {Hours(drillsOnly):0.0} h");
    }

    [Fact]
    public void Every_tier_takes_longer_than_the_one_before()
    {
        // A smooth climb, not a short start and one long wall at the end.
        var reference = TierPacing.Estimate(C, new BalanceAssumptions { UpgradePaybackSeconds = 1800 });
        var waits = reference.Where(e => !double.IsNaN(e.Seconds)).ToList();
        for (int i = 1; i < waits.Count; i++)
            Assert.True(waits[i].Seconds > waits[i - 1].Seconds,
                $"{waits[i].Name} ({waits[i].Seconds / 60:0} min) is quicker than {waits[i - 1].Name} ({waits[i - 1].Seconds / 60:0} min)");
        Assert.True(waits[^1].Seconds < Hours(reference) * 3600 / 3, "no single tier should be most of the game");
    }

    [Fact]
    public void A_rising_step_makes_every_level_a_bigger_multiple_than_the_last()
    {
        var def = new BuildingDef { Id = "x", Cost = 100 };
        var track = new UpgradeTrack { MaxLevel = null, CostFactor = 2, CostGrowth = 1.9, CostGrowthStep = 0.1 };

        Assert.Equal(200, track.UpgradeCost(def, 1).ToDouble(), 9);
        Assert.Equal(380, track.UpgradeCost(def, 2).ToDouble(), 9);
        Assert.Equal(760, track.UpgradeCost(def, 3).ToDouble(), 9);
        Assert.Equal(1596, track.UpgradeCost(def, 4).ToDouble(), 9);
        for (int level = 2; level < 40; level++)
        {
            double before = track.UpgradeCost(def, level).ToDouble() / track.UpgradeCost(def, level - 1).ToDouble();
            double after = track.UpgradeCost(def, level + 1).ToDouble() / track.UpgradeCost(def, level).ToDouble();
            Assert.Equal(before + 0.1, after, 9);
        }

        double paid = 100;
        for (int level = 1; level < 30; level++) paid += track.UpgradeCost(def, level).ToDouble();
        Assert.Equal(paid, track.Invested(def, 30).ToDouble(), paid * 1e-12);

        // Without a step it is the plain geometric track it always was.
        var plain = new UpgradeTrack { CostFactor = 2, CostGrowth = 1.9 };
        Assert.Equal(200 * Math.Pow(1.9, 9), plain.UpgradeCost(def, 10).ToDouble(), 6);
    }

    [Fact]
    public void Depot_and_export_terminal_levels_get_steeper_and_never_stop()
    {
        foreach (var id in new[] { "seller", "export_terminal" })
        {
            var def = C.Buildings[id];
            var track = def.Upgrade!;
            Assert.Null(track.MaxLevel);
            Assert.True(track.CostGrowthStep > 0, $"{id} levels should get steeper");
            double previous = 0;
            for (int level = 2; level < 60; level++)
            {
                double multiple = track.UpgradeCost(def, level).ToDouble() / track.UpgradeCost(def, level - 1).ToDouble();
                Assert.True(multiple > previous, $"{id}: level {level} is no steeper than the one before");
                previous = multiple;
            }
        }
    }

    [Fact]
    public void Level_tracks_come_from_the_content_by_behavior()
    {
        var content = With("""
            { "levelTracks": {
                "miner": { "maxLevel": 3, "speedPerLevel": 1, "costFactor": 5, "costGrowth": 4 },
                "conveyor": { "maxLevel": 2 } } }
            """);

        // Every miner without its own "upgrade" takes the pack's track ...
        var drill = content.Buildings["iron_miner"];
        Assert.Equal(3, drill.Upgrade!.MaxLevel);
        Assert.Equal(drill.Cost.ToDouble() * 5 * 4, drill.Upgrade.UpgradeCost(drill, 2).ToDouble(), 6);
        // ... the base pack's other behaviors keep the base tracks ...
        Assert.Equal(C.Buildings["smelter"].Upgrade!.CostGrowth, content.Buildings["smelter"].Upgrade!.CostGrowth);
        // ... and a building with its own "upgrade" keeps it (every belt sets one).
        Assert.Equal(9, content.Buildings["conveyor"].Upgrade!.MaxLevel);

        // The base content itself declares the tracks it uses.
        var basePack = ContentRegistry.ParsePack(ContentRegistry.BasePackJson());
        Assert.Contains("processor", basePack.LevelTracks.Keys);
        Assert.Contains("miner", basePack.LevelTracks.Keys);
        Assert.Contains("seller", basePack.LevelTracks.Keys);
        Assert.Equal(basePack.LevelTracks["miner"].CostGrowth, C.Buildings["iron_miner"].Upgrade!.CostGrowth);
    }

    [Fact]
    public void A_falling_step_is_rejected()
    {
        Assert.Throws<ContentException>(() =>
            With("""{ "levelTracks": { "seller": { "maxLevel": null, "costGrowth": 2, "costGrowthStep": -0.1 } } }"""));
    }
}
