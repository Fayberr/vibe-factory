using FactorySim.Samples;

namespace FactorySim.Tests;

public class GridAndTopologyTests
{
    [Fact]
    public void Rotation_maps_local_sides_to_world_directions()
    {
        Assert.Equal(Dir.North, Side.Front.ToWorld(Dir.North));
        Assert.Equal(Dir.East, Side.Front.ToWorld(Dir.East));
        Assert.Equal(Dir.West, Side.Back.ToWorld(Dir.East));
        Assert.Equal(Dir.North, Side.Left.ToWorld(Dir.East));
        Assert.Equal(new GridPos(1, 0, 0), new GridPos(0, -1, 0).Rotate(Dir.East)); // north offset → east
        Assert.Equal(new GridPos(0, 0, 1), new GridPos(0, 0, 1).Rotate(Dir.West)); // z unaffected
    }

    [Fact]
    public void Placement_rejects_overlap_and_out_of_bounds()
    {
        var sim = TestUtil.NewSim();
        sim.Place("conveyor", 3, 3, 0, Dir.East);

        var overlap = sim.Execute(new PlaceBuilding("conveyor", new GridPos(3, 3, 0), Dir.North));
        Assert.False(overlap.Ok);

        var outside = sim.Execute(new PlaceBuilding("conveyor", new GridPos(-1, 0, 0), Dir.North));
        Assert.False(outside.Ok);

        // Ramps occupy two layers, so the cell above a ramp is taken.
        sim.Place("ramp_up", 5, 5, 0, Dir.East);
        Assert.False(sim.Execute(new PlaceBuilding("conveyor", new GridPos(5, 5, 1), Dir.East)).Ok);
        Assert.Same(sim.World.EntityAt(new GridPos(5, 5, 0)), sim.World.EntityAt(new GridPos(5, 5, 1)));
    }

    [Fact]
    public void Remove_frees_every_footprint_cell_and_refunds()
    {
        var sim = TestUtil.NewSim(sandbox: false, money: 100);
        sim.Place("ramp_up", 1, 1, 0, Dir.North);
        Assert.Equal(75, sim.World.Money.ToDouble(), 9);

        Assert.True(sim.Execute(new RemoveBuilding(new GridPos(1, 1, 1))).Ok); // remove via upper cell
        Assert.Null(sim.World.EntityAt(new GridPos(1, 1, 0)));
        Assert.Null(sim.World.EntityAt(new GridPos(1, 1, 1)));
        Assert.Equal(100, sim.World.Money.ToDouble(), 9);
    }

    [Fact]
    public void Costs_are_enforced_outside_sandbox()
    {
        var sim = TestUtil.NewSim(sandbox: false, money: 20);
        var r = sim.Execute(new PlaceBuilding("iron_miner", new GridPos(0, 0, 0), Dir.East));
        Assert.False(r.Ok);
        Assert.Contains("Need", r.Error);
        Assert.Equal(0, sim.World.EntityCount);
    }

    [Fact]
    public void Belts_link_front_to_back_and_side_but_not_head_on()
    {
        var sim = TestUtil.NewSim();
        int a = sim.Place("conveyor", 0, 0, 0, Dir.East);
        int b = sim.Place("conveyor", 1, 0, 0, Dir.East);  // a → b (back)
        int c = sim.Place("conveyor", 1, 1, 0, Dir.North); // c → b (side-load from south)
        int d = sim.Place("conveyor", 3, 0, 0, Dir.East);
        sim.Place("conveyor", 4, 0, 0, Dir.West);          // head-on with d

        _ = sim.World.UpdateOrder;
        var w = sim.World;
        Assert.Equal(b, OutTarget(w, a));
        Assert.Equal(b, OutTarget(w, c));
        Assert.Null(OutTarget(w, d));
    }

    [Fact]
    public void Ramps_connect_across_layers()
    {
        var sim = TestUtil.NewSim();
        DemoLayout.Build(sim);
        _ = sim.World.UpdateOrder;
        var w = sim.World;

        var rampUp = w.EntityAt(new GridPos(1, 4, 0))!;
        var bridge = w.EntityAt(new GridPos(2, 4, 1))!;
        var rampDown = w.EntityAt(new GridPos(3, 4, 0))!;
        var smelter = w.EntityAt(new GridPos(4, 4, 0))!;
        var tunnel = w.EntityAt(new GridPos(2, 4, 0))!; // line B under the bridge

        Assert.Equal(bridge.Id, OutTarget(w, rampUp.Id));
        Assert.Equal(rampDown.Id, OutTarget(w, bridge.Id));
        Assert.Equal(smelter.Id, OutTarget(w, rampDown.Id));
        Assert.Equal(w.EntityAt(new GridPos(2, 5, 0))!.Id, OutTarget(w, tunnel.Id));
    }

    [Fact]
    public void Update_order_is_downstream_first()
    {
        var sim = TestUtil.NewSim();
        sim.Place("iron_miner", 0, 0, 0, Dir.East);
        sim.Line(new GridPos(1, 0, 0), Dir.East, 3);
        sim.Place("seller", 4, 0, 0, Dir.East);

        var xs = sim.World.UpdateOrder.Select(e => e.Pos.X).ToList();
        Assert.Equal(new[] { 4, 3, 2, 1, 0 }, xs);
    }

    [Fact]
    public void Rotating_relinks()
    {
        var sim = TestUtil.NewSim();
        int a = sim.Place("conveyor", 0, 0, 0, Dir.North);
        int b = sim.Place("conveyor", 1, 0, 0, Dir.East);
        _ = sim.World.UpdateOrder;
        Assert.Null(OutTarget(sim.World, a));

        Assert.True(sim.Execute(new RotateBuilding(new GridPos(0, 0, 0), Dir.East)).Ok);
        _ = sim.World.UpdateOrder;
        Assert.Equal(b, OutTarget(sim.World, a));
    }

    private static int? OutTarget(World w, int entityId)
    {
        var e = w.GetEntity(entityId)!;
        return e.Link(e.Def.OutputPorts[0]).Target?.Id;
    }
}
