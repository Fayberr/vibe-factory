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

        // Items enter the curve at its start (not mid-tile) → 20 ticks per tile, like straight belts.
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
        Assert.Equal(produced + 2 * 20, sold);
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
        sim.Place("seller", 2, 1, 0, Dir.North); // left (north), taking in from the south
        sim.Place("seller", 2, 3, 0, Dir.South); // right (south), taking in from the north

        sim.Step(1000);
        var perSeller = sim.DrainEvents().OfType<ItemSold>().GroupBy(s => s.EntityId).Select(g => g.Count()).ToList();
        Assert.Equal(3, perSeller.Count);
        Assert.True(perSeller.Max() - perSeller.Min() <= 1, string.Join(",", perSeller));
        Assert.True(perSeller.Sum() > 190); // full belt throughput (0.2 items/tick) passes through
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
        Assert.InRange(sim.Sold("iron_ore") - before, 195, 201); // everything goes to the free branch
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
        Assert.InRange(byProducer.Sum(), 328, 340); // the merger caps the line: 0.167 items/tick (3.33/s)
    }

    [Fact]
    public void Merger_is_a_throughput_gate_that_widens_with_levels()
    {
        // Three saturated lines into a merger that feeds a depot directly: the merger alone sets the pace.
        long Throughput(int level)
        {
            var sim = TestUtil.NewSim(TestUtil.FastContent);
            sim.Place("fast_miner", 0, 2, 0, Dir.East);  // back
            sim.Place("conveyor", 1, 2, 0, Dir.East);
            sim.Place("fast_miner", 2, 0, 0, Dir.South); // left
            sim.Place("conveyor", 2, 1, 0, Dir.South);
            sim.Place("fast_miner", 2, 4, 0, Dir.North); // right
            sim.Place("conveyor", 2, 3, 0, Dir.North);
            sim.Place("merger", 2, 2, 0, Dir.East);
            sim.Place("seller", 3, 2, 0, Dir.East);
            foreach (var pos in new[] { new GridPos(1, 2, 0), new GridPos(2, 1, 0), new GridPos(2, 3, 0) })
                Assert.True(sim.Execute(new SetBuildingLevels(new[] { new LevelChange(pos, 9) })).Ok); // maxed feeds
            if (level > 1)
                Assert.True(sim.Execute(new SetBuildingLevels(new[] { new LevelChange(new GridPos(2, 2, 0), level) })).Ok);

            sim.Step(200);
            long before = sim.Sold("iron_ore");
            sim.Step(2000);
            return sim.Sold("iron_ore") - before;
        }

        // Level 1: speed 50 / spacing 300 = 0.167 items/tick (3.33/s), slower than a level-1 belt,
        // even though the three maxed input belts could bring 60/s.
        Assert.InRange(Throughput(1), 328, 338);
        // Level 3: speed × 2.67, so just under 9/s.
        Assert.InRange(Throughput(3), 870, 900);
        // Level 7 (the max) reaches the physical cap, speed = spacing: one item per tick (20/s),
        // less the small overhead of a hub picking outputs.
        Assert.InRange(Throughput(7), 1985, 2001);
        Assert.Equal(7, TestUtil.Content.Building("merger").Upgrade!.MaxLevel);
    }

    [Fact]
    public void Hub_items_travel_from_entry_edge_to_exit_edge()
    {
        var sim = TestUtil.NewSim();
        sim.Place("iron_miner", 0, 0, 0, Dir.East);
        sim.Place("splitter", 1, 0, 0, Dir.East);
        sim.Place("seller", 1, 1, 0, Dir.South); // only the right (south) output is connected

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

    [Fact]
    public void An_item_the_hub_still_holds_is_drawn_inside_the_hub_not_on_the_belt()
    {
        // His report: the tar belt is full, the splitter holds a tar bound for it, and the picture showed
        // that tar on the belt's edge, on top of the belt's own item, so it looked as if the tar had left
        // and the plastic behind it was first. Both were drawn on the cell edge they share.
        var sim = TestUtil.NewSim();
        sim.Place("splitter", 2, 2, 0, Dir.East);
        sim.Place("conveyor", 2, 3, 0, Dir.South); // the right output, packed solid
        var hub = sim.World.EntityAt(new GridPos(2, 2, 0))!;
        Assert.True(sim.Execute(new SetFilter(hub.Pos, 0, "plastic")).Ok);
        Assert.True(sim.Execute(new SetFilter(hub.Pos, 2, "tar")).Ok);
        var right = sim.Belt(2, 3, 0);
        var rightEntity = sim.World.EntityAt(new GridPos(2, 3, 0))!;
        for (int pos = 1000; pos >= 0; pos -= 250)
            right.Items.Add(new BeltItem(sim.World.CreateItem("tar", 1, 1), pos));
        // A tar at the end of the hub's lane, bound for the right output, which cannot take it.
        ((RouterState)hub.State).Items.Add(
            new RouterItem(sim.World.CreateItem("tar", 1, 1), 1000, 0, hub.Def.OutputPorts[2]));
        sim.Step(20);

        var seen = new List<PositionedItem>();
        TransportPath.CollectAll(sim.World, seen);
        var held = seen.Single(s => s.EntityId == hub.Id).Point;
        var nearest = seen.Where(s => s.EntityId == rightEntity.Id).MinBy(s => s.Point.Y).Point;

        // The hub's cell ends at y = 3 and the belt's begins there. The tar the hub still holds has to be
        // drawn inside the hub, the belt's own item inside the belt, and the two must not coincide.
        Assert.Equal(2.5f, held.X, 3);
        Assert.True(held.Y <= 2.9f, $"the tar the hub holds is drawn at y={held.Y}, out on the belt's edge");
        Assert.True(nearest.Y >= 3.1f, $"the belt's own tar is drawn at y={nearest.Y}, up on the hub's edge");
        Assert.True(Math.Abs(held.Y - nearest.Y) >= 0.2f,
            $"the held tar (y={held.Y}) and the belt's tar (y={nearest.Y}) are drawn on top of each other");
    }
}
