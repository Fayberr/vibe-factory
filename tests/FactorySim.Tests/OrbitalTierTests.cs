using FactorySim.Balance;
using FactorySim.Behaviors;
using FactorySim.Content;

namespace FactorySim.Tests;

/// <summary>
/// Orbital tier content drop (4.13.0): a ninth tier after Space. Delete every base.json entry marked
/// "Orbital tier", its "Tier orders" entry (station_supplies), the three item mesh cases and this file to remove it, and put back the Space-era
/// expectations noted in <c>GoalsTests</c>, <c>RecipeTreeTests</c>, <c>ByproductTrialTests</c> and
/// <c>DeepSpaceProbeTests</c>.
/// </summary>
public class OrbitalTierTests
{
    private static readonly ContentRegistry C = TestUtil.Content;
    public static readonly string[] Items = { "titanium_ore", "titanium", "solar_panel", "habitat_module", "orbital_station" };
    private static readonly string[] Recipes = { "make_titanium", "make_solar_panel", "make_habitat_module", "make_orbital_station" };
    private static readonly string[] Buildings = { "titanium_mine", "arc_furnace", "solar_works", "module_yard", "station_dock" };
    private static readonly string[] Milestones = { "titanium_200", "habitat_module_1", "orbital_station_1" };
    private const int Orbital = 8;

    private static ContentRegistry Without()
    {
        var pack = ContentRegistry.ParsePack(ContentRegistry.BasePackJson());
        Assert.Equal("Orbital", pack.Tiers[^1].Name);
        pack.Tiers.RemoveAt(pack.Tiers.Count - 1);
        Assert.Equal(5, pack.Items.RemoveAll(i => Items.Contains(i.Id)));
        Assert.Equal(4, pack.Recipes.RemoveAll(r => Recipes.Contains(r.Id)));
        Assert.Equal(5, pack.Buildings.RemoveAll(b => Buildings.Contains(b.Id)));
        Assert.Equal(3, pack.Milestones.RemoveAll(m => Milestones.Contains(m.Id)));
        pack.ContractBundles.RemoveAll(b => b.Tier == Orbital); // the tier's own customer order (4.23.0)
        return ContentRegistry.Build(BehaviorRegistry.CreateDefault(), new[] { pack });
    }

    [Fact]
    public void The_tier_comes_after_space_and_asks_for_satellites()
    {
        var tier = C.Tiers[Orbital];
        Assert.Equal("Orbital", tier.Name);
        Assert.Equal(Orbital + 1, C.Tiers.Count);
        Assert.Equal("satellite", Assert.Single(tier.Deliver).Item);
        Assert.Equal(Orbital - 1, C.ItemValue["satellite"].Tier);
        Assert.True(tier.Cost.ToDouble() > C.Tiers[Orbital - 1].Cost.ToDouble());
        Assert.True(tier.RequiredEarnings.ToDouble() > C.Tiers[Orbital - 1].RequiredEarnings.ToDouble());
    }

    [Fact]
    public void Every_new_building_and_item_belongs_to_the_tier_and_each_machine_has_its_own_recipe()
    {
        foreach (var id in Buildings) Assert.Equal(Orbital, C.Buildings[id].Tier);
        foreach (var id in Items) Assert.Equal(Orbital, C.ItemValue[id].Tier);
        Assert.True(C.Items["titanium_ore"].Raw);

        foreach (var recipe in Recipes)
        {
            var makers = C.BuildingList.Where(b => b.Params is ProcessorParams p && p.Recipes.Contains(recipe)).ToList();
            Assert.Single(makers);
            Assert.Single(((ProcessorParams)makers[0].Params!).Recipes);
        }
    }

    [Fact]
    public void Stations_are_the_most_valuable_good_and_the_tier_earns_clearly_more_than_space()
    {
        var v = C.ItemValue;
        Assert.Equal("orbital_station", C.Items.Keys.Where(id => !C.Items[id].Science).MaxBy(id => v[id].Value));

        var pacing = TierPacing.Estimate(C, new BalanceAssumptions { Level = 1 });
        Assert.InRange(pacing[Orbital].IncomePerSecond / pacing[Orbital - 1].IncomePerSecond, 1.5, 3);
        var station = Assert.Single(pacing[Orbital].Products, p => p.Item == "orbital_station");
        Assert.True(station.IncomePerSecond > 0.25 * pacing[Orbital].IncomePerSecond);
    }

    [Fact]
    public void The_wait_for_it_is_no_longer_than_twice_the_wait_for_space()
    {
        var pacing = TierPacing.Estimate(C, new BalanceAssumptions { Level = 1 });
        Assert.InRange(pacing[Orbital - 1].Seconds, 0, 2 * pacing[Orbital - 2].Seconds);
    }

    [Fact]
    public void A_titanium_line_works()
    {
        var sim = TestUtil.NewSim();
        sim.Place("titanium_mine", 2, 0, 0, Dir.South);
        sim.Place("conveyor", 2, 1, 0, Dir.South);
        sim.Place("arc_furnace", 2, 2, 0, Dir.South);
        sim.Place("coal_miner", 1, 2, 0, Dir.East);
        sim.Step(20 * 30);

        var furnace = sim.World.EntityAt(new GridPos(2, 2, 0))!;
        Assert.Contains(((ProcessorState)furnace.State).Output, s => s.Type == "titanium");
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
        Assert.Equal(C.Tiers.Take(Orbital).Select(t => (t.Name, t.Cost, t.RequiredEarnings, t.Description)),
            without.Tiers.Select(t => (t.Name, t.Cost, t.RequiredEarnings, t.Description)));
        Assert.Equal(C.Upgrades.Keys.OrderBy(id => id), without.Upgrades.Keys.OrderBy(id => id));
        foreach (var (item, info) in without.ItemValue) Assert.Equal(info, C.ItemValue[item]);

        // Every tier before it earns the same, so the drop only adds a step at the end.
        var with = TierPacing.Estimate(C, new BalanceAssumptions { Level = 1 });
        var before = TierPacing.Estimate(without, new BalanceAssumptions { Level = 1 });
        for (int t = 0; t < Orbital; t++) Assert.Equal(before[t].IncomePerSecond, with[t].IncomePerSecond, 6);
        Assert.Equal(before[^1].CumulativeSeconds, with[Orbital - 2].CumulativeSeconds, 6); // reaching Space; a row's total includes its own wait
    }
}
