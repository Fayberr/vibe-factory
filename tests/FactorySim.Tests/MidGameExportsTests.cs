using FactorySim.Balance;
using FactorySim.Behaviors;
using FactorySim.Content;

namespace FactorySim.Tests;

/// <summary>
/// Industrial exports content drop (4.7.0). Delete every base.json entry marked "Industrial exports",
/// the two item mesh cases and this file to remove the increment. No earlier content refers to it.
/// </summary>
public class MidGameExportsTests
{
    private static readonly ContentRegistry C = TestUtil.Content;
    public static readonly string[] Items = { "toolkit", "industrial_pump" };
    private static readonly string[] Recipes = { "make_toolkit", "make_industrial_pump" };
    private static readonly string[] Buildings = { "tool_works", "pump_works" };
    private static readonly string[] Milestones = { "toolkits_25", "industrial_pump_1" };

    private static ContentRegistry Without()
    {
        var pack = ContentRegistry.ParsePack(ContentRegistry.BasePackJson());
        Assert.Equal(2, pack.Items.RemoveAll(i => Items.Contains(i.Id)));
        Assert.Equal(2, pack.Recipes.RemoveAll(r => Recipes.Contains(r.Id)));
        Assert.Equal(2, pack.Buildings.RemoveAll(b => Buildings.Contains(b.Id)));
        Assert.Equal(2, pack.Milestones.RemoveAll(m => Milestones.Contains(m.Id)));
        return ContentRegistry.Build(BehaviorRegistry.CreateDefault(), new[] { pack });
    }

    [Theory]
    [InlineData("toolkit", "tool_works", 2)]
    [InlineData("industrial_pump", "pump_works", 3)]
    public void Each_export_is_optional_and_has_a_dedicated_machine(string item, string machine, int tier)
    {
        var def = C.Buildings[machine];
        Assert.Equal(tier, def.Tier);
        var recipe = C.Recipes[Assert.Single(Assert.IsType<ProcessorParams>(def.Params).Recipes)];
        Assert.Equal(item, Assert.Single(recipe.Outputs).Item);
        Assert.Equal(tier, C.ItemValue[item].Tier);
        Assert.DoesNotContain(C.Tiers.SelectMany(t => t.Deliver), d => d.Item == item);

        var makers = C.BuildingList.Where(b => b.Params is ProcessorParams p && p.Recipes.Contains(recipe.Id));
        Assert.Equal(machine, Assert.Single(makers).Id);
    }

    [Fact]
    public void Exports_give_underused_mid_game_parts_another_destination()
    {
        var toolkit = C.Recipes["make_toolkit"].Inputs.Select(i => i.Item).ToHashSet();
        Assert.Equal(new HashSet<string> { "iron_rod", "screw", "gear" }, toolkit);

        var pump = C.Recipes["make_industrial_pump"].Inputs.Select(i => i.Item).ToHashSet();
        Assert.Contains("cable", pump);
        Assert.Contains("frame", pump);
        Assert.Contains("steel", pump);
    }

    [Fact]
    public void Exports_do_not_move_tier_income_by_more_than_a_little()
    {
        var without = TierPacing.Estimate(Without(), new BalanceAssumptions { Level = 1 });
        var with = TierPacing.Estimate(C, new BalanceAssumptions { Level = 1 });
        foreach (int tier in new[] { 2, 3 })
            Assert.InRange(with[tier].IncomePerSecond, without[tier].IncomePerSecond - 1e-6, without[tier].IncomePerSecond * 1.15);
        Assert.True(with[2].IncomePerSecond > without[2].IncomePerSecond);
    }

    [Fact]
    public void Removing_the_increment_leaves_all_previous_content_unchanged()
    {
        var without = Without();
        Assert.Equal(C.Items.Keys.Except(Items).OrderBy(id => id), without.Items.Keys.OrderBy(id => id));
        Assert.Equal(C.Recipes.Keys.Except(Recipes).OrderBy(id => id), without.Recipes.Keys.OrderBy(id => id));
        Assert.Equal(C.Buildings.Keys.Except(Buildings).OrderBy(id => id), without.Buildings.Keys.OrderBy(id => id));
        Assert.Equal(C.Milestones.Select(m => m.Id).Except(Milestones).OrderBy(id => id),
            without.Milestones.Select(m => m.Id).OrderBy(id => id));
        Assert.Equal(C.Tiers.Select(t => (t.Name, t.Cost, t.RequiredEarnings, t.Description)),
            without.Tiers.Select(t => (t.Name, t.Cost, t.RequiredEarnings, t.Description)));
        Assert.Equal(C.Upgrades.Keys.OrderBy(id => id), without.Upgrades.Keys.OrderBy(id => id));
        foreach (var (item, info) in without.ItemValue) Assert.Equal(info, C.ItemValue[item]);
    }
}
