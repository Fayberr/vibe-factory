using System.Text.Json;
using FactorySim.Content;
using FactorySim.Editing;
using FactorySim.Persistence;

namespace FactorySim.Tests;

/// <summary>
/// The map is a fixed 5 x 5 grid of 15 x 15 plots. Everybody starts with the bottom middle plot and
/// buys the ones that share an edge with their land. A plot costs more the further it is from the
/// start (not the more you own), so the three plots next to the start cost the same.
/// </summary>
public class LandTests
{
    private static Simulation Sim(BigNum? money = null, bool sandbox = false) =>
        TestUtil.NewSim(sandbox: sandbox, money: money ?? 1e12, allLand: false);

    private static Land LandOf(Simulation sim) => sim.World.Land;

    // ---- The map and the start ---------------------------------------------------------------

    [Fact]
    public void The_map_is_five_by_five_plots_of_fifteen_cells()
    {
        var land = LandOf(Sim());
        Assert.Equal((5, 5, 15), (land.Columns, land.Rows, land.PlotSize));
        Assert.Equal(75, land.Width);
        Assert.Equal(75, land.Height);
        Assert.Equal(25, land.Count);
        Assert.Equal(new GridPos(0, 0, 0), Sim().World.Bounds.Min);
        Assert.Equal(74, Sim().World.Bounds.Max.X);
        Assert.Equal(74, Sim().World.Bounds.Max.Y);
    }

    [Fact]
    public void Everyone_starts_with_the_bottom_middle_plot_and_nothing_else()
    {
        var sim = Sim();
        var land = LandOf(sim);
        Assert.Equal(new PlotId(2, 4), land.Start);
        Assert.Equal(1, land.OwnedCount);
        Assert.Equal(new[] { new PlotId(2, 4) }, land.Owned);

        // Cells 30..44 across and 60..74 down.
        Assert.True(land.Owns(30, 60));
        Assert.True(land.Owns(44, 74));
        Assert.False(land.Owns(29, 60));
        Assert.False(land.Owns(45, 74));
        Assert.False(land.Owns(35, 59));
        Assert.Equal(land.Start, land.PlotAt(35, 65));
    }

    [Fact]
    public void A_new_game_never_depends_on_how_many_plots_you_own()
    {
        // Two worlds, one with more land: the start is where it is in both.
        var a = Sim();
        var b = Sim();
        Assert.True(b.Execute(new BuyPlot(1, 4)).Ok);
        Assert.Equal(LandOf(a).Start, LandOf(b).Start);
    }

    // ---- Prices ---------------------------------------------------------------------------------

    [Fact]
    public void The_start_is_free_and_the_three_plots_next_to_it_cost_the_same()
    {
        var land = LandOf(Sim());
        Assert.Equal(0, land.PriceOf(land.Start).ToDouble());
        var west = land.PriceOf(new PlotId(1, 4));
        var east = land.PriceOf(new PlotId(3, 4));
        var north = land.PriceOf(new PlotId(2, 3));
        Assert.Equal(west, east);
        Assert.Equal(west, north);
        Assert.True(west.ToDouble() > 0);
    }

    [Fact]
    public void Plots_further_from_the_start_cost_more()
    {
        var land = LandOf(Sim());
        var all = land.All().Where(p => p != land.Start).ToList();
        foreach (var p in all)
            foreach (var q in all)
            {
                if (land.Distance(p) == land.Distance(q)) Assert.Equal(land.PriceOf(p), land.PriceOf(q));
                if (land.Distance(p) < land.Distance(q)) Assert.True(land.PriceOf(p) < land.PriceOf(q), $"{p} vs {q}");
            }

        // The corner opposite the start is the furthest and the dearest.
        var far = land.PriceOf(new PlotId(0, 0));
        Assert.All(all, p => Assert.True(land.PriceOf(p) <= far));
    }

    [Fact]
    public void Plots_are_not_cheap()
    {
        // The first plot is a real decision: well above a drill and a smelter, and each ring more.
        var land = LandOf(Sim());
        var first = land.PriceOf(new PlotId(1, 4)).ToDouble();
        Assert.True(first >= 1000, $"first plot costs only {first}");
        var second = land.PriceOf(new PlotId(0, 4)).ToDouble();
        Assert.True(second >= first * 2, $"second ring costs {second}, the first {first}");
    }

    [Fact]
    public void A_price_does_not_change_when_you_buy_other_plots()
    {
        var sim = Sim();
        var land = LandOf(sim);
        var before = land.All().ToDictionary(p => p, land.PriceOf);
        Assert.True(sim.Execute(new BuyPlot(1, 4)).Ok);
        Assert.True(sim.Execute(new BuyPlot(2, 3)).Ok);
        Assert.True(sim.Execute(new BuyPlot(3, 4)).Ok);
        Assert.All(land.All(), p => Assert.Equal(before[p], land.PriceOf(p)));
    }

    [Fact]
    public void Prices_come_from_the_content()
    {
        var content = ContentRegistry.LoadDefault(null, new ContentPack { Map = new MapDef { PlotPrice = 100, PriceGrowth = 3 } });
        var land = Simulation.CreateNew(content, 0).World.Land;
        Assert.Equal(100, land.PriceOf(new PlotId(1, 4)).ToDouble());
        Assert.Equal(300, land.PriceOf(new PlotId(0, 4)).ToDouble());
        Assert.Equal(900, land.PriceOf(new PlotId(0, 3)).ToDouble());
    }

    // ---- Buying -------------------------------------------------------------------------------

    [Fact]
    public void Only_plots_that_share_an_edge_can_be_bought()
    {
        var sim = Sim();
        var land = LandOf(sim);

        // Cheapest first, and within a price row by row: the plot to the north, then west and east.
        Assert.Equal(new[] { new PlotId(2, 3), new PlotId(1, 4), new PlotId(3, 4) }, land.Buyable().ToArray());

        // A diagonal neighbour is not a neighbour.
        var diagonal = sim.Execute(new BuyPlot(1, 3));
        Assert.False(diagonal.Ok);
        Assert.Contains("share an edge", diagonal.Error);
        Assert.False(land.Owns(new PlotId(1, 3)));

        // Nor is anything further out.
        Assert.False(sim.Execute(new BuyPlot(2, 0)).Ok);
        Assert.False(sim.Execute(new BuyPlot(0, 4)).Ok);
    }

    [Fact]
    public void Buying_a_plot_opens_up_its_neighbours()
    {
        var sim = Sim();
        var land = LandOf(sim);
        Assert.DoesNotContain(new PlotId(2, 2), land.Buyable());
        Assert.True(sim.Execute(new BuyPlot(2, 3)).Ok);
        Assert.Contains(new PlotId(2, 2), land.Buyable());
        Assert.Contains(new PlotId(1, 3), land.Buyable());   // now shares an edge with (2,3)
        Assert.Contains(new PlotId(3, 3), land.Buyable());
        Assert.DoesNotContain(new PlotId(2, 3), land.Buyable());  // already yours
    }

    [Fact]
    public void Buying_costs_the_price_and_says_so()
    {
        var sim = Sim(money: 1e6);
        var price = LandOf(sim).PriceOf(new PlotId(1, 4));
        sim.DrainEvents();

        Assert.True(sim.Execute(new BuyPlot(1, 4)).Ok);
        Assert.Equal((BigNum)1e6 - price, sim.World.Money);
        Assert.True(LandOf(sim).Owns(20, 65));
        var bought = Assert.Single(sim.DrainEvents().OfType<PlotBought>());
        Assert.Equal((1, 4, price), (bought.Column, bought.Row, bought.Price));
    }

    [Fact]
    public void Buying_needs_the_money_and_changes_nothing_without_it()
    {
        var sim = Sim(money: 100);
        var r = sim.Execute(new BuyPlot(1, 4));
        Assert.False(r.Ok);
        Assert.Equal((BigNum)100, sim.World.Money);
        Assert.False(LandOf(sim).Owns(20, 65));
        Assert.Equal(1, LandOf(sim).OwnedCount);
    }

    [Fact]
    public void A_plot_cannot_be_bought_twice_or_from_off_the_map()
    {
        var sim = Sim();
        Assert.True(sim.Execute(new BuyPlot(1, 4)).Ok);
        var money = sim.World.Money;
        Assert.False(sim.Execute(new BuyPlot(1, 4)).Ok);
        Assert.False(sim.Execute(new BuyPlot(2, 4)).Ok);   // the start is yours from the beginning
        Assert.False(sim.Execute(new BuyPlot(-1, 4)).Ok);
        Assert.False(sim.Execute(new BuyPlot(5, 4)).Ok);
        Assert.False(sim.Execute(new BuyPlot(2, 5)).Ok);
        Assert.Equal(money, sim.World.Money);
    }

    [Fact]
    public void Sandbox_buys_for_free()
    {
        var sim = Sim(money: 0, sandbox: true);
        Assert.True(sim.Execute(new BuyPlot(1, 4)).Ok);
        Assert.Equal((BigNum)0, sim.World.Money);
    }

    [Fact]
    public void The_whole_map_can_be_bought_out()
    {
        var sim = Sim();
        var land = LandOf(sim);
        while (land.Buyable().Any())
        {
            var next = land.Buyable().First();
            Assert.True(sim.Execute(new BuyPlot(next.Column, next.Row)).Ok);
        }
        Assert.Equal(25, land.OwnedCount);
        Assert.Empty(land.Buyable());
    }

    // ---- Building on land ---------------------------------------------------------------------------

    [Fact]
    public void You_can_only_build_on_land_you_own()
    {
        var sim = Sim();
        Assert.True(sim.Execute(new PlaceBuilding("iron_miner", new GridPos(35, 65, 0), Dir.East)).Ok);

        var next = sim.Execute(new PlaceBuilding("iron_miner", new GridPos(20, 65, 0), Dir.East));
        Assert.False(next.Ok);
        Assert.Contains("not yours yet", next.Error);
        Assert.False(sim.Execute(new PlaceBuilding("conveyor", new GridPos(45, 74, 0), Dir.East)).Ok);

        Assert.True(sim.Execute(new BuyPlot(1, 4)).Ok);
        Assert.True(sim.Execute(new PlaceBuilding("iron_miner", new GridPos(20, 65, 0), Dir.East)).Ok);
    }

    [Fact]
    public void Sandbox_builds_on_all_of_the_map_and_nowhere_else()
    {
        var sim = Sim(sandbox: true);
        Assert.True(sim.Execute(new PlaceBuilding("iron_miner", new GridPos(2, 3, 0), Dir.East)).Ok);
        var off = sim.Execute(new PlaceBuilding("iron_miner", new GridPos(75, 3, 0), Dir.East));
        Assert.False(off.Ok);
        Assert.Contains("Outside the map", off.Error);
    }

    [Fact]
    public void Buildings_cannot_be_moved_onto_land_you_do_not_own()
    {
        var sim = Sim();
        Assert.True(sim.Execute(new PlaceBuilding("conveyor", new GridPos(31, 65, 0), Dir.East)).Ok);
        var r = sim.Execute(new MoveBuildings(new[] { new GridPos(31, 65, 0) }, new GridPos(-5, 0, 0)));
        Assert.False(r.Ok);
        Assert.Contains("not yours yet", r.Error);
        Assert.NotNull(sim.World.EntityAt(new GridPos(31, 65, 0)));
    }

    [Fact]
    public void A_belt_route_stays_on_your_land()
    {
        var sim = Sim();
        // The straight way from here to the goal leads over the plot to the north, which is not yours.
        var route = new BuildPlanner(sim).Route(new GridPos(31, 62, 0), new GridPos(31, 58, 0), firstLegX: false);
        Assert.True(route == null || route.All(c => sim.World.Land.Owns(c.X, c.Y)), "the route left your land");
    }

    // ---- Saving and loading ---------------------------------------------------------------------

    [Fact]
    public void Your_land_survives_a_save_and_load()
    {
        var sim = Sim();
        Assert.True(sim.Execute(new BuyPlot(1, 4)).Ok);
        Assert.True(sim.Execute(new BuyPlot(2, 3)).Ok);
        Assert.True(sim.Execute(new BuyPlot(2, 2)).Ok);

        var json = SaveSystem.Serialize(sim);
        var loaded = SaveSystem.Deserialize(json, TestUtil.Content);
        Assert.Empty(loaded.Warnings);
        Assert.Equal(LandOf(sim).Owned, LandOf(loaded.Simulation).Owned);
        Assert.Equal(json, SaveSystem.Serialize(loaded.Simulation));
        Assert.Equal(sim.World.Bounds, loaded.Simulation.World.Bounds);
    }

    [Fact]
    public void A_save_from_before_the_land_owns_the_plots_its_old_square_covered()
    {
        // Version 1 had one square that grew with the tiers, from the corner of the map (0,0).
        var sim = Sim(sandbox: true);
        sim.Place("iron_miner", 5, 5, 0, Dir.East);
        sim.Place("conveyor", 6, 5, 0, Dir.East);
        sim.Place("smelter", 7, 5, 0, Dir.East);
        sim.Place("seller", 8, 5, 0, Dir.East);
        var old = OldSave(sim, maxX: 23, maxY: 23);

        var loaded = SaveSystem.Deserialize(old, TestUtil.Content);
        Assert.Empty(loaded.Warnings);
        var world = loaded.Simulation.World;
        Assert.Equal(4, world.EntityCount);

        var owned = world.Land.Owned.ToHashSet();
        foreach (var p in new[] { new PlotId(0, 0), new PlotId(1, 0), new PlotId(0, 1), new PlotId(1, 1), world.Land.Start })
            Assert.Contains(p, owned);
        Assert.Equal(5, owned.Count);
        Assert.Equal(SaveSystem.CurrentVersion, JsonDocument.Parse(SaveSystem.Serialize(loaded.Simulation)).RootElement.GetProperty("version").GetInt32());
    }

    [Fact]
    public void Loading_never_drops_a_building_even_on_land_that_was_never_bought()
    {
        var sim = Sim(sandbox: true);
        sim.Place("iron_miner", 50, 10, 0, Dir.East);   // plot (3,0), nowhere near the start
        sim.Place("conveyor", 51, 10, 0, Dir.East);
        sim.World.Sandbox = false;

        var loaded = SaveSystem.Deserialize(SaveSystem.Serialize(sim), TestUtil.Content);
        Assert.Empty(loaded.Warnings);
        var world = loaded.Simulation.World;
        Assert.NotNull(world.EntityAt(new GridPos(50, 10, 0)));
        Assert.NotNull(world.EntityAt(new GridPos(51, 10, 0)));
        Assert.True(world.Land.Owns(50, 10));
        Assert.True(world.Land.Owns(world.Land.Start.Column * 15, world.Land.Start.Row * 15));
    }

    [Fact]
    public void A_very_large_old_factory_makes_the_map_grow_instead_of_being_cut_off()
    {
        var sim = Sim(sandbox: true);
        sim.Place("iron_miner", 5, 5, 0, Dir.East);
        var big = SaveWith(OldSave(sim, maxX: 99, maxY: 99), data =>
        {
            var first = data.Entities[0];
            data.Entities.Add(new EntitySave { Id = 900, Def = first.Def, Pos = new GridPos(90, 95, 0), Facing = first.Facing, State = first.State });
        });

        var loaded = SaveSystem.Deserialize(big, TestUtil.Content);
        var world = loaded.Simulation.World;
        Assert.NotNull(world.EntityAt(new GridPos(5, 5, 0)));
        Assert.NotNull(world.EntityAt(new GridPos(90, 95, 0)));
        Assert.True(world.Bounds.Max.X >= 99 && world.Bounds.Max.Y >= 99);
        Assert.True(world.Land.Columns >= 7 && world.Land.Rows >= 7);
        Assert.Equal(new PlotId(2, 4), world.Land.Start);   // the start does not move
        Assert.True(world.Land.Owns(90, 95));
    }

    [Fact]
    public void Land_bought_under_another_plot_size_is_kept_when_the_plots_change_size()
    {
        // A game saved when plots were 10 cells wide owned cells 60..79 across and 50..59 down.
        var sim = Sim();
        var json = SaveWith(SaveSystem.Serialize(sim), data =>
            data.Land = new LandSave { PlotSize = 10, Owned = new List<int[]> { new[] { 6, 5 }, new[] { 7, 5 } } });

        var world = SaveSystem.Deserialize(json, TestUtil.Content).Simulation.World;
        var owned = world.Land.Owned.ToHashSet();
        Assert.Contains(new PlotId(4, 3), owned);   // 60..74 across, 45..59 down
        Assert.Contains(new PlotId(5, 3), owned);   // 75..89 (grows the map to fit)
        Assert.Contains(world.Land.Start, owned);
        Assert.All(new[] { (60, 50), (79, 59) }, c => Assert.True(world.Land.Owns(c.Item1, c.Item2)));
    }

    // ---- The definition -----------------------------------------------------------------------------

    [Theory]
    [InlineData("""{ "map": { "plotSize": 2 } }""", "plots must be at least")]
    [InlineData("""{ "map": { "columns": 0 } }""", "at least one")]
    [InlineData("""{ "map": { "startColumn": 5 } }""", "start")]
    [InlineData("""{ "map": { "startRow": -1 } }""", "start")]
    [InlineData("""{ "map": { "plotPrice": -1 } }""", "price")]
    [InlineData("""{ "map": { "priceGrowth": 0.5 } }""", "growth")]
    public void A_broken_map_definition_is_rejected_at_load(string json, string expected)
    {
        var pack = JsonSerializer.Deserialize<ContentPack>(json, Json.Options)!;
        var ex = Assert.Throws<ContentException>(() => ContentRegistry.LoadDefault(null, pack));
        Assert.Contains(expected, ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_pack_can_reshape_the_map()
    {
        var content = ContentRegistry.LoadDefault(null, new ContentPack
        {
            Map = new MapDef { PlotSize = 20, Columns = 3, Rows = 3, StartColumn = 1, StartRow = 2 },
        });
        var world = Simulation.CreateNew(content, 0).World;
        Assert.Equal(60, world.Land.Width);
        Assert.Equal(59, world.Bounds.Max.X);
        Assert.Equal(new PlotId(1, 2), world.Land.Start);
        Assert.True(world.Land.Owns(30, 50));
    }

    // ---- Helpers -----------------------------------------------------------------------------------

    /// <summary>What a save from before the land looked like: version 1, no land, one square from the corner.</summary>
    private static string OldSave(Simulation sim, int maxX, int maxY) =>
        SaveWith(SaveSystem.Serialize(sim), data =>
        {
            data.Version = 1;
            data.Land = null;
            data.Sandbox = false;
            data.Bounds = new GridBounds(new GridPos(0, 0, 0), new GridPos(maxX, maxY, 4));
        });

    private static string SaveWith(string json, Action<SaveData> change)
    {
        var data = JsonSerializer.Deserialize<SaveData>(json, Json.Options)!;
        change(data);
        return JsonSerializer.Serialize(data, Json.Options);
    }
}
