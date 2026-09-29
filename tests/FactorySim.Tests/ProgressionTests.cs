using FactorySim.Behaviors;
using FactorySim.Content;
using FactorySim.Editing;
using FactorySim.Persistence;

namespace FactorySim.Tests;

public class ProgressionTests
{
    // ---- Replacing by placing on top -------------------------------------------------

    [Fact]
    public void Polisher_dropped_on_a_belt_replaces_it_and_keeps_its_items()
    {
        var sim = TestUtil.NewSim(sandbox: false, money: 1000);
        sim.World.UnlockedTier = 1;
        sim.Place("iron_miner", 0, 0, 0, Dir.East);
        for (int x = 1; x <= 3; x++) sim.Place("conveyor", x, 0, 0, Dir.East);
        sim.Step(40); // an ore is on its way, the line has no sink yet
        var carrying = sim.World.Entities.First(e => e.Def.Id == "conveyor" && ((ConveyorState)e.State).Items.Count > 0);
        var moneyBefore = sim.World.Money;

        var r = sim.Execute(new PlaceBuilding("polisher", carrying.Pos, carrying.Facing, Replace: true));
        Assert.True(r.Ok, r.Error);
        var polisher = sim.World.EntityAt(carrying.Pos)!;
        Assert.Equal("polisher", polisher.Def.Id);
        Assert.Single(((ConveyorState)polisher.State).Items);                      // the ore rode along
        Assert.Equal((moneyBefore - 150 + 10).ToDouble(), sim.World.Money.ToDouble(), 6); // belt refunded
    }

    [Fact]
    public void Belts_never_replace_machines_but_machines_drop_into_belt_lines()
    {
        var sim = TestUtil.NewSim();
        sim.Place("smelter", 2, 0, 0, Dir.East);
        Assert.False(sim.Execute(new PlaceBuilding("conveyor", new GridPos(2, 0, 0), Dir.East, Replace: true)).Ok);

        sim.Place("conveyor", 5, 0, 0, Dir.East);
        Assert.True(sim.Execute(new PlaceBuilding("splitter", new GridPos(5, 0, 0), Dir.East, Replace: true)).Ok);
        Assert.False(sim.Execute(new PlaceBuilding("press", new GridPos(5, 0, 0), Dir.East, Replace: true)).Ok); // a hub is not a belt

        sim.Place("conveyor", 7, 0, 0, Dir.East);
        Assert.True(sim.Execute(new PlaceBuilding("press", new GridPos(7, 0, 0), Dir.East, Replace: true)).Ok); // machines drop into lines
        Assert.True(sim.Execute(new PlaceBuilding("sawmill", new GridPos(7, 0, 0), Dir.East, Replace: true)).Ok); // and swap
    }

    [Fact]
    public void Placing_without_replace_still_refuses_occupied_cells()
    {
        var sim = TestUtil.NewSim();
        sim.Place("conveyor", 1, 1, 0, Dir.East);
        Assert.False(sim.Execute(new PlaceBuilding("polisher", new GridPos(1, 1, 0), Dir.East)).Ok);
    }

    [Fact]
    public void Undo_of_a_replacement_restores_the_original_building()
    {
        var sim = TestUtil.NewSim();
        var history = new EditHistory(sim);
        sim.Place("conveyor", 1, 1, 0, Dir.North);
        history.Execute(new PlaceBuilding("polisher", new GridPos(1, 1, 0), Dir.North, Replace: true));
        Assert.Equal("polisher", sim.World.EntityAt(new GridPos(1, 1, 0))!.Def.Id);

        Assert.True(history.Undo().Ok);
        var back = sim.World.EntityAt(new GridPos(1, 1, 0))!;
        Assert.Equal("conveyor", back.Def.Id);
        Assert.Equal(Dir.North, back.Facing);
        Assert.True(history.Redo().Ok);
        Assert.Equal("polisher", sim.World.EntityAt(new GridPos(1, 1, 0))!.Def.Id);
    }

    [Fact]
    public void Ramp_placed_on_a_belt_and_under_a_bridge_end_replaces_both()
    {
        var sim = TestUtil.NewSim();
        sim.Place("conveyor", 3, 3, 0, Dir.East);
        sim.Place("conveyor", 3, 3, 1, Dir.East); // bridge end, one level up
        Assert.True(sim.Execute(new PlaceBuilding("ramp_down", new GridPos(3, 3, 0), Dir.East, Replace: true)).Ok);
        Assert.Equal(1, sim.World.EntityCount);
    }

    // ---- Building levels ---------------------------------------------------------------

    [Fact]
    public void Upgrading_a_drill_costs_money_and_speeds_it_up()
    {
        var sim = TestUtil.NewSim(sandbox: false, money: 10_000);
        sim.Place("iron_miner", 73, 0, 0, Dir.East);
        sim.Place("seller", 74, 0, 0, Dir.East); // depots stand on the edge of the map
        var drill = sim.World.EntityAt(new GridPos(73, 0, 0))!;
        var track = drill.Def.Upgrade!;

        var before = sim.World.Money;
        Assert.True(sim.Execute(new SetBuildingLevels(new[] { new LevelChange(drill.Pos, 3) })).Ok);
        var paid = before - sim.World.Money;
        Assert.Equal((track.UpgradeCost(drill.Def, 1) + track.UpgradeCost(drill.Def, 2)).ToDouble(), paid.ToDouble(), 6);
        Assert.Equal(2.0, drill.SpeedFactor, 9); // +50% per level

        sim.Step(20 * 10);
        Assert.InRange(sim.Sold("iron_ore"), 19, 20); // 2 per second
    }

    [Fact]
    public void Belts_cap_at_level_nine_where_they_reach_full_speed()
    {
        var sim = TestUtil.NewSim();
        sim.Place("conveyor", 0, 0, 0, Dir.East);
        Assert.True(sim.Execute(new SetBuildingLevels(new[] { new LevelChange(new GridPos(0, 0, 0), 9) })).Ok);
        Assert.False(sim.Execute(new SetBuildingLevels(new[] { new LevelChange(new GridPos(0, 0, 0), 10) })).Ok);
        Assert.Equal(5.0, sim.World.EntityAt(new GridPos(0, 0, 0))!.SpeedFactor, 9);
    }

    [Fact]
    public void Upgraded_belts_carry_more_items()
    {
        var sim = TestUtil.NewSim(TestUtil.FastContent);
        sim.Place("fast_miner", 0, 0, 0, Dir.East);
        sim.Line(new GridPos(1, 0, 0), Dir.East, 4);
        sim.Place("seller", 5, 0, 0, Dir.East);
        var cells = Enumerable.Range(1, 4).Select(x => new LevelChange(new GridPos(x, 0, 0), 9)).ToList();
        Assert.True(sim.Execute(new SetBuildingLevels(cells)).Ok);

        sim.Step(200);
        long before = sim.Sold("iron_ore");
        sim.Step(1000);
        Assert.InRange(sim.Sold("iron_ore") - before, 990, 1001); // spacing-limited: 1 per tick (vs 0.2 at level 1)
    }

    [Fact]
    public void Removing_refunds_upgrades_and_undo_downgrades()
    {
        var sim = TestUtil.NewSim(sandbox: false, money: 10_000);
        var history = new EditHistory(sim);
        var at = new GridPos(74, 0, 0); // on the edge of the map, where depots belong
        sim.Place("seller", at.X, at.Y, at.Z, Dir.East);
        var start = sim.World.Money;

        history.Execute(new SetBuildingLevels(new[] { new LevelChange(at, 4) }));
        Assert.True(sim.World.Money < start);
        history.Undo();
        Assert.Equal(1, sim.World.EntityAt(at)!.Level);
        Assert.Equal(start.ToDouble(), sim.World.Money.ToDouble(), 6);

        history.Redo();
        history.Execute(new RemoveBuildings(new[] { at }));
        var price = sim.Content.Buildings["seller"].Cost;
        Assert.Equal((start + price).ToDouble(), sim.World.Money.ToDouble(), 6); // price + all upgrades back
        history.Undo();
        Assert.Equal(4, sim.World.EntityAt(at)!.Level);
    }

    [Fact]
    public void Copies_keep_levels_and_cost_what_they_are_worth()
    {
        var sim = TestUtil.NewSim(sandbox: false, money: 10_000);
        sim.Place("iron_miner", 0, 0, 0, Dir.East);
        sim.Execute(new SetBuildingLevels(new[] { new LevelChange(new GridPos(0, 0, 0), 3) }));
        var bp = Blueprint.FromEntities(sim.World.Entities, GridPos.Zero);
        var before = sim.World.Money;

        Assert.True(sim.Execute(new PlaceBlueprint(bp, new GridPos(5, 5, 0))).Ok);
        Assert.Equal(3, sim.World.EntityAt(new GridPos(5, 5, 0))!.Level);
        Assert.Equal(bp.Cost(sim.Content).ToDouble(), (before - sim.World.Money).ToDouble(), 6);
    }

    // ---- Tiers -------------------------------------------------------------------------

    [Fact]
    public void Locked_buildings_cannot_be_placed_until_their_tier_is_unlocked()
    {
        var sim = TestUtil.NewSim(sandbox: false, money: 100_000);
        var r = sim.Execute(new PlaceBuilding("copper_miner", new GridPos(0, 0, 0), Dir.East));
        Assert.False(r.Ok);
        Assert.Contains("tier 1", r.Error);

        Assert.False(sim.Execute(new UnlockTier()).Ok); // needs lifetime earnings first
        sim.World.Stats.TotalEarned = 1000;
        var mapBefore = sim.World.Bounds;
        int plotsBefore = sim.World.Land.OwnedCount;
        Assert.True(sim.Execute(new UnlockTier()).Ok);
        Assert.Equal(1, sim.World.UnlockedTier);
        Assert.Equal(mapBefore, sim.World.Bounds);            // land is bought, not handed out by tiers
        Assert.Equal(plotsBefore, sim.World.Land.OwnedCount);
        Assert.True(sim.Execute(new PlaceBuilding("copper_miner", new GridPos(0, 0, 0), Dir.East)).Ok);
    }

    [Fact]
    public void Tiers_and_levels_survive_save_and_load()
    {
        var sim = TestUtil.NewSim(sandbox: false, money: 100_000);
        sim.World.Stats.TotalEarned = 1000;
        sim.Execute(new UnlockTier());
        sim.Place("copper_miner", 0, 0, 0, Dir.East);
        sim.Execute(new SetBuildingLevels(new[] { new LevelChange(new GridPos(0, 0, 0), 7) }));

        var loaded = SaveSystem.Deserialize(SaveSystem.Serialize(sim), TestUtil.Content).Simulation;
        Assert.Equal(1, loaded.World.UnlockedTier);
        Assert.Equal(7, loaded.World.EntityAt(new GridPos(0, 0, 0))!.Level);
        Assert.Equal(sim.World.Bounds, loaded.World.Bounds);
    }

    // ---- Content graph -------------------------------------------------------------------

    [Fact]
    public void Every_item_is_producible_and_every_machine_is_useful_when_unlocked()
    {
        var c = TestUtil.Content;
        // Earliest tier at which each item can be obtained.
        var available = new Dictionary<string, int>();
        foreach (var b in c.BuildingList)
            if (b.Params is MinerParams m) available[m.Item] = Math.Min(available.GetValueOrDefault(m.Item, int.MaxValue), b.Tier);

        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (var b in c.BuildingList)
            {
                if (b.Params is not ProcessorParams p) continue;
                foreach (var r in p.Recipes.Select(id => c.Recipes[id]))
                {
                    if (!r.Inputs.All(i => available.ContainsKey(i.Item))) continue;
                    int tier = Math.Max(b.Tier, r.Inputs.Max(i => available[i.Item]));
                    foreach (var o in r.Outputs)
                        if (tier < available.GetValueOrDefault(o.Item, int.MaxValue)) { available[o.Item] = tier; changed = true; }
                }
            }
        }

        foreach (var item in c.Items.Keys) Assert.True(available.ContainsKey(item), $"{item} can never be produced");

        // A freshly unlocked machine must have at least one recipe it can run right away.
        foreach (var b in c.BuildingList.Where(b => b.Params is ProcessorParams))
        {
            var recipes = ((ProcessorParams)b.Params!).Recipes.Select(id => c.Recipes[id]);
            Assert.True(recipes.Any(r => r.Inputs.All(i => available[i.Item] <= b.Tier)),
                $"{b.Id} (tier {b.Tier}) has no recipe whose inputs are available by then");
        }
        Assert.Equal(5, available["robot"]);
        Assert.True(c.Tiers.Count >= 6);
    }

    [Fact]
    public void The_best_product_of_each_tier_is_worth_at_least_double_the_last()
    {
        var c = TestUtil.Content;
        var value = c.ItemValue; // level-1 value of every item and the tier it becomes available
        double Best(int tier) => value.Values.Where(v => v.Tier <= tier).Max(v => v.Value);
        for (int t = 1; t < c.Tiers.Count; t++)
            Assert.True(Best(t) >= 2 * Best(t - 1), $"tier {t}: best {Best(t)} vs {Best(t - 1)} before");
        Assert.True(value["robot"].Value > 1000 * value["iron_ore"].Value);

        var sim = TestUtil.NewSim();
        sim.Place("oil_pump", 0, 0, 0, Dir.East);
        sim.Place("refinery", 1, 0, 0, Dir.East);
        sim.Place("seller", 2, 0, 0, Dir.East);
        sim.Step(20 * 20);
        Assert.True(sim.Sold("plastic") >= 10); // tier-3 chain runs end to end
    }

    // ---- Limits and the ground ---------------------------------------------------------

    [Fact]
    public void Drills_and_depots_are_limited_and_later_tiers_raise_the_limit()
    {
        var sim = TestUtil.NewSim(sandbox: false, money: 100_000);
        var drill = sim.Content.Buildings["iron_miner"];
        int max = sim.World.LimitOf(drill)!.Value;
        for (int i = 0; i < max; i++) sim.Place("iron_miner", i * 2, 0, 0, Dir.South);

        var r = sim.Execute(new PlaceBuilding("iron_miner", new GridPos(0, 5, 0), Dir.South));
        Assert.False(r.Ok);
        Assert.Contains("limit", r.Error);
        var copy = Blueprint.FromEntities(sim.World.Entities.Take(1), GridPos.Zero);
        Assert.False(sim.Execute(new PlaceBlueprint(copy, new GridPos(0, 5, 0))).Ok); // pasting counts too

        sim.World.Stats.TotalEarned = 1e6;
        Assert.True(sim.Execute(new UnlockTier()).Ok);
        Assert.Equal(max + 2, sim.World.LimitOf(drill));
        Assert.True(sim.Execute(new PlaceBuilding("iron_miner", new GridPos(0, 5, 0), Dir.South)).Ok);

        Assert.Null(sim.World.LimitOf(sim.Content.Buildings["conveyor"])); // belts are unlimited
        Assert.NotNull(sim.World.LimitOf(sim.Content.Buildings["seller"]));
    }

    [Fact]
    public void Nothing_can_be_built_below_the_ground_plate()
    {
        var sim = TestUtil.NewSim();
        var r = sim.Execute(new PlaceBuilding("conveyor", new GridPos(3, 3, -1), Dir.East));
        Assert.False(r.Ok);
        Assert.Contains("below the ground", r.Error);

        // A ramp down placed on the ground spans ground + one level: it lands on the ground.
        sim.Place("ramp_down", 3, 3, 0, Dir.East);
        var ramp = sim.World.EntityAt(new GridPos(3, 3, 1))!;
        Assert.Equal(0, ramp.Pos.Z);
    }
}

public class RecipeChoiceTests
{
    /// <summary>Iron drill → smelter → belt → press at (3,0).</summary>
    private static Simulation IronIntoPress()
    {
        var sim = TestUtil.NewSim();
        sim.Place("iron_miner", 0, 0, 0, Dir.East);
        sim.Place("smelter", 1, 0, 0, Dir.East);
        sim.Place("conveyor", 2, 0, 0, Dir.East);
        sim.Place("press", 3, 0, 0, Dir.East);
        sim.Place("seller", 4, 0, 0, Dir.East);
        return sim;
    }

    [Fact]
    public void A_machine_set_to_one_recipe_only_takes_its_ingredients()
    {
        var sim = IronIntoPress();
        Assert.True(sim.Execute(new SelectRecipe(new GridPos(3, 0, 0), "draw_wire")).Ok);
        sim.Step(20 * 10);
        Assert.Equal(0, sim.Sold("iron_plate"));                     // ingots are refused...
        Assert.NotEmpty(sim.Belt(2, 0, 0).Items);                    // ...and wait on the belt

        Assert.True(sim.Execute(new SelectRecipe(new GridPos(3, 0, 0), null)).Ok); // back to automatic
        sim.Step(20 * 10);
        Assert.True(sim.Sold("iron_plate") > 0);
    }

    [Fact]
    public void Choosing_a_recipe_drops_ingredients_it_cannot_use()
    {
        var sim = IronIntoPress();
        sim.Step(20 * 5);
        var press = sim.World.EntityAt(new GridPos(3, 0, 0))!;
        var state = (ProcessorState)press.State;
        state.Output.Clear();
        sim.Execute(new RemoveBuildings(new[] { new GridPos(4, 0, 0) })); // back up the press
        sim.Step(20 * 5);
        Assert.True(state.Inputs["iron_ingot"].Count > 0);

        Assert.True(sim.Execute(new SelectRecipe(press.Pos, "draw_wire")).Ok);
        Assert.Equal(0, state.Inputs["iron_ingot"].Count);
    }

    [Fact]
    public void Recipe_choice_is_undoable_copied_and_saved()
    {
        var sim = IronIntoPress();
        var history = new EditHistory(sim);
        var cell = new GridPos(3, 0, 0);
        var press = sim.World.EntityAt(cell)!;

        Assert.False(sim.Execute(new SelectRecipe(cell, "smelt_iron")).Ok); // not a press recipe
        history.Execute(new SelectRecipe(cell, "draw_wire"));
        Assert.Equal("draw_wire", press.Behavior.Selection(press));
        history.Undo();
        Assert.Null(press.Behavior.Selection(press));
        history.Redo();

        var copy = Blueprint.FromEntities(new[] { press }, press.Pos);
        Assert.True(sim.Execute(new PlaceBlueprint(copy, new GridPos(3, 5, 0))).Ok);
        var pasted = sim.World.EntityAt(new GridPos(3, 5, 0))!;
        Assert.Equal("draw_wire", pasted.Behavior.Selection(pasted));

        var loaded = SaveSystem.Deserialize(SaveSystem.Serialize(sim), TestUtil.Content).Simulation;
        var again = loaded.World.EntityAt(cell)!;
        Assert.Equal("draw_wire", again.Behavior.Selection(again));
    }

    [Fact]
    public void Buildings_without_choices_refuse_one()
    {
        var sim = TestUtil.NewSim();
        sim.Place("conveyor", 0, 0, 0, Dir.East);
        Assert.False(sim.Execute(new SelectRecipe(new GridPos(0, 0, 0), "draw_wire")).Ok);
        Assert.True(sim.Execute(new SelectRecipe(new GridPos(0, 0, 0), null)).Ok); // nothing to change
    }

    [Fact]
    public void Reference_values_follow_the_recipes()
    {
        var v = TestUtil.Content.ItemValue;
        Assert.Equal(2, v["iron_ingot"].Value, 9);                // ore 1 × smelt 2
        Assert.Equal(2, v["copper_wire"].Value, 9);               // ore 1 × 2 × 2 / 2 wires
        Assert.Equal(4, v["iron_plate"].Value, 9);                // ingot 2 × 2
        // crate = (4 planks × 0.9 + 2 plates × 4 + 4 screws × 0.9) × 2.6
        Assert.Equal((4 * 0.9 + 2 * 4 + 4 * 0.9) * 2.6, v["crate"].Value, 9);
        Assert.Equal(0, v["iron_ore"].Tier);
        Assert.Equal(5, v["robot"].Tier);
    }
}
