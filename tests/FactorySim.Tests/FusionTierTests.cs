using FactorySim.Balance;
using FactorySim.Behaviors;
using FactorySim.Content;

namespace FactorySim.Tests;

/// <summary>
/// Fusion tier content drop (4.25.0): a tenth tier after Orbital. Delete every base.json entry marked "Fusion tier"
/// (and the <c>shipyard_order</c> tier order), the two item mesh cases (<c>fusion_cell</c>, <c>starship</c>) and
/// this file to remove it, and put back the Orbital-era expectations in <c>GoalsTests</c> (9 tiers),
/// <c>RecipeTreeTests</c> and <c>ByproductTrialTests</c> (dead ends <c>orbital_station</c>) and
/// <c>OrbitalTierTests</c> (stations the most valuable good, Orbital the last tier).
/// </summary>
public class FusionTierTests
{
    private static readonly ContentRegistry C = TestUtil.Content;
    public static readonly string[] Items = { "lithium_brine", "lithium", "superconductor", "fusion_cell", "starship" };
    private static readonly string[] Recipes = { "make_lithium", "make_superconductor", "make_fusion_cell", "make_starship" };
    private static readonly string[] Buildings = { "brine_well", "lithium_plant", "superconductor_works", "cell_foundry", "shipyard" };
    private static readonly string[] Milestones = { "lithium_500", "fusion_cell_1", "starship_1" };
    public const int Fusion = 9;

    /// <summary>Takes the whole drop out of a parsed base pack (used by the Orbital tier's removal test too).</summary>
    internal static void RemoveFrom(ContentPack pack)
    {
        Assert.Equal("Fusion", pack.Tiers[^1].Name);
        pack.Tiers.RemoveAt(pack.Tiers.Count - 1);
        Assert.Equal(5, pack.Items.RemoveAll(i => Items.Contains(i.Id)));
        Assert.Equal(4, pack.Recipes.RemoveAll(r => Recipes.Contains(r.Id)));
        Assert.Equal(5, pack.Buildings.RemoveAll(b => Buildings.Contains(b.Id)));
        Assert.Equal(3, pack.Milestones.RemoveAll(m => Milestones.Contains(m.Id)));
        Assert.Equal(1, pack.ContractBundles.RemoveAll(b => b.Tier == Fusion));
    }

    /// <summary>The game as it was before this drop; earlier drops test themselves against it.</summary>
    internal static ContentRegistry Without()
    {
        var pack = ContentRegistry.ParsePack(ContentRegistry.BasePackJson());
        RemoveFrom(pack);
        return ContentRegistry.Build(BehaviorRegistry.CreateDefault(), new[] { pack });
    }

    [Fact]
    public void The_tier_comes_after_orbital_and_asks_for_stations()
    {
        var tier = C.Tiers[Fusion];
        Assert.Equal("Fusion", tier.Name);
        Assert.Equal(Fusion + 1, C.Tiers.Count);
        Assert.Equal("orbital_station", Assert.Single(tier.Deliver).Item);
        Assert.True(tier.Cost.ToDouble() > C.Tiers[Fusion - 1].Cost.ToDouble());
        Assert.True(tier.RequiredEarnings.ToDouble() > C.Tiers[Fusion - 1].RequiredEarnings.ToDouble());
    }

    [Fact]
    public void Every_new_building_and_item_belongs_to_the_tier_and_each_machine_has_its_own_recipe()
    {
        foreach (var id in Buildings) Assert.Equal(Fusion, C.Buildings[id].Tier);
        foreach (var id in Items) Assert.Equal(Fusion, C.ItemValue[id].Tier);
        Assert.True(C.Items["lithium_brine"].Raw);
        foreach (var recipe in Recipes)
        {
            var makers = C.BuildingList.Where(b => b.Params is ProcessorParams p && p.Recipes.Contains(recipe)).ToList();
            Assert.Single(makers);
            Assert.Single(((ProcessorParams)makers[0].Params!).Recipes);
        }
    }

    [Fact]
    public void The_tier_reuses_the_old_lines()
    {
        // Superconductors give gold ingots and titanium a second use; fusion cells pull batteries; the starship
        // is built around a station, so stations stop being a dead end.
        var inputs = Recipes.SelectMany(r => C.Recipes[r].Inputs.Select(i => i.Item)).ToHashSet();
        Assert.Superset(new HashSet<string> { "gold_ingot", "titanium", "cable", "battery", "orbital_station" }, inputs);
    }

    [Fact]
    public void Starships_are_the_most_valuable_good_and_the_tier_earns_clearly_more_than_orbital()
    {
        var v = C.ItemValue;
        Assert.Equal("starship", C.Items.Keys.Where(id => !C.Items[id].Science).MaxBy(id => v[id].Value));

        var pacing = TierPacing.Estimate(C, new BalanceAssumptions { Level = 1 });
        Assert.InRange(pacing[Fusion].IncomePerSecond / pacing[Fusion - 1].IncomePerSecond, 1.5, 3);
        var ship = Assert.Single(pacing[Fusion].Products, p => p.Item == "starship");
        Assert.True(ship.IncomePerSecond > 0.25 * pacing[Fusion].IncomePerSecond);
    }

    [Fact]
    public void The_wait_for_it_is_no_longer_than_twice_the_wait_for_orbital()
    {
        var pacing = TierPacing.Estimate(C, new BalanceAssumptions { Level = 1 });
        Assert.InRange(pacing[Fusion - 1].Seconds, 0, 2 * pacing[Fusion - 2].Seconds);
    }

    [Fact]
    public void A_lithium_line_works()
    {
        var sim = TestUtil.NewSim();
        sim.Place("brine_well", 2, 0, 0, Dir.South);
        sim.Place("conveyor", 2, 1, 0, Dir.South);
        sim.Place("lithium_plant", 2, 2, 0, Dir.South);
        sim.Step(20 * 30);
        var plant = sim.World.EntityAt(new GridPos(2, 2, 0))!;
        Assert.Contains(((ProcessorState)plant.State).Output, s => s.Type == "lithium");
    }

    [Fact]
    public void Removing_the_tier_leaves_all_previous_content_unchanged()
    {
        var without = Without();
        Assert.Equal(C.Items.Keys.Except(Items).OrderBy(id => id), without.Items.Keys.OrderBy(id => id));
        Assert.Equal(C.Recipes.Keys.Except(Recipes).OrderBy(id => id), without.Recipes.Keys.OrderBy(id => id));
        Assert.Equal(C.Buildings.Keys.Except(Buildings).OrderBy(id => id), without.Buildings.Keys.OrderBy(id => id));
        Assert.Equal(C.Milestones.Select(m => m.Id).Except(Milestones).OrderBy(id => id),
            without.Milestones.Select(m => m.Id).OrderBy(id => id));
        Assert.Equal(C.Tiers.Take(Fusion).Select(t => (t.Name, t.Cost, t.RequiredEarnings, t.Description)),
            without.Tiers.Select(t => (t.Name, t.Cost, t.RequiredEarnings, t.Description)));
        foreach (var (item, info) in without.ItemValue) Assert.Equal(info, C.ItemValue[item]);

        var with = TierPacing.Estimate(C, new BalanceAssumptions { Level = 1 });
        var before = TierPacing.Estimate(without, new BalanceAssumptions { Level = 1 });
        for (int t = 0; t < Fusion; t++) Assert.Equal(before[t].IncomePerSecond, with[t].IncomePerSecond, 6);
    }
}
