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
    public void Hub_items_ride_in_to_the_middle_and_then_out()
    {
        var sim = TestUtil.NewSim();
        sim.Place("iron_miner", 0, 0, 0, Dir.East);
        sim.Place("splitter", 1, 0, 0, Dir.East);
        sim.Place("seller", 1, 1, 0, Dir.South); // only the right (south) output is connected

        var hub = sim.World.EntityAt(new GridPos(1, 0, 0))!;
        var seen = new List<PositionedItem>();
        var points = new List<GridPoint>();
        for (int t = 0; t < 40; t++)
        {
            sim.Step();
            TransportPath.CollectAll(sim.World, seen);
            points.AddRange(seen.Where(s => s.EntityId == hub.Id).Select(s => s.Point));
        }
        Assert.Contains(points, p => p.X < 1.5f && Math.Abs(p.Y - 0.5f) < 0.01f); // entered from the west edge
        Assert.Contains(points, p => Math.Abs(p.X - 1.5f) < 0.01f && Math.Abs(p.Y - 0.5f) < 0.01f); // reached the middle
        // and it still delivers: the hand-over works from the middle, out at the edge.
        sim.Step(60);
        Assert.True(sim.Sold("iron_ore") > 0, "nothing came out of the splitter at all");
    }

    /// <summary>
    /// A splitter sorting tar to its right output, whose belt is packed solid, so nothing can leave that way.
    /// Its front output is left as the test needs it: connected to a free belt, or not connected at all.
    /// </summary>
    private static (Simulation Sim, Entity Hub, RouterState Lane) SplitterWithAFullTarBelt()
    {
        var sim = TestUtil.NewSim();
        sim.Place("splitter", 2, 2, 0, Dir.East);
        sim.Place("conveyor", 2, 3, 0, Dir.South); // the right output, packed solid
        var hub = sim.World.EntityAt(new GridPos(2, 2, 0))!;
        Assert.True(sim.Execute(new SetFilter(hub.Pos, 0, "plastic")).Ok);
        Assert.True(sim.Execute(new SetFilter(hub.Pos, 2, "tar")).Ok);
        var right = sim.Belt(2, 3, 0);
        for (int pos = 1000; pos >= 0; pos -= 250)
            right.Items.Add(new BeltItem(sim.World.CreateItem("tar", 1, 1), pos));
        return (sim, hub, (RouterState)hub.State);
    }

    [Fact]
    public void An_item_left_past_the_middle_by_an_old_save_comes_back_into_the_middle()
    {
        // Before 3.8.9 a hub lane ran on to the cell edge, so a save can hold an item parked out there. It is
        // drawn on the belt's own first item there, which is exactly the picture that read as "it has already
        // left". Once the hub has been held up for a second it lays its lane out again, from the middle back
        // to the entry: the item comes back in, and nothing is dropped.
        var (sim, hub, lane) = SplitterWithAFullTarBelt();
        lane.Items.Add(new RouterItem(sim.World.CreateItem("tar", 1, 1), 1000, 0, hub.Def.OutputPorts[2]));
        lane.Items.Add(new RouterItem(sim.World.CreateItem("plastic", 1, 1), 750, 0, -1));

        sim.Step(2);
        Assert.Equal(1000, lane.Items[0].Pos); // still where the old lane parked it

        sim.Step(40);
        Assert.Equal(2, lane.Items.Count); // nothing dropped
        Assert.Equal(500, lane.Items[0].Pos); // the tar waits in the middle now
        Assert.Equal(250, lane.Items[1].Pos);

        var seen = new List<PositionedItem>();
        TransportPath.CollectAll(sim.World, seen);
        var held = seen.Single(s => s.EntityId == hub.Id && s.Item.Type == "tar").Point;
        Assert.Equal(2.5f, held.X, 3); // drawn in the middle of the splitter, not out on the belt
        Assert.Equal(2.5f, held.Y, 3);
    }

    [Fact]
    public void Items_move_in_one_even_step_all_the_way_through_a_line()
    {
        // Fabian: "the items move very weird ... there are three plastics in a row, and then each one jumps a
        // bit", "not like it was before". A path's end and the next path's start are the *same* point, so
        // nothing may offset one against the other: 3.8.7/3.8.8 shifted both ends inwards, which made every
        // hand-over a hop (half a cell out of a hub, a quarter of a cell between two belt tiles) against a
        // normal step of 0.05 of a cell. This walks items through a whole line and fails on any step that is
        // bigger than the exit roll out of the middle.
        var sim = TestUtil.NewSim();
        sim.Place("iron_miner", 0, 2, 0, Dir.East);
        sim.Place("conveyor", 1, 2, 0, Dir.East);
        sim.Place("conveyor", 2, 2, 0, Dir.East);
        sim.Place("splitter", 3, 2, 0, Dir.East);
        sim.Place("conveyor", 4, 2, 0, Dir.East);
        sim.Place("conveyor", 5, 2, 0, Dir.East);
        sim.Place("seller", 6, 2, 0, Dir.East);

        var seen = new List<PositionedItem>();
        var last = new Dictionary<long, GridPoint>();
        double worst = 0;
        for (int t = 0; t < 900; t++)
        {
            sim.Step();
            TransportPath.CollectAll(sim.World, seen);
            foreach (var s in seen)
            {
                if (last.TryGetValue(s.Item.Uid, out var was))
                    worst = Math.Max(worst, Math.Sqrt(Math.Pow(s.Point.X - was.X, 2) + Math.Pow(s.Point.Y - was.Y, 2)));
                last[s.Item.Uid] = s.Point;
            }
        }
        // Twice the belt's 50/1000 is the fastest a step can be, the roll out of a hub's middle. A hop is 5
        // to 11 times that, so 0.15 of a cell separates them cleanly.
        Assert.True(worst <= 0.15, $"an item moved {worst:0.###} of a cell in one tick");
    }

    [Fact]
    public void An_item_whose_output_is_full_waits_in_the_middle_of_the_splitter()
    {
        // Fabian: "didn't I say for the item that can't go in a direction to stay in the middle ... instead
        // it stays on the right edge of the splitter". The hub's lane now ends at its middle, which is where
        // an item both waits and leaves from, so a refused item never reaches the edge it shares with the
        // belt that is refusing it.
        var (sim, hub, lane) = SplitterWithAFullTarBelt();
        sim.Place("seller", 3, 2, 0, Dir.East);    // the front output, free
        var right = sim.Belt(2, 3, 0);
        var rightEntity = sim.World.EntityAt(new GridPos(2, 3, 0))!;
        // The lane as a hub holds it: a tar in the middle that the right belt keeps refusing, and the two
        // plastics the splitter is sorting to the front queued behind it, one spacing apart.
        lane.Items.Add(new RouterItem(sim.World.CreateItem("tar", 1, 1), 500, 0, hub.Def.OutputPorts[2]));
        lane.Items.Add(new RouterItem(sim.World.CreateItem("plastic", 1, 1), 250, 0, -1));
        lane.Items.Add(new RouterItem(sim.World.CreateItem("plastic", 1, 1), 0, 0, -1));
        sim.Step(20);

        Assert.Equal("tar", lane.Items[0].Item.Type);
        Assert.Equal(500, lane.Items[0].Pos); // in the middle, where it wants to leave from
        var seen = new List<PositionedItem>();
        TransportPath.CollectAll(sim.World, seen);
        var held = seen.Single(s => s.EntityId == hub.Id && s.Item.Type == "tar").Point;
        Assert.Equal(2.5f, held.X, 3); // drawn in the middle of the splitter, not out at the right belt
        Assert.Equal(2.5f, held.Y, 3);
        Assert.True(seen.Where(s => s.EntityId == rightEntity.Id).All(s => s.Point.Y >= 3.0f),
            "the belt's own tar is drawn up on the hub's edge");

        // The moment the belt has room again the tar goes, and the plastic behind it gets out too.
        right.Items.Clear();
        sim.Step(20);
        Assert.DoesNotContain(lane.Items, it => it.Item.Type == "tar");
        Assert.True(sim.Sold("plastic") > 0, "the plastic behind the tar never got out");
    }

    [Fact]
    public void A_full_belt_is_drawn_evenly_spaced_end_to_end()
    {
        // The direct guard on drawn spacing: nothing may be offset at either end of a path. 3.8.7 and 3.8.8
        // moved the ends inwards, which bunched the items near them towards the middle ("the spacings of
        // pretty much all items are now messed up, sometimes there are pairs of two that are closer
        // together" - Fabian, after 3.8.7). A path's end and the next path's start are the *same* point, so
        // no such offset is possible: see Items_move_in_one_even_step_all_the_way_through_a_line.
        var sim = TestUtil.NewSim();
        sim.Place("conveyor", 2, 2, 0, Dir.South);
        var belt = sim.Belt(2, 2, 0);
        var entity = sim.World.EntityAt(new GridPos(2, 2, 0))!;
        for (int pos = 1000; pos >= 0; pos -= 250)
            belt.Items.Add(new BeltItem(sim.World.CreateItem("iron_plate", 1, 1), pos));

        var seen = new List<PositionedItem>();
        TransportPath.CollectAll(sim.World, seen);
        var points = seen.Where(s => s.EntityId == entity.Id).Select(s => s.Point.Y).OrderBy(y => y).ToList();

        Assert.Equal(5, points.Count); // a packed belt tile: 4 spacings over the length, both ends included
        double gap = points[1] - points[0];
        for (int i = 1; i < points.Count; i++)
            Assert.Equal(gap, points[i] - points[i - 1], 3);
    }

    [Fact]
    public void A_splitter_keeps_up_with_a_congested_belt_it_feeds()
    {
        // The hub waits in the middle, which is exactly where it hands over from, so a full belt downstream
        // never costs it time: it can send on the first tick the belt has room, and the next item needs only
        // one spacing to reach the middle. A hub that had to travel on to the edge after the belt freed up
        // would hand over less often than the belt can take, and would become the bottleneck itself.
        var sim = TestUtil.NewSim(TestUtil.FastContent);
        sim.Place("fast_miner", 0, 2, 0, Dir.East); // interval 1: far more than the line can carry
        sim.Place("splitter", 1, 2, 0, Dir.East);
        sim.Place("conveyor", 2, 2, 0, Dir.East);
        sim.Place("conveyor", 3, 2, 0, Dir.East);
        sim.Place("seller", 4, 2, 0, Dir.East);

        sim.Step(200);
        long before = sim.Sold("iron_ore");
        sim.Step(2000);
        // Saturating the line: speed 50 / spacing 250 = 0.2 items per tick (4/s) all the way along.
        Assert.InRange(sim.Sold("iron_ore") - before, 380, 401);
    }
}
