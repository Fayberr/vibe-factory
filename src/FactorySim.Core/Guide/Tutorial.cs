namespace FactorySim.Guide;

/// <summary>
/// One tutorial step. <paramref name="Done"/> is checked against the world; steps without
/// it are read-and-continue. <paramref name="Focus"/> tells a frontend what to point at
/// ("slot:iron_miner" for a hotbar building, "upgrade", "progress", "height").
/// </summary>
public sealed record TutorialStep(string Id, string Title, string Text, Func<World, bool>? Done = null, string? Focus = null);

/// <summary>
/// The first-factory tutorial: drill → belt → smelter → depot, earning, upgrading, then
/// bridges and tiers. Pure data plus world checks, so any frontend can present it and
/// tests can play it through with commands.
/// </summary>
public static class Tutorial
{
    public static IReadOnlyList<TutorialStep> Steps { get; } = new TutorialStep[]
    {
        new("welcome", "Welcome to Vibe Factory",
            "Build machines, connect them with conveyors and sell what they make. Let's set up your first production line."),
        new("drill", "Place an Iron Drill",
            "Press 6 (or open the build menu with B) and click on the ground. R rotates it; the orange arrow shows where the ore comes out.",
            w => w.CountOf("iron_miner") > 0, "slot:iron_miner"),
        new("belt", "Lay a conveyor",
            "Press 1, then press on the ground in front of the drill's orange arrow and drag away from it. Belts follow the drag and turn corners by themselves.",
            w => Fed(w, "conveyor"), "slot:conveyor"),
        new("smelter", "Smelt the ore",
            "Raw ore sells for only a quarter of its value. Press 7 and place a Smelter at the end of the belt, facing the same way.",
            w => Fed(w, "smelter"), "slot:smelter"),
        new("depot", "Sell the ingots",
            "Press 9 and place a Market Depot where the smelter's orange arrow points (or lay a belt to it). A depot takes items in on one side only, the blue one, and turns to face the line you put it on.",
            w => Fed(w, "seller"), "slot:seller"),
        new("earn", "Earn your first $10",
            "Items now flow from the drill through the smelter into the depot. Watch the money in the bottom left.",
            w => w.Stats.TotalEarned >= 10),
        new("upgrade", "Upgrade the drill",
            "Click the drill to open its Manage window and press Upgrade. Every building is upgraded on its own: faster drills, faster machines, better prices.",
            w => w.Entities.Any(e => e.Def.Id == "iron_miner" && e.Level >= 2), "upgrade"),
        new("bridge", "Crossing lines",
            "Drag a belt straight across another belt and it bridges over it by itself. E and Q change the build height by hand; the ladder next to the hotbar shows it.",
            Focus: "height"),
        new("progress", "Grow your factory",
            "Earn $750 in total to unlock the Workshop tier: copper, wood, presses and polishers, a bigger plot and more drills. Press P to see your progress.",
            Focus: "progress"),
        new("done", "You're ready",
            "Tip: machines that can make several things let you choose in their Manage window. You can reopen this tutorial from the Game menu (G)."),
    };

    /// <summary>A building of <paramref name="defId"/> that something feeds into.</summary>
    private static bool Fed(World w, string defId)
    {
        w.EnsureTopology();
        return w.Entities.Any(e => e.Def.Id == defId && e.Def.InputPorts.Any(e.IsInputFed));
    }
}
