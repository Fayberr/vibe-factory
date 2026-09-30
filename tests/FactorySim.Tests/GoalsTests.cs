using FactorySim.Content;
using FactorySim.Persistence;

namespace FactorySim.Tests;

public class GoalsTests
{
    /// <summary>Iron drill → smelter → depot, with contracts and milestones on.</summary>
    private static Simulation IngotLine(bool sandbox = true)
    {
        var sim = TestUtil.NewSim(sandbox: sandbox, money: 10_000, goals: true);
        sim.Place("iron_miner", 72, 0, 0, Dir.East);
        sim.Place("smelter", 73, 0, 0, Dir.East);
        sim.Place("seller", 74, 0, 0, Dir.East); // depots stand on the edge of the map
        return sim;
    }

    [Fact]
    public void Orders_arrive_once_the_factory_sells_and_pay_a_bonus_when_delivered()
    {
        var sim = IngotLine();
        Assert.Empty(sim.World.Contracts.Open);
        sim.Step(20 * 5);
        var order = Assert.Single(sim.World.Contracts.Open);
        Assert.Equal("iron_ingot", order.Item); // the only thing made at tier 0
        Assert.True(order.Reward.ToDouble() >= order.Quantity * 2 * 2); // ≥ 2× market value

        var before = sim.World.Money;
        sim.Step(20 * (order.Quantity + 10));
        Assert.Equal(1, sim.World.Contracts.Completed);
        Assert.True((sim.World.Money - before).ToDouble() > order.Reward.ToDouble()); // reward on top of sales
        Assert.Contains(sim.DrainEvents(), e => e is ContractCompleted c && c.Contract.Id == order.Id);
    }

    [Fact]
    public void Orders_expire_and_can_be_swapped_for_a_fee()
    {
        var sim = IngotLine(sandbox: false);
        sim.Step(20 * 3); // first sale, then the first order
        sim.Execute(new RemoveBuildings(new[] { new GridPos(74, 0, 0) })); // nothing is sold any more
        var order = Assert.Single(sim.World.Contracts.Open);

        var money = sim.World.Money;
        Assert.True(sim.Execute(new RerollContract(order.Id)).Ok);
        Assert.Equal((money - order.Reward * 0.1).ToDouble(), sim.World.Money.ToDouble(), 6);
        var fresh = Assert.Single(sim.World.Contracts.Open);
        Assert.NotEqual(order.Id, fresh.Id);

        sim.Step(fresh.ExpiresAtTick - sim.World.Tick + 20);
        Assert.DoesNotContain(sim.World.Contracts.Open, c => c.Id == fresh.Id);
        Assert.True(sim.World.Contracts.Expired >= 1);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Byproducts_are_never_ordered(bool byproduct)
    {
        // A tier 0 item next to the iron ingot: flagged as a byproduct it is never asked for, and the
        // same item without the flag is (so the check is not passing by luck).
        var content = ContentRegistry.LoadDefault(null, ContentRegistry.ParsePack($$"""
            {
              "items": [ { "id": "test_slag", "name": "Slag", "byproduct": {{(byproduct ? "true" : "false")}} } ],
              "recipes": [ { "id": "test_smelt_slag", "inputs": [ { "item": "iron_ore", "count": 1 } ],
                             "outputs": [ { "item": "iron_ingot", "count": 1 }, { "item": "test_slag", "count": 1 } ], "ticks": 16, "valueMultiplier": 4 } ],
              "buildings": [ { "id": "test_slag_smelter", "behavior": "processor",
                               "ports": [ { "kind": "in", "side": "back" }, { "kind": "out", "side": "front" } ],
                               "params": { "recipes": [ "test_smelt_slag" ] } } ]
            }
            """));
        var sim = TestUtil.NewSim(content, money: 1e9, goals: true);
        sim.Place("iron_miner", 72, 0, 0, Dir.East);
        sim.Place("smelter", 73, 0, 0, Dir.East);
        sim.Place("seller", 74, 0, 0, Dir.East);
        sim.Step(20 * 5);

        var offered = new HashSet<string>();
        for (int i = 0; i < 40; i++)
        {
            var order = sim.World.Contracts.Open[0];
            offered.Add(order.Item);
            Assert.True(sim.Execute(new RerollContract(order.Id)).Ok);
        }
        Assert.Equal(!byproduct, offered.Contains("test_slag"));
        Assert.Contains("iron_ingot", offered);
    }

    [Fact]
    public void Milestones_pay_once_and_count_toward_lifetime_earnings()
    {
        var sim = IngotLine();
        var reward = sim.Content.Milestones.First(m => m.Id == "first_sale").Reward;
        sim.Step(20 * 3);
        Assert.Contains("first_sale", sim.World.Milestones);
        Assert.Single(sim.DrainEvents().OfType<MilestoneReached>(), m => m.Id == "first_sale");
        Assert.True(sim.World.Stats.TotalEarned.ToDouble() >= reward.ToDouble());
        sim.Step(20 * 3);
        Assert.DoesNotContain(sim.DrainEvents().OfType<MilestoneReached>(), m => m.Id == "first_sale");
    }

    [Fact]
    public void Orders_and_milestones_survive_save_and_load_and_stay_deterministic()
    {
        var a = IngotLine();
        a.Step(20 * 40);
        var loaded = SaveSystem.Deserialize(SaveSystem.Serialize(a), TestUtil.Content).Simulation;
        Assert.Equal(a.World.Contracts.Open.Select(c => (c.Id, c.Item, c.Quantity, c.Delivered)),
                     loaded.World.Contracts.Open.Select(c => (c.Id, c.Item, c.Quantity, c.Delivered)));
        Assert.Equal(a.World.Milestones.OrderBy(x => x), loaded.World.Milestones.OrderBy(x => x));

        a.Step(20 * 60);
        loaded.Step(20 * 60);
        Assert.Equal(SaveSystem.Serialize(a), SaveSystem.Serialize(loaded));
    }

    [Fact]
    public void Every_milestone_is_reachable_with_the_content()
    {
        var c = TestUtil.Content;
        foreach (var m in c.Milestones)
        {
            if (m.Kind is "produced" or "sold" && m.Item != null) Assert.True(c.ItemValue.ContainsKey(m.Item), $"{m.Id}: {m.Item} can't be made");
            if (m.Kind == "tier") Assert.True(m.Target < c.Tiers.Count, m.Id);
            if (m.Kind == "level") Assert.True(c.BuildingList.Any(b => b.Upgrade?.MaxLevel is not int max || max >= m.Target), m.Id);
            Assert.True(m.Reward.ToDouble() > 0, m.Id);
        }
        Assert.Equal(c.Milestones.Count, c.Milestones.Select(m => m.Id).Distinct().Count());
    }

    [Fact]
    public void The_new_tiers_end_in_satellites()
    {
        var v = TestUtil.Content.ItemValue;
        Assert.Equal(6, v["drone"].Tier);
        Assert.Equal(7, v["satellite"].Tier);
        Assert.True(v["satellite"].Value > 2 * v["drone"].Value);
        // Orbital tier (4.13.0): a ninth tier after Space, ending in stations built around a satellite.
        Assert.Equal(8, v["orbital_station"].Tier);
        Assert.True(v["orbital_station"].Value > 2 * v["satellite"].Value);
        Assert.Equal(9, TestUtil.Content.Tiers.Count);
    }
}
