using FactorySim.Behaviors;
using FactorySim.Content;

namespace FactorySim.Tests;

/// <summary>
/// Bulk machines content drop (idea B11, 4.22.0): a Bulk Smelter, Bulk Press and Bulk Forge at the Electronics
/// tier. Delete every base.json entry marked "Bulk machines", the words "and bulk machines" in the Electronics
/// tier's description, and this file to remove them.
/// </summary>
public class BulkMachinesTests
{
    private static readonly ContentRegistry C = TestUtil.Content;
    private static readonly (string Bulk, string Plain)[] Pairs =
    {
        ("bulk_smelt_iron", "smelt_iron"), ("bulk_smelt_copper", "smelt_copper"), ("bulk_smelt_gold", "smelt_gold"),
        ("bulk_press_plate", "press_plate"), ("bulk_draw_wire", "draw_wire"), ("bulk_forge_steel", "forge_steel"),
    };
    private static readonly string[] Buildings = { "bulk_smelter", "bulk_press", "bulk_forge" };

    [Fact]
    public void Each_bulk_recipe_is_twice_the_plain_one_in_one_and_a_half_times_the_time()
    {
        foreach (var (bulk, plain) in Pairs)
        {
            var b = C.Recipes[bulk];
            var p = C.Recipes[plain];
            Assert.Equal(p.Ticks * 3, b.Ticks * 2);
            Assert.Equal(p.ValueMultiplier, b.ValueMultiplier);
            Assert.Equal(p.Inputs.Select(i => (i.Item, i.Count * 2)), b.Inputs.Select(i => (i.Item, i.Count)));
            Assert.Equal(p.Outputs.Select(o => (o.Item, o.Count * 2)), b.Outputs.Select(o => (o.Item, o.Count)));
        }
    }

    [Fact]
    public void The_machines_sit_at_the_electronics_tier_and_only_they_run_bulk_recipes()
    {
        foreach (var id in Buildings) Assert.Equal(4, C.Buildings[id].Tier);
        var bulkRecipes = Pairs.Select(p => p.Bulk).ToHashSet();
        foreach (var def in C.BuildingList.Where(d => d.Params is ProcessorParams))
        {
            var recipes = ((ProcessorParams)def.Params!).Recipes;
            bool bulkMachine = Buildings.Contains(def.Id);
            Assert.All(recipes, r => Assert.Equal(bulkMachine, bulkRecipes.Contains(r)));
        }
    }

    [Fact]
    public void Removing_the_drop_leaves_every_item_worth_the_same()
    {
        var pack = ContentRegistry.ParsePack(ContentRegistry.BasePackJson());
        Assert.Equal(Pairs.Length, pack.Recipes.RemoveAll(r => Pairs.Any(p => p.Bulk == r.Id)));
        Assert.Equal(Buildings.Length, pack.Buildings.RemoveAll(b => Buildings.Contains(b.Id)));
        var without = ContentRegistry.Build(BehaviorRegistry.CreateDefault(), new[] { pack });
        foreach (var (item, value) in without.ItemValue)
        {
            Assert.Equal(value.Tier, C.ItemValue[item].Tier);
            Assert.Equal(value.Value, C.ItemValue[item].Value, 6);
        }
    }

    [Fact]
    public void A_bulk_smelter_makes_a_third_more_than_a_smelter_from_the_same_ore()
    {
        long Made(string machine)
        {
            var sim = TestUtil.NewSim();
            sim.Place("iron_miner", 2, 0, 0, Dir.South);
            sim.Place("iron_miner", 1, 1, 0, Dir.East);
            sim.Place("iron_miner", 3, 1, 0, Dir.West);
            sim.Place(machine, 2, 1, 0, Dir.South);
            sim.Place("seller", 2, 2, 0, Dir.South);
            sim.Step(120 * Simulation.TicksPerSecond);
            return sim.World.Stats.Produced.GetValueOrDefault("iron_ingot");
        }
        long plain = Made("smelter"), bulk = Made("bulk_smelter");
        Assert.True(plain > 0);
        Assert.InRange(bulk / (double)plain, 1.2, 1.45);
    }
}
