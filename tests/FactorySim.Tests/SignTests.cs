using FactorySim.Behaviors;
using FactorySim.Editing;
using FactorySim.Persistence;
using FactorySim.View;

namespace FactorySim.Tests;

/// <summary>Signs (idea G3): a line of text on a building that does nothing else.</summary>
public class SignTests
{
    private static string? TextAt(Simulation sim, int x, int y)
    {
        var e = sim.World.EntityAt(new GridPos(x, y, 0))!;
        return e.Behavior.Selection(e);
    }

    [Fact]
    public void A_sign_takes_one_clean_line_of_text()
    {
        var sim = TestUtil.NewSim();
        sim.Place("sign", 0, 0, 0, Dir.North);
        Assert.Null(TextAt(sim, 0, 0));

        Assert.True(sim.Execute(new SelectRecipe(new GridPos(0, 0, 0), "  Iron\nplates  ")).Ok);
        Assert.Equal("Iron plates", TextAt(sim, 0, 0));

        var tooLong = sim.Execute(new SelectRecipe(new GridPos(0, 0, 0), new string('x', 41)));
        Assert.False(tooLong.Ok);
        Assert.Contains("40", tooLong.Error);
        Assert.Equal("Iron plates", TextAt(sim, 0, 0));

        Assert.True(sim.Execute(new SelectRecipe(new GridPos(0, 0, 0), null)).Ok);
        Assert.Null(TextAt(sim, 0, 0));
    }

    [Fact]
    public void Undo_brings_the_old_text_back()
    {
        var sim = TestUtil.NewSim();
        var history = new EditHistory(sim);
        history.Execute(new PlaceBuilding("sign", new GridPos(0, 0, 0), Dir.North));
        history.Execute(new SelectRecipe(new GridPos(0, 0, 0), "Smelting"));
        history.Execute(new SelectRecipe(new GridPos(0, 0, 0), "Copper"));

        history.Undo();
        Assert.Equal("Smelting", TextAt(sim, 0, 0));
        history.Undo();
        Assert.Null(TextAt(sim, 0, 0));
        history.Redo();
        Assert.Equal("Smelting", TextAt(sim, 0, 0));

        history.Execute(new RemoveBuildings(new List<GridPos> { new(0, 0, 0) }));
        history.Undo();
        Assert.Equal("Smelting", TextAt(sim, 0, 0));
    }

    [Fact]
    public void Copies_and_saves_keep_the_text()
    {
        var sim = TestUtil.NewSim();
        sim.Place("sign", 0, 0, 0, Dir.North);
        sim.Execute(new SelectRecipe(new GridPos(0, 0, 0), "Main bus"));

        var bp = Blueprint.FromJson(Blueprint.FromEntities(sim.World.Entities, new GridPos(0, 0, 0)).ToJson());
        Assert.True(sim.Execute(new PlaceBlueprint(bp, new GridPos(5, 5, 0))).Ok);
        Assert.Equal("Main bus", TextAt(sim, 5, 5));

        var loaded = SaveSystem.Deserialize(SaveSystem.Serialize(sim), TestUtil.Content);
        Assert.Empty(loaded.Warnings);
        Assert.Equal("Main bus", TextAt(loaded.Simulation, 0, 0));
        Assert.Equal("Main bus", TextAt(loaded.Simulation, 5, 5));
    }

    [Fact]
    public void A_sign_never_counts_as_stopped_or_waiting()
    {
        var sim = TestUtil.NewSim();
        sim.Place("sign", 0, 0, 0, Dir.North);
        var e = sim.World.EntityAt(new GridPos(0, 0, 0))!;
        var status = e.Behavior.GetStatus(e);
        Assert.False(status.Working);
        Assert.Equal(IdleReason.None, status.Idle);

        var alerts = new AlertLog();
        for (int i = 0; i < 120; i++)
        {
            sim.Step(Simulation.TicksPerSecond);
            alerts.Observe(sim.World);
        }
        Assert.Empty(alerts.Entries);
    }

    [Fact]
    public void Text_too_long_in_a_save_is_cut_with_a_warning()
    {
        var sim = TestUtil.NewSim();
        sim.Place("sign", 0, 0, 0, Dir.North);
        var e = sim.World.EntityAt(new GridPos(0, 0, 0))!;
        ((SignState)e.State).Text = new string('y', 60);

        var loaded = SaveSystem.Deserialize(SaveSystem.Serialize(sim), TestUtil.Content);
        Assert.Single(loaded.Warnings);
        Assert.Equal(new string('y', 40), TextAt(loaded.Simulation, 0, 0));
    }
}
