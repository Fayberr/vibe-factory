using FactorySim.Balance;

namespace FactorySim.Tests;

/// <summary>The in-game chain and ratio helper (idea F9): the balance tool asked about the player's factory.</summary>
public class LinePlannerTests
{
    [Fact]
    public void A_new_factory_can_only_plan_what_it_has_unlocked()
    {
        var sim = TestUtil.NewSim(sandbox: false);
        var book = LinePlanner.BookFor(sim.World);

        var items = LinePlanner.Plannable(book);
        Assert.Contains("iron_ingot", items);
        Assert.DoesNotContain("iron_ore", items); // raw resources are not a line to plan
        Assert.DoesNotContain("steel", items);
        Assert.Null(LinePlanner.Plan(book, "steel", 1));
    }

    [Fact]
    public void Sandbox_sees_every_tier_and_the_list_starts_with_the_earliest()
    {
        var sim = TestUtil.NewSim(sandbox: true);
        var book = LinePlanner.BookFor(sim.World);
        var items = LinePlanner.Plannable(book);

        Assert.Contains("steel", items);
        Assert.Contains("satellite", items);
        Assert.Equal("iron_ingot", items[0]);
        var tiers = items.Select(i => book.Sources[i].Tier).ToList();
        Assert.Equal(tiers.OrderBy(t => t), tiers);
    }

    [Fact]
    public void A_plan_says_how_many_of_each_building_the_line_needs()
    {
        var sim = TestUtil.NewSim(sandbox: true);
        var plan = LinePlanner.Plan(LinePlanner.BookFor(sim.World), "steel", 2)!;

        // Same numbers as `balance item steel 2 --tier 2`: 3 furnaces, 2 smelters, 3 coal and 2 iron drills.
        Assert.Equal(3, plan.Steps.Single(s => s.Item == "steel").Built);
        Assert.Equal(2, plan.Steps.Single(s => s.Item == "iron_ingot").Built);
        Assert.Equal(2, plan.Steps.Single(s => s.Item == "iron_ore").Built);
        Assert.Equal(2, plan.RawPerSecond["iron_ore"], 6);
        Assert.Equal(2, plan.RawPerSecond["coal"], 6);
    }

    [Fact]
    public void Higher_levels_need_no_more_buildings_and_polish_is_left_out()
    {
        var sim = TestUtil.NewSim(sandbox: true);
        var low = LinePlanner.Plan(LinePlanner.BookFor(sim.World, level: 1), "circuit", 1)!;
        var high = LinePlanner.Plan(LinePlanner.BookFor(sim.World, level: 5), "circuit", 1)!;

        Assert.True(high.Steps.Sum(s => s.Built) < low.Steps.Sum(s => s.Built));
        Assert.All(high.Steps, h => Assert.True(h.Built <= low.Steps.Single(l => l.Item == h.Item).Built));
        Assert.Equal(PolishMode.None, LinePlanner.BookFor(sim.World).Assumptions.Polish);
    }

    [Fact]
    public void Nothing_to_plan_for_a_zero_rate()
    {
        var sim = TestUtil.NewSim(sandbox: true);
        Assert.Null(LinePlanner.Plan(LinePlanner.BookFor(sim.World), "steel", 0));
    }
}
