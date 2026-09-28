using FactorySim.Editing;

namespace FactorySim.Tests;

public class DepotTests
{
    [Fact]
    public void A_depot_takes_items_in_through_its_back_only()
    {
        var sim = TestUtil.NewSim();
        sim.Place("iron_miner", 0, 0, 0, Dir.East);
        sim.Place("seller", 1, 0, 0, Dir.East);  // fed from behind
        sim.Place("iron_miner", 3, 0, 0, Dir.East);
        sim.Place("seller", 4, 0, 0, Dir.North); // the drill points at its side
        sim.Step(20 * 10);

        var fedBehind = sim.World.EntityAt(new GridPos(1, 0, 0))!;
        var fedSide = sim.World.EntityAt(new GridPos(4, 0, 0))!;
        Assert.True(fedBehind.Fed[0]);
        Assert.False(fedSide.Fed[0]);
        Assert.Equal(10, sim.Sold("iron_ore"));
    }

    [Theory]
    [InlineData(Dir.East)]
    [InlineData(Dir.South)]
    [InlineData(Dir.North)]
    public void A_depot_placed_at_the_end_of_a_line_turns_to_take_it_in(Dir belt)
    {
        var sim = TestUtil.NewSim();
        var end = new GridPos(5, 5, 0);
        var from = end.Step(belt.Opposite());
        sim.Place("conveyor", from.X, from.Y, from.Z, belt);
        var planner = new BuildPlanner(sim);
        var seller = sim.Content.Buildings["seller"];

        foreach (var facing in new[] { Dir.North, Dir.East, Dir.South, Dir.West })
            Assert.Equal(belt, planner.Click(seller, end, facing).Steps.Single().Facing);
    }

    [Fact]
    public void A_depot_keeps_its_rotation_when_nothing_feeds_it_or_it_already_takes_a_line_in()
    {
        var sim = TestUtil.NewSim();
        var planner = new BuildPlanner(sim);
        var seller = sim.Content.Buildings["seller"];
        Assert.Equal(Dir.West, planner.Click(seller, new GridPos(5, 5, 0), Dir.West).Steps.Single().Facing);

        // Belts run in from the west and from the north: either rotation that takes one in stays.
        sim.Place("conveyor", 4, 5, 0, Dir.East);
        sim.Place("conveyor", 5, 4, 0, Dir.South);
        Assert.Equal(Dir.East, planner.Click(seller, new GridPos(5, 5, 0), Dir.East).Steps.Single().Facing);
        Assert.Equal(Dir.South, planner.Click(seller, new GridPos(5, 5, 0), Dir.South).Steps.Single().Facing);
        Assert.Contains(planner.Click(seller, new GridPos(5, 5, 0), Dir.West).Steps.Single().Facing, new[] { Dir.East, Dir.South });
    }

    [Fact]
    public void Depots_are_scarce_and_grow_by_one_every_second_tier()
    {
        var sim = TestUtil.NewSim(sandbox: false);
        var seller = sim.Content.Buildings["seller"];
        var limits = Enumerable.Range(0, 8).Select(tier =>
        {
            sim.World.UnlockedTier = tier;
            return sim.World.LimitOf(seller);
        }).ToList();
        Assert.Equal(new int?[] { 2, 2, 3, 3, 4, 4, 5, 5 }, limits);
        Assert.True(seller.Cost > sim.Content.Buildings["iron_miner"].Cost); // pricier than a drill
    }
}
