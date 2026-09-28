using FactorySim.Behaviors;
using FactorySim.View;

namespace FactorySim.Tests;

public class LogisticsTests
{
    [Fact]
    public void Belt_fed_only_from_one_side_becomes_a_full_length_curve()
    {
        var sim = TestUtil.NewSim();
        sim.Place("iron_miner", 0, 0, 0, Dir.South);
        sim.Place("conveyor", 0, 1, 0, Dir.East);  // fed from its left (north) side only → curve
        sim.Place("conveyor", 1, 1, 0, Dir.East);
        sim.Place("seller", 2, 1, 0, Dir.East);
        sim.World.EnsureTopology();

        var corner = sim.World.EntityAt(new GridPos(0, 1, 0))!;
        Assert.Equal(Side.Left, ConveyorBehavior.CurveSide(corner));
        Assert.Equal(PathKind.CurveLeft, TransportPath.ShapeOf(corner).Kind);

        // Items enter the curve at its start (not mid-tile) → 10 ticks per tile, like straight belts.
        long? produced = null, sold = null;
        for (int t = 0; t < 200 && sold == null; t++)
        {
            sim.Step();
            foreach (var ev in sim.DrainEvents())
            {
                if (ev is ItemProduced p) produced ??= p.Tick;
                if (ev is ItemSold s) sold = s.Tick;
            }
        }
        Assert.Equal(produced + 2 * 10, sold);
    }

    [Fact]
    public void Belt_with_a_fed_back_stays_straight_and_side_loads()
    {
        var sim = TestUtil.NewSim();
        sim.Place("conveyor", 0, 1, 0, Dir.East);
        sim.Place("conveyor", 1, 1, 0, Dir.East);
        sim.Place("conveyor", 1, 0, 0, Dir.South);
        sim.World.EnsureTopology();
        Assert.Null(ConveyorBehavior.CurveSide(sim.World.EntityAt(new GridPos(1, 1, 0))!));
    }

    [Fact]
    public void Curved_path_starts_at_side_edge_and_ends_at_front_edge()
    {
        var left = new PathShape(PathKind.CurveLeft);
        Assert.Equal(new GridPoint(-0.5f, 0, 0), SampleRounded(left, 0));
        Assert.Equal(new GridPoint(0, -0.5f, 0), SampleRounded(left, 1));
        var right = new PathShape(PathKind.CurveRight);
        Assert.Equal(new GridPoint(0.5f, 0, 0), SampleRounded(right, 0));

        var ramp = new PathShape(PathKind.Straight, 0, 1);
        Assert.Equal(0.5f, TransportPath.SampleLocal(ramp, 0.5f).Z, 3);
        Assert.Equal(1f, TransportPath.SampleLocal(ramp, 1f).Z, 3);
    }

    private static GridPoint SampleRounded(PathShape s, float t)
    {
        var p = TransportPath.SampleLocal(s, t);
        return new GridPoint(MathF.Round(p.X, 4) + 0f, MathF.Round(p.Y, 4) + 0f, p.Z);
    }

    [Fact]
    public void Splitter_distributes_evenly_over_three_outputs()
    {
        var sim = TestUtil.NewSim(TestUtil.FastContent);
        sim.Place("fast_miner", 0, 2, 0, Dir.East);
        sim.Place("conveyor", 1, 2, 0, Dir.East);
        sim.Place("splitter", 2, 2, 0, Dir.East);
        sim.Place("seller", 3, 2, 0, Dir.East); // front
        sim.Place("seller", 2, 1, 0, Dir.East); // left (north)
        sim.Place("seller", 2, 3, 0, Dir.East); // right (south)

        sim.Step(1000);
        var perSeller = sim.DrainEvents().OfType<ItemSold>().GroupBy(s => s.EntityId).Select(g => g.Count()).ToList();
        Assert.Equal(3, perSeller.Count);
        Assert.True(perSeller.Max() - perSeller.Min() <= 1, string.Join(",", perSeller));
        Assert.True(perSeller.Sum() > 380); // full belt throughput passes through
    }

    [Fact]
    public void Splitter_skips_a_blocked_output()
    {
        var sim = TestUtil.NewSim(TestUtil.FastContent);
        sim.Place("fast_miner", 0, 2, 0, Dir.East);
        sim.Place("conveyor", 1, 2, 0, Dir.East);
        sim.Place("splitter", 2, 2, 0, Dir.East);
        sim.Place("seller", 3, 2, 0, Dir.East);
        sim.Line(new GridPos(2, 1, 0), Dir.North, 1); // dead end: fills and blocks

        sim.Step(400);
        long before = sim.Sold("iron_ore");
        sim.Step(1000);
        Assert.InRange(sim.Sold("iron_ore") - before, 390, 401); // everything goes to the free branch
    }

    [Fact]
    public void Merger_serves_saturated_inputs_fairly()
    {
        var sim = TestUtil.NewSim(TestUtil.FastContent);
        sim.Place("fast_miner", 0, 2, 0, Dir.East);          // back input
        sim.Place("conveyor", 1, 2, 0, Dir.East);
        sim.Place("fast_miner", 2, 0, 0, Dir.South);         // left input (north)
        sim.Place("conveyor", 2, 1, 0, Dir.South);
        sim.Place("merger", 2, 2, 0, Dir.East);
        sim.Place("conveyor", 3, 2, 0, Dir.East);
        sim.Place("seller", 4, 2, 0, Dir.East);

        sim.Step(200);
        sim.DrainEvents();
        sim.Step(2000);
        var byProducer = sim.DrainEvents().OfType<ItemProduced>().GroupBy(p => p.EntityId).Select(g => g.Sum(p => p.Count)).ToList();
        Assert.Equal(2, byProducer.Count);
        Assert.True(Math.Abs(byProducer[0] - byProducer[1]) <= 4, string.Join(",", byProducer));
        Assert.InRange(byProducer.Sum(), 790, 810); // output belt saturated: 0.4 items/tick
    }

    [Fact]
    public void Hub_items_travel_from_entry_edge_to_exit_edge()
    {
        var sim = TestUtil.NewSim();
        sim.Place("iron_miner", 0, 0, 0, Dir.East);
        sim.Place("splitter", 1, 0, 0, Dir.East);
        sim.Place("seller", 1, 1, 0, Dir.East); // only the right (south) output is connected

        var seen = new List<PositionedItem>();
        var points = new List<GridPoint>();
        for (int t = 0; t < 40; t++)
        {
            sim.Step();
            TransportPath.CollectAll(sim.World, seen);
            points.AddRange(seen.Select(s => s.Point));
        }
        Assert.Contains(points, p => p.X < 1.5f && Math.Abs(p.Y - 0.5f) < 0.01f);  // entered from the west edge
        Assert.Contains(points, p => Math.Abs(p.X - 1.5f) < 0.01f && p.Y > 0.6f); // left through the south edge
    }
}
