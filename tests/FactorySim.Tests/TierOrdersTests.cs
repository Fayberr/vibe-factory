using FactorySim.Behaviors;
using FactorySim.Content;

namespace FactorySim.Tests;

/// <summary>
/// Customer orders for every tier (content drop, 4.23.0): one mixed order a tier from Industry to Orbital, pure
/// data on top of the 4.10.0 order lines. Delete every base.json entry marked "Tier orders" and this file to
/// remove them.
/// </summary>
public class TierOrdersTests
{
    private static readonly ContentRegistry C = TestUtil.Content;
    public static readonly string[] Bundles =
    {
        "builders_order", "hardware_store", "gadget_shop", "robot_workshop", "hangar_order", "mission_supplies", "station_supplies",
    };

    private static ContentRegistry Without()
    {
        var pack = ContentRegistry.ParsePack(ContentRegistry.BasePackJson());
        Assert.Equal(Bundles.Length, pack.ContractBundles.RemoveAll(x => Bundles.Contains(x.Id)));
        return ContentRegistry.Build(BehaviorRegistry.CreateDefault(), new[] { pack });
    }

    [Fact]
    public void Every_tier_after_basics_has_exactly_one_mixed_order()
    {
        for (int tier = 1; tier < C.Tiers.Count; tier++)
            Assert.Single(C.ContractBundles, b => b.Tier == tier);
    }

    [Fact]
    public void Each_order_asks_only_for_goods_its_tier_can_make_and_never_pays_more_than_they_are_worth()
    {
        foreach (var id in Bundles)
        {
            var bundle = Assert.Single(C.ContractBundles, b => b.Id == id);
            Assert.InRange(bundle.RewardMultiplier, double.Epsilon, 1);
            Assert.InRange(bundle.Items.Length, 2, 3);
            Assert.All(bundle.Items, x => Assert.InRange(C.ItemValue[x.Item].Tier, 0, bundle.Tier));
            Assert.Contains(bundle.Items, x => C.ItemValue[x.Item].Tier == bundle.Tier); // something new to make
            Assert.All(bundle.Items, x => Assert.False(C.Items[x.Item].Raw || C.Items[x.Item].Byproduct || C.Items[x.Item].Science));
            // Main-line goods only, so removing a side drop never leaves an order asking for a missing item.
            Assert.All(bundle.Items, x => Assert.DoesNotContain(x.Item, ConsumerGoodsTests.Items));
        }
    }

    [Theory]
    [InlineData(2, "builders_order")]
    [InlineData(5, "robot_workshop")]
    [InlineData(8, "station_supplies")]
    public void The_order_can_be_drawn_once_its_tier_is_the_newest(int tier, string id)
    {
        var sim = TestUtil.NewSim(goals: true, money: 1e15);
        sim.World.UnlockedTier = tier;
        sim.World.Stats.TotalEarned = 1;
        sim.Step();
        for (int i = 0; i < 300 && sim.World.Contracts.Open[0].Bundle != id; i++)
            Assert.True(sim.Execute(new RerollContract(sim.World.Contracts.Open[0].Id)).Ok);

        var order = Assert.Single(sim.World.Contracts.Open);
        Assert.Equal(id, order.Bundle);
        double goodsValue = order.Lines().Sum(x => x.Quantity * C.ItemValue[x.Item].Value);
        Assert.True(order.Reward.ToDouble() <= goodsValue);
    }

    [Fact]
    public void Removing_the_drop_leaves_all_previous_content_unchanged()
    {
        var without = Without();
        Assert.Equal(new[] { "workshop_supplies" }, without.ContractBundles.Select(b => b.Id));
        Assert.Equal(C.Items.Keys.OrderBy(id => id), without.Items.Keys.OrderBy(id => id));
        Assert.Equal(C.Recipes.Keys.OrderBy(id => id), without.Recipes.Keys.OrderBy(id => id));
        Assert.Equal(C.Buildings.Keys.OrderBy(id => id), without.Buildings.Keys.OrderBy(id => id));
        Assert.Equal(C.Milestones.Select(x => x.Id), without.Milestones.Select(x => x.Id));
        foreach (var (item, info) in without.ItemValue) Assert.Equal(info, C.ItemValue[item]);
    }
}
