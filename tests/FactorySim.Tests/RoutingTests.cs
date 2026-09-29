using FactorySim.Behaviors;
using FactorySim.Editing;

namespace FactorySim.Tests;

public class RoutingTests
{
    private static int Turns(IReadOnlyList<GridPos> cells)
    {
        int turns = 0;
        for (int i = 2; i < cells.Count; i++)
            if (BuildPlanner.Toward(cells[i - 2], cells[i - 1]) != BuildPlanner.Toward(cells[i - 1], cells[i])) turns++;
        return turns;
    }

    private static void AssertConnected(IReadOnlyList<GridPos> cells)
    {
        for (int i = 1; i < cells.Count; i++)
            Assert.Equal(1, Math.Abs(cells[i].X - cells[i - 1].X) + Math.Abs(cells[i].Y - cells[i - 1].Y));
    }

    private static void Wall(Simulation sim, int x, int y0, int y1)
    {
        for (int y = y0; y <= y1; y++) sim.Place("smelter", x, y, 0, Dir.West);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void On_open_ground_the_route_is_an_L_with_the_preferred_first_leg(bool firstLegX)
    {
        var sim = TestUtil.NewSim();
        var route = new BuildPlanner(sim).Route(new GridPos(2, 2, 0), new GridPos(8, 6, 0), firstLegX)!;
        Assert.Equal(BuildPlanner.LPath(new GridPos(2, 2, 0), new GridPos(8, 6, 0), firstLegX), route);
    }

    [Fact]
    public void A_belt_goes_around_a_wall_with_as_many_turns_as_it_takes()
    {
        var sim = TestUtil.NewSim();
        Wall(sim, 5, 2, 8);
        var route = new BuildPlanner(sim).Route(new GridPos(1, 5, 0), new GridPos(9, 5, 0), firstLegX: true)!;

        AssertConnected(route);
        Assert.Equal(new GridPos(1, 5, 0), route[0]);
        Assert.Equal(new GridPos(9, 5, 0), route[^1]);
        Assert.All(route, c => Assert.Null(sim.World.EntityAt(c)));
        Assert.Equal(2, Turns(route)); // up (or down) past the wall's end, across, and back to the row
        Assert.True(route.Min(c => c.Y) <= 1 || route.Max(c => c.Y) >= 9);
    }

    [Fact]
    public void Dragging_from_a_drill_to_a_depot_connects_them_around_obstacles()
    {
        var sim = TestUtil.NewSim();
        sim.Place("iron_miner", 0, 5, 0, Dir.East);
        sim.Place("seller", 10, 5, 0, Dir.South); // its input faces north: the belt must come in from above
        Wall(sim, 5, 3, 7);
        var planner = new BuildPlanner(sim);

        var route = planner.Route(new GridPos(0, 5, 0), new GridPos(10, 5, 0), firstLegX: true)!;
        Assert.Equal(new GridPos(0, 5, 0), route[0]);            // leaves through the drill's output
        Assert.Equal(new GridPos(1, 5, 0), route[1]);
        Assert.Equal(new GridPos(10, 5, 0), route[^1]);          // and enters the depot...
        Assert.Equal(new GridPos(10, 4, 0), route[^2]);          // ...through its input, from the north

        var (changed, failed, error) = BuildPlanner.Apply(planner.Drag(sim.Content.Buildings["conveyor"], route, Dir.East), sim.Execute);
        Assert.True(failed == 0, error);
        Assert.Equal(route.Count - 2, changed); // every cell but the drill and the depot
        sim.Step(20 * 30);
        Assert.True(sim.Sold("iron_ore") > 0);
    }

    [Fact]
    public void A_long_belt_line_in_the_way_is_bridged_rather_than_walked_around()
    {
        var sim = TestUtil.NewSim();
        sim.Line(new GridPos(5, 0, 0), Dir.South, 12);
        var planner = new BuildPlanner(sim);
        var route = planner.Route(new GridPos(1, 5, 0), new GridPos(9, 5, 0), firstLegX: true)!;

        Assert.Equal(BuildPlanner.LPath(new GridPos(1, 5, 0), new GridPos(9, 5, 0), true), route);
        var plan = planner.Drag(sim.Content.Buildings["conveyor"], route, Dir.East);
        Assert.Equal(1, plan.Bridges);
        Assert.Null(plan.BridgeProblem);
    }

    [Fact]
    public void A_route_never_passes_where_a_building_outputs()
    {
        var sim = TestUtil.NewSim();
        sim.Place("iron_miner", 3, 3, 0, Dir.South); // drops ore onto (3, 4)
        var route = new BuildPlanner(sim).Route(new GridPos(0, 4, 0), new GridPos(6, 4, 0), firstLegX: true)!;
        AssertConnected(route);
        Assert.DoesNotContain(new GridPos(3, 4, 0), route);
    }

    [Fact]
    public void A_line_dragged_into_the_side_of_an_unfed_belt_makes_it_a_curve_instead_of_turning_it()
    {
        var sim = TestUtil.NewSim();
        sim.Line(new GridPos(0, 8, 0), Dir.East, 7); // its first belt has nothing behind it
        var planner = new BuildPlanner(sim);
        var route = planner.Route(new GridPos(0, 2, 0), new GridPos(0, 8, 0), firstLegX: true)!;
        Assert.Equal(new GridPos(0, 7, 0), route[^2]);

        var plan = planner.Drag(sim.Content.Buildings["conveyor"], route, Dir.East);
        Assert.Equal(PlanAction.Keep, plan.Steps[^1].Action);
        BuildPlanner.Apply(plan, sim.Execute);
        Assert.Equal(Dir.East, sim.World.EntityAt(new GridPos(0, 8, 0))!.Facing);
        Assert.Equal(Dir.South, sim.World.EntityAt(new GridPos(0, 7, 0))!.Facing);
        sim.World.EnsureTopology();
        Assert.Equal(Side.Left, ConveyorBehavior.CurveSide(sim.World.EntityAt(new GridPos(0, 8, 0))!));
    }

    [Fact]
    public void A_belt_that_is_already_fed_is_not_a_route_target_from_its_side_and_is_not_turned()
    {
        var sim = TestUtil.NewSim();
        sim.Line(new GridPos(0, 8, 0), Dir.East, 7);
        var planner = new BuildPlanner(sim);
        // (3, 8) is fed from behind by (2, 8): a belt on its side would only jam against it, so no route ends there.
        Assert.Null(planner.Route(new GridPos(3, 2, 0), new GridPos(3, 8, 0), firstLegX: true));

        // A plain drag there still leaves the line alone rather than re-aiming its belt.
        var plan = planner.Drag(sim.Content.Buildings["conveyor"], BuildPlanner.LPath(new GridPos(3, 2, 0), new GridPos(3, 8, 0), true), Dir.East);
        Assert.Equal(PlanAction.Keep, plan.Steps[^1].Action);
        BuildPlanner.Apply(plan, sim.Execute);
        Assert.Equal(Dir.East, sim.World.EntityAt(new GridPos(3, 8, 0))!.Facing);
    }

    [Fact]
    public void No_route_into_a_building_without_inputs_or_out_of_the_plot()
    {
        var sim = TestUtil.NewSim();
        sim.Place("iron_miner", 8, 8, 0, Dir.East);
        var planner = new BuildPlanner(sim);
        Assert.Null(planner.Route(new GridPos(2, 2, 0), new GridPos(8, 8, 0), true));
        Assert.Null(planner.Route(new GridPos(2, 2, 0), new GridPos(-3, 2, 0), true));
    }

    [Fact]
    public void A_drawn_trail_follows_the_cursor_fills_gaps_and_backs_up()
    {
        var trail = new List<GridPos>();
        foreach (var (x, y) in new[] { (0, 0), (1, 0), (2, 0), (2, 1), (2, 2), (4, 2) })
            BuildPlanner.ExtendTrail(trail, new GridPos(x, y, 0));
        Assert.Equal(new[] { (0, 0), (1, 0), (2, 0), (2, 1), (2, 2), (3, 2), (4, 2) }, trail.Select(c => (c.X, c.Y)));
        Assert.Equal(2, Turns(trail));

        BuildPlanner.ExtendTrail(trail, new GridPos(2, 1, 0)); // back up the way it came
        Assert.Equal(new[] { (0, 0), (1, 0), (2, 0), (2, 1) }, trail.Select(c => (c.X, c.Y)));
    }
}
