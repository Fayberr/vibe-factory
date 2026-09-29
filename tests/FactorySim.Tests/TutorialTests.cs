using FactorySim.Guide;

namespace FactorySim.Tests;

public class TutorialTests
{
    [Fact]
    public void The_tutorial_can_be_played_through_with_the_starting_money()
    {
        var sim = TestUtil.NewSim(sandbox: false, money: TestUtil.Content.StartingMoney);
        var steps = Tutorial.Steps.ToDictionary(s => s.Id);
        bool Done(string id) => steps[id].Done!(sim.World);

        // The line sits at the east end of the map (75 wide), because a depot has to
        // stand on the map edge with its input facing the factory.
        var drillAt = new GridPos(70, 0, 0);
        Assert.False(Done("drill"));
        sim.Place("iron_miner", drillAt.X, drillAt.Y, drillAt.Z, Dir.East);
        Assert.True(Done("drill"));

        Assert.False(Done("belt"));
        sim.Line(new GridPos(71, 0, 0), Dir.East, 2);
        Assert.True(Done("belt"));

        Assert.False(Done("smelter"));
        sim.Place("smelter", 73, 0, 0, Dir.East);
        Assert.True(Done("smelter"));

        Assert.False(Done("depot"));
        sim.Place("seller", 74, 0, 0, Dir.East);
        Assert.True(Done("depot"));

        Assert.False(Done("earn"));
        sim.Step(20 * 15);
        Assert.True(Done("earn"));

        Assert.False(Done("upgrade"));
        Assert.True(sim.Execute(new SetBuildingLevels(new[] { new LevelChange(drillAt, 2) })).Ok);
        Assert.True(Done("upgrade"));
    }

    [Fact]
    public void Key_tokens_are_filled_in_with_the_current_keys()
    {
        var text = Tutorial.Steps.Single(s => s.Id == "drill").Text;
        Assert.Contains("{rotate}", text);
        var filled = Tutorial.WithKeys(text, id => id == "rotate" ? "T" : "?");
        Assert.Contains("T rotates it", filled);
        Assert.DoesNotContain("{", string.Concat(Tutorial.Steps.Select(s => Tutorial.WithKeys(s.Text, _ => "K"))));
    }

    [Fact]
    public void Steps_are_unique_and_the_hotbar_ones_name_real_buildings()
    {
        Assert.Equal(Tutorial.Steps.Count, Tutorial.Steps.Select(s => s.Id).Distinct().Count());
        foreach (var s in Tutorial.Steps.Where(s => s.Focus?.StartsWith("slot:") == true))
            Assert.True(TestUtil.Content.Buildings.ContainsKey(s.Focus!["slot:".Length..]), s.Id);
        Assert.Null(Tutorial.Steps[0].Done);  // starts with a message
        Assert.Null(Tutorial.Steps[^1].Done); // and ends with one
    }
}
