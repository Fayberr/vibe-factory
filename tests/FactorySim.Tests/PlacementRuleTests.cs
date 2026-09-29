using FactorySim.Content;
using FactorySim.Persistence;

namespace FactorySim.Tests;

/// <summary>
/// Depots belong on the edge of the map, taking items in from the inside, so goods leave at the
/// rim and the belts that feed them become the factory's arteries. The rule is checked when
/// something is placed, turned or moved, never when a save is loaded: a building the player
/// already paid for is not thrown away because a rule changed.
/// </summary>
public class PlacementRuleTests
{
    // The starting plot is 24 x 24: cells 0..23, so the edges are x = 0 / 23 and y = 0 / 23.
    private const int Edge = 23;

    private static Simulation Sim(bool sandbox = false) => TestUtil.NewSim(sandbox: sandbox, money: 10_000);

    private static CommandResult TryPlace(Simulation sim, int x, int y, Dir facing, string def = "seller") =>
        sim.Execute(new PlaceBuilding(def, new GridPos(x, y, 0), facing));

    [Fact]
    public void A_depot_on_the_edge_facing_inwards_is_allowed()
    {
        var sim = Sim();
        var depot = TestUtil.Content.Building("seller");

        // The rule itself, on all four rims and on a corner (checked directly: only two depots
        // exist at this tier, which the build limit enforces separately).
        Assert.True(sim.World.CheckPlacement(depot, new GridPos(Edge, 0, 0), Dir.East).Ok);   // fed from the west, faces east
        Assert.True(sim.World.CheckPlacement(depot, new GridPos(5, 0, 0), Dir.North).Ok);     // north rim
        Assert.True(sim.World.CheckPlacement(depot, new GridPos(5, Edge, 0), Dir.South).Ok);  // south rim
        Assert.True(sim.World.CheckPlacement(depot, new GridPos(0, 5, 0), Dir.West).Ok);      // west rim
        Assert.True(sim.World.CheckPlacement(depot, new GridPos(Edge, Edge, 0), Dir.East).Ok);

        // And a real placement on the rim goes through.
        Assert.True(TryPlace(sim, Edge, 0, Dir.East).Ok);
        Assert.Equal(1, sim.World.CountOf("seller"));
    }

    [Fact]
    public void A_depot_anywhere_else_is_refused()
    {
        var sim = Sim();
        var inland = TryPlace(sim, 10, 10, Dir.East);
        Assert.False(inland.Ok);
        Assert.Contains("edge of the map", inland.Error);

        // On the rim, but the wrong way round: its input would face the map edge, so the belt
        // would have to come from off the plot.
        var backwards = TryPlace(sim, Edge, 0, Dir.West);
        Assert.False(backwards.Ok);
        Assert.Contains("edge of the map", backwards.Error);

        // One cell in from the rim is already inland.
        Assert.False(TryPlace(sim, Edge - 1, 0, Dir.East).Ok);
        Assert.Equal(0, sim.World.CountOf("seller"));
    }

    [Fact]
    public void Every_depot_type_obeys_the_rule()
    {
        var sim = TestUtil.NewSim(sandbox: false, money: 1e9);
        sim.World.UnlockedTier = 3;
        sim.GrowPlot(3);
        int edge = sim.World.Bounds.Max.X; // 44 wide at tier 3
        Assert.True(TryPlace(sim, edge, 4, Dir.East, "export_terminal").Ok);
        Assert.False(TryPlace(sim, 10, 4, Dir.East, "export_terminal").Ok);
    }

    [Fact]
    public void A_depot_is_turned_but_not_turned_badly()
    {
        var sim = Sim();
        Assert.True(TryPlace(sim, Edge, 0, Dir.East).Ok);
        var at = new GridPos(Edge, 0, 0);

        // Turning it to face west would point its input at the map edge.
        Assert.False(sim.Execute(new RotateBuilding(at)).Ok);
        Assert.Equal(Dir.East, sim.World.EntityAt(at)!.Facing);
    }

    [Fact]
    public void A_depot_can_be_moved_along_the_edge_but_not_inland()
    {
        var sim = Sim();
        Assert.True(TryPlace(sim, Edge, 10, Dir.East).Ok);
        var at = new GridPos(Edge, 10, 0);

        Assert.True(sim.Execute(new MoveBuildings(new[] { at }, new GridPos(0, 2, 0))).Ok);
        var moved = new GridPos(Edge, 12, 0);
        Assert.Equal(moved, sim.World.EntityAt(moved)!.Pos);

        Assert.False(sim.Execute(new MoveBuildings(new[] { moved }, new GridPos(-5, 0, 0))).Ok);
        Assert.NotNull(sim.World.EntityAt(moved));
    }

    [Fact]
    public void A_depot_that_already_breaks_the_rule_can_still_be_rearranged()
    {
        // Sandbox is free building, so a depot may stand anywhere in it. When it comes back to
        // normal play it is not stuck: it keeps working and can still be turned and moved.
        var sim = Sim(sandbox: true);
        Assert.True(TryPlace(sim, 10, 10, Dir.East).Ok);
        var at = new GridPos(10, 10, 0);
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
        sim.World.Sandbox = false; // the depot is now against the rules, and stays anyway

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
        // Built with money instead of sandbox, so the placement rules apply: the reference factory
        // has to be a legal factory, because the New game menu offers it to players.
        foreach (var origin in new[] { GridPos.Zero, new GridPos(8, 8, 0) })
        {
            var sim = TestUtil.NewSim(sandbox: false, money: 1e9);
            Samples.DemoLayout.Build(sim, origin, free: false);
            Assert.Equal(3, sim.World.CountOf("seller"));
            Assert.All(sim.World.Entities, e => Assert.True(sim.World.CheckPlacement(e.Def, e.Pos, e.Facing).Ok,
                $"{e.Def.Name} at {e.Pos} breaks its placement rule (origin {origin})"));
        }
    }
}
