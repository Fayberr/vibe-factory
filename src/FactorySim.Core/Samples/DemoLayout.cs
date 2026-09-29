namespace FactorySim.Samples;

/// <summary>
/// A small reference factory used by the CLI, tests and the Godot client:
///  • Line A (y=4):  iron drill → ramp up → raised belt → ramp down → smelter → splitter →
///    three parallel polishers (curved branches) → merger → north to a depot on the rim
///  • Line B (x=2):  copper drill running south *under* line A's bridge → smelter → press (wire)
///    → east, then north to its own depot
///  • Line C (y=13): iron → smelter → press (plates) → workshop ← planks (lumber camp → sawmill)
///    → east, then north to the third depot
/// Depots take items in on one side and must stand on the edge of the plot, so every line ends
/// with a belt run to the rim that turns north into it. Unlocks the tiers it uses (up to Industry) and grows the plot to match.
/// </summary>
public static class DemoLayout
{
    public const int TierUsed = 2;

    public static void Build(Simulation sim, GridPos origin = default, bool free = true)
    {
        bool sandbox = sim.World.Sandbox;
        if (free) sim.World.Sandbox = true;
        if (sim.World.UnlockedTier < TierUsed)
        {
            sim.World.UnlockedTier = TierUsed;
            sim.GrowPlot(TierUsed);
        }
        try
        {
            void Place(string def, int x, int y, int z, Dir facing)
            {
                var r = sim.Execute(new PlaceBuilding(def, origin + new GridPos(x, y, z), facing));
                if (!r.Ok) throw new InvalidOperationException($"Demo layout: {def} at ({x},{y},{z}): {r.Error}");
            }

            // Goods leave at the edge of the plot, so each line ends with a column of belts
            // running north from (x, from) to the rim, where its depot takes them in facing out.
            // The layout is placed anywhere, so the edge is wherever the plot ends (local y).
            int rim = sim.World.Bounds.Min.Y - origin.Y;
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
            Tail(11, 4); // curves north and runs to the edge of the plot

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
