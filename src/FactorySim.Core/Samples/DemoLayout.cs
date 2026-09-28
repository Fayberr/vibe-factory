namespace FactorySim.Samples;

/// <summary>
/// A small reference factory used by the CLI, tests and the Godot client:
///  • Line A: iron miner → ramp up → bridge → ramp down → smelter → polisher → seller
///  • Line B: copper miner running south *under* line A's bridge → seller
///  • Line C: iron → smelter → alloy forge ← copper (side input) → seller
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

            // Line A (y = 2), bridging over line B at x = 2.
            Place("iron_miner", 0, 2, 0, Dir.East);
            Place("ramp_up", 1, 2, 0, Dir.East);
            Place("conveyor", 2, 2, 1, Dir.East);
            Place("ramp_down", 3, 2, 0, Dir.East);
            Place("smelter", 4, 2, 0, Dir.East);
            Place("conveyor", 5, 2, 0, Dir.East);
            Place("polisher", 6, 2, 0, Dir.East);
            Place("conveyor", 7, 2, 0, Dir.East);
            Place("seller", 8, 2, 0, Dir.East);

            // Line B (x = 2), passing under the bridge.
            Place("copper_miner", 2, 0, 0, Dir.South);
            for (int y = 1; y <= 4; y++) Place("conveyor", 2, y, 0, Dir.South);
            Place("seller", 2, 5, 0, Dir.South);

            // Line C (y = 8): merge two ingredients in the alloy forge.
            Place("iron_miner", 0, 8, 0, Dir.East);
            Place("conveyor", 1, 8, 0, Dir.East);
            Place("smelter", 2, 8, 0, Dir.East);
            Place("conveyor", 3, 8, 0, Dir.East);
            Place("alloy_forge", 4, 8, 0, Dir.East);
            Place("conveyor", 5, 8, 0, Dir.East);
            Place("seller", 6, 8, 0, Dir.East);
            Place("copper_miner", 4, 6, 0, Dir.South);
            Place("conveyor", 4, 7, 0, Dir.South);
        }
        finally
        {
            sim.World.Sandbox = sandbox;
        }
    }
}
