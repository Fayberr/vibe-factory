using FactorySim.Balance;
using FactorySim.Behaviors;
using FactorySim.Cli;
using FactorySim.Content;

namespace FactorySim.Tests;

/// <summary>
/// The balance tool must agree with the simulation, or it is worse than useless: every number it
/// prints is checked here against the content file or against a factory that actually runs.
/// </summary>
public class BalanceTests
{
    private static RecipeBook Book(int level = 1, PolishMode polish = PolishMode.None, int? tier = null) =>
        RecipeBook.Create(TestUtil.Content, new BalanceAssumptions { Level = level, Polish = polish }, tier);

    [Fact]
    public void Book_agrees_with_ItemValues_at_level_one()
    {
        var book = Book();
        var expected = ItemValues.Compute(TestUtil.Content);

        Assert.Equal(expected.Count, book.Sources.Count);
        foreach (var (item, info) in expected)
        {
            Assert.True(book.Sources.TryGetValue(item, out var source), $"{item} is missing from the book");
            Assert.Equal(info.Value, source!.Value, 9);
            Assert.Equal(info.Tier, source.Tier);
        }
    }

    [Fact]
    public void Iron_ingot_line_is_one_smelter_behind_one_drill()
    {
        var chain = ProductionChain.For(Book(tier: 0), "iron_ingot");

        Assert.Equal(1, chain.Rate);
        Assert.Equal(1, chain.Depth);
        var smelter = chain.Steps.Single(s => s.Item == "iron_ingot");
        Assert.Equal("smelter", smelter.Building.Id);
        Assert.Equal(0.8, smelter.Buildings, 9); // 16 ticks per craft at 20 ticks/s = 1.25/s
        Assert.Equal(1, smelter.Built);
        var ore = chain.Steps.Single(s => s.Item == "iron_ore");
        Assert.Equal(1, ore.Rate, 9);
        Assert.Equal(1, ore.Buildings, 9);
        Assert.Equal(4, ore.Allowed); // four drills at tier 0

        Assert.Equal(new Dictionary<string, double> { ["iron_ore"] = 1 }, chain.RawPerSecond);
        Assert.Equal(2, chain.SaleValue);        // $1 of ore, doubled by smelting
        Assert.Equal(2, chain.IncomePerSecond);
        Assert.Equal(new[] { "iron_ingot", "iron_ore" }, chain.Steps.Select(s => s.Item));
    }

    [Fact]
    public void Crate_pulls_logs_and_iron_ore_from_two_raw_resources()
    {
        var chain = ProductionChain.For(Book(tier: 2), "crate");

        Assert.Equal(3, chain.Depth); // ore -> ingot -> plate -> crate, and log -> plank -> crate
        Assert.Equal(1, chain.RawPerSecond["log"], 9);        // 2 planks per crate, 2 planks per log
        Assert.Equal(1, chain.RawPerSecond["iron_ore"], 9);   // one plate per crate, one ingot per plate
        Assert.Equal(2, chain.RawPerSecond.Count);
        Assert.Equal(1, chain.Steps.Single(s => s.Item == "crate").Rate);
        Assert.Equal(2, chain.Steps.Single(s => s.Item == "plank").Rate, 9);
    }

    [Fact]
    public void Raw_share_of_every_item_sums_to_one()
    {
        var book = Book();
        foreach (var source in book.Sources.Values)
        {
            Assert.True(source.RawShare.Count > 0, $"{source.Item} has no ores");
            Assert.Equal(1, source.RawShare.Values.Sum(), 9);
            Assert.All(source.RawShare, kv => Assert.True(kv.Value >= -1e-12, $"{source.Item} has a negative share of {kv.Key}"));
        }
    }

    [Fact]
    public void A_chain_that_runs_in_the_simulation_earns_what_the_tool_says()
    {
        // The tool's "1 iron ingot/s" line, built for real: one drill, one smelter, one depot.
        var sim = TestUtil.NewSim();
        sim.Place("iron_miner", 0, 0, 0, Dir.East);
        sim.Line(new GridPos(1, 0, 0), Dir.East, 2);
        sim.Place("smelter", 3, 0, 0, Dir.East);
        sim.Line(new GridPos(4, 0, 0), Dir.East, 2);
        sim.Place("seller", 6, 0, 0, Dir.East);

        var chain = ProductionChain.For(Book(tier: 0), "iron_ingot");
        sim.Step(1200); // fill the belts and settle
        double before = sim.World.Stats.TotalEarned.ToDouble();
        sim.Step(2000);
        double perSecond = (sim.World.Stats.TotalEarned.ToDouble() - before) / (2000.0 / Simulation.TicksPerSecond);

        Assert.Equal(chain.IncomePerSecond, perSecond, 1); // 2/s, measured 1 decimal
    }

    [Fact]
    public void Polishing_the_product_multiplies_what_the_line_earns()
    {
        var sim = TestUtil.NewSim();
        sim.Place("iron_miner", 0, 0, 0, Dir.East);
        sim.Line(new GridPos(1, 0, 0), Dir.East, 2);
        sim.Place("smelter", 3, 0, 0, Dir.East);
        sim.Line(new GridPos(4, 0, 0), Dir.East, 1);
        sim.Place("polisher", 5, 0, 0, Dir.East);
        sim.Line(new GridPos(6, 0, 0), Dir.East, 1);
        sim.Place("seller", 7, 0, 0, Dir.East);

        // The polisher is an in-line belt upgrade: ×1.5 value, once per item.
        var chain = ProductionChain.For(Book(1, PolishMode.Products), "iron_ingot");
        Assert.Equal(3, chain.SaleValue, 9);

        sim.Step(1200);
        double before = sim.World.Stats.TotalEarned.ToDouble();
        sim.Step(2000);
        double perSecond = (sim.World.Stats.TotalEarned.ToDouble() - before) / (2000.0 / Simulation.TicksPerSecond);

        Assert.Equal(chain.IncomePerSecond, perSecond, 1); // 3/s
    }

    [Fact]
    public void Levels_raise_speed_value_and_price_together()
    {
        var one = Book(1);
        var five = Book(5);

        // Belts: 1 + 0.5 per level above 1.
        Assert.Equal(1, one.Speed(one.Belt!));
        Assert.Equal(3, five.Speed(five.Belt!));
        Assert.Equal(4, one.BeltItemsPerSecond, 9);   // speed 50, spacing 250, 20 ticks/s
        Assert.Equal(12, five.BeltItemsPerSecond, 9);

        // Depots: 1 + 0.1 per level above 1, and a price that includes the upgrades.
        var seller = one.Depot!;
        Assert.Equal(1, one.ValueFactor(seller));
        Assert.Equal(1.4, five.ValueFactor(seller), 9);
        Assert.Equal(50, one.BuildCost(seller), 9);
        Assert.Equal(50 + 100 + 190 + 361 + 685.9, five.BuildCost(seller), 6); // $50, then ×2 growth 1.9 each

        // Above its maximum a building simply stops: a merger tops out at 7.
        var high = Book(12);
        var merger = TestUtil.Content.Building("merger");
        Assert.Equal(7, high.LevelOf(merger));
        Assert.Equal(4, high.Speed(merger), 9); // 1 + 0.5 × 6
    }

    [Fact]
    public void Polishing_every_step_compounds_through_the_chain()
    {
        var plain = Book(1, PolishMode.None);
        var all = Book(1, PolishMode.EveryStep);

        // Ore, then the ingot, then the plate: 1.5 × 2 × 1.5, then × 1.5 again.
        Assert.Equal(2, plain.Sources["iron_ingot"].Value, 9);
        Assert.Equal(4.5, all.Sources["iron_ingot"].Value, 9);
        Assert.Equal(3, plain.Sources["iron_plate"].Value, 9);
        Assert.Equal(10.125, all.Sources["iron_plate"].Value, 9);

        // Chains stay proportional: same machines, same belts, more money.
        var a = ProductionChain.For(plain, "iron_plate");
        var b = ProductionChain.For(all, "iron_plate");
        Assert.Equal(a.Steps.Select(s => s.Buildings), b.Steps.Select(s => s.Buildings));
        Assert.Equal(3.375, b.IncomePerSecond / a.IncomePerSecond, 9);
    }

    [Fact]
    public void Every_tier_has_a_factory_and_a_lower_bound_on_its_time()
    {
        var estimates = TierPacing.Estimate(TestUtil.Content, new BalanceAssumptions { Level = 1 });
        Assert.Equal(TestUtil.Content.Tiers.Count, estimates.Count);

        double previous = 0;
        for (int i = 0; i < estimates.Count; i++)
        {
            var e = estimates[i];
            Assert.True(e.IncomePerSecond > 0, $"{e.Name} earns nothing");
            Assert.True(e.SetupCost > 0, $"{e.Name} has no factory to build");
            Assert.All(e.IdleRaw, kv => Assert.InRange(kv.Value, 0, 1));
            Assert.InRange(e.CumulativeSeconds, previous - 1e-9, double.MaxValue);
            previous = e.CumulativeSeconds;

            bool last = i == estimates.Count - 1;
            Assert.Equal(last, double.IsNaN(e.Seconds));
            if (!last) Assert.True(e.Seconds >= 0);
        }

        // The first tier is the tutorial: a minute or two, not hours.
        Assert.InRange(estimates[0].Seconds, 30, 600);
        // Later tiers are longer, but never artificially so: nothing takes weeks at level 1.
        Assert.True(estimates[^1].CumulativeSeconds < 14 * 86400);
    }

    [Fact]
    public void A_recipe_that_makes_its_own_input_is_ignored()
    {
        // A pack can be silly without hanging the tool or inventing free items.
        var content = ContentRegistry.LoadDefault(null, new ContentPack
        {
            Items = { new ItemDef { Id = "ouroboros", Name = "Ouroboros" } },
            Buildings =
            {
                new BuildingDef
                {
                    Id = "cursed_machine", Name = "Cursed Machine", Behavior = "processor", Tier = 1,
                    Ports = new[] { new PortDef { Kind = PortKind.In, Side = Side.Back }, new PortDef { Kind = PortKind.Out, Side = Side.Front } },
                    Params = new ProcessorParams { Recipes = new[] { "ouroboros_loop" } },
                },
            },
            Recipes =
            {
                new RecipeDef
                {
                    Id = "ouroboros_loop",
                    Inputs = new[] { new ItemAmount("ouroboros", 1) },
                    Outputs = new[] { new ItemAmount("ouroboros", 1) },
                },
            },
        });

        var book = RecipeBook.Create(content);
        Assert.False(book.Sources.ContainsKey("ouroboros"));
        Assert.Throws<ArgumentException>(() => ProductionChain.For(book, "ouroboros"));
    }

    [Fact]
    public void A_miner_keeps_its_item_even_if_a_later_recipe_could_make_it()
    {
        var content = ContentRegistry.LoadDefault(null, new ContentPack
        {
            Buildings =
            {
                new BuildingDef
                {
                    Id = "ore_printer", Name = "Ore Printer", Behavior = "processor", Tier = 2,
                    Ports = new[] { new PortDef { Kind = PortKind.In, Side = Side.Back }, new PortDef { Kind = PortKind.Out, Side = Side.Front } },
                    Params = new ProcessorParams { Recipes = new[] { "print_iron" } },
                },
            },
            Recipes =
            {
                new RecipeDef
                {
                    Id = "print_iron", Ticks = 20, ValueMultiplier = 99,
                    Inputs = new[] { new ItemAmount("sand", 1) },
                    Outputs = new[] { new ItemAmount("iron_ore", 1) },
                },
            },
        });

        var book = RecipeBook.Create(content);
        var ore = book.Sources["iron_ore"];
        Assert.True(ore.IsExtracted);          // tier 0 mining beats a tier 2 recipe
        Assert.Equal(0, ore.Tier);
        Assert.Equal(1, ore.Value, 9);         // and its value did not jump to the printed one
    }

    [Fact]
    public void Reports_print_without_throwing()
    {
        var content = TestUtil.Content;
        var assumptions = new BalanceAssumptions { Level = 3, Polish = PolishMode.Products };
        var book = RecipeBook.Create(content, assumptions);

        Assert.False(string.IsNullOrWhiteSpace(BalanceCommand.TierReport(content, assumptions)));
        Assert.False(string.IsNullOrWhiteSpace(BalanceCommand.ItemReport(book)));
        Assert.False(string.IsNullOrWhiteSpace(BalanceCommand.ChainReport(book, "robot", 2.5)));
        Assert.Contains("Robot", BalanceCommand.ChainReport(book, "robot", 2.5));
        // A tier that cannot build the item says so instead of printing nonsense.
        Assert.Throws<ArgumentException>(() => BalanceCommand.ChainReport(RecipeBook.Create(content, unlockedTier: 1), "robot", 1));
    }

    [Fact]
    public void Items_are_found_by_id_or_name()
    {
        var book = Book();
        Assert.Equal("rocket_fuel", BalanceCommand.ResolveItem(book, "rocket fuel"));
        Assert.Equal("steel", BalanceCommand.ResolveItem(book, "Steel Beam"));
        Assert.Equal("steel", BalanceCommand.ResolveItem(book, "  STEEL  "));

        var error = Assert.Throws<ArgumentException>(() => BalanceCommand.ResolveItem(book, "cratez"));
        Assert.Contains("Did you mean", error.Message);
    }

    [Fact]
    public void Numbers_read_the_way_a_player_would_say_them()
    {
        Assert.Equal("3", BalanceCommand.Num(3));
        Assert.Equal("3.5", BalanceCommand.Num(3.5));
        Assert.Equal("1.50K", BalanceCommand.Num(1500));
        Assert.Equal("$1.50K", BalanceCommand.Money(1500));
        Assert.Equal("never", BalanceCommand.Num(double.PositiveInfinity));
        Assert.Equal("100%", BalanceCommand.Percent(1));
        Assert.Equal("45%", BalanceCommand.Percent(0.45));
        Assert.Equal("2m 5s", BalanceCommand.Duration(125));
        Assert.Equal("1h 1m", BalanceCommand.Duration(3660));
        Assert.Equal("2d 1h", BalanceCommand.Duration(176400));
        Assert.Equal("-", BalanceCommand.Duration(double.NaN));
    }
}
