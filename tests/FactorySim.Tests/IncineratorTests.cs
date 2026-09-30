using FactorySim.Behaviors;
using FactorySim.Content;
using FactorySim.Persistence;

namespace FactorySim.Tests;

/// <summary>
/// Incinerator content drop (4.5.0). Delete the base.json entry marked "Incinerator" and this file
/// to remove the increment. The general discarder behavior may stay registered for content packs.
/// </summary>
public class IncineratorTests
{
    private static readonly ContentRegistry C = TestUtil.Content;

    private static ContentRegistry Without()
    {
        var pack = ContentRegistry.ParsePack(ContentRegistry.BasePackJson());
        Assert.Equal(1, pack.Buildings.RemoveAll(b => b.Id == "incinerator"));
        return ContentRegistry.Build(BehaviorRegistry.CreateDefault(), new[] { pack });
    }

    [Fact]
    public void Incinerator_is_a_petroleum_tier_one_input_sink()
    {
        var def = C.Buildings["incinerator"];
        Assert.Equal(3, def.Tier);
        Assert.Equal(1000, def.Cost.ToDouble());
        Assert.Equal("discarder", def.Behavior);
        Assert.Single(def.InputPorts);
        Assert.Empty(def.OutputPorts);
        Assert.Equal(1, Assert.IsType<UpgradeTrack>(def.Upgrade).MaxLevel);
    }

    [Fact]
    public void Incinerator_destroys_any_item_without_money_or_sales()
    {
        var sim = TestUtil.NewSim(sandbox: false);
        sim.World.UnlockedTier = 3;
        sim.World.Money = 2000;
        sim.Place("incinerator", 0, 0, 0, Dir.East);
        var entity = sim.World.EntityAt(GridPos.Zero)!;
        var moneyAfterBuild = sim.World.Money;

        Assert.True(entity.Behavior.TryAccept(new TickContext(sim), entity,
            new ItemStack { Type = "tar", Count = 3, UnitValue = 7, ValueBonus = 2 }, 0, 0));

        var state = Assert.IsType<DiscarderState>(entity.State);
        Assert.Equal(3, state.Units);
        Assert.Equal(21, state.Value.ToDouble());
        Assert.Equal(moneyAfterBuild, sim.World.Money);
        Assert.Equal(0, sim.Sold("tar"));
        Assert.DoesNotContain(sim.DrainEvents(), e => e is ItemSold);
    }

    [Fact]
    public void Removing_the_increment_leaves_all_previous_content_unchanged()
    {
        var without = Without();
        Assert.Equal(C.Buildings.Keys.Except(new[] { "incinerator" }).OrderBy(id => id),
            without.Buildings.Keys.OrderBy(id => id));
        Assert.Equal(C.Items.Keys.OrderBy(id => id), without.Items.Keys.OrderBy(id => id));
        Assert.Equal(C.Recipes.Keys.OrderBy(id => id), without.Recipes.Keys.OrderBy(id => id));
        Assert.Equal(C.Tiers.Select(t => (t.Name, t.Cost, t.RequiredEarnings)),
            without.Tiers.Select(t => (t.Name, t.Cost, t.RequiredEarnings)));
        Assert.Equal(C.Milestones.Select(g => g.Id), without.Milestones.Select(g => g.Id));
        Assert.Equal(C.Upgrades.Keys.OrderBy(id => id), without.Upgrades.Keys.OrderBy(id => id));
        foreach (var (item, info) in without.ItemValue) Assert.Equal(info, C.ItemValue[item]);
    }
}
