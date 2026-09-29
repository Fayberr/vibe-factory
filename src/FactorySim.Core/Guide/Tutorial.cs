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
/// tests can play it through with commands. Keys a player can rebind appear as {action}
/// tokens ({rotate}, {progress}); <see cref="WithKeys"/> fills in the current keys.
/// </summary>
public static class Tutorial
{
    public static IReadOnlyList<TutorialStep> Steps { get; } = new TutorialStep[]
    {
        new("welcome", "Welcome to Vibe Factory",
            "Build machines, connect them with conveyors and sell what they make. Let's set up your first production line."),
        new("drill", "Place an Iron Drill",
            "Press 6 (or open the build menu with {build_menu}) and click on the ground. {rotate} rotates it; the orange arrow shows where the ore comes out.",
            w => w.CountOf("iron_miner") > 0, "slot:iron_miner"),
        new("belt", "Lay a conveyor",
            "Press 1, then drag from the drill to where the belt should go. Belts find their own way: around buildings, over other belts and into the machine you drag them to. Hold Shift to draw the path yourself.",
            w => Fed(w, "conveyor"), "slot:conveyor"),
        new("smelter", "Smelt the ore",
            "Raw ore sells for only a quarter of its value. Press 7 and place a Smelter at the end of the belt, facing the same way.",
            w => Fed(w, "smelter"), "slot:smelter"),
        new("depot", "Sell the ingots",
            "Press 9 and place a Market Depot on the blue rim around the edge of the map (the bottom edge of your plot is part of it), with its blue input side facing your factory, then lay a belt from the smelter out to it. Goods leave at the rim. A depot takes items in on one side only, that blue one, and turns by itself to take in the belt you run to it.",
            w => Fed(w, "seller"), "slot:seller"),
        new("earn", "Earn your first $10",
            "Items now flow from the drill through the smelter into the depot. Watch the money in the bottom left.",
            w => w.Stats.TotalEarned >= 10),
        new("upgrade", "Upgrade the drill",
            "Click the drill to open its Manage window and press Upgrade. Every building is upgraded on its own: faster drills, faster machines, better prices.",
            w => w.Entities.Any(e => e.Def.Id == "iron_miner" && e.Level >= 2), "upgrade"),
        new("bridge", "Crossing lines",
            "Drag a belt across another belt line and it bridges over it by itself. {height_up} and {height_down} change the build height by hand; the ladder next to the hotbar shows it.",
            Focus: "height"),
        new("progress", "Grow your factory",
            "Earn $750 in total to unlock the Workshop tier: copper, wood, presses and polishers, and more drills. Later tiers also ask you to sell the goods the tier before makes, so an early tier means running a full chain, not only earning. Press {progress} to see what the next tier wants. Your factory will also need room: while you build, every plot you do not own shows its price on the ground; click one next to yours to buy it.",
            Focus: "progress"),
        new("done", "You're ready",
            "Tip: machines that can make several things let you choose in their Manage window. You can reopen this tutorial from the Game menu ({game_menu})."),
    };

    /// <summary>Replaces each {action} token in <paramref name="text"/> with the key bound to it.</summary>
    public static string WithKeys(string text, Func<string, string> keyOf) =>
        System.Text.RegularExpressions.Regex.Replace(text, @"\{([a-z_]+)\}", m => keyOf(m.Groups[1].Value));

    /// <summary>A building of <paramref name="defId"/> that something feeds into.</summary>
    private static bool Fed(World w, string defId)
    {
        w.EnsureTopology();
        return w.Entities.Any(e => e.Def.Id == defId && e.Def.InputPorts.Any(e.IsInputFed));
    }
}
