using FactorySim.Behaviors;
using FactorySim.Content;
using FactorySim.Persistence;

namespace FactorySim.Tests;

/// <summary>
/// The 4.9.0 content drop: a generic sustained production-rate goal and one optional steel challenge.
/// Removing the marked milestone leaves all previous content unchanged; the generic goal kind may stay.
/// </summary>
public class SustainedProductionGoalTests
{
    private static readonly ContentRegistry C = TestUtil.Content;
    private const string Milestone = "steel_rate";

    private static ContentRegistry Without()
    {
        var pack = ContentRegistry.ParsePack(ContentRegistry.BasePackJson());
        Assert.Equal(1, pack.Milestones.RemoveAll(m => m.Id == Milestone));
        return ContentRegistry.Build(BehaviorRegistry.CreateDefault(), new[] { pack });
    }

    private static void Second(Simulation sim, long produced)
    {
        if (produced > 0) sim.World.Stats.RecordProduced("steel", produced);
        sim.Step(sim.World.Tick == 0 ? 1 : Simulation.TicksPerSecond);
    }

    [Fact]
    public void Steel_goal_is_optional_and_pays_less_than_the_required_goods()
    {
        var goal = Assert.Single(C.Milestones, m => m.Id == Milestone);
        Assert.Equal("produced_rate", goal.Kind);
        Assert.Equal("steel", goal.Item);
        Assert.Equal(4, goal.Rate);
        Assert.Equal(30, goal.Target);
        double requiredGoodsValue = goal.Rate * goal.Target * C.ItemValue["steel"].Value;
        Assert.True(goal.Reward.ToDouble() <= requiredGoodsValue);
    }

    [Fact]
    public void Rate_goal_tolerates_one_recipe_boundary_second_but_resets_when_output_stops()
    {
        var sim = TestUtil.NewSim(goals: true);
        var goal = C.Milestones.Single(m => m.Id == Milestone);

        Second(sim, 6);
        Second(sim, 6);
        Second(sim, 0);
        Assert.Equal(3, sim.MilestoneProgress(goal));

        Second(sim, 0);
        Assert.Equal(0, sim.MilestoneProgress(goal));
        Assert.DoesNotContain(Milestone, sim.World.Milestones);
    }

    [Fact]
    public void Sustained_rate_progress_survives_save_and_completes_once()
    {
        var sim = TestUtil.NewSim(goals: true);
        var goal = C.Milestones.Single(m => m.Id == Milestone);
        for (int second = 0; second < 12; second++) Second(sim, 4);

        var loaded = SaveSystem.Deserialize(SaveSystem.Serialize(sim), C).Simulation;
        Assert.Equal(12, loaded.MilestoneProgress(goal));
        for (int second = 12; second < goal.Target; second++) Second(loaded, 4);

        Assert.Contains(Milestone, loaded.World.Milestones);
        Assert.Single(loaded.DrainEvents().OfType<MilestoneReached>(), e => e.Id == Milestone);
        Second(loaded, 4);
        Assert.DoesNotContain(loaded.DrainEvents().OfType<MilestoneReached>(), e => e.Id == Milestone);
    }

    [Fact]
    public void Removing_the_increment_leaves_all_previous_content_unchanged()
    {
        var without = Without();
        Assert.Equal(C.Items.Keys.OrderBy(id => id), without.Items.Keys.OrderBy(id => id));
        Assert.Equal(C.Recipes.Keys.OrderBy(id => id), without.Recipes.Keys.OrderBy(id => id));
        Assert.Equal(C.Buildings.Keys.OrderBy(id => id), without.Buildings.Keys.OrderBy(id => id));
        Assert.Equal(C.Milestones.Select(m => m.Id).Except(new[] { Milestone }).OrderBy(id => id),
            without.Milestones.Select(m => m.Id).OrderBy(id => id));
        Assert.Equal(C.Tiers.Select(t => (t.Name, t.Cost, t.RequiredEarnings, t.Description)),
            without.Tiers.Select(t => (t.Name, t.Cost, t.RequiredEarnings, t.Description)));
        Assert.Equal(C.Upgrades.Keys.OrderBy(id => id), without.Upgrades.Keys.OrderBy(id => id));
        foreach (var (item, info) in without.ItemValue) Assert.Equal(info, C.ItemValue[item]);
    }
}
