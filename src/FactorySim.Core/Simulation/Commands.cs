using FactorySim.Editing;

namespace FactorySim;

/// <summary>
/// Every player-initiated mutation is a Command. Routing all changes through
/// <see cref="Simulation.Execute"/> keeps them validated, applied at tick boundaries,
/// and loggable — the basis for undo, replays and server-verified leaderboard runs.
/// </summary>
public abstract record Command;

public sealed record PlaceBuilding(string DefId, GridPos Pos, Dir Facing) : Command;

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

public readonly record struct CommandResult(bool Ok, string? Error = null, int EntityId = 0, IReadOnlyList<int>? EntityIds = null)
{
    public static CommandResult Success(int entityId = 0) => new(true, null, entityId);
    public static CommandResult Success(IReadOnlyList<int> ids) => new(true, null, ids.Count > 0 ? ids[0] : 0, ids);
    public static CommandResult Fail(string error) => new(false, error);
}
