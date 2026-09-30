using FactorySim.Editing;

namespace FactorySim.Tests;

/// <summary>Copying settings between buildings (idea H3).</summary>
public class SettingsCopyTests
{
    private static Entity At(Simulation sim, int x, int y) => sim.World.EntityAt(new GridPos(x, y, 0))!;

    [Fact]
    public void A_recipe_pastes_onto_every_machine_of_the_same_kind_as_one_undo_step()
    {
        var sim = TestUtil.NewSim();
        sim.Place("smelter", 2, 1, 0, Dir.South);
        sim.Place("smelter", 5, 1, 0, Dir.South);
        sim.Place("smelter", 8, 1, 0, Dir.South);
        Assert.True(sim.Execute(new SelectRecipe(new GridPos(2, 1, 0), "smelt_iron")).Ok);

        var copied = SettingsCopy.Capture(At(sim, 2, 1))!;
        Assert.Equal("smelt_iron", copied.Selection);
        var history = new EditHistory(sim);
        var (changed, error) = SettingsCopy.Apply(history, copied, sim.World.Entities);
        Assert.Null(error);
        Assert.Equal(2, changed); // the source already matches
        Assert.All(new[] { 2, 5, 8 }, x => Assert.Equal("smelt_iron", At(sim, x, 1).Behavior.Selection(At(sim, x, 1))));

        Assert.True(history.Undo().Ok);
        Assert.Null(At(sim, 5, 1).Behavior.Selection(At(sim, 5, 1)));
        Assert.Null(At(sim, 8, 1).Behavior.Selection(At(sim, 8, 1)));
        Assert.Equal("smelt_iron", At(sim, 2, 1).Behavior.Selection(At(sim, 2, 1)));
        Assert.False(history.CanUndo);
    }

    [Fact]
    public void Automatic_is_a_setting_too()
    {
        var sim = TestUtil.NewSim();
        sim.Place("smelter", 2, 1, 0, Dir.South);
        sim.Place("smelter", 5, 1, 0, Dir.South);
        Assert.True(sim.Execute(new SelectRecipe(new GridPos(5, 1, 0), "smelt_iron")).Ok);
        var copied = SettingsCopy.Capture(At(sim, 2, 1))!;
        Assert.Null(copied.Selection);
        Assert.Equal(1, SettingsCopy.Changes(copied, sim.World.Entities));
        SettingsCopy.Apply(new EditHistory(sim), copied, sim.World.Entities);
        Assert.Null(At(sim, 5, 1).Behavior.Selection(At(sim, 5, 1)));
    }

    [Fact]
    public void Splitter_filters_copy_output_by_output()
    {
        var sim = TestUtil.NewSim();
        sim.Place("splitter", 2, 2, 0, Dir.East);
        sim.Place("splitter", 2, 6, 0, Dir.East);
        Assert.True(sim.Execute(new SetFilter(new GridPos(2, 2, 0), 0, "iron_ore")).Ok);
        Assert.True(sim.Execute(new SetFilter(new GridPos(2, 2, 0), 2, "copper_ore")).Ok);

        var copied = SettingsCopy.Capture(At(sim, 2, 2))!;
        var history = new EditHistory(sim);
        Assert.Equal(1, SettingsCopy.Apply(history, copied, new[] { At(sim, 2, 6) }).Changed);
        var other = At(sim, 2, 6);
        Assert.Equal(At(sim, 2, 2).Behavior.Filters(At(sim, 2, 2)), other.Behavior.Filters(other));

        history.Undo();
        Assert.True(other.Behavior.Filters(other) is null || other.Behavior.Filters(other)!.All(f => f == null));
        // Copying a plain splitter clears the filters again.
        SettingsCopy.Apply(history, SettingsCopy.Capture(other)!, new[] { At(sim, 2, 2) });
        Assert.True(At(sim, 2, 2).Behavior.Filters(At(sim, 2, 2)) is null || At(sim, 2, 2).Behavior.Filters(At(sim, 2, 2))!.All(f => f == null));
    }

    [Fact]
    public void Buildings_of_another_kind_are_left_alone()
    {
        var sim = TestUtil.NewSim();
        sim.Place("smelter", 2, 1, 0, Dir.South);
        sim.Place("splitter", 2, 5, 0, Dir.East);
        sim.Place("conveyor", 6, 6, 0, Dir.East);
        Assert.True(sim.Execute(new SelectRecipe(new GridPos(2, 1, 0), "smelt_iron")).Ok);
        var copied = SettingsCopy.Capture(At(sim, 2, 1))!;
        Assert.Empty(SettingsCopy.Paste(copied, new[] { At(sim, 2, 5), At(sim, 6, 6) }));
        Assert.Null(SettingsCopy.Capture(At(sim, 6, 6))); // a belt has nothing to copy
    }

    [Fact]
    public void Sign_text_copies()
    {
        var sim = TestUtil.NewSim();
        sim.Place("sign", 2, 1, 0, Dir.South);
        sim.Place("sign", 4, 1, 0, Dir.South);
        Assert.True(sim.Execute(new SelectRecipe(new GridPos(2, 1, 0), "Iron line")).Ok);
        SettingsCopy.Apply(new EditHistory(sim), SettingsCopy.Capture(At(sim, 2, 1))!, sim.World.Entities);
        Assert.Equal("Iron line", At(sim, 4, 1).Behavior.Selection(At(sim, 4, 1)));
    }
}
