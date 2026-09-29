using FactorySim.Behaviors;
using FactorySim.Content;
using FactorySim.Persistence;

namespace FactorySim.Tests;

/// <summary>
/// The research trial in base.json: a Science Bench makes Basic Science Packs from plates and wire, a
/// Lab banks them, and three global upgrades are bought with the bank. Deleting the trial means deleting
/// its seven content entries and this file (see "Research" in docs/ARCHITECTURE.md); the lab behavior,
/// the bank and pack prices on upgrades can stay dormant, as the upgrade mechanism did before.
/// </summary>
public class ResearchTrialTests
{
    private static readonly ContentRegistry C = TestUtil.Content;

    /// <summary>base.json with the trial taken out, exactly as the removal steps say.</summary>
    internal static ContentPack WithoutTrialPack()
    {
        var pack = ContentRegistry.ParsePack(ContentRegistry.BasePackJson());
        int removed = pack.Items.RemoveAll(i => i.Id == "science_1")
                      + pack.Recipes.RemoveAll(r => r.Id == "pack_1")
                      + pack.Buildings.RemoveAll(b => b.Id is "science_bench" or "lab")
                      + pack.Upgrades.RemoveAll(u => u.Id.StartsWith("research_", StringComparison.Ordinal));
        Assert.Equal(7, removed);
        return pack;
    }

    private static ContentRegistry Without() => ContentRegistry.Build(BehaviorRegistry.CreateDefault(), new[] { WithoutTrialPack() });

    /// <summary>Base content plus a drill that mines packs, so a lab can be fed from a belt in a few cells.</summary>
    private static readonly ContentRegistry WithSource = ContentRegistry.LoadDefault(null, ContentRegistry.ParsePack("""
        {
          "buildings": [ { "id": "test_pack_source", "behavior": "miner", "cost": 1,
                           "ports": [ { "kind": "out", "side": "front" } ],
                           "params": { "item": "science_1", "interval": 5, "amount": 1 } } ]
        }
        """));

    /// <summary>Source → belt → lab along a row; returns the lab.</summary>
    private static Entity LabLine(Simulation sim)
    {
        sim.Place("test_pack_source", 3, 5, 0, Dir.East);
        sim.Place("conveyor", 4, 5, 0, Dir.East);
        sim.Place("lab", 5, 5, 0, Dir.East); // its input is on the back, facing the belt
        return sim.World.EntityAt(new GridPos(5, 5, 0))!;
    }

    [Fact]
    public void The_trial_changes_no_existing_items_value_or_tier()
    {
        var before = Without().ItemValue;
        var after = C.ItemValue;
        Assert.Equal(before.Keys.Append("science_1").OrderBy(k => k), after.Keys.OrderBy(k => k));
        foreach (var (item, info) in before) Assert.Equal(info, after[item]);
    }

    [Fact]
    public void A_pack_is_worth_its_parts_and_is_made_from_tier_1()
    {
        var v = C.ItemValue;
        Assert.True(C.Items["science_1"].Science);
        Assert.False(C.Items["science_1"].Raw);
        Assert.Equal(v["iron_plate"].Value + v["copper_wire"].Value, v["science_1"].Value);
        Assert.Equal(1, v["science_1"].Tier);
        Assert.All(C.UpgradeList.Where(u => u.Packs.Length > 0), u => Assert.Equal(1, u.Tier));
    }

    [Fact]
    public void Prices_in_packs_double_each_level()
    {
        int[] Prices(string id) => Enumerable.Range(0, 5).Select(l => C.Upgrades[id].PacksForLevel(l).Single().Count).ToArray();
        Assert.Equal(new[] { 20, 40, 80, 160, 320 }, Prices("research_drills"));
        Assert.Equal(new[] { 20, 40, 80, 160, 320 }, Prices("research_prices"));
        Assert.Equal(new[] { 10, 20, 40, 80, 160 }, Prices("research_machines"));
        Assert.True(C.Upgrades["research_drills"].CostForLevel(3).IsZero); // no money on top
    }

    [Fact]
    public void A_lab_banks_one_pack_every_four_seconds_from_a_belt()
    {
        var sim = TestUtil.NewSim(WithSource);
        var lab = LabLine(sim);
        Assert.Equal("waiting for packs", lab.Behavior.GetStatus(lab).Detail);

        sim.Step(20 * 60);
        long banked = sim.World.ScienceOf("science_1");
        Assert.InRange(banked, 14, 15); // 60 s at one pack per 4 s, less the first pack's trip
        Assert.Equal(banked, ((LabState)lab.State).Banked);
        Assert.StartsWith("studying", lab.Behavior.GetStatus(lab).Detail);
        // The source makes packs faster than the lab studies them, so the lab holds a full load.
        Assert.Equal(10, ((LabState)lab.State).Held["science_1"]);
    }

    [Fact]
    public void A_lab_takes_only_packs()
    {
        var content = ContentRegistry.LoadDefault(null, ContentRegistry.ParsePack("""
            { "buildings": [ { "id": "test_pack_source", "behavior": "miner", "ports": [ { "kind": "out", "side": "front" } ],
                               "params": { "item": "iron_ore", "interval": 5, "amount": 1 } } ] }
            """));
        var sim = TestUtil.NewSim(content);
        var lab = LabLine(sim);
        sim.Step(20 * 20);
        Assert.Empty(sim.World.Science);
        Assert.Empty(((LabState)lab.State).Held);
        Assert.Equal("waiting for packs", lab.Behavior.GetStatus(lab).Detail);
    }

    [Fact]
    public void Buying_research_spends_packs_and_raises_the_stat()
    {
        var sim = TestUtil.NewSim(sandbox: false, money: 1000);
        sim.World.UnlockedTier = 1;
        sim.World.AddScience("science_1", 30);

        Assert.True(sim.Execute(new BuyUpgrade("research_drills")).Ok);
        Assert.Equal(10, sim.World.ScienceOf("science_1"));
        Assert.Equal(1, sim.World.UpgradeLevel("research_drills"));
        Assert.Equal(1.05, sim.World.Stat(StatIds.MinerRate), 9);
        Assert.Equal(1000, sim.World.Money.ToDouble()); // research costs packs, not money
        Assert.Contains(sim.DrainEvents(), e => e is UpgradePurchased { UpgradeId: "research_drills", Level: 1 });

        var second = sim.Execute(new BuyUpgrade("research_drills")); // 40 packs now
        Assert.False(second.Ok);
        Assert.Contains("40 Basic Science Pack", second.Error);
        Assert.Equal(10, sim.World.ScienceOf("science_1"));
        Assert.Equal(1, sim.World.UpgradeLevel("research_drills"));
    }

    [Fact]
    public void Research_waits_for_its_tier()
    {
        var sim = TestUtil.NewSim(sandbox: false, money: 1000);
        sim.World.AddScience("science_1", 1000);
        var r = sim.Execute(new BuyUpgrade("research_machines"));
        Assert.False(r.Ok);
        Assert.Contains("tier 1", r.Error);
        Assert.Equal(1000, sim.World.ScienceOf("science_1"));
    }

    [Fact]
    public void Levels_are_capped_at_five()
    {
        var sim = TestUtil.NewSim(sandbox: false);
        sim.World.UnlockedTier = 1;
        sim.World.AddScience("science_1", 10_000);
        for (int i = 0; i < 5; i++) Assert.True(sim.Execute(new BuyUpgrade("research_machines")).Ok);
        Assert.False(sim.Execute(new BuyUpgrade("research_machines")).Ok);
        Assert.Equal(10_000 - (10 + 20 + 40 + 80 + 160), sim.World.ScienceOf("science_1"));
        Assert.Equal(Math.Pow(1.1, 5), sim.World.Stat(StatIds.MachineSpeed), 9);
    }

    [Fact]
    public void The_bank_and_the_labs_survive_a_save_and_continue_identically()
    {
        var sim = TestUtil.NewSim(WithSource, sandbox: false, money: 1e6);
        sim.World.UnlockedTier = 1;
        LabLine(sim);
        sim.Step(20 * 60);
        Assert.True(sim.Execute(new BuyUpgrade("research_machines")).Ok);

        string json = SaveSystem.Serialize(sim);
        var loaded = SaveSystem.Deserialize(json, WithSource);
        Assert.Empty(loaded.Warnings);
        Assert.Equal(sim.World.ScienceOf("science_1"), loaded.Simulation.World.ScienceOf("science_1"));
        Assert.Equal(1, loaded.Simulation.World.UpgradeLevel("research_machines"));
        Assert.Equal(json, SaveSystem.Serialize(loaded.Simulation));

        sim.Step(20 * 30);
        loaded.Simulation.Step(20 * 30);
        Assert.Equal(SaveSystem.Serialize(sim), SaveSystem.Serialize(loaded.Simulation));
    }

    [Fact]
    public void A_save_with_research_loads_into_the_game_without_it()
    {
        var sim = TestUtil.NewSim(sandbox: false, money: 1e6);
        sim.World.UnlockedTier = 1;
        sim.Place("iron_miner", 2, 2, 0, Dir.East);
        sim.Place("lab", 6, 6, 0, Dir.East);
        ((LabState)sim.World.EntityAt(new GridPos(6, 6, 0))!.State).Held["science_1"] = 4;
        sim.World.AddScience("science_1", 50);
        Assert.True(sim.Execute(new BuyUpgrade("research_prices")).Ok);
        string json = SaveSystem.Serialize(sim);

        var loaded = SaveSystem.Deserialize(json, Without());
        var world = loaded.Simulation.World;
        Assert.Contains(loaded.Warnings, w => w.Contains("research_prices"));
        Assert.Contains(loaded.Warnings, w => w.Contains("science_1"));
        Assert.Contains(loaded.Warnings, w => w.Contains("'lab'"));
        Assert.Empty(world.Science);
        Assert.Equal(1, world.Stat(StatIds.SellMultiplier));
        Assert.NotNull(world.EntityAt(new GridPos(2, 2, 0))); // everything else is kept
        loaded.Simulation.Step(20 * 5);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Packs_are_never_ordered(bool science)
    {
        // The same item without the flag is ordered, so the check is not passing by luck.
        var content = ContentRegistry.LoadDefault(null, ContentRegistry.ParsePack($$"""
            { "items": [ { "id": "science_1", "name": "Basic Science Pack", "science": {{(science ? "true" : "false")}} } ] }
            """));
        var sim = TestUtil.NewSim(content, money: 1e9, goals: true);
        sim.World.UnlockedTier = 1;
        sim.Place("iron_miner", 72, 0, 0, Dir.East);
        sim.Place("smelter", 73, 0, 0, Dir.East);
        sim.Place("seller", 74, 0, 0, Dir.East);
        sim.Step(20 * 5);

        var offered = new HashSet<string>();
        for (int i = 0; i < 80; i++)
        {
            var order = sim.World.Contracts.Open[0];
            offered.Add(order.Item);
            Assert.True(sim.Execute(new RerollContract(order.Id)).Ok);
        }
        Assert.Equal(!science, offered.Contains("science_1"));
    }
}
