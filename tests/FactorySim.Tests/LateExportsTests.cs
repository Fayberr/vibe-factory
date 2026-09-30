using FactorySim.Balance;
using FactorySim.Behaviors;
using FactorySim.Content;

namespace FactorySim.Tests;

/// <summary>
/// Late exports (content drop, 4.26.0): a planetary rover at Space, a space suit at Orbital and a maglev train at
/// Fusion, one product and one machine a tier, from main-line parts only. Delete every base.json entry marked
/// "Late exports", the three item mesh cases and this file to remove it (and the <c>LateExportsTests.Items</c>
/// exclusions in the dead-end checks, and the <see cref="RemoveTier"/> calls in the Orbital and Fusion tier tests).
/// </summary>
public class LateExportsTests
{
    private static readonly ContentRegistry C = TestUtil.Content;

    private static readonly (int Tier, string Item, string Recipe, string Machine, string Goal)[] Line =
    {
        (7, "rover", "make_rover", "rover_works", "rover_1"),
        (8, "space_suit", "make_space_suit", "suit_lab", "space_suit_1"),
        (9, "maglev", "make_maglev", "maglev_works", "maglev_1"),
    };

    /// <summary>The line's items, all only sold. Dead-end checks leave these out.</summary>
    public static readonly string[] Items = Line.Select(x => x.Item).ToArray();

    /// <summary>The ids this drop adds at one tier, for a removable tier's own removal test.</summary>
    internal static (string[] Items, string[] Recipes, string[] Buildings, string[] Goals) At(int tier)
    {
        var x = Line.Where(l => l.Tier == tier).ToArray();
        return (x.Select(l => l.Item).ToArray(), x.Select(l => l.Recipe).ToArray(), x.Select(l => l.Machine).ToArray(), x.Select(l => l.Goal).ToArray());
    }

    /// <summary>Takes this drop's product at <paramref name="tier"/> out of a pack; a removed tier takes its export with it.</summary>
    internal static void RemoveTier(ContentPack pack, int tier)
    {
        var (items, recipes, buildings, goals) = At(tier);
        Assert.Equal(items.Length, pack.Items.RemoveAll(i => items.Contains(i.Id)));
        Assert.Equal(recipes.Length, pack.Recipes.RemoveAll(r => recipes.Contains(r.Id)));
        Assert.Equal(buildings.Length, pack.Buildings.RemoveAll(b => buildings.Contains(b.Id)));
        Assert.Equal(goals.Length, pack.Milestones.RemoveAll(m => goals.Contains(m.Id)));
    }

    private static ContentRegistry Without()
    {
        var pack = ContentRegistry.ParsePack(ContentRegistry.BasePackJson());
        foreach (var tier in Line.Select(l => l.Tier)) RemoveTier(pack, tier);
        return ContentRegistry.Build(BehaviorRegistry.CreateDefault(), new[] { pack });
    }

    [Fact]
    public void Each_export_has_a_machine_of_its_own_at_its_tier()
    {
        foreach (var (tier, item, recipe, machine, goal) in Line)
        {
            var def = C.Buildings[machine];
            Assert.Equal(tier, def.Tier);
            Assert.Equal(recipe, Assert.Single(((ProcessorParams)def.Params!).Recipes));
            Assert.Equal(item, Assert.Single(C.Recipes[recipe].Outputs).Item);
            Assert.Equal(tier, C.ItemValue[item].Tier);
            var makers = C.BuildingList.Where(b => b.Params is ProcessorParams p && p.Recipes.Any(r => C.Recipes[r].Outputs.Any(o => o.Item == item)));
            Assert.Equal(machine, Assert.Single(makers).Id);
            Assert.Equal(item, Assert.Single(C.Milestones, m => m.Id == goal).Item);
        }
    }

    [Fact]
    public void Exports_are_built_from_main_line_parts_and_used_in_nothing()
    {
        var sideLines = ConsumerGoodsTests.Items.Concat(MidGameExportsTests.Items).Concat(HomeAppliancesTests.Items).Concat(Items)
            .Append("space_probe").ToHashSet();
        foreach (var (_, _, recipe, _, _) in Line)
            Assert.All(C.Recipes[recipe].Inputs, i => Assert.DoesNotContain(i.Item, sideLines));
        Assert.DoesNotContain(C.Recipes.Values, r => r.Inputs.Any(i => Items.Contains(i.Item)));
        Assert.All(C.Tiers, t => Assert.DoesNotContain(t.Deliver, d => Items.Contains(d.Item)));
        Assert.DoesNotContain(C.ContractBundles, b => b.Items.Any(i => Items.Contains(i.Item)));
    }

    [Fact]
    public void An_export_is_worth_less_than_its_tiers_main_product()
    {
        var v = C.ItemValue;
        Assert.True(v["rover"].Value < v["satellite"].Value);
        Assert.True(v["space_suit"].Value < v["habitat_module"].Value);
        Assert.True(v["maglev"].Value < v["starship"].Value);
    }

    [Fact]
    public void A_rover_works_turns_its_parts_into_a_rover()
    {
        var sim = TestUtil.NewSim();
        sim.Place("rover_works", 5, 5, 0, Dir.South);
        var state = (ProcessorState)sim.World.EntityAt(new GridPos(5, 5, 0))!.State;
        foreach (var input in C.Recipes["make_rover"].Inputs)
            state.Inputs[input.Item] = new InputBuffer { Count = input.Count, ValueSum = input.Count * C.ItemValue[input.Item].Value };
        sim.Step(20 * 7);
        Assert.Contains(state.Output, s => s.Type == "rover");
    }

    [Fact]
    public void Removing_the_line_leaves_every_other_item_and_tier_income_unchanged()
    {
        var without = Without();
        Assert.Equal(C.Items.Keys.Except(Items).OrderBy(k => k), without.Items.Keys.OrderBy(k => k));
        Assert.Equal(C.Milestones.Count - 3, without.Milestones.Count);
        foreach (var (item, info) in without.ItemValue) Assert.Equal(info, C.ItemValue[item]);

        var with = TierPacing.Estimate(C, new BalanceAssumptions { Level = 1 });
        var before = TierPacing.Estimate(without, new BalanceAssumptions { Level = 1 });
        for (int t = 0; t < with.Count; t++) Assert.Equal(before[t].IncomePerSecond, with[t].IncomePerSecond, 6);
    }
}
