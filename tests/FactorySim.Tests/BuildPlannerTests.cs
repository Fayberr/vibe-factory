using FactorySim.Editing;

namespace FactorySim.Tests;

public class BuildPlannerTests
{
    private static BuildPlan Drag(Simulation sim, string def, GridPos from, GridPos to, bool firstLegX = true) =>
        new BuildPlanner(sim).Drag(sim.Content.Buildings[def], BuildPlanner.LPath(from, to, firstLegX), Dir.East);

    private static void Apply(Simulation sim, BuildPlan plan)
    {
        var (_, failed, error) = BuildPlanner.Apply(plan, sim.Execute);
        Assert.True(failed == 0, error);
    }

    /// <summary>A north→south iron line at x = 5 from y = 0 to a depot at y = 6.</summary>
    private static Simulation WithVerticalLine()
    {
        var sim = TestUtil.NewSim();
        sim.Place("iron_miner", 5, 0, 0, Dir.South);
        sim.Line(new GridPos(5, 1, 0), Dir.South, 5);
        sim.Place("seller", 5, 6, 0, Dir.South);
        return sim;
    }

    [Fact]
    public void Dragging_a_belt_across_another_line_bridges_over_it_and_both_lines_keep_flowing()
    {
        var sim = WithVerticalLine();
        sim.Place("iron_miner", 1, 3, 0, Dir.East);
        var plan = Drag(sim, "conveyor", new GridPos(2, 3, 0), new GridPos(8, 3, 0));
        Assert.Equal(1, plan.Bridges);
        Apply(sim, plan);
        sim.Place("seller", 9, 3, 0, Dir.East);

        Assert.Equal("ramp_up", sim.World.EntityAt(new GridPos(4, 3, 0))!.Def.Id);
        Assert.Equal("conveyor", sim.World.EntityAt(new GridPos(5, 3, 1))!.Def.Id);
        Assert.Equal("ramp_down", sim.World.EntityAt(new GridPos(6, 3, 0))!.Def.Id);
        Assert.Equal(Dir.South, sim.World.EntityAt(new GridPos(5, 3, 0))!.Facing); // crossed belt untouched

        sim.Step(20 * 30); // the bridged line is ~9 tiles long: ~9 s before its first sale
        var sold = sim.DrainEvents().OfType<ItemSold>().GroupBy(s => sim.World.GetEntity(s.EntityId)!.Pos).ToDictionary(g => g.Key, g => g.Sum(s => s.Count));
        Assert.True(sold.GetValueOrDefault(new GridPos(5, 6, 0)) >= 15, "the crossed line still delivers");
        Assert.True(sold.GetValueOrDefault(new GridPos(9, 3, 0)) >= 15, "the bridged line delivers");
    }

    [Fact]
    public void Neighbouring_crossings_share_one_bridge()
    {
        var sim = TestUtil.NewSim();
        sim.Line(new GridPos(4, 0, 0), Dir.South, 6);
        sim.Line(new GridPos(6, 0, 0), Dir.South, 6);
        var plan = Drag(sim, "conveyor", new GridPos(1, 3, 0), new GridPos(9, 3, 0));
        Assert.Equal(1, plan.Bridges);
        Apply(sim, plan);
        Assert.Equal("ramp_up", sim.World.EntityAt(new GridPos(3, 3, 0))!.Def.Id);
        for (int x = 4; x <= 6; x++) Assert.NotNull(sim.World.EntityAt(new GridPos(x, 3, 1)));
        Assert.Equal("ramp_down", sim.World.EntityAt(new GridPos(7, 3, 0))!.Def.Id);
    }

    [Fact]
    public void A_crossing_right_after_a_corner_is_not_bridged_and_left_alone()
    {
        var sim = WithVerticalLine();
        // Down from (4,0) then east along y = 3: the corner at (4,3) leaves no straight run-in.
        var plan = Drag(sim, "conveyor", new GridPos(4, 0, 0), new GridPos(8, 3, 0), firstLegX: false);
        Assert.Equal(0, plan.Bridges);
        Assert.NotNull(plan.BridgeProblem);
        Apply(sim, plan);
        Assert.Equal(Dir.South, sim.World.EntityAt(new GridPos(5, 3, 0))!.Facing);
        Assert.Null(sim.World.EntityAt(new GridPos(5, 3, 1)));
    }

    [Fact]
    public void Dragging_belts_along_a_line_keeps_polishers_and_re_aims_belts()
    {
        var sim = TestUtil.NewSim();
        sim.Place("conveyor", 1, 0, 0, Dir.West);
        sim.Place("polisher", 2, 0, 0, Dir.East);
        Apply(sim, Drag(sim, "conveyor", new GridPos(0, 0, 0), new GridPos(4, 0, 0)));

        Assert.Equal(Dir.East, sim.World.EntityAt(new GridPos(1, 0, 0))!.Facing);
        Assert.Equal("polisher", sim.World.EntityAt(new GridPos(2, 0, 0))!.Def.Id);
        Assert.Equal(5, sim.World.EntityCount);

        // Dragging polishers over belts does upgrade them.
        Apply(sim, Drag(sim, "polisher", new GridPos(3, 0, 0), new GridPos(4, 0, 0)));
        Assert.Equal("polisher", sim.World.EntityAt(new GridPos(4, 0, 0))!.Def.Id);
    }

    [Fact]
    public void Clicking_a_polisher_onto_a_belt_keeps_the_belts_direction()
    {
        var sim = TestUtil.NewSim();
        sim.Place("conveyor", 2, 2, 0, Dir.South);
        var plan = new BuildPlanner(sim).Click(sim.Content.Buildings["polisher"], new GridPos(2, 2, 0), Dir.East);
        Apply(sim, plan);
        var p = sim.World.EntityAt(new GridPos(2, 2, 0))!;
        Assert.Equal(("polisher", Dir.South), (p.Def.Id, p.Facing));
    }

    [Theory]
    [InlineData("ramp_down", 0, 0)] // on the ground: spans ground and level 1, lands on the ground
    [InlineData("ramp_down", 1, 0)] // from a bridge: comes down to the ground
    [InlineData("ramp_down", 3, 2)]
    [InlineData("ramp_up", 0, 0)]
    [InlineData("ramp_up", 2, 2)]
    [InlineData("conveyor", 2, 2)]
    public void Ramps_anchor_so_their_input_is_at_the_build_height_and_never_underground(string def, int height, int anchor)
    {
        var planner = new BuildPlanner(TestUtil.NewSim());
        Assert.Equal(anchor, planner.AnchorHeight(TestUtil.Content.Buildings[def], height));
    }

    [Fact]
    public void Belts_continue_at_the_top_of_a_ramp_up_and_the_bottom_of_a_ramp_down()
    {
        var planner = new BuildPlanner(TestUtil.NewSim());
        Assert.Equal(1, planner.OutputHeight(TestUtil.Content.Buildings["ramp_up"], 0));
        Assert.Equal(0, planner.OutputHeight(TestUtil.Content.Buildings["ramp_down"], 0));
        Assert.Equal(2, planner.OutputHeight(TestUtil.Content.Buildings["conveyor"], 2));
    }
}
