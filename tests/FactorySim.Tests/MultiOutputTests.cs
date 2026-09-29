using FactorySim.Behaviors;
using FactorySim.Content;
using FactorySim.Persistence;

namespace FactorySim.Tests;

/// <summary>
/// A recipe with two outputs (a product and a byproduct). The processor already runs these: a craft's
/// value is split evenly over every unit it makes, and each output leaves as its own stack through
/// the one output port. These tests use their own machine, so they do not depend on the trial content.
/// </summary>
public class MultiOutputTests
{
    // One ingot ($2) makes a plate and two rods at ×3: $6 over three units, so $2 each.
    internal static readonly ContentRegistry Content = ContentRegistry.LoadDefault(null, ContentRegistry.ParsePack("""
        {
          "recipes": [
            { "id": "test_split_ingot", "inputs": [ { "item": "iron_ingot", "count": 1 } ],
              "outputs": [ { "item": "iron_plate", "count": 1 }, { "item": "iron_rod", "count": 2 } ], "ticks": 10, "valueMultiplier": 3 }
          ],
          "buildings": [
            { "id": "test_splitting_press", "name": "Splitting Press", "behavior": "processor",
              "ports": [ { "kind": "in", "side": "back" }, { "kind": "out", "side": "front" } ],
              "params": { "recipes": [ "test_split_ingot" ], "inputCapacity": 8, "outputCapacity": 4 } }
          ]
        }
        """));

    /// <summary>A press at the origin facing east, holding <paramref name="ingots"/> ingots worth $2 each, with nothing on its output.</summary>
    private static (Simulation Sim, Entity Press) Press(int ingots)
    {
        var sim = TestUtil.NewSim(Content);
        sim.Place("test_splitting_press", 0, 0, 0, Dir.East);
        var press = sim.World.EntityAt(GridPos.Zero)!;
        ((ProcessorState)press.State).Inputs["iron_ingot"] = new InputBuffer { Count = ingots, ValueSum = 2 * ingots };
        return (sim, press);
    }

    private static List<ItemStack> Output(Entity e) => ((ProcessorState)e.State).Output;

    [Fact]
    public void A_two_output_recipe_emits_both_items()
    {
        var (sim, press) = Press(1);
        sim.Step(20);

        var made = Output(press).GroupBy(o => o.Type).ToDictionary(g => g.Key, g => g.Sum(o => o.Count));
        Assert.Equal(new Dictionary<string, long> { ["iron_plate"] = 1, ["iron_rod"] = 2 }, made);
        Assert.Equal(1, sim.World.Stats.Produced["iron_plate"]);
        Assert.Equal(2, sim.World.Stats.Produced["iron_rod"]);
    }

    [Fact]
    public void The_crafts_value_is_split_evenly_over_every_output_unit()
    {
        var (sim, press) = Press(1);
        sim.Step(20);

        // (1 ingot × $2) × 3 / (1 plate + 2 rods) = $2 a unit, whatever the item.
        Assert.All(Output(press), o => Assert.Equal(2.0, o.UnitValue.ToDouble(), 9));
        Assert.Equal(6.0, Output(press).Sum(o => o.TotalValue.ToDouble()), 9);
    }

    [Fact]
    public void Reference_values_split_the_same_way()
    {
        // ItemValues splits the same way. The earliest tier that can make an item sets its reference
        // value, and this tier 0 machine beats the press (tier 1) and the Machine Shop (tier 2), so
        // both outputs take its $2. A byproduct recipe must therefore never make an existing item
        // earlier than it is made today, or it changes that item's value.
        Assert.Equal(new ItemValues.Info(2, 0), Content.ItemValue["iron_plate"]);
        Assert.Equal(new ItemValues.Info(2, 0), Content.ItemValue["iron_rod"]);
        Assert.Equal(new ItemValues.Info(4, 1), TestUtil.Content.ItemValue["iron_plate"]);
    }

    [Fact]
    public void The_output_buffer_counts_the_units_of_both_outputs()
    {
        var (sim, press) = Press(8);
        sim.Step(20 * 10);

        // Capacity 4: the first craft leaves 3 waiting (below 4), the second takes it to 6, then it pauses.
        var state = (ProcessorState)press.State;
        Assert.Equal(6, Output(press).Sum(o => o.Count));
        Assert.Equal(6, state.Inputs["iron_ingot"].Count);
        Assert.Contains(Output(press), o => o.Type == "iron_plate");
        Assert.Contains(Output(press), o => o.Type == "iron_rod");
    }

    [Fact]
    public void Both_outputs_leave_through_the_one_output_port_as_a_mixed_belt()
    {
        var (sim, _) = Press(8);
        sim.Line(new GridPos(1, 0, 0), Dir.East, 3);
        sim.Place("seller", 4, 0, 0, Dir.East);
        sim.Step(20 * 20);

        Assert.Equal(8, sim.Sold("iron_plate"));
        Assert.Equal(16, sim.Sold("iron_rod"));
    }

    [Fact]
    public void A_save_round_trip_keeps_both_outputs_and_continues_identically()
    {
        var (uninterrupted, _) = Press(8);
        uninterrupted.Step(40);

        var (first, press) = Press(8);
        first.Step(15);
        Assert.Equal(2, Output(press).Select(o => o.Type).Distinct().Count());
        var loaded = SaveSystem.Deserialize(SaveSystem.Serialize(first), Content);
        Assert.Empty(loaded.Warnings);

        var restored = loaded.Simulation.World.EntityAt(GridPos.Zero)!;
        Assert.Equal(Output(press).Select(o => (o.Type, o.Count, o.UnitValue)), Output(restored).Select(o => (o.Type, o.Count, o.UnitValue)));
        loaded.Simulation.Step(25);
        Assert.Equal(SaveSystem.Serialize(uninterrupted), SaveSystem.Serialize(loaded.Simulation));
    }

    [Fact]
    public void Status_and_inspector_name_every_output()
    {
        var (sim, press) = Press(1);
        sim.Step(1);

        Assert.Equal("making Iron Plate + Iron Rod", press.Behavior.GetStatus(press).Detail);
        var lines = new List<View.InfoLine>();
        press.Behavior.Describe(press, lines);
        Assert.Contains(lines, l => l.Label == "Recipe" && l.Value.Contains("1 Iron Plate + 2 Iron Rod"));
    }
}
