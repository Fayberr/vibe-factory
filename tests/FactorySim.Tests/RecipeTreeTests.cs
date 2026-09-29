using FactorySim.Balance;
using FactorySim.Behaviors;
using FactorySim.Content;
using FactorySim.Persistence;

namespace FactorySim.Tests;

/// <summary>
/// Roadmap steps 2 and 3: value comes from the work in an item, the recipes form one connected tree
/// (shared parts, no dead ends), and a tier opens by delivering the goods the tier before makes.
/// </summary>
public class RecipeTreeTests
{
    private static readonly ContentRegistry C = TestUtil.Content;

    // ---- Value from processing ---------------------------------------------------------

    [Fact]
    public void Every_ore_is_worth_the_same_and_nothing_else_has_a_base_value()
    {
        var raws = C.Items.Values.Where(i => i.Raw).ToList();
        Assert.True(raws.Count >= 8);
        Assert.All(raws, i => Assert.Equal(1, i.BaseValue));
        Assert.All(C.Items.Values.Where(i => !i.Raw), i => Assert.Equal(0, i.BaseValue));
    }

    [Fact]
    public void Every_craft_adds_value_so_the_more_work_in_an_item_the_more_it_is_worth()
    {
        // Research packs are the exception: a pack sells for exactly its parts, so selling is never a reason to make one.
        bool MakesPacks(RecipeDef r) => r.Outputs.Any(o => C.Items[o.Item].Science);
        Assert.All(C.Recipes.Values.Where(r => !MakesPacks(r)), r => Assert.True(r.ValueMultiplier > 1, $"{r.Id} does not add value"));
        Assert.All(C.Recipes.Values.Where(MakesPacks), r => Assert.Equal(1, r.ValueMultiplier));

        var v = C.ItemValue;
        double Parts(string recipe) => C.Recipes[recipe].Inputs.Sum(i => v[i.Item].Value * i.Count);
        // Each finished good is worth more than everything that went into it.
        foreach (var id in new[] { "build_crate", "make_toy", "make_robot", "make_drone", "make_satellite" })
        {
            var r = C.Recipes[id];
            double made = r.Outputs.Sum(o => v[o.Item].Value * o.Count);
            Assert.True(made > Parts(id), $"{id}: {made} is not more than its parts, {Parts(id)}");
        }

        // A robot is a toy, motors and circuits, so it is worth more than all of them together.
        Assert.True(v["robot"].Value > v["toy"].Value + 2 * v["motor"].Value + 2 * v["circuit"].Value);
        Assert.True(v["robot"].Value > 1000 * v["iron_ore"].Value);
    }

    // ---- The connected recipe tree -----------------------------------------------------

    private static Dictionary<string, int> UsedIn()
    {
        var used = new Dictionary<string, int>();
        foreach (var r in C.Recipes.Values)
            foreach (var input in r.Inputs)
                used[input.Item] = used.GetValueOrDefault(input.Item) + 1;
        return used;
    }

    [Fact]
    public void Only_the_final_product_is_a_dead_end()
    {
        // Research packs end in a lab, not a recipe, so they are dead ends by design.
        var used = UsedIn();
        var deadEnds = C.Items.Keys.Where(id => !used.ContainsKey(id) && !C.Items[id].Science).ToList();
        Assert.Equal(new[] { "satellite" }, deadEnds);
    }

    [Fact]
    public void Old_dead_ends_became_ingredients()
    {
        Assert.Contains(C.Recipes.Values, r => r.Id == "make_toy" && r.Inputs.Any(i => i.Item == "crate"));   // crates pack toys
        Assert.Contains(C.Recipes.Values, r => r.Id == "make_robot" && r.Inputs.Any(i => i.Item == "toy"));   // toys are robot bodies
        Assert.Contains(C.Recipes.Values, r => r.Id == "make_satellite" && r.Inputs.Any(i => i.Item == "jewelry")); // jewelry is a part
    }

    [Theory]
    [InlineData("iron_plate", 3)]
    [InlineData("iron_rod", 2)]
    [InlineData("screw", 2)]
    [InlineData("gear", 2)]
    [InlineData("steel", 2)]
    [InlineData("copper_wire", 2)]
    [InlineData("plastic", 3)]
    [InlineData("glass", 2)]
    [InlineData("circuit", 2)]
    public void Shared_parts_feed_many_recipes(string part, int recipes)
    {
        Assert.True(UsedIn().GetValueOrDefault(part) >= recipes, $"{part} feeds {UsedIn().GetValueOrDefault(part)} recipes");
    }

    [Fact]
    public void Batteries_need_electronics_and_motors_need_batteries()
    {
        var ins = (string recipe) => C.Recipes[recipe].Inputs.Select(i => i.Item).ToHashSet();
        Assert.Contains("circuit", ins("make_battery"));
        Assert.Contains("battery", ins("make_motor"));
    }

    [Fact]
    public void Recipes_never_form_a_cycle()
    {
        var made = C.Recipes.Values.SelectMany(r => r.Outputs.Select(o => (o.Item, r))).ToLookup(x => x.Item, x => x.r);
        var state = new Dictionary<string, int>(); // 1 = on the path, 2 = done
        void Visit(string item, string path)
        {
            if (state.GetValueOrDefault(item) == 2) return;
            Assert.True(state.GetValueOrDefault(item) != 1, $"recipe cycle: {path} -> {item}");
            state[item] = 1;
            foreach (var r in made[item])
                foreach (var input in r.Inputs) Visit(input.Item, $"{path} -> {item}");
            state[item] = 2;
        }
        foreach (var id in C.Items.Keys) Visit(id, "");
    }

    [Fact]
    public void One_late_product_pulls_every_raw_resource_through_the_factory()
    {
        var book = RecipeBook.Create(C, new BalanceAssumptions { Level = 1 }, C.Tiers.Count - 1);
        var chain = ProductionChain.For(book, "satellite");
        var raws = C.Items.Values.Where(i => i.Raw).Select(i => i.Id).ToHashSet();
        Assert.Equal(raws, chain.RawPerSecond.Keys.ToHashSet());
        Assert.True(chain.Depth >= 8);
    }

    [Fact]
    public void Every_machine_can_hold_the_ingredients_of_all_its_recipes()
    {
        foreach (var b in C.BuildingList.Where(b => b.Params is ProcessorParams))
        {
            var p = (ProcessorParams)b.Params!;
            foreach (var id in p.Recipes)
                Assert.All(C.Recipes[id].Inputs, i => Assert.True(i.Count <= p.InputCapacity, $"{b.Id} cannot hold {i.Count} {i.Item} for {id}"));
        }
    }

    [Fact]
    public void A_recipe_that_needs_more_than_its_machine_holds_is_rejected()
    {
        var pack = ContentRegistry.ParsePack("""
            { "buildings": [ { "id": "tiny_shop", "behavior": "processor",
                "ports": [ { "kind": "in", "side": "back" }, { "kind": "out", "side": "front" } ],
                "params": { "recipes": [ "build_crate" ], "inputCapacity": 3 } } ] }
            """);
        var ex = Assert.Throws<ContentException>(() => ContentRegistry.LoadDefault(null, pack));
        Assert.Contains("input capacity", ex.Message);
    }

    // ---- Tiers open by delivering goods ------------------------------------------------

    [Fact]
    public void Tiers_from_industry_on_ask_for_goods_the_tier_before_makes()
    {
        Assert.Empty(C.Tiers[0].Deliver);
        Assert.Empty(C.Tiers[1].Deliver);
        for (int t = 2; t < C.Tiers.Count; t++)
        {
            Assert.NotEmpty(C.Tiers[t].Deliver);
            foreach (var need in C.Tiers[t].Deliver)
            {
                Assert.False(C.Items[need.Item].Raw, $"tier {t} asks for raw {need.Item}");
                Assert.True(C.ItemValue[need.Item].Tier < t, $"tier {t} asks for {need.Item}, which comes later");
            }
        }
    }

    private static Simulation SimAtTier1()
    {
        var sim = TestUtil.NewSim(sandbox: false, money: 1e9);
        sim.World.Stats.TotalEarned = 1000;
        Assert.True(sim.Execute(new UnlockTier()).Ok);
        return sim;
    }

    [Fact]
    public void A_tier_stays_locked_until_its_goods_are_sold_however_rich_you_are()
    {
        var sim = SimAtTier1();
        sim.World.Stats.TotalEarned = 1e9;

        var r = sim.Execute(new UnlockTier());
        Assert.False(r.Ok);
        Assert.Contains("Iron Plate", r.Error);
        Assert.Equal(1, sim.World.UnlockedTier);

        sim.World.Stats.Sold["iron_plate"] = 150;
        r = sim.Execute(new UnlockTier());
        Assert.False(r.Ok); // wire is still missing
        Assert.Contains("Copper Wire", r.Error);

        sim.World.Stats.Sold["copper_wire"] = 149;
        Assert.False(sim.Execute(new UnlockTier()).Ok);

        sim.World.Stats.Sold["copper_wire"] = 150;
        Assert.True(sim.Execute(new UnlockTier()).Ok);
        Assert.Equal(2, sim.World.UnlockedTier);
        Assert.Equal(150, sim.World.Stats.Sold["iron_plate"]); // delivered goods are counted, not spent
    }

    [Fact]
    public void Deliveries_are_lifetime_sales_and_survive_saving()
    {
        var sim = SimAtTier1();
        sim.World.Stats.TotalEarned = 1e9;
        sim.World.Stats.Sold["iron_plate"] = 500;
        sim.World.Stats.Sold["copper_wire"] = 500;

        var loaded = SaveSystem.Deserialize(SaveSystem.Serialize(sim), TestUtil.Content).Simulation;
        Assert.True(C.Tiers[2].DeliveriesMet(loaded.World.Stats));
        Assert.True(loaded.Execute(new UnlockTier()).Ok);
    }

    [Fact]
    public void Sandbox_ignores_deliveries()
    {
        var sim = TestUtil.NewSim(sandbox: true);
        sim.World.Stats.TotalEarned = 1e9;
        Assert.True(sim.Execute(new UnlockTier()).Ok);
        Assert.True(sim.Execute(new UnlockTier()).Ok); // tier 2 needs goods, sandbox does not care
        Assert.Equal(2, sim.World.UnlockedTier);
    }

    [Fact]
    public void Real_sales_count_toward_a_delivery()
    {
        var sim = TestUtil.NewSim();
        sim.Place("iron_miner", 0, 0, 0, Dir.East);
        sim.Place("smelter", 1, 0, 0, Dir.East);
        sim.Place("press", 2, 0, 0, Dir.East);
        sim.Place("seller", 3, 0, 0, Dir.East);
        sim.Step(20 * 30);
        long plates = sim.Sold("iron_plate");
        Assert.True(plates > 5);
        var need = C.Tiers[2].Deliver.Single(d => d.Item == "iron_plate");
        Assert.Equal(plates, TierDef.SoldOf(sim.World.Stats, need));
        Assert.False(C.Tiers[2].DeliveriesMet(sim.World.Stats)); // wire is missing
    }

    private static ContentPack TiersWith(int tier, params ItemAmount[] deliver) => new()
    {
        Tiers = C.Tiers.Select((t, i) => i != tier ? t : new TierDef
        {
            Name = t.Name, Description = t.Description, Cost = t.Cost, RequiredEarnings = t.RequiredEarnings, Deliver = deliver,
        }).ToList(),
    };

    [Fact]
    public void A_delivery_that_could_never_be_made_is_rejected()
    {
        var unknown = Assert.Throws<ContentException>(() => ContentRegistry.LoadDefault(null, TiersWith(3, new ItemAmount("unobtainium", 5))));
        Assert.Contains("unknown item", unknown.Message);

        var none = Assert.Throws<ContentException>(() => ContentRegistry.LoadDefault(null, TiersWith(3, new ItemAmount("steel", 0))));
        Assert.Contains("must be > 0", none.Message);

        // A robot cannot be made before Robotics, so Industry cannot ask for one: nobody could unlock it.
        var late = Assert.Throws<ContentException>(() => ContentRegistry.LoadDefault(null, TiersWith(2, new ItemAmount("robot", 1))));
        Assert.Contains("cannot be made before", late.Message);

        Assert.NotNull(ContentRegistry.LoadDefault(null, TiersWith(3, new ItemAmount("steel", 5)))); // a fair one loads
    }

    // ---- Old saves ---------------------------------------------------------------------

    [Fact]
    public void A_saved_choice_of_a_recipe_that_no_longer_exists_runs_automatically()
    {
        var sim = TestUtil.NewSim();
        sim.Place("iron_miner", 0, 0, 0, Dir.East);
        int smelter = sim.Place("smelter", 1, 0, 0, Dir.East);
        sim.Place("seller", 2, 0, 0, Dir.East);
        ((ProcessorState)sim.World.GetEntity(smelter)!.State).Chosen = "recipe_from_an_older_version";

        sim.Step(20 * 10);
        Assert.True(sim.Sold("iron_ingot") > 0);

        var loaded = SaveSystem.Deserialize(SaveSystem.Serialize(sim), TestUtil.Content).Simulation;
        loaded.Step(20 * 10);
        Assert.True(loaded.Sold("iron_ingot") > sim.Sold("iron_ingot"));
    }

    // ---- Pacing ------------------------------------------------------------------------

    [Fact]
    public void The_balance_estimate_counts_the_time_to_make_the_goods_a_tier_asks_for()
    {
        var estimates = TierPacing.Estimate(C, new BalanceAssumptions { Level = 1 });
        for (int t = 0; t < estimates.Count - 1; t++)
        {
            bool asks = C.Tiers[t + 1].Deliver.Length > 0;
            Assert.Equal(asks, estimates[t].DeliverySeconds > 0);
            Assert.True(estimates[t].Seconds >= estimates[t].DeliverySeconds); // it runs alongside earning, never faster
            Assert.False(double.IsInfinity(estimates[t].DeliverySeconds));
        }
    }
}
