namespace FactorySim.Samples;

/// <summary>
/// A small reference factory used by the CLI, tests and the Godot client:
///  • Line A (y=4): iron drill → ramp up → bridge → ramp down → smelter → splitter →
///    three parallel polishers (curved branches) → merger → depot
///  • Line B (x=2): copper drill running south *under* line A's bridge, curving into…
///  • Line C (y=10): iron → smelter → alloy forge (copper from line B on its side) → depot
/// </summary>
public static class DemoLayout
{
    public static void Build(Simulation sim, GridPos origin = default, bool free = true)
    {
        bool sandbox = sim.World.Sandbox;
        if (free) sim.World.Sandbox = true;
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
            Place("conveyor", 2, 4, 1, Dir.East);
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

            // Line B: copper passes under the bridge, then curves east and south into the forge.
            Place("copper_miner", 2, 1, 0, Dir.South);
            for (int y = 2; y <= 7; y++) Place("conveyor", 2, y, 0, Dir.South);
            Place("conveyor", 2, 8, 0, Dir.East);
            Place("conveyor", 3, 8, 0, Dir.East);
            Place("conveyor", 4, 8, 0, Dir.South);
            Place("conveyor", 4, 9, 0, Dir.South);

            // Line C: merge two ingredients in the alloy forge.
            Place("iron_miner", 0, 10, 0, Dir.East);
            Place("conveyor", 1, 10, 0, Dir.East);
            Place("smelter", 2, 10, 0, Dir.East);
            Place("conveyor", 3, 10, 0, Dir.East);
            Place("alloy_forge", 4, 10, 0, Dir.East);
            Place("conveyor", 5, 10, 0, Dir.East);
            Place("conveyor", 6, 10, 0, Dir.East);
            Place("seller", 7, 10, 0, Dir.East);
        }
        finally
        {
            sim.World.Sandbox = sandbox;
        }
    }
}
