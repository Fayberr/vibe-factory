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

/// <summary>Coarse machine status for UI and effects.</summary>
public readonly record struct EntityStatus(bool Working, float Progress, string? Detail = null);

/// <summary>A point in continuous grid space (cell (x,y,z) spans [x,x+1]×[y,y+1]×[z,z+1]).</summary>
public readonly record struct GridPoint(float X, float Y, float Z)
{
    public static GridPoint Lerp(GridPoint a, GridPoint b, float t) =>
        new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Z + (b.Z - a.Z) * t);
}
