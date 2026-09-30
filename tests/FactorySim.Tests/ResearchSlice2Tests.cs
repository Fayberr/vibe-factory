using FactorySim.Behaviors;
using FactorySim.Content;
using FactorySim.Persistence;
using System.Text.Json;

namespace FactorySim.Tests;

/// <summary>
/// Research slice 2 in base.json (content drop, 4.4.0): one Advanced Science Pack and three higher
/// continuations of the flat research bonuses. Delete every entry marked "Research slice 2", remove
/// pack_2 from the Science Bench and delete this file to remove the increment.
/// </summary>
public class ResearchSlice2Tests
{
    private static readonly ContentRegistry C = TestUtil.Content;

    private static ContentPack WithoutSlice2Pack()
    {
        var pack = ContentRegistry.ParsePack(ContentRegistry.BasePackJson());
        int removed = pack.Items.RemoveAll(i => i.Id == "science_2")
                      + pack.Recipes.RemoveAll(r => r.Id == "pack_2")
                      + pack.Upgrades.RemoveAll(u => u.Id.EndsWith("_advanced", StringComparison.Ordinal));
        Assert.Equal(5, removed);
        var bench = Assert.Single(pack.Buildings, b => b.Id == "science_bench");
        var parameters = Assert.IsType<JsonElement>(bench.Params).Deserialize<ProcessorParams>(Json.Options)!;
        Assert.Contains("pack_2", parameters.Recipes);
        bench.Params = new ProcessorParams
        {
            Recipes = parameters.Recipes.Where(id => id != "pack_2").ToArray(),
            InputCapacity = parameters.InputCapacity,
            OutputCapacity = parameters.OutputCapacity
        };
        return pack;
    }

    private static ContentRegistry Without() =>
        ContentRegistry.Build(BehaviorRegistry.CreateDefault(), new[] { WithoutSlice2Pack() });

    [Fact]
    public void Advanced_pack_is_an_industry_recipe_in_the_existing_bench()
    {
        var pack = C.Items["science_2"];
        Assert.True(pack.Science);
        Assert.Equal(2, C.ItemValue[pack.Id].Tier);

        var recipe = C.Recipes["pack_2"];
        Assert.Equal(new[] { ("steel", 1), ("gear", 1), ("screw", 2) },
            recipe.Inputs.Select(i => (i.Item, i.Count)));
        Assert.Equal("science_2", Assert.Single(recipe.Outputs).Item);
        Assert.Contains("pack_2", Assert.IsType<ProcessorParams>(C.Buildings["science_bench"].Params).Recipes);
    }

    [Fact]
    public void Advanced_pack_is_not_a_profitable_depot_shortcut()
    {
        var values = C.ItemValue;
        double parts = values["steel"].Value + values["gear"].Value + 2 * values["screw"].Value;
        Assert.Equal(parts / 2, values["science_2"].Value, 9);
        Assert.True(values["science_2"].Value < values["gear"].Value);
    }

    [Fact]
    public void Advanced_research_opens_at_industry_and_uses_only_the_higher_pack()
    {
        foreach (var id in new[] { "research_drills_advanced", "research_prices_advanced", "research_machines_advanced" })
        {
            var upgrade = C.Upgrades[id];
            Assert.Equal(2, upgrade.Tier);
            Assert.Equal(3, upgrade.MaxLevel);
            Assert.Equal("science_2", Assert.Single(upgrade.Packs).Item);
        }

        int[] Prices(string id) => Enumerable.Range(0, 3)
            .Select(level => C.Upgrades[id].PacksForLevel(level).Single().Count).ToArray();
        Assert.Equal(new[] { 20, 40, 80 }, Prices("research_drills_advanced"));
        Assert.Equal(new[] { 20, 40, 80 }, Prices("research_prices_advanced"));
        Assert.Equal(new[] { 10, 20, 40 }, Prices("research_machines_advanced"));
    }

    [Fact]
    public void Advanced_levels_stack_with_basic_levels_but_stay_optional()
    {
        var sim = TestUtil.NewSim(sandbox: false);
        sim.World.UnlockedTier = 2;
        sim.World.AddScience("science_1", 10_000);
        sim.World.AddScience("science_2", 10_000);
        for (int i = 0; i < 5; i++) Assert.True(sim.Execute(new BuyUpgrade("research_drills")).Ok);
        for (int i = 0; i < 3; i++) Assert.True(sim.Execute(new BuyUpgrade("research_drills_advanced")).Ok);
        Assert.Equal(Math.Pow(1.05, 8), sim.World.Stat(StatIds.MinerRate), 9);
    }

    [Fact]
    public void Removing_slice_2_restores_every_existing_item_and_research_entry()
    {
        var without = Without();
        Assert.Equal(C.ItemValue.Keys.Except(new[] { "science_2" }).OrderBy(k => k), without.ItemValue.Keys.OrderBy(k => k));
        foreach (var (item, info) in without.ItemValue) Assert.Equal(info, C.ItemValue[item]);

        Assert.Equal(C.Upgrades.Keys.Where(id => !id.EndsWith("_advanced", StringComparison.Ordinal)).OrderBy(id => id),
            without.Upgrades.Keys.OrderBy(id => id));
        Assert.Equal(new[] { "pack_1" }, Assert.IsType<ProcessorParams>(without.Buildings["science_bench"].Params).Recipes);
        Assert.Equal(C.Buildings.Keys.OrderBy(id => id), without.Buildings.Keys.OrderBy(id => id));
    }
}
