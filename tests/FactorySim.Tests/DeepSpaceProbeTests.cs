using FactorySim.Balance;
using FactorySim.Behaviors;
using FactorySim.Content;

namespace FactorySim.Tests;

/// <summary>
/// Deep-space probes content drop (4.6.0). Delete every base.json entry marked "Deep-space probes"
/// and this file to remove the increment. No tier or existing machine refers to the drop.
/// </summary>
public class DeepSpaceProbeTests
{
    private static readonly ContentRegistry C = TestUtil.Content;
    private static readonly string[] Items = { "space_probe" };
    private static readonly string[] Recipes = { "make_space_probe" };
    private static readonly string[] Buildings = { "probe_works" };
    private static readonly string[] Milestones = { "space_probe_1" };

    private static ContentRegistry Without()
    {
        var pack = ContentRegistry.ParsePack(ContentRegistry.BasePackJson());
        Assert.Equal(1, pack.Items.RemoveAll(i => Items.Contains(i.Id)));
        Assert.Equal(1, pack.Recipes.RemoveAll(r => Recipes.Contains(r.Id)));
        Assert.Equal(1, pack.Buildings.RemoveAll(b => Buildings.Contains(b.Id)));
        Assert.Equal(1, pack.Milestones.RemoveAll(m => Milestones.Contains(m.Id)));
        return ContentRegistry.Build(BehaviorRegistry.CreateDefault(), new[] { pack });
    }

    [Fact]
    public void Probe_is_an_optional_space_product_that_uses_satellites()
    {
        var def = C.Buildings["probe_works"];
        Assert.Equal(7, def.Tier);
        var recipe = C.Recipes[Assert.Single(Assert.IsType<ProcessorParams>(def.Params).Recipes)];
        Assert.Contains(recipe.Inputs, i => i.Item == "satellite" && i.Count == 1);
        Assert.Equal("space_probe", Assert.Single(recipe.Outputs).Item);
        Assert.DoesNotContain(C.Tiers.SelectMany(t => t.Deliver), d => d.Item == "space_probe");

        var makers = C.BuildingList.Where(b => b.Params is ProcessorParams p && p.Recipes.Contains(recipe.Id));
        Assert.Equal("probe_works", Assert.Single(makers).Id);
    }

    [Fact]
    public void Probe_adds_value_without_changing_the_time_to_reach_space()
    {
        var without = Without();
        var withPacing = TierPacing.Estimate(C, new BalanceAssumptions { Level = 1 });
        var withoutPacing = TierPacing.Estimate(without, new BalanceAssumptions { Level = 1 });
        // Space, not the last tier: the Orbital tier (4.13.0) comes after it, and probes do speed that one up.
        int space = C.Tiers.ToList().FindIndex(t => t.Name == "Space");
        Assert.Equal(withoutPacing[space - 1].CumulativeSeconds, withPacing[space - 1].CumulativeSeconds, 6); // a row's total includes its own wait
        // Up to half as much again: the Space value boost (4.28.0) is paid on the probe's extra step too.
        Assert.InRange(withPacing[space].IncomePerSecond, withoutPacing[space].IncomePerSecond, withoutPacing[space].IncomePerSecond * 1.5);
        Assert.True(C.ItemValue["space_probe"].Value > C.ItemValue["satellite"].Value);
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
