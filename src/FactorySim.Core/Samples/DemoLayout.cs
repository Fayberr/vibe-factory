namespace FactorySim.Samples;

/// <summary>
/// A small reference factory used by the CLI, tests and the Godot client. It fits in the starting
/// plot exactly (14 x 14 cells, and the depots on the rim), so a new game can offer it before any
/// land is bought:
///  • Line A (local y=4):  iron drill → ramp up → raised belt → ramp down → smelter → splitter →
///    three parallel polishers (curved branches) → merger → out to a depot on the rim
///  • Line B (local x=2):  copper drill running *under* line A's bridge → smelter → press (wire)
///    → onward, then out to its own depot
///  • Line C (local y=13): iron → smelter → press (plates) → workshop ← planks (lumber camp → sawmill)
///    → onward, then out to the third depot
/// Depots take items in on one side and must stand on the edge of the map, so every line ends
/// with a belt run to the rim. The starting plot touches the south edge of the map, so the layout
/// is drawn flowing east with its depots to the north and then turned half way round: in the world
/// the lines flow west and the depots stand on the south rim. Unlocks the tiers it uses (up to Industry).
/// </summary>
public static class DemoLayout
{
    public const int TierUsed = 2;

    /// <summary>The layout occupies cells 0..Extent in both directions (before the depots' row).</summary>
    private const int Extent = 13;

    /// <summary>The north-west corner of the starting plot: where the layout is built unless told otherwise.</summary>
    public static GridPos StartCorner(Simulation sim)
    {
        var land = sim.World.Land;
        return new GridPos(land.Start.Column * land.PlotSize, land.Start.Row * land.PlotSize, 0);
    }

    /// <summary>Where the layout's local cell (x, y) ends up in the world, for finding a building of it.</summary>
    public static GridPos CellOf(Simulation sim, int x, int y, int z = 0, GridPos? origin = null) =>
        (origin ?? StartCorner(sim)) + new GridPos(Extent - x, Extent - y, z);

    public static void Build(Simulation sim, GridPos? origin = null, bool free = true)
    {
        var at = origin ?? StartCorner(sim);
        bool sandbox = sim.World.Sandbox;
        if (free) sim.World.Sandbox = true;
        if (sim.World.UnlockedTier < TierUsed) sim.World.UnlockedTier = TierUsed;
        try
        {
            // The layout is drawn flowing east, with the depots to its north; every building is
            // placed half a turn round (x and y mirrored through the centre, facing reversed).
            void Place(string def, int x, int y, int z, Dir facing)
            {
                var pos = CellOf(sim, x, y, z, at);
                var r = sim.Execute(new PlaceBuilding(def, pos, facing.Opposite()));
                if (!r.Ok) throw new InvalidOperationException($"Demo layout: {def} at ({x},{y},{z}) = {pos}: {r.Error}");
            }

            // Goods leave at the edge of the map, so each line ends with a column of belts running
            // out (drawn northward) from (x, from) to the rim, where its depot takes them in facing out.
            // The rim is wherever the south edge of the map is, in the layout's own coordinates.
            int rim = Extent - (sim.World.Bounds.Max.Y - at.Y);
            void Tail(int x, int from)
            {
                for (int y = from; y > rim; y--) Place("conveyor", x, y, 0, Dir.North);
                Place("seller", x, rim, 0, Dir.North);
            }

            // Line A: bridge over line B, then split into three polishers and merge again.
            Place("iron_miner", 0, 4, 0, Dir.East);
            Place("ramp_up", 1, 4, 0, Dir.East);
            Place("conveyor", 2, 4, 1, Dir.East);   // a belt at height 1 is the bridge
            Place("ramp_down", 3, 4, 0, Dir.East);
            Place("smelter", 4, 4, 0, Dir.East);
            Place("conveyor", 5, 4, 0, Dir.East);
            Place("splitter", 6, 4, 0, Dir.East);
            Place("polisher", 7, 4, 0, Dir.East);
            Place("conveyor", 8, 4, 0, Dir.East);
            foreach (var (y, turn) in new[] { (3, Dir.South), (5, Dir.North) })
            {
                Place("conveyor", 6, y, 0, Dir.East); // curves out of the splitter's side
                Place("polisher", 7, y, 0, Dir.East);
                Place("conveyor", 8, y, 0, Dir.East);
                Place("conveyor", 9, y, 0, turn);     // curves into the merger's side
            }
            Place("merger", 9, 4, 0, Dir.East);
            Place("conveyor", 10, 4, 0, Dir.East);
            Tail(11, 4); // curves out and runs to the edge of the map

            // Line B: copper passes under the bridge, then is smelted and drawn into wire.
            Place("copper_miner", 2, 1, 0, Dir.South);
            for (int y = 2; y <= 7; y++) Place("conveyor", 2, y, 0, Dir.South);
            Place("conveyor", 2, 8, 0, Dir.East);
            Place("smelter", 3, 8, 0, Dir.East);
            Place("conveyor", 4, 8, 0, Dir.East);
            Place("press", 5, 8, 0, Dir.East);
            for (int x = 6; x <= 11; x++) Place("conveyor", x, 8, 0, Dir.East);
            Tail(12, 8);

            // Line C: iron plates and planks merge in the workshop into crates.
            Place("iron_miner", 0, 13, 0, Dir.East);
            Place("conveyor", 1, 13, 0, Dir.East);
            Place("smelter", 2, 13, 0, Dir.East);
            Place("conveyor", 3, 13, 0, Dir.East);
            Place("press", 4, 13, 0, Dir.East);
            Place("conveyor", 5, 13, 0, Dir.East);
            Place("workshop", 6, 13, 0, Dir.East);
            for (int x = 7; x <= 12; x++) Place("conveyor", x, 13, 0, Dir.East);
            Tail(13, 13);
            Place("lumber_camp", 6, 10, 0, Dir.South);
            Place("sawmill", 6, 11, 0, Dir.South);
            Place("conveyor", 6, 12, 0, Dir.South);   // into the workshop's side
        }
        finally
        {
            sim.World.Sandbox = sandbox;
        }
    }
}
