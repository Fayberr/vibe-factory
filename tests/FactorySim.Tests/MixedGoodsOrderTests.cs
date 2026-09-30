using FactorySim.Behaviors;
using FactorySim.Content;
using FactorySim.Persistence;

namespace FactorySim.Tests;

/// <summary>The 4.10.0 drop: one removable mixed-goods order definition and general order lines.</summary>
public class MixedGoodsOrderTests
{
    private const string Bundle = "workshop_supplies";
    private static readonly ContentRegistry C = TestUtil.Content;

    private static ContentRegistry Without()
    {
        var pack = ContentRegistry.ParsePack(ContentRegistry.BasePackJson());
        Assert.Equal(1, pack.ContractBundles.RemoveAll(x => x.Id == Bundle));
        return ContentRegistry.Build(BehaviorRegistry.CreateDefault(), new[] { pack });
    }

    [Fact]
    public void Workshop_bundle_asks_for_three_existing_goods_and_never_overpays()
    {
        var bundle = Assert.Single(C.ContractBundles, x => x.Id == Bundle);
        Assert.Equal(new[] { "iron_plate", "copper_wire", "plank" }, bundle.Items.Select(x => x.Item));
        Assert.Equal(1, bundle.Tier);
        Assert.All(bundle.Items, x => Assert.True(C.ItemValue[x.Item].Tier <= 1));
        Assert.InRange(bundle.RewardMultiplier, double.Epsilon, 1);
    }

    [Fact]
    public void Workshop_can_draw_one_order_with_one_deadline_and_payout_for_every_line()
    {
        var sim = TestUtil.NewSim(goals: true, money: 1_000_000);
        sim.World.UnlockedTier = 1;
        sim.World.Stats.TotalEarned = 1;
        sim.Step();
        for (int i = 0; i < 100 && sim.World.Contracts.Open[0].Bundle != Bundle; i++)
            Assert.True(sim.Execute(new RerollContract(sim.World.Contracts.Open[0].Id)).Ok);

        var order = Assert.Single(sim.World.Contracts.Open);
        Assert.Equal(Bundle, order.Bundle);
        Assert.Equal(3, order.Lines().Count());
        double goodsValue = order.Lines().Sum(x => x.Quantity * C.ItemValue[x.Item].Value);
        Assert.True(order.Reward.ToDouble() <= goodsValue);
        Assert.True(order.ExpiresAtTick > order.OfferedAtTick);
    }

    [Fact]
    public void Mixed_order_completes_only_after_every_line_and_survives_save()
    {
        var sim = TestUtil.NewSim(goals: true);
        var contract = new Contract
        {
            Id = 77, Bundle = Bundle, Item = "iron_plate", Quantity = 4, Delivered = 1,
            Additional = new()
            {
                new() { Item = "copper_wire", Quantity = 4, Delivered = 2 },
                new() { Item = "plank", Quantity = 2, Delivered = 1 },
            },
            Reward = 100, ExpiresAtTick = 10_000,
        };
        sim.World.Contracts.Open.Add(contract);

        var loaded = SaveSystem.Deserialize(SaveSystem.Serialize(sim), C).Simulation;
        var saved = Assert.Single(loaded.World.Contracts.Open);
        Assert.Equal(new long[] { 1, 2, 1 }, saved.Lines().Select(x => x.Delivered));

        loaded.Deliver("iron_plate", 3);
        loaded.Deliver("copper_wire", 2);
        Assert.False(saved.IsComplete);
        loaded.Deliver("plank", 1);
        Assert.Empty(loaded.World.Contracts.Open);
        Assert.Equal(1, loaded.World.Contracts.Completed);
    }

    [Fact]
    public void Loading_drops_a_mixed_order_if_any_requested_item_is_missing()
    {
        var sim = TestUtil.NewSim(goals: true);
        sim.World.Contracts.Open.Add(new Contract
        {
            Id = 9, Item = "iron_plate", Quantity = 5,
            Additional = new() { new() { Item = "removed_item", Quantity = 5 } },
        });
        var loaded = SaveSystem.Deserialize(SaveSystem.Serialize(sim), C);
        Assert.Empty(loaded.Simulation.World.Contracts.Open);
        Assert.Contains(loaded.Warnings, x => x.Contains("Dropped order #9", StringComparison.Ordinal));
    }

    [Fact]
    public void Removing_the_increment_leaves_all_previous_content_unchanged()
    {
        var without = Without();
        Assert.DoesNotContain(without.ContractBundles, x => x.Id == Bundle);
        Assert.Equal(C.Items.Keys.OrderBy(id => id), without.Items.Keys.OrderBy(id => id));
        Assert.Equal(C.Recipes.Keys.OrderBy(id => id), without.Recipes.Keys.OrderBy(id => id));
        Assert.Equal(C.Buildings.Keys.OrderBy(id => id), without.Buildings.Keys.OrderBy(id => id));
        Assert.Equal(C.Milestones.Select(x => x.Id), without.Milestones.Select(x => x.Id));
        Assert.Equal(C.Tiers.Select(t => (t.Name, t.Cost, t.RequiredEarnings, t.Description)),
            without.Tiers.Select(t => (t.Name, t.Cost, t.RequiredEarnings, t.Description)));
        Assert.Equal(C.Upgrades.Keys.OrderBy(id => id), without.Upgrades.Keys.OrderBy(id => id));
        foreach (var (item, info) in without.ItemValue) Assert.Equal(info, C.ItemValue[item]);
    }
}
