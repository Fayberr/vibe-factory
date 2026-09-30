using FactorySim.Balance;
using FactorySim.Behaviors;
using FactorySim.Content;

namespace FactorySim.Tests;

/// <summary>
/// The consumer goods side line in base.json (content drop, 4.3.0): one product and one machine a tier, from
/// chairs at Workshop to airliners at Aerospace, built from parts the main line already makes. Removing it
/// means deleting every entry marked "Consumer goods" and this file; nothing else refers to it.
/// </summary>
public class ConsumerGoodsTests
{
    private static readonly ContentRegistry C = TestUtil.Content;

    /// <summary>The side line's items. Main line tests leave these out (see RecipeTreeTests).</summary>
    public static readonly string[] Items = { "chair", "lantern", "tire", "phone", "car", "airliner" };

    private static readonly string[] Machines = { "carpenter", "lamp_works", "tire_plant", "phone_factory", "car_plant", "aircraft_works" };

    private static ContentPack WithoutConsumerGoodsPack()
    {
        var pack = ContentRegistry.ParsePack(ContentRegistry.BasePackJson());
        var recipes = pack.Recipes.Where(r => r.Outputs.Any(o => Items.Contains(o.Item))).Select(r => r.Id).ToHashSet();
        int removed = pack.Items.RemoveAll(i => Items.Contains(i.Id))
                      + pack.Recipes.RemoveAll(r => recipes.Contains(r.Id))
                      + pack.Buildings.RemoveAll(b => Machines.Contains(b.Id))
                      + pack.Milestones.RemoveAll(m => m.Item is { } item && Items.Contains(item));
        Assert.Equal(6 + 6 + 6 + 6, removed);
        return pack;
    }

    [Theory]
    [InlineData("chair", "carpenter", 1)]
    [InlineData("lantern", "lamp_works", 2)]
    [InlineData("tire", "tire_plant", 3)]
    [InlineData("phone", "phone_factory", 4)]
    [InlineData("car", "car_plant", 5)]
    [InlineData("airliner", "aircraft_works", 6)]
    public void Each_good_has_a_machine_of_its_own_one_tier_after_another(string item, string machine, int tier)
    {
        var def = C.Buildings[machine];
        Assert.Equal(tier, def.Tier);
        var recipe = C.Recipes[Assert.Single(((ProcessorParams)def.Params!).Recipes)];
        Assert.Equal(item, Assert.Single(recipe.Outputs).Item);
        Assert.Equal(tier, C.ItemValue[item].Tier);

        // No other machine makes it, so no existing machine on automatic starts making it instead.
        var makers = C.BuildingList.Where(b => b.Params is ProcessorParams p && p.Recipes.Any(r => C.Recipes[r].Outputs.Any(o => o.Item == item)));
        Assert.Equal(machine, Assert.Single(makers).Id);
    }

    [Fact]
    public void Only_the_side_line_uses_its_goods()
    {
        foreach (var r in C.Recipes.Values.Where(r => r.Inputs.Any(i => Items.Contains(i.Item))))
            Assert.All(r.Outputs, o => Assert.Contains(o.Item, Items));
        Assert.All(C.Tiers, t => Assert.DoesNotContain(t.Deliver, d => Items.Contains(d.Item)));
    }

    [Fact]
    public void Removing_the_side_line_leaves_every_other_item_as_it_was()
    {
        var without = ContentRegistry.Build(BehaviorRegistry.CreateDefault(), new[] { WithoutConsumerGoodsPack() });
        Assert.Equal(C.ItemValue.Keys.Except(Items).OrderBy(k => k), without.ItemValue.Keys.OrderBy(k => k));
        foreach (var (item, info) in without.ItemValue) Assert.Equal(info, C.ItemValue[item]);
    }

    /// <summary>
    /// The point of the side line: at the tier it opens, the best factory sells some of it beside the main
    /// product, without it taking over. Tires are only a part of cars.
    /// </summary>
    [Theory]
    [InlineData("chair", 1)]
    [InlineData("lantern", 2)]
    [InlineData("phone", 4)]
    [InlineData("car", 5)]
    [InlineData("airliner", 6)]
    public void Each_sold_good_earns_a_real_share_of_its_tier(string item, int tier)
    {
        var estimate = TierPacing.Estimate(C, new BalanceAssumptions { Level = 1 })[tier];
        double share = estimate.Products.Where(p => p.Item == item).Sum(p => p.IncomePerSecond) / estimate.IncomePerSecond;
        Assert.InRange(share, 0.05, 0.6);
    }

    [Fact]
    public void The_side_line_speeds_up_the_game_without_skipping_it()
    {
        var with = TierPacing.Estimate(C, new BalanceAssumptions { Level = 1 });
        var without = TierPacing.Estimate(ContentRegistry.Build(BehaviorRegistry.CreateDefault(), new[] { WithoutConsumerGoodsPack() }),
            new BalanceAssumptions { Level = 1 });
        for (int t = 0; t < with.Count; t++)
            Assert.InRange(with[t].IncomePerSecond, without[t].IncomePerSecond - 1e-6, without[t].IncomePerSecond * 1.5);
        Assert.True(with[^1].CumulativeSeconds < without[^1].CumulativeSeconds);
        Assert.True(with[^1].CumulativeSeconds > without[^1].CumulativeSeconds * 0.6);
    }
}
