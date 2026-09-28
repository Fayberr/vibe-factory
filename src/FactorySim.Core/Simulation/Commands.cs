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

public readonly record struct CommandResult(bool Ok, string? Error = null, int EntityId = 0)
{
    public static CommandResult Success(int entityId = 0) => new(true, null, entityId);
    public static CommandResult Fail(string error) => new(false, error);
}
