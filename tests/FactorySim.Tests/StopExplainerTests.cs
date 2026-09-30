using FactorySim.View;

namespace FactorySim.Tests;

/// <summary>The "why is this stopped" explainer (idea H6).</summary>
public class StopExplainerTests
{
    private static StopExplanation? At(Simulation sim, int x, int y) => StopExplainer.Explain(sim.World, sim.World.EntityAt(new GridPos(x, y, 0))!);

    [Fact]
    public void A_machine_with_no_drill_is_told_which_building_to_add()
    {
        var sim = TestUtil.NewSim();
        sim.Place("smelter", 2, 1, 0, Dir.South);
        Assert.True(sim.Execute(new SelectRecipe(new GridPos(2, 1, 0), "smelt_iron")).Ok);
        sim.Step(20);

        var why = At(sim, 2, 1)!;
        Assert.Equal("Waiting for Iron Ore", why.Headline);
        Assert.Equal(new[] { "iron_ore" }, why.Items);
        var line = Assert.Single(why.Lines);
        Assert.Contains("You have none yet", line);
        Assert.Contains(sim.Content.Buildings["iron_miner"].Name, line);
    }

    [Fact]
    public void On_automatic_with_nothing_in_it_names_what_each_recipe_starts_from()
    {
        var sim = TestUtil.NewSim();
        sim.Place("smelter", 2, 1, 0, Dir.South);
        sim.Step(20);
        var why = At(sim, 2, 1)!;
        Assert.Equal("Waiting for input", why.Headline);
        Assert.Contains("iron_ore", why.Items);
        Assert.Contains("copper_ore", why.Items);
        Assert.True(why.Items.Count <= StopExplainer.MaxItems);
    }

    [Fact]
    public void A_working_machine_a_quiet_belt_and_a_depot_need_no_explaining()
    {
        var sim = TestUtil.NewSim();
        sim.Place("iron_miner", 2, 0, 0, Dir.South);
        sim.Place("smelter", 2, 1, 0, Dir.South);
        sim.Place("seller", 2, 2, 0, Dir.South);
        sim.Place("conveyor", 6, 6, 0, Dir.East);
        for (int i = 0; i < 40; i++)
        {
            sim.Step(Simulation.TicksPerSecond / 2);
            var smelter = sim.World.EntityAt(new GridPos(2, 1, 0))!;
            var why = At(sim, 2, 1);
            // Working: nothing to say. Between two items of this underfed line: which input and where it comes from.
            if (smelter.Behavior.GetStatus(smelter).Working) Assert.Null(why);
            else Assert.Equal("iron_ore", why!.Items[0]); // of the smelter's three ores, the one this factory mines
            Assert.Null(At(sim, 6, 6));
            Assert.Null(At(sim, 2, 2));
        }
    }

    [Fact]
    public void A_longer_view_explains_a_machine_that_happens_to_run_right_now()
    {
        var sim = TestUtil.NewSim();
        sim.Place("iron_miner", 2, 0, 0, Dir.South);
        sim.Place("smelter", 2, 1, 0, Dir.South);
        sim.Place("seller", 2, 2, 0, Dir.South);
        var smelter = sim.World.EntityAt(new GridPos(2, 1, 0))!;
        for (int i = 0; i < 400 && !smelter.Behavior.GetStatus(smelter).Working; i++) sim.Step(1);
        Assert.True(smelter.Behavior.GetStatus(smelter).Working);

        Assert.Null(StopExplainer.Explain(sim.World, smelter));
        var why = StopExplainer.Explain(sim.World, smelter, IdleReason.Starved)!;
        Assert.Equal("Waiting for Iron Ore", why.Headline);
        Assert.StartsWith("Made by", why.Lines[0]);
        Assert.Equal("Its output backs up", StopExplainer.Explain(sim.World, smelter, IdleReason.Blocked)!.Headline);
    }

    [Fact]
    public void A_stopped_drill_is_reported_as_stopped_too()
    {
        var sim = TestUtil.NewSim();
        sim.Place("iron_miner", 2, 0, 0, Dir.South); // points into nothing, so it jams
        sim.Place("smelter", 5, 1, 0, Dir.South);
        Assert.True(sim.Execute(new SelectRecipe(new GridPos(5, 1, 0), "smelt_iron")).Ok);
        sim.Step(20 * Simulation.TicksPerSecond);

        var why = At(sim, 5, 1)!;
        Assert.Contains("you have 1, stopped too", Assert.Single(why.Lines));
    }

    [Fact]
    public void A_full_output_says_where_the_item_could_go()
    {
        var sim = TestUtil.NewSim();
        sim.Place("iron_miner", 2, 0, 0, Dir.South);
        sim.Place("smelter", 2, 1, 0, Dir.South);
        sim.Step(60 * Simulation.TicksPerSecond);

        var why = At(sim, 2, 1)!;
        Assert.Equal("Output full: nothing takes its Iron Ingot", why.Headline);
        Assert.Equal(new[] { "iron_ingot" }, why.Items);
        Assert.Contains(why.Lines, l => l.Contains("depot"));
        Assert.Equal("Output full: nothing takes its Iron Ore", At(sim, 2, 0)!.Headline); // the drill behind it
    }

    [Fact]
    public void A_maker_behind_a_tier_names_the_tier()
    {
        var sim = TestUtil.NewSim();
        // A tier 0 machine's input that only a later-tier building makes.
        var content = sim.Content;
        var (user, item) = content.BuildingList
            .Where(d => d.Tier == 0 && d.Params is FactorySim.Behaviors.ProcessorParams)
            .SelectMany(d => content.Recipes.Values.Where(r => ((FactorySim.Behaviors.ProcessorParams)d.Params!).Recipes.Contains(r.Id))
                .SelectMany(r => r.Inputs.Select(i => (d, i.Item))))
            .FirstOrDefault(pair => content.BuildingList.Any(b => b.Tier > 0 && Makes(b, pair.Item)) && !content.BuildingList.Any(b => b.Tier == 0 && Makes(b, pair.Item)));
        Assert.NotNull(user); // the base content has one (gold ore for the smelter, say)

        sim.World.Sandbox = true;
        sim.Place(user.Id, 2, 1, 0, Dir.South);
        sim.World.Sandbox = false;
        var recipe = content.Recipes.Values.First(r => ((FactorySim.Behaviors.ProcessorParams)user.Params!).Recipes.Contains(r.Id) && r.Inputs.Any(i => i.Item == item));
        Assert.True(sim.Execute(new SelectRecipe(new GridPos(2, 1, 0), recipe.Id)).Ok);
        sim.Step(20);
        Assert.Contains(At(sim, 2, 1)!.Lines, l => l.Contains("unlocks at tier"));
    }

    private static bool Makes(FactorySim.Content.BuildingDef def, string item) => def.Params switch
    {
        FactorySim.Behaviors.ProcessorParams p => TestUtil.Content.Recipes.Values.Any(r => p.Recipes.Contains(r.Id) && r.Outputs.Any(o => o.Item == item)),
        FactorySim.Behaviors.MinerParams m => m.Item == item,
        _ => false,
    };
}
