using FactorySim.Behaviors;
using FactorySim.Content;
using FactorySim.Persistence;
using FactorySim.View;

namespace FactorySim.Tests;

/// <summary>
/// The away report (idea F1): what the offline catch-up says about the time away. Per product rates and
/// earnings, the buildings that sat waiting and why, and science packs extrapolated like money.
/// </summary>
public class AwayReportTests
{
    private static readonly DateTimeOffset At = new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Drill → belt → smelter → belt → depot, all at level 1.</summary>
    private static Simulation SmeltLine(ContentRegistry? content = null, bool goals = false)
    {
        var sim = TestUtil.NewSim(content, goals: goals);
        sim.Place("iron_miner", 2, 2, 0, Dir.East);
        sim.Place("conveyor", 3, 2, 0, Dir.East);
        sim.Place("smelter", 4, 2, 0, Dir.East);
        sim.Place("conveyor", 5, 2, 0, Dir.East);
        sim.Place("seller", 6, 2, 0, Dir.East);
        return sim;
    }

    [Fact]
    public void Sampling_does_not_change_the_catch_up()
    {
        var plain = SmeltLine();
        var reported = SmeltLine();
        plain.Step(20 * 90);
        var report = reported.CatchUp(90);

        Assert.Equal(0, report.ExtrapolatedTicks);
        Assert.Equal(SaveSystem.Serialize(plain, At), SaveSystem.Serialize(reported, At));
    }

    [Fact]
    public void Products_are_listed_with_their_rates_and_what_they_earned()
    {
        var sim = SmeltLine();
        var report = sim.CatchUp(3600);

        Assert.Equal(300, report.MeasuredSeconds); // the second half of the 10 simulated minutes
        var ingot = Assert.Single(report.Items, i => i.Item == "iron_ingot");
        Assert.Equal(ingot, report.Items[0]); // the only thing sold, so the best earner
        Assert.InRange(ingot.SoldPerMinute, 59, 61); // one ore a second from a level 1 drill
        Assert.InRange(ingot.MadePerMinute, 59, 61);
        var ore = Assert.Single(report.Items, i => i.Item == "iron_ore");
        Assert.InRange(ore.MadePerMinute, 59, 61);
        Assert.Equal(0, ore.SoldPerMinute);

        // Nothing but sales here, so the lines add up to the money credited.
        var sum = report.Items.Aggregate(BigNum.Zero, (s, i) => s + i.Earned);
        Assert.InRange(sum.ToDouble() / report.Earned.ToDouble(), 0.999999, 1.000001);
        Assert.True(report.Rewards.IsZero);
        Assert.Empty(report.Problems); // the line keeps up: nothing waited long enough to count
    }

    [Fact]
    public void Order_and_goal_rewards_are_the_part_no_product_explains()
    {
        var sim = SmeltLine(goals: true);
        var report = sim.CatchUp(3600);

        Assert.True(sim.World.Milestones.Count > 0); // the first sales reach a goal or two
        Assert.True(report.Rewards > BigNum.Zero);
        var sum = report.Items.Aggregate(report.Rewards, (s, i) => s + i.Earned);
        Assert.InRange(sum.ToDouble() / report.Earned.ToDouble(), 0.999999, 1.000001);
    }

    [Fact]
    public void Starved_and_blocked_buildings_are_ranked_with_the_reason()
    {
        var sim = TestUtil.NewSim();
        // A blast furnace that gets iron ingots and never coal: it starves, and so everything behind it
        // backs up (the smelter's output fills, then the drill's belt).
        sim.Place("iron_miner", 2, 2, 0, Dir.East);
        sim.Place("conveyor", 3, 2, 0, Dir.East);
        sim.Place("smelter", 4, 2, 0, Dir.East);
        sim.Place("conveyor", 5, 2, 0, Dir.East);
        sim.Place("blast_furnace", 6, 2, 0, Dir.East);
        // Two drills into belts that lead nowhere.
        foreach (int y in new[] { 6, 8 })
        {
            sim.Place("iron_miner", 2, y, 0, Dir.East);
            sim.Line(new GridPos(3, y, 0), Dir.East, 3);
        }

        var report = sim.CatchUp(3600);

        var furnace = Assert.Single(report.Problems, p => p.Building == "blast_furnace");
        Assert.Equal(IdleReason.Starved, furnace.Reason);
        Assert.Equal("waiting for Coal", furnace.Detail);
        Assert.Equal(1, furnace.Count);
        Assert.Equal(1.0, furnace.IdleShare, 6);
        Assert.Equal(new GridPos(6, 2, 0), furnace.ExamplePos);

        var smelter = Assert.Single(report.Problems, p => p.Building == "smelter");
        Assert.Equal((IdleReason.Blocked, "output full"), (smelter.Reason, smelter.Detail));

        var drills = Assert.Single(report.Problems, p => p.Building == "iron_miner");
        Assert.Equal((IdleReason.Blocked, "output blocked", 3), (drills.Reason, drills.Detail, drills.Count));
        Assert.Equal(drills, report.Problems[0]); // three drills lost the most time
    }

    [Fact]
    public void A_status_says_whether_a_machine_is_starved_or_blocked()
    {
        var sim = TestUtil.NewSim();
        sim.Place("smelter", 4, 2, 0, Dir.East);
        var smelter = sim.World.EntityAt(new GridPos(4, 2, 0))!;
        sim.Step(5);
        Assert.Equal(new EntityStatus(false, 0, "no input", IdleReason.Starved), smelter.Behavior.GetStatus(smelter));

        sim.Place("iron_miner", 2, 2, 0, Dir.East);
        sim.Place("conveyor", 3, 2, 0, Dir.East);
        sim.Step(20 * 5);
        // Fed once a second and done in 0.8 s: working most ticks, starved (never blocked) in between.
        int working = 0;
        for (int t = 0; t < 20; t++)
        {
            sim.Step();
            var status = smelter.Behavior.GetStatus(smelter);
            if (status.Working) working++;
            Assert.Equal(status.Working ? IdleReason.None : IdleReason.Starved, status.Idle);
        }
        Assert.InRange(working, 14, 18);

        sim.Step(20 * 30); // nothing takes the ingots
        var full = smelter.Behavior.GetStatus(smelter);
        Assert.False(full.Working);
        Assert.Equal((IdleReason.Blocked, "output full"), (full.Idle, full.Detail));
    }

    /// <summary>A pack of its own, so this survives the research trial being removed from base.json.</summary>
    private static readonly ContentRegistry WithPacks = ContentRegistry.LoadDefault(null, ContentRegistry.ParsePack("""
        {
          "items": [ { "id": "test_pack", "name": "Test Pack", "baseValue": 1, "science": true } ],
          "buildings": [
            { "id": "test_pack_source", "behavior": "miner", "ports": [ { "kind": "out", "side": "front" } ],
              "params": { "item": "test_pack", "interval": 5, "amount": 1 } },
            { "id": "test_lab", "behavior": "lab", "ports": [ { "kind": "in", "side": "back" } ],
              "params": { "items": [ "test_pack" ], "interval": 80, "capacity": 10 } }
          ]
        }
        """));

    [Fact]
    public void Packs_are_extrapolated_like_money()
    {
        var sim = TestUtil.NewSim(WithPacks);
        sim.Place("test_pack_source", 3, 5, 0, Dir.East);
        sim.Place("conveyor", 4, 5, 0, Dir.East);
        sim.Place("test_lab", 5, 5, 0, Dir.East);

        var report = sim.CatchUp(3600);

        // One pack every 4 seconds for an hour is 900, less the first pack's trip.
        long banked = sim.World.ScienceOf("test_pack");
        Assert.InRange(banked, 890, 900);
        Assert.Equal(banked, report.Science["test_pack"]);
        Assert.True(report.ExtrapolatedTicks > 0);

        var half = TestUtil.NewSim(WithPacks);
        half.Place("test_pack_source", 3, 5, 0, Dir.East);
        half.Place("conveyor", 4, 5, 0, Dir.East);
        half.Place("test_lab", 5, 5, 0, Dir.East);
        half.CatchUp(3600, new OfflineOptions { Efficiency = 0.5 });
        long simulated = 149; // what 10 simulated minutes bank, give or take the trip
        Assert.InRange(half.World.ScienceOf("test_pack"), simulated + (banked - simulated) / 2 - 2, simulated + (banked - simulated) / 2 + 2);
    }

    [Fact]
    public void A_waiting_lab_is_starved_not_blocked()
    {
        var sim = TestUtil.NewSim(WithPacks);
        sim.Place("test_lab", 5, 5, 0, Dir.East);
        var lab = sim.World.EntityAt(new GridPos(5, 5, 0))!;
        Assert.Equal(IdleReason.Starved, lab.Behavior.GetStatus(lab).Idle);
    }
}
