using FactorySim.Balance;
using FactorySim.Behaviors;
using FactorySim.Content;

namespace FactorySim.Tests;

/// <summary>
/// Home appliances side line (content drop, 4.24.0): a kettle, television, washing machine and e-bike, one product
/// and one machine a tier from Petrochemicals to Aerospace, from main-line parts only. Delete every base.json entry
/// marked "Home appliances", the appliance words in four tier descriptions, the four item mesh cases, and this file
/// to remove it (and the <c>HomeAppliancesTests.Items</c> exclusions in the dead-end checks).
/// </summary>
public class HomeAppliancesTests
{
    private static readonly ContentRegistry C = TestUtil.Content;

    /// <summary>The line's items, all only sold. Dead-end checks leave these out.</summary>
    public static readonly string[] Items = { "kettle", "television", "washing_machine", "ebike" };
    private static readonly string[] Machines = { "kettle_works", "tv_plant", "appliance_plant", "bike_works" };

    private static ContentRegistry Without()
    {
        var pack = ContentRegistry.ParsePack(ContentRegistry.BasePackJson());
        var recipes = pack.Recipes.Where(r => r.Outputs.Any(o => Items.Contains(o.Item))).Select(r => r.Id).ToHashSet();
        int removed = pack.Items.RemoveAll(i => Items.Contains(i.Id))
                      + pack.Recipes.RemoveAll(r => recipes.Contains(r.Id))
                      + pack.Buildings.RemoveAll(b => Machines.Contains(b.Id))
                      + pack.Milestones.RemoveAll(m => m.Item is { } item && Items.Contains(item));
        Assert.Equal(4 * 4, removed);
        return ContentRegistry.Build(BehaviorRegistry.CreateDefault(), new[] { pack });
    }

    [Theory]
    [InlineData("kettle", "kettle_works", 3)]
    [InlineData("television", "tv_plant", 4)]
    [InlineData("washing_machine", "appliance_plant", 5)]
    [InlineData("ebike", "bike_works", 6)]
    public void Each_appliance_has_a_machine_of_its_own(string item, string machine, int tier)
    {
        var def = C.Buildings[machine];
        Assert.Equal(tier, def.Tier);
        var recipe = C.Recipes[Assert.Single(((ProcessorParams)def.Params!).Recipes)];
        Assert.Equal(item, Assert.Single(recipe.Outputs).Item);
        Assert.Equal(tier, C.ItemValue[item].Tier);
        var makers = C.BuildingList.Where(b => b.Params is ProcessorParams p && p.Recipes.Any(r => C.Recipes[r].Outputs.Any(o => o.Item == item)));
        Assert.Equal(machine, Assert.Single(makers).Id);
    }

    [Fact]
    public void Appliances_are_built_from_main_line_parts_and_used_in_nothing()
    {
        foreach (var r in C.Recipes.Values.Where(r => r.Outputs.Any(o => Items.Contains(o.Item))))
            Assert.All(r.Inputs, i => Assert.DoesNotContain(i.Item, ConsumerGoodsTests.Items.Concat(MidGameExportsTests.Items).Concat(Items)));
        Assert.DoesNotContain(C.Recipes.Values, r => r.Inputs.Any(i => Items.Contains(i.Item)));
        Assert.All(C.Tiers, t => Assert.DoesNotContain(t.Deliver, d => Items.Contains(d.Item)));
    }

    [Fact]
    public void An_appliance_is_worth_less_than_the_tiers_main_product()
    {
        var v = C.ItemValue;
        Assert.True(v["television"].Value < v["motor"].Value);
        Assert.True(v["washing_machine"].Value < v["robot"].Value);
        Assert.True(v["ebike"].Value < v["drone"].Value);
    }

    [Fact]
    public void A_kettle_works_turns_its_parts_into_a_kettle()
    {
        var sim = TestUtil.NewSim();
        sim.Place("kettle_works", 5, 5, 0, Dir.South);
        var state = (ProcessorState)sim.World.EntityAt(new GridPos(5, 5, 0))!.State;
        foreach (var input in C.Recipes["make_kettle"].Inputs)
            state.Inputs[input.Item] = new InputBuffer { Count = input.Count, ValueSum = input.Count * C.ItemValue[input.Item].Value };
        sim.Step(20 * 3);
        Assert.Contains(state.Output, s => s.Type == "kettle");
    }

    [Fact]
    public void Removing_the_line_leaves_every_other_item_tier_and_income_unchanged()
    {
        var without = Without();
        Assert.Equal(C.Items.Keys.Except(Items).OrderBy(k => k), without.Items.Keys.OrderBy(k => k));
        foreach (var (item, info) in without.ItemValue) Assert.Equal(info, C.ItemValue[item]);

        // The line only adds: no tier earns less with it than without it.
        var with = TierPacing.Estimate(C, new BalanceAssumptions { Level = 1 });
        var before = TierPacing.Estimate(without, new BalanceAssumptions { Level = 1 });
        for (int t = 0; t < with.Count; t++) Assert.True(with[t].IncomePerSecond >= before[t].IncomePerSecond - 1e-9);
    }
}
