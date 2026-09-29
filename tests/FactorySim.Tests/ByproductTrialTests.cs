using FactorySim.Behaviors;
using FactorySim.Content;
using FactorySim.Persistence;

namespace FactorySim.Tests;

/// <summary>
/// The byproduct trial content in base.json: an Oil Refinery can crack crude oil into plastic and tar,
/// and a Blast Furnace can burn tar instead of coal. Deleting the trial means deleting this file too
/// (see "Byproducts" in docs/ARCHITECTURE.md); the sorting and multi-output tests stay.
/// </summary>
public class ByproductTrialTests
{
    private static readonly ContentRegistry C = TestUtil.Content;

    /// <summary>
    /// base.json with the trial taken out, exactly as the removal steps in docs/ARCHITECTURE.md say:
    /// the tar item, the two recipes, and their ids in the two machines' recipe lists.
    /// </summary>
    internal static string WithoutTrial()
    {
        var lines = ContentRegistry.BasePackJson().Split('\n')
            .Where(l => !l.Contains("\"id\": \"tar\"") && !l.Contains("\"id\": \"crack_oil\"") && !l.Contains("\"id\": \"forge_steel_tar\""));
        string json = string.Join('\n', lines).Replace(", \"crack_oil\"", "").Replace(", \"forge_steel_tar\"", "");
        Assert.DoesNotContain("\"tar\"", json);
        Assert.DoesNotContain("crack_oil", json);
        Assert.DoesNotContain("forge_steel_tar", json);
        return json;
    }

    private static ContentRegistry Without() => ContentRegistry.Build(BehaviorRegistry.CreateDefault(), new[] { ContentRegistry.ParsePack(WithoutTrial()) });

    [Fact]
    public void The_trial_changes_no_existing_items_value_or_tier()
    {
        var before = Without().ItemValue;
        var after = C.ItemValue;
        Assert.Equal(before.Keys.Append("tar").OrderBy(k => k), after.Keys.OrderBy(k => k));
        foreach (var (item, info) in before) Assert.Equal(info, after[item]);
    }

    [Fact]
    public void Each_output_is_worth_what_the_plain_recipes_make()
    {
        // Two oil ($2) × 2 over 3 plastic + 1 tar = $1 a unit, like refined plastic; tar is worth the coal it replaces.
        Assert.Equal(new ItemValues.Info(1, 3), C.ItemValue["tar"]);
        Assert.Equal(C.ItemValue["plastic"].Value, 2 * 1.0 * C.Recipes["crack_oil"].ValueMultiplier / 4);
        Assert.Equal(C.ItemValue["coal"].Value, C.ItemValue["tar"].Value);
        Assert.True(C.Items["tar"].Byproduct);
        Assert.False(C.Items["tar"].Raw);
    }

    [Fact]
    public void Tar_is_used_so_the_satellite_stays_the_only_dead_end()
    {
        Assert.Contains(C.Recipes.Values, r => r.Inputs.Any(i => i.Item == "tar"));
    }

    private static (Simulation Sim, Entity Machine) Machine(string def, string? recipe, params (string Item, int Count, double Value)[] inputs)
    {
        var sim = TestUtil.NewSim();
        sim.Place(def, 0, 0, 0, Dir.East);
        var e = sim.World.EntityAt(GridPos.Zero)!;
        if (recipe != null) Assert.True(sim.Execute(new SelectRecipe(GridPos.Zero, recipe)).Ok);
        foreach (var (item, count, value) in inputs)
            ((ProcessorState)e.State).Inputs[item] = new InputBuffer { Count = count, ValueSum = count * value };
        return (sim, e);
    }

    [Fact]
    public void Cracking_makes_plastic_and_tar_at_a_dollar_each()
    {
        var (sim, refinery) = Machine("refinery", "crack_oil", ("crude_oil", 2, 1));
        sim.Step(60);

        var output = ((ProcessorState)refinery.State).Output;
        Assert.Equal(3, output.Where(o => o.Type == "plastic").Sum(o => o.Count));
        Assert.Equal(1, output.Where(o => o.Type == "tar").Sum(o => o.Count));
        Assert.All(output, o => Assert.Equal(1.0, o.UnitValue.ToDouble(), 9));
    }

    [Fact]
    public void A_refinery_on_automatic_never_cracks()
    {
        var (sim, refinery) = Machine("refinery", null, ("crude_oil", 8, 1));
        sim.Step(20 * 20);

        var state = (ProcessorState)refinery.State;
        Assert.DoesNotContain(state.Output, o => o.Type == "tar");
        Assert.Contains(state.Output, o => o.Type == "plastic");
    }

    [Fact]
    public void Steel_forged_with_tar_is_worth_the_same_as_steel_forged_with_coal()
    {
        // Automatic: with tar and no coal, the furnace burns tar.
        var (sim, furnace) = Machine("blast_furnace", null, ("iron_ingot", 1, 2), ("tar", 1, 1));
        sim.Step(40);

        var steel = Assert.Single(((ProcessorState)furnace.State).Output);
        Assert.Equal("steel", steel.Type);
        Assert.Equal(7.5, steel.UnitValue.ToDouble(), 9);
    }

    [Fact]
    public void A_cracking_line_sorts_plastic_one_way_and_tar_into_a_depot()
    {
        var sim = TestUtil.NewSim();
        sim.Place("refinery", 0, 2, 0, Dir.East);
        Assert.True(sim.Execute(new SelectRecipe(new GridPos(0, 2, 0), "crack_oil")).Ok);
        ((ProcessorState)sim.World.EntityAt(new GridPos(0, 2, 0))!.State).Inputs["crude_oil"] = new InputBuffer { Count = 8, ValueSum = 8 };
        sim.Place("conveyor", 1, 2, 0, Dir.East);
        sim.Place("splitter", 2, 2, 0, Dir.East);
        sim.Place("seller", 3, 2, 0, Dir.East);  // front: plastic
        sim.Place("seller", 2, 3, 0, Dir.South); // right: overflow, the lazy way out for tar
        Assert.True(sim.Execute(new SetFilter(new GridPos(2, 2, 0), 0, "plastic")).Ok);
        Assert.True(sim.Execute(new SetFilter(new GridPos(2, 2, 0), 2, RouterBehavior.Overflow)).Ok);
        sim.Step(20 * 30);

        var sales = sim.DrainEvents().OfType<ItemSold>().GroupBy(s => (sim.World.GetEntity(s.EntityId)!.Pos, s.Item))
            .ToDictionary(g => g.Key, g => g.Sum(s => s.Count));
        Assert.Equal(new Dictionary<(GridPos, string), long> { [(new GridPos(3, 2, 0), "plastic")] = 12, [(new GridPos(2, 3, 0), "tar")] = 4 }, sales);
    }

    [Fact]
    public void Without_the_trial_the_game_is_valid_and_keeps_its_one_dead_end()
    {
        var without = Without();
        Assert.False(without.Items.ContainsKey("tar"));
        var used = without.Recipes.Values.SelectMany(r => r.Inputs).Select(i => i.Item).ToHashSet();
        Assert.Equal(new[] { "satellite" }, without.Items.Keys.Where(id => !used.Contains(id)));
    }

    [Fact]
    public void A_save_from_the_trial_loads_without_it_and_keeps_every_building()
    {
        var sim = TestUtil.NewSim(goals: true);
        sim.Place("refinery", 0, 2, 0, Dir.East);
        Assert.True(sim.Execute(new SelectRecipe(new GridPos(0, 2, 0), "crack_oil")).Ok);
        ((ProcessorState)sim.World.EntityAt(new GridPos(0, 2, 0))!.State).Inputs["crude_oil"] = new InputBuffer { Count = 8, ValueSum = 8 };
        sim.Place("conveyor", 1, 2, 0, Dir.East);
        sim.Place("splitter", 2, 2, 0, Dir.East);
        sim.Place("conveyor", 3, 2, 0, Dir.East); // dead ends: items stay on the belts
        sim.Place("conveyor", 2, 3, 0, Dir.South);
        Assert.True(sim.Execute(new SetFilter(new GridPos(2, 2, 0), 2, "tar")).Ok);
        sim.Place("blast_furnace", 6, 6, 0, Dir.East);
        Assert.True(sim.Execute(new SelectRecipe(new GridPos(6, 6, 0), "forge_steel_tar")).Ok);
        sim.World.Contracts.Open.Add(new Contract { Id = 99, Item = "tar", Quantity = 10 });
        sim.Step(20 * 10);
        string json = SaveSystem.Serialize(sim);
        Assert.Contains("\"tar\"", json);

        var loaded = SaveSystem.Deserialize(json, Without());
        var world = loaded.Simulation.World;
        Assert.Equal(sim.World.EntityCount, world.EntityCount);
        Assert.DoesNotContain(loaded.Warnings, w => w.StartsWith("Dropped entity"));
        var hub = world.EntityAt(new GridPos(2, 2, 0))!;
        Assert.Null(hub.Behavior.Filters(hub)); // the tar filter is cleared, so the splitter is plain again
        Assert.Empty(world.Contracts.Open);

        // Machines whose chosen recipe is gone run automatically: given oil, the refinery refines plastic again.
        var refinery = (ProcessorState)world.EntityAt(new GridPos(0, 2, 0))!.State;
        refinery.Inputs["crude_oil"] = new InputBuffer { Count = 1, ValueSum = 1 };
        loaded.Simulation.Step(1);
        Assert.Equal("refine_plastic", refinery.Recipe);
        Assert.Equal("Automatic", Describe(world.EntityAt(new GridPos(6, 6, 0))!).First(l => l.Label == "Producing").Value);
        Assert.Equal("Automatic", Describe(world.EntityAt(new GridPos(0, 2, 0))!).First(l => l.Label == "Producing").Value);
    }

    private static List<View.InfoLine> Describe(Entity e)
    {
        var lines = new List<View.InfoLine>();
        e.Behavior.Describe(e, lines);
        return lines;
    }
}
