using FactorySim.Behaviors;
using FactorySim.Content;
using FactorySim.Editing;
using FactorySim.Persistence;

namespace FactorySim.Tests;

/// <summary>
/// Sorting a mixed belt: a splitter's outputs can each take one item type, anything, or the overflow
/// (what the others refuse). With nothing set it is the plain round-robin splitter. The mixed belt
/// comes from a test machine that makes a plate and a rod per craft, so these tests do not depend on
/// the trial byproduct content.
/// </summary>
public class SplitterFilterTests
{
    private const int Front = 0, Left = 1, Right = 2; // the splitter's outputs, in its def's order

    private static readonly ContentRegistry Content = ContentRegistry.LoadDefault(null, ContentRegistry.ParsePack("""
        {
          "recipes": [
            { "id": "test_plate_and_rod", "inputs": [ { "item": "iron_ore", "count": 1 } ],
              "outputs": [ { "item": "iron_plate", "count": 1 }, { "item": "iron_rod", "count": 1 } ], "ticks": 1, "valueMultiplier": 2 }
          ],
          "buildings": [
            { "id": "test_fast_drill", "name": "Fast Drill", "behavior": "miner",
              "ports": [ { "kind": "out", "side": "front" } ], "params": { "item": "iron_ore", "interval": 1, "amount": 1 } },
            { "id": "test_mixer", "name": "Mixer", "behavior": "processor",
              "ports": [ { "kind": "in", "side": "back" }, { "kind": "out", "side": "front" } ],
              "params": { "recipes": [ "test_plate_and_rod" ], "inputCapacity": 8, "outputCapacity": 8 } }
          ]
        }
        """));

    private static readonly GridPos Hub = new(3, 2, 0);

    /// <summary>
    /// A full belt alternating plates and rods into a splitter at (3,2) facing east. Depots on the front
    /// (4,2), left (3,1) and right (3,3) unless <paramref name="front"/>, <paramref name="left"/> or
    /// <paramref name="right"/> say otherwise ("belt" = a one-tile dead end that fills and blocks).
    /// </summary>
    private static Simulation Sorter(string? front = "seller", string? left = "seller", string? right = "seller")
    {
        var sim = TestUtil.NewSim(Content);
        sim.Place("test_fast_drill", 0, 2, 0, Dir.East);
        sim.Place("test_mixer", 1, 2, 0, Dir.East);
        sim.Place("conveyor", 2, 2, 0, Dir.East);
        sim.Place("splitter", Hub.X, Hub.Y, 0, Dir.East);
        void Out(string? what, int x, int y, Dir dir)
        {
            if (what == "belt") sim.Place("conveyor", x, y, 0, dir);
            else if (what != null) sim.Place(what, x, y, 0, dir);
        }
        Out(front, 4, 2, Dir.East);
        Out(left, 3, 1, Dir.North);
        Out(right, 3, 3, Dir.South);
        return sim;
    }

    private static void Filter(Simulation sim, int output, string? item)
    {
        var r = sim.Execute(new SetFilter(Hub, output, item));
        Assert.True(r.Ok, r.Error);
    }

    /// <summary>Units sold per depot position, per item.</summary>
    private static Dictionary<(GridPos, string), long> SalesBySpot(Simulation sim)
    {
        var sales = new Dictionary<(GridPos, string), long>();
        foreach (var s in sim.DrainEvents().OfType<ItemSold>())
        {
            var key = (sim.World.GetEntity(s.EntityId)!.Pos, s.Item);
            sales[key] = sales.GetValueOrDefault(key) + s.Count;
        }
        return sales;
    }

    private static readonly GridPos FrontDepot = new(4, 2, 0), LeftDepot = new(3, 1, 0), RightDepot = new(3, 3, 0);

    // ---- Unset: the plain splitter ------------------------------------------------------

    [Fact]
    public void Without_filters_a_mixed_belt_is_split_round_robin_as_before()
    {
        var sim = Sorter();
        sim.Step(200);
        sim.DrainEvents();
        sim.Step(2000);
        var sales = SalesBySpot(sim);

        // Every depot gets both items, and the three get equal shares of a full belt (4/s).
        foreach (var spot in new[] { FrontDepot, LeftDepot, RightDepot })
        {
            Assert.True(sales.GetValueOrDefault((spot, "iron_plate")) > 0, $"{spot} got no plates");
            Assert.True(sales.GetValueOrDefault((spot, "iron_rod")) > 0, $"{spot} got no rods");
        }
        var perSpot = sales.GroupBy(kv => kv.Key.Item1).Select(g => g.Sum(kv => kv.Value)).ToList();
        Assert.True(perSpot.Max() - perSpot.Min() <= 1, string.Join(",", perSpot));
        Assert.InRange(perSpot.Sum(), 395, 401);
    }

    [Fact]
    public void Clearing_every_filter_leaves_the_splitter_exactly_as_if_none_was_ever_set()
    {
        var plain = Sorter();
        var cleared = Sorter();
        Filter(cleared, Front, "iron_plate");
        Filter(cleared, Right, RouterBehavior.Overflow);
        Filter(cleared, Front, null);
        Filter(cleared, Right, null);
        var hub = cleared.World.EntityAt(Hub)!;
        Assert.Null(hub.Behavior.Filters(hub));

        plain.Step(1500);
        cleared.Step(1500);
        string json = SaveSystem.Serialize(cleared);
        Assert.Equal(SaveSystem.Serialize(plain), json);
        Assert.DoesNotContain("filters", json); // no filter, nothing new in the save
    }

    // ---- Sorting ------------------------------------------------------------------------

    [Fact]
    public void A_filter_sends_one_item_to_its_output_and_the_rest_share_the_others()
    {
        var sim = Sorter();
        Filter(sim, Front, "iron_plate");
        sim.Step(200);
        sim.DrainEvents();
        sim.Step(2000);
        var sales = SalesBySpot(sim);

        Assert.Equal(0, sales.GetValueOrDefault((FrontDepot, "iron_rod")));
        Assert.Equal(0, sales.GetValueOrDefault((LeftDepot, "iron_plate")));
        Assert.Equal(0, sales.GetValueOrDefault((RightDepot, "iron_plate")));
        long plates = sales.GetValueOrDefault((FrontDepot, "iron_plate"));
        long left = sales.GetValueOrDefault((LeftDepot, "iron_rod")), right = sales.GetValueOrDefault((RightDepot, "iron_rod"));
        Assert.InRange(plates, 197, 201);          // half of a 4/s belt
        Assert.InRange(Math.Abs(left - right), 0, 1); // the rods alternate between the open outputs
        Assert.InRange(plates + left + right, 395, 401); // sorting costs no throughput
    }

    [Fact]
    public void Two_filters_sort_a_mixed_belt_completely()
    {
        var sim = Sorter(right: null);
        Filter(sim, Front, "iron_rod");
        Filter(sim, Left, "iron_plate");
        sim.Step(2200);
        var sales = SalesBySpot(sim);

        Assert.Equal(new[] { (FrontDepot, "iron_rod"), (LeftDepot, "iron_plate") }.ToHashSet(), sales.Keys.ToHashSet());
    }

    [Fact]
    public void An_item_no_output_takes_waits_in_the_splitter_and_holds_up_the_belt()
    {
        var sim = Sorter(left: null, right: null);
        Filter(sim, Front, "iron_plate");
        sim.Step(400);
        long plates = sim.Sold("iron_plate");
        sim.Step(400);

        Assert.Equal(0, sim.Sold("iron_rod"));
        Assert.Equal(plates, sim.Sold("iron_plate")); // a rod sits at the centre, so nothing passes
        var state = (RouterState)sim.World.EntityAt(Hub)!.State;
        Assert.Contains(state.Items, it => it.Item.Type == "iron_rod");
    }

    [Fact]
    public void A_jammed_output_does_not_stall_items_bound_for_a_free_output()
    {
        // Front takes plates but its belt is a dead end, so it fills and refuses forever. Left takes
        // rods and has a seller, so it always has room. Once the front output is jammed, rods must
        // keep leaving for a while longer (held plates make room), even though the splitter will
        // eventually run out of room to hold plates and lock up too, same as it does today.
        var sim = Sorter(front: "belt", left: "seller", right: null);
        Filter(sim, Front, "iron_plate");
        Filter(sim, Left, "iron_rod");
        var state = (RouterState)sim.World.EntityAt(Hub)!.State;

        int tick = 0;
        while (state.Held.Count == 0 && tick < 1000) { sim.Step(1); tick++; }
        Assert.True(state.Held.Count > 0, "the front output never jammed; the test setup is wrong");
        long rodsAtFirstJam = sim.Sold("iron_rod");

        sim.Step(500);
        long rodsAfterHoldingFilled = sim.Sold("iron_rod");

        Assert.True(rodsAfterHoldingFilled > rodsAtFirstJam,
            "rods bound for the free left output stopped the moment the front output jammed, instead of keeping flowing while the splitter still had room to hold plates");
    }

    [Fact]
    public void A_splitter_with_held_items_saves_and_loads_identically()
    {
        Simulation Build()
        {
            var sim = Sorter(front: "belt", left: "seller", right: null);
            Filter(sim, Front, "iron_plate");
            Filter(sim, Left, "iron_rod");
            return sim;
        }
        var uninterrupted = Build();
        uninterrupted.Step(300);
        var state = (RouterState)uninterrupted.World.EntityAt(Hub)!.State;
        Assert.True(state.Held.Count > 0, "test setup did not jam the front output");
        uninterrupted.Step(200);

        var first = Build();
        first.Step(300);
        var loaded = SaveSystem.Deserialize(SaveSystem.Serialize(first), Content);
        Assert.Empty(loaded.Warnings);
        loaded.Simulation.Step(200);

        Assert.Equal(SaveSystem.Serialize(uninterrupted), SaveSystem.Serialize(loaded.Simulation));
    }

    [Fact]
    public void A_save_from_before_the_held_field_existed_loads_with_none_held()
    {
        var sim = Sorter(front: "belt", left: "seller", right: null);
        Filter(sim, Front, "iron_plate");
        Filter(sim, Left, "iron_rod");
        string json = SaveSystem.Serialize(sim).Replace("\"held\":[]", "").Replace(",,", ",");
        Assert.DoesNotContain("\"held\"", json);

        var loaded = SaveSystem.Deserialize(json, Content);
        Assert.Empty(loaded.Warnings);
        var hub = loaded.Simulation.World.EntityAt(Hub)!;
        Assert.Equal("splitter", hub.Def.Id);
        Assert.Empty(((RouterState)hub.State).Held);
    }

    // ---- Overflow -----------------------------------------------------------------------

    [Fact]
    public void Overflow_takes_what_no_other_output_is_set_to()
    {
        var sim = Sorter(left: null);
        Filter(sim, Front, "iron_plate");
        Filter(sim, Right, RouterBehavior.Overflow);
        sim.Step(200);
        sim.DrainEvents();
        sim.Step(2000);
        var sales = SalesBySpot(sim);

        // Plates keep flowing to the front; the rods, which nothing asks for, go to the overflow depot.
        Assert.Equal(new[] { (FrontDepot, "iron_plate"), (RightDepot, "iron_rod") }.ToHashSet(), sales.Keys.ToHashSet());
        Assert.InRange(sales.Values.Sum(), 395, 401);
    }

    [Fact]
    public void Overflow_takes_the_excess_when_the_wanted_output_backs_up()
    {
        var sim = Sorter(front: "belt", left: "seller");
        Filter(sim, Front, "iron_plate");
        Filter(sim, Left, "iron_rod");
        Filter(sim, Right, RouterBehavior.Overflow);
        sim.Step(200); // the front belt is a dead end: it fills with plates, then refuses
        sim.DrainEvents();
        sim.Step(2000);
        var sales = SalesBySpot(sim);

        // Rods still go left only; the plates the full front belt refuses spill into the overflow depot.
        Assert.Equal(new[] { (LeftDepot, "iron_rod"), (RightDepot, "iron_plate") }.ToHashSet(), sales.Keys.ToHashSet());
        Assert.InRange(sales.Values.Sum(), 395, 401);
        Assert.All(sim.Belt(4, 2, 0).Items, it => Assert.Equal("iron_plate", it.Item.Type));
    }

    [Fact]
    public void Overflow_gets_nothing_while_the_other_outputs_keep_up()
    {
        var sim = Sorter();
        Filter(sim, Front, "iron_plate");
        Filter(sim, Right, RouterBehavior.Overflow);
        sim.Step(2200);
        var sales = SalesBySpot(sim);

        Assert.DoesNotContain(sales.Keys, k => k.Item1 == RightDepot);
    }

    // ---- The command: validated, undoable, copied, saved ---------------------------------

    [Fact]
    public void Filters_are_validated()
    {
        var sim = Sorter();
        sim.Place("merger", 6, 6, 0, Dir.East);

        Assert.False(sim.Execute(new SetFilter(Hub, Front, "no_such_item")).Ok);
        Assert.False(sim.Execute(new SetFilter(Hub, 3, "iron_plate")).Ok);
        Assert.False(sim.Execute(new SetFilter(Hub, -1, "iron_plate")).Ok);
        Assert.False(sim.Execute(new SetFilter(new GridPos(6, 6, 0), 0, "iron_plate")).Ok); // one output: nothing to sort
        Assert.False(sim.Execute(new SetFilter(new GridPos(1, 2, 0), 0, "iron_plate")).Ok); // a machine does not sort
        Assert.False(sim.Execute(new SetFilter(new GridPos(9, 9, 0), 0, "iron_plate")).Ok);
        Assert.True(sim.Execute(new SetFilter(Hub, Front, "iron_plate")).Ok);
        Assert.Contains(sim.DrainEvents(), e => e is EntityFilterChanged { Output: Front, Filter: "iron_plate" });
    }

    [Fact]
    public void No_item_can_be_mistaken_for_the_overflow_rule()
    {
        var pack = ContentRegistry.ParsePack("""{ "items": [ { "id": "@overflow", "name": "Trick" } ] }""");
        var error = Assert.Throws<ContentException>(() => ContentRegistry.Build(BehaviorRegistry.CreateDefault(), new[] { pack }));
        Assert.Contains("start with '@'", error.Message);
    }

    [Fact]
    public void Setting_a_filter_is_undoable()
    {
        var sim = Sorter();
        var history = new EditHistory(sim);
        var hub = sim.World.EntityAt(Hub)!;

        Assert.True(history.Execute(new SetFilter(Hub, Front, "iron_plate")).Ok);
        Assert.True(history.Execute(new SetFilter(Hub, Front, "iron_rod")).Ok);
        Assert.True(history.Execute(new SetFilter(Hub, Right, RouterBehavior.Overflow)).Ok);
        Assert.Equal(new string?[] { "iron_rod", null, RouterBehavior.Overflow }, hub.Behavior.Filters(hub));

        history.Undo();
        Assert.Equal(new string?[] { "iron_rod", null, null }, hub.Behavior.Filters(hub));
        history.Undo();
        Assert.Equal(new string?[] { "iron_plate", null, null }, hub.Behavior.Filters(hub));
        history.Undo();
        Assert.Null(hub.Behavior.Filters(hub));
        history.Redo();
        Assert.Equal(new string?[] { "iron_plate", null, null }, hub.Behavior.Filters(hub));
    }

    [Fact]
    public void Removing_a_sorting_splitter_and_undoing_brings_its_filters_back()
    {
        var sim = Sorter();
        var history = new EditHistory(sim);
        Filter(sim, Left, "iron_rod");
        Assert.True(history.Execute(new RemoveBuilding(Hub)).Ok);
        history.Undo();

        var hub = sim.World.EntityAt(Hub)!;
        Assert.Equal(new string?[] { null, "iron_rod", null }, hub.Behavior.Filters(hub));
    }

    [Fact]
    public void Blueprints_keep_filters_and_they_turn_with_the_splitter()
    {
        var sim = Sorter();
        Filter(sim, Left, "iron_rod");
        Filter(sim, Right, RouterBehavior.Overflow);
        var hub = sim.World.EntityAt(Hub)!;
        var bp = Blueprint.FromJson(Blueprint.FromEntities(new[] { hub }, Hub).ToJson());

        var r = sim.Execute(new PlaceBlueprint(bp, new GridPos(10, 10, 0), QuarterTurns: 1));
        Assert.True(r.Ok, r.Error);
        var copy = sim.World.GetEntity(r.EntityId)!;
        Assert.Equal(Dir.South, copy.Facing);
        // Output numbers are the splitter's own, so its left output (now facing east) still takes rods.
        Assert.Equal(new string?[] { null, "iron_rod", RouterBehavior.Overflow }, copy.Behavior.Filters(copy));
    }

    [Fact]
    public void A_blueprint_filter_for_an_item_this_game_lacks_is_left_open()
    {
        var sim = Sorter();
        var bp = new Blueprint { Entries = { new BlueprintEntry("splitter", GridPos.Zero, Dir.East, Filters: new string?[] { "unobtainium", "iron_rod", null }) } };
        var r = sim.Execute(new PlaceBlueprint(bp, new GridPos(10, 10, 0)));
        Assert.True(r.Ok, r.Error);
        var copy = sim.World.GetEntity(r.EntityId)!;
        Assert.Equal(new string?[] { null, "iron_rod", null }, copy.Behavior.Filters(copy));
    }

    [Fact]
    public void Filters_survive_a_save_and_the_run_continues_identically()
    {
        Simulation Build()
        {
            var sim = Sorter(left: null);
            Filter(sim, Front, "iron_plate");
            Filter(sim, Right, RouterBehavior.Overflow);
            return sim;
        }
        var uninterrupted = Build();
        uninterrupted.Step(1500);

        var first = Build();
        first.Step(700);
        var loaded = SaveSystem.Deserialize(SaveSystem.Serialize(first), Content);
        Assert.Empty(loaded.Warnings);
        var hub = loaded.Simulation.World.EntityAt(Hub)!;
        Assert.Equal(new string?[] { "iron_plate", null, RouterBehavior.Overflow }, hub.Behavior.Filters(hub));
        loaded.Simulation.Step(800);

        Assert.Equal(SaveSystem.Serialize(uninterrupted), SaveSystem.Serialize(loaded.Simulation));
    }

    [Fact]
    public void Loading_clears_a_filter_for_an_item_that_no_longer_exists_and_keeps_the_splitter()
    {
        var sim = Sorter();
        Filter(sim, Front, "iron_plate");
        Filter(sim, Left, "iron_rod");
        string json = SaveSystem.Serialize(sim).Replace("\"iron_rod\"", "\"removed_item\"");

        var loaded = SaveSystem.Deserialize(json, Content);
        var hub = loaded.Simulation.World.EntityAt(Hub)!;
        Assert.Equal("splitter", hub.Def.Id);
        Assert.Equal(new string?[] { "iron_plate", null, null }, hub.Behavior.Filters(hub));
        Assert.Contains(loaded.Warnings, w => w.Contains("removed_item") && w.Contains("left filter"));
    }

    [Fact]
    public void The_inspector_lists_the_filters()
    {
        var sim = Sorter();
        Filter(sim, Front, "iron_plate");
        Filter(sim, Right, RouterBehavior.Overflow);
        var hub = sim.World.EntityAt(Hub)!;
        var lines = new List<View.InfoLine>();
        hub.Behavior.Describe(hub, lines);
        Assert.Contains(lines, l => l.Label == "Sorting" && l.Value == "Front: Iron Plate · Left: anything · Right: overflow");
    }
}
