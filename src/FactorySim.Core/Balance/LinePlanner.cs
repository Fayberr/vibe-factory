namespace FactorySim.Balance;

/// <summary>
/// The in-game chain and ratio helper (idea F9): the balance tool's <see cref="ProductionChain"/>, asked
/// about the player's own factory. It sees what the factory has unlocked (everything in sandbox), assumes
/// every building at one chosen level and no polishing, and ignores research bonuses, so its answer is a
/// plain, slightly cautious plan: "for 2 steel a second, build 4 smelters and 2 blast furnaces".
///
/// Pure calculation, like the rest of the balance layer; it never touches the simulation.
/// </summary>
public static class LinePlanner
{
    /// <summary>The recipe book as the factory stands: its unlocked tier, buildings at <paramref name="level"/>.</summary>
    public static RecipeBook BookFor(World world, int level = 1) => RecipeBook.Create(
        world.Content,
        new BalanceAssumptions { Level = Math.Max(1, level), Polish = PolishMode.None },
        world.Sandbox ? world.Content.Tiers.Count - 1 : world.UnlockedTier);

    /// <summary>Everything the factory can make right now, earliest tier first, then by name. Raw resources are left out.</summary>
    public static List<string> Plannable(RecipeBook book) => book.Sources.Values
        .Where(s => !s.IsExtracted)
        .OrderBy(s => s.Tier)
        .ThenBy(s => book.NameOf(s.Item), StringComparer.Ordinal)
        .Select(s => s.Item)
        .ToList();

    /// <summary>The line for <paramref name="item"/> at <paramref name="perSecond"/>, or null if the factory cannot make it yet.</summary>
    public static ChainResult? Plan(RecipeBook book, string item, double perSecond) =>
        book.Sources.ContainsKey(item) && perSecond > 0 ? ProductionChain.For(book, item, perSecond) : null;
}
