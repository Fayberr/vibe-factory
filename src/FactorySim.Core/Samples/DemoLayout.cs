namespace FactorySim.Samples;

/// <summary>
/// A small reference factory used by the CLI, tests and the Godot client:
///  • Line A (y=4):  iron drill → ramp up → bridge belt → ramp down → smelter → splitter →
///    three parallel polishers (curved branches) → merger → depot
///  • Line B (x=2):  copper drill running south *under* line A's bridge → smelter → press (wire) → depot
///  • Line C (y=13): iron → smelter → press (plates) → workshop ← planks (lumber camp → sawmill) → depot
/// Unlocks the tiers it uses (up to Industry) and grows the plot to match.
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

            // Line A: bridge over line B, then split into three polishers and merge again.
            Place("iron_miner", 0, 4, 0, Dir.East);
            Place("ramp_up", 1, 4, 0, Dir.East);
            Place("bridge_belt", 2, 4, 1, Dir.East);
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
            Place("seller", 11, 4, 0, Dir.East);

            // Line B: copper passes under the bridge, then is smelted and drawn into wire.
            Place("copper_miner", 2, 1, 0, Dir.South);
            for (int y = 2; y <= 7; y++) Place("conveyor", 2, y, 0, Dir.South);
            Place("conveyor", 2, 8, 0, Dir.East);
            Place("smelter", 3, 8, 0, Dir.East);
            Place("conveyor", 4, 8, 0, Dir.East);
            Place("press", 5, 8, 0, Dir.East);
            Place("conveyor", 6, 8, 0, Dir.East);
            Place("seller", 7, 8, 0, Dir.East);

            // Line C: iron plates and planks merge in the workshop into crates.
            Place("iron_miner", 0, 13, 0, Dir.East);
            Place("conveyor", 1, 13, 0, Dir.East);
            Place("smelter", 2, 13, 0, Dir.East);
            Place("conveyor", 3, 13, 0, Dir.East);
            Place("press", 4, 13, 0, Dir.East);
            Place("conveyor", 5, 13, 0, Dir.East);
            Place("workshop", 6, 13, 0, Dir.East);
            Place("conveyor", 7, 13, 0, Dir.East);
            Place("seller", 8, 13, 0, Dir.East);
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
