namespace FactorySim.View;

// Read-only shapes that renderers consume. Any frontend (Godot, CLI/ASCII, a
// future web or server dashboard) reads the simulation through these and the
// public World queries, and changes it only through Simulation.Execute(Command).

/// <summary>
/// An item on/in an entity, with progress 0..1 along the entity's transport path.
/// Hubs (splitters/mergers) also report the port it came from and the one it is heading to (-1 = unknown).
/// </summary>
public readonly record struct ItemView(ItemStack Item, float Progress, int FromPort = -1, int ToPort = -1);

/// <summary>One labelled line of inspector detail ("Recipe", "smelt_iron").</summary>
public readonly record struct InfoLine(string Label, string Value);

/// <summary>
/// Coarse machine status for UI and effects. <paramref name="Idle"/> says why a building that is not
/// working is waiting, so the client can colour it and the away report can rank what held a factory back.
/// </summary>
public readonly record struct EntityStatus(bool Working, float Progress, string? Detail = null, IdleReason Idle = IdleReason.None);

/// <summary>Why a building is not working.</summary>
public enum IdleReason
{
    /// <summary>Working, or a building for which waiting is not a problem (an empty belt, a depot).</summary>
    None,

    /// <summary>Waiting for something to arrive: an input, a pack.</summary>
    Starved,

    /// <summary>Has something it cannot hand on: a full output, a jammed splitter.</summary>
    Blocked,
}

/// <summary>A point in continuous grid space (cell (x,y,z) spans [x,x+1]×[y,y+1]×[z,z+1]).</summary>
public readonly record struct GridPoint(float X, float Y, float Z)
{
    public static GridPoint Lerp(GridPoint a, GridPoint b, float t) =>
        new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Z + (b.Z - a.Z) * t);
}
