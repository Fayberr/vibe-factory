using FactorySim.Editing;

namespace FactorySim;

/// <summary>
/// Every player-initiated mutation is a Command. Routing all changes through
/// <see cref="Simulation.Execute"/> keeps them validated, applied at tick boundaries,
/// and loggable: the basis for undo, replays and server-verified leaderboard runs.
/// </summary>
public abstract record Command;

/// <summary>
/// Places a building. With <paramref name="Replace"/>, buildings in its footprint whose group it
/// may replace are removed first (refunded), and belt contents carry over when compatible.
/// </summary>
public sealed record PlaceBuilding(string DefId, GridPos Pos, Dir Facing, bool Replace = false) : Command;

/// <summary>Removes whatever building occupies <paramref name="Cell"/> (any footprint cell).</summary>
public sealed record RemoveBuilding(GridPos Cell) : Command;

/// <summary>Sets the facing of the building at <paramref name="Cell"/>, or turns it clockwise when null.</summary>
public sealed record RotateBuilding(GridPos Cell, Dir? Facing = null) : Command;

public sealed record BuyUpgrade(string UpgradeId) : Command;

/// <summary>Places every entry of a blueprint, or nothing (atomic: all cells must be free and affordable).</summary>
public sealed record PlaceBlueprint(Blueprint Blueprint, GridPos At, int QuarterTurns = 0) : Command;

/// <summary>Removes every building occupying any of the cells.</summary>
public sealed record RemoveBuildings(IReadOnlyList<GridPos> Cells) : Command;

/// <summary>
/// Moves the buildings at <paramref name="Cells"/> (atomic, keeps their state and ids):
/// new anchor = rotate(anchor − Pivot, QuarterTurns) + Pivot + Delta.
/// </summary>
public sealed record MoveBuildings(IReadOnlyList<GridPos> Cells, GridPos Delta, int QuarterTurns = 0, GridPos Pivot = default) : Command;

/// <summary>Target level for the building at <paramref name="Cell"/>.</summary>
public readonly record struct LevelChange(GridPos Cell, int Level);

/// <summary>Sets building levels atomically, charging (or refunding) the difference in upgrade costs.</summary>
public sealed record SetBuildingLevels(IReadOnlyList<LevelChange> Changes) : Command;

/// <summary>
/// Chooses what the building at <paramref name="Cell"/> produces (a machine's recipe id);
/// null returns it to automatic.
/// </summary>
public sealed record SelectRecipe(GridPos Cell, string? Recipe) : Command;

/// <summary>Swaps an open contract for a new one, for a fee (a tenth of its reward).</summary>
public sealed record RerollContract(int ContractId) : Command;

/// <summary>Unlocks the next progression tier (needs lifetime earnings and money).</summary>
public sealed record UnlockTier : Command;

/// <summary>
/// Buys the plot at (<paramref name="Column"/>, <paramref name="Row"/>). It has to share an edge with
/// land already owned; the price depends on its distance from the starting plot.
/// </summary>
public sealed record BuyPlot(int Column, int Row) : Command;

public readonly record struct CommandResult(bool Ok, string? Error = null, int EntityId = 0, IReadOnlyList<int>? EntityIds = null)
{
    public static CommandResult Success(int entityId = 0) => new(true, null, entityId);
    public static CommandResult Success(IReadOnlyList<int> ids) => new(true, null, ids.Count > 0 ? ids[0] : 0, ids);
    public static CommandResult Fail(string error) => new(false, error);
}
