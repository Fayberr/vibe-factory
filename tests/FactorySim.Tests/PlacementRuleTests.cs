using FactorySim.Content;
using FactorySim.Persistence;

namespace FactorySim.Tests;

/// <summary>
/// Depots belong on the outer border of the whole map, taking items in from the inside, so goods
/// leave at the rim and the belts that feed them become the factory's arteries. The rule is
/// checked when something is placed, turned or moved, never when a save is loaded: a building the
/// player already paid for is not thrown away because a rule changed.
///
/// The map is 5 x 5 plots of 15 x 15 cells (0..74 both ways) and the player starts on the bottom
/// middle plot (x 30..44, y 60..74), which touches the south border and nothing else.
/// </summary>
public class PlacementRuleTests
{
    private const int Last = 74;     // the outermost cell row/column of the map
    private const int StartX = 35;   // a cell in the middle of the starting plot's south edge

    private static Simulation Sim(bool sandbox = false) => TestUtil.NewSim(sandbox: sandbox, money: 1e9, allLand: false);

    private static CommandResult TryPlace(Simulation sim, int x, int y, Dir facing, string def = "seller") =>
        sim.Execute(new PlaceBuilding(def, new GridPos(x, y, 0), facing));

    [Fact]
    public void A_depot_on_the_map_border_facing_outwards_is_allowed()
    {
        var sim = Sim();
        var depot = TestUtil.Content.Building("seller");

        // The rule itself, on all four borders and on a corner (checked directly: it says where a
        // depot may stand on the map, not which land is yours).
        Assert.True(sim.World.CheckPlacement(depot, new GridPos(Last, 0, 0), Dir.East).Ok);   // fed from the west, faces east
        Assert.True(sim.World.CheckPlacement(depot, new GridPos(5, 0, 0), Dir.North).Ok);     // north border
        Assert.True(sim.World.CheckPlacement(depot, new GridPos(5, Last, 0), Dir.South).Ok);  // south border
        Assert.True(sim.World.CheckPlacement(depot, new GridPos(0, 5, 0), Dir.West).Ok);      // west border
        Assert.True(sim.World.CheckPlacement(depot, new GridPos(Last, Last, 0), Dir.East).Ok);

        // And a real placement on the south border of the starting plot goes through.
        Assert.True(TryPlace(sim, StartX, Last, Dir.South).Ok);
        Assert.Equal(1, sim.World.CountOf("seller"));
    }

    [Fact]
    public void A_depot_anywhere_else_is_refused()
    {
        var sim = Sim();
        var inland = TryPlace(sim, StartX, 66, Dir.South);
        Assert.False(inland.Ok);
        Assert.Contains("edge of the map", inland.Error);

        // On the border, but the wrong way round: its input would face the map edge, so the belt
        // would have to come from off the map.
        var backwards = TryPlace(sim, StartX, Last, Dir.North);
        Assert.False(backwards.Ok);
        Assert.Contains("edge of the map", backwards.Error);

        // One cell in from the border is already inland.
        Assert.False(TryPlace(sim, StartX, Last - 1, Dir.South).Ok);
        Assert.Equal(0, sim.World.CountOf("seller"));
    }

    [Fact]
    public void The_edge_of_your_land_is_not_the_edge_of_the_map()
    {
        var sim = Sim();
        // The starting plot's west and east sides and its north side are only the border of what
        // you own; the depot rule looks at the border of the whole map.
        Assert.False(TryPlace(sim, 30, 66, Dir.West).Ok);
        Assert.False(TryPlace(sim, 44, 66, Dir.East).Ok);
        Assert.False(TryPlace(sim, StartX, 60, Dir.North).Ok);
    }

    [Fact]
    public void A_depot_on_a_plot_you_do_not_own_waits_until_you_buy_it()
    {
        var sim = Sim();
        // The south-west plot of the bottom row is on the map border but two plots from the start.
        var far = TryPlace(sim, 5, Last, Dir.South);
        Assert.False(far.Ok);
        Assert.Contains("not yours yet", far.Error);

        // The plot next to the start is one step away: buy it and its rim is yours to use.
        var next = TryPlace(sim, 20, Last, Dir.South);
        Assert.False(next.Ok);
        Assert.True(sim.Execute(new BuyPlot(1, 4)).Ok);
        Assert.True(TryPlace(sim, 20, Last, Dir.South).Ok);
    }

    [Fact]
    public void Every_depot_type_obeys_the_rule()
    {
        var sim = TestUtil.NewSim(sandbox: false, money: 1e9, allLand: false);
        sim.World.UnlockedTier = 3;
        Assert.True(TryPlace(sim, StartX, Last, Dir.South, "export_terminal").Ok);
        Assert.False(TryPlace(sim, StartX, 66, Dir.South, "export_terminal").Ok);
    }

    [Fact]
    public void A_depot_is_turned_but_not_turned_badly()
    {
        var sim = Sim();
        Assert.True(TryPlace(sim, StartX, Last, Dir.South).Ok);
        var at = new GridPos(StartX, Last, 0);

        // Turning it to face west would point its input at the map edge.
        Assert.False(sim.Execute(new RotateBuilding(at)).Ok);
        Assert.Equal(Dir.South, sim.World.EntityAt(at)!.Facing);
    }

    [Fact]
    public void A_depot_can_be_moved_along_the_border_but_not_inland()
    {
        var sim = Sim();
        Assert.True(TryPlace(sim, StartX, Last, Dir.South).Ok);
        var at = new GridPos(StartX, Last, 0);

        Assert.True(sim.Execute(new MoveBuildings(new[] { at }, new GridPos(2, 0, 0))).Ok);
        var moved = new GridPos(StartX + 2, Last, 0);
        Assert.Equal(moved, sim.World.EntityAt(moved)!.Pos);

        Assert.False(sim.Execute(new MoveBuildings(new[] { moved }, new GridPos(0, -5, 0))).Ok);
        Assert.NotNull(sim.World.EntityAt(moved));
    }

    [Fact]
    public void A_depot_that_already_breaks_the_rule_can_still_be_rearranged()
    {
        // Sandbox is free building, so a depot may stand anywhere in it. When it comes back to
        // normal play it is not stuck: it keeps working and can still be turned and moved.
        var sim = Sim(sandbox: true);
        Assert.True(TryPlace(sim, StartX, 66, Dir.East).Ok);
        var at = new GridPos(StartX, 66, 0);
        sim.World.Sandbox = false;

        Assert.True(sim.Execute(new RotateBuilding(at)).Ok);
        Assert.Equal(Dir.South, sim.World.EntityAt(at)!.Facing);
        Assert.True(sim.Execute(new MoveBuildings(new[] { at }, new GridPos(-2, 0, 0))).Ok);
    }

    [Fact]
    public void Sandbox_places_depots_anywhere()
    {
        var sim = Sim(sandbox: true);
        Assert.True(TryPlace(sim, 10, 10, Dir.East).Ok);
    }

    [Fact]
    public void An_inland_depot_survives_a_save_and_load()
    {
        var sim = Sim(sandbox: true);
        sim.Place("iron_miner", 8, 10, 0, Dir.East);
        sim.Place("seller", 9, 10, 0, Dir.East);
        sim.World.Sandbox = false; // the depot is now against the rules, and on land not bought, and stays anyway

        var loaded = SaveSystem.Deserialize(SaveSystem.Serialize(sim), TestUtil.Content);
        Assert.Empty(loaded.Warnings);
        Assert.NotNull(loaded.Simulation.World.EntityAt(new GridPos(9, 10, 0)));

        loaded.Simulation.Step(20 * 20);
        Assert.True(loaded.Simulation.Sold("iron_ore") > 0);
    }

    [Fact]
    public void An_unknown_placement_rule_is_rejected_at_load()
    {
        var ex = Assert.Throws<ContentException>(() => ContentRegistry.LoadDefault(null, new ContentPack
        {
            Buildings =
            {
                new BuildingDef { Id = "odd_depot", Name = "Odd Depot", Behavior = "seller", Placement = "middle" },
            },
        }));
        Assert.Contains("unknown placement rule", ex.Message);
    }

    [Fact]
    public void The_example_factory_obeys_the_rules()
    {
        // Built with money instead of sandbox, so the placement rules and the land apply: the
        // reference factory has to be a legal factory that fits the starting plot, because the
        // New game menu offers it to players who own nothing else.
        var sim = TestUtil.NewSim(sandbox: false, money: 1e9, allLand: false);
        Samples.DemoLayout.Build(sim, free: false);
        Assert.Equal(3, sim.World.CountOf("seller"));
        Assert.All(sim.World.Entities, e =>
        {
            Assert.True(sim.World.CheckPlacement(e.Def, e.Pos, e.Facing).Ok, $"{e.Def.Name} at {e.Pos} breaks its placement rule");
            Assert.All(e.Cells(), c => Assert.True(sim.World.Land.Owns(c.X, c.Y), $"{e.Def.Name} at {c} is off the starting plot"));
        });
        Assert.All(sim.World.Entities.Where(e => e.Def.Id == "seller"), e => Assert.Equal(sim.World.Bounds.Max.Y, e.Pos.Y));
    }
}
