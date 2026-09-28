using FactorySim.Behaviors;
using FactorySim.Editing;

namespace FactorySim.Tests;

public class EditingTests
{
    [Fact]
    public void Blueprint_pastes_rotated_and_round_trips_as_json()
    {
        var sim = TestUtil.NewSim();
        sim.Place("iron_miner", 0, 0, 0, Dir.East);
        sim.Place("conveyor", 1, 0, 0, Dir.East);
        sim.Place("seller", 2, 0, 0, Dir.East);

        var bp = Blueprint.FromJson(Blueprint.FromEntities(sim.World.Entities, new GridPos(1, 0, 0)).ToJson());
        Assert.Equal(3, bp.Count);

        // Rotated a quarter turn clockwise around the belt: the line now runs north → south.
        var r = sim.Execute(new PlaceBlueprint(bp, new GridPos(10, 10, 0), 1));
        Assert.True(r.Ok, r.Error);
        Assert.Equal(3, r.EntityIds!.Count);
        Assert.Equal("iron_miner", sim.World.EntityAt(new GridPos(10, 9, 0))!.Def.Id);
        Assert.Equal(Dir.South, sim.World.EntityAt(new GridPos(10, 10, 0))!.Facing);
        Assert.Equal("seller", sim.World.EntityAt(new GridPos(10, 11, 0))!.Def.Id);

        sim.Step(20 * 10);
        Assert.True(sim.Sold("iron_ore") >= 8 * 2); // both copies work
    }

    [Fact]
    public void Blueprint_placement_is_all_or_nothing()
    {
        var sim = TestUtil.NewSim(sandbox: false, money: 1000);
        sim.Place("conveyor", 5, 5, 0, Dir.East);
        var money = sim.World.Money;

        var bp = new Blueprint { Entries = { new("seller", new GridPos(0, 0, 0), Dir.North), new("seller", new GridPos(1, 0, 0), Dir.North) } };
        Assert.False(sim.Execute(new PlaceBlueprint(bp, new GridPos(4, 5, 0))).Ok); // second overlaps the belt
        Assert.Null(sim.World.EntityAt(new GridPos(4, 5, 0)));
        Assert.Equal(money, sim.World.Money);
    }

    [Fact]
    public void Move_keeps_state_and_can_rotate_a_group()
    {
        var sim = TestUtil.NewSim();
        sim.Place("iron_miner", 0, 0, 0, Dir.East);
        int beltId = sim.Place("conveyor", 1, 0, 0, Dir.East);
        sim.Step(25); // one ore on the belt, no sink

        var cells = new[] { new GridPos(0, 0, 0), new GridPos(1, 0, 0) };
        var r = sim.Execute(new MoveBuildings(cells, new GridPos(5, 5, 0), 1, new GridPos(0, 0, 0)));
        Assert.True(r.Ok, r.Error);

        var belt = sim.World.GetEntity(beltId)!;
        Assert.Equal(new GridPos(5, 6, 0), belt.Pos);          // (1,0) rotated → (0,1), + (5,5)
        Assert.Equal(Dir.South, belt.Facing);
        Assert.Single(((ConveyorState)belt.State).Items);      // the ore travelled with it
        Assert.Null(sim.World.EntityAt(new GridPos(1, 0, 0)));
    }

    [Fact]
    public void Move_into_occupied_cells_fails_without_changes()
    {
        var sim = TestUtil.NewSim();
        sim.Place("conveyor", 0, 0, 0, Dir.East);
        sim.Place("seller", 3, 0, 0, Dir.East);
        Assert.False(sim.Execute(new MoveBuildings(new[] { new GridPos(0, 0, 0) }, new GridPos(3, 0, 0))).Ok);
        Assert.NotNull(sim.World.EntityAt(new GridPos(0, 0, 0)));
    }

    [Fact]
    public void Undo_and_redo_placement_restores_money()
    {
        var sim = TestUtil.NewSim(sandbox: false, money: 100);
        var history = new EditHistory(sim);

        Assert.True(history.Execute(new PlaceBuilding("iron_miner", new GridPos(0, 0, 0), Dir.East)).Ok);
        Assert.Equal((BigNum)75, sim.World.Money);

        Assert.True(history.Undo().Ok);
        Assert.Equal(0, sim.World.EntityCount);
        Assert.Equal((BigNum)100, sim.World.Money);

        Assert.True(history.Redo().Ok);
        Assert.Equal("iron_miner", sim.World.EntityAt(new GridPos(0, 0, 0))!.Def.Id);
        Assert.False(history.CanRedo);
    }

    [Fact]
    public void Grouped_edits_undo_as_one_step()
    {
        var sim = TestUtil.NewSim();
        var history = new EditHistory(sim);
        history.BeginGroup();
        for (int x = 0; x < 5; x++) history.Execute(new PlaceBuilding("conveyor", new GridPos(x, 0, 0), Dir.East));
        history.EndGroup();
        history.Execute(new PlaceBuilding("seller", new GridPos(5, 0, 0), Dir.East));

        history.Undo();
        Assert.Equal(5, sim.World.EntityCount);
        history.Undo();
        Assert.Equal(0, sim.World.EntityCount);
        Assert.False(history.CanUndo);
    }

    [Fact]
    public void Undo_remove_rotate_and_move_restore_the_layout()
    {
        var sim = TestUtil.NewSim();
        var history = new EditHistory(sim);
        sim.Place("iron_miner", 0, 0, 0, Dir.East);
        sim.Place("ramp_up", 1, 0, 0, Dir.East);
        string before = Layout(sim);

        history.Execute(new RotateBuilding(new GridPos(0, 0, 0)));
        history.Execute(new MoveBuildings(new[] { new GridPos(1, 0, 1) }, new GridPos(2, 3, 0), 2, new GridPos(1, 0, 0)));
        history.Execute(new RemoveBuildings(new[] { new GridPos(0, 0, 0) }));
        Assert.NotEqual(before, Layout(sim));

        while (history.CanUndo) Assert.True(history.Undo().Ok);
        Assert.Equal(before, Layout(sim));

        while (history.CanRedo) Assert.True(history.Redo().Ok);
        Assert.Equal(1, sim.World.EntityCount);
    }

    private static string Layout(Simulation sim) =>
        string.Join(";", sim.World.Entities.OrderBy(e => e.Pos.X).ThenBy(e => e.Pos.Y).Select(e => $"{e.Def.Id}{e.Pos}{e.Facing}"));
}
