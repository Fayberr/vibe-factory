namespace FactorySim;

/// <summary>
/// Integer cell coordinate in the sparse 3D build grid.
/// X → east, Y → south, Z → up (layers; negative Z = underground/tunnels).
/// Engine adapters map this onto their own axes (Godot: X→X, Y→Z, Z→Y).
/// </summary>
public readonly record struct GridPos(int X, int Y, int Z)
{
    public static readonly GridPos Zero = new(0, 0, 0);

    public static GridPos operator +(GridPos a, GridPos b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static GridPos operator -(GridPos a, GridPos b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    public GridPos Step(Dir dir) => this + dir.Offset();
    public GridPos Above(int levels = 1) => new(X, Y, Z + levels);

    /// <summary>Rotates a local offset clockwise (seen from above) to match a building's facing.</summary>
    public GridPos Rotate(Dir facing) => Rotate((int)facing);

    /// <summary>Rotates clockwise (seen from above) by quarter turns; negative turns rotate counter-clockwise.</summary>
    public GridPos Rotate(int quarterTurns)
    {
        int x = X, y = Y;
        for (int i = 0; i < (((quarterTurns % 4) + 4) % 4); i++) (x, y) = (-y, x);
        return new GridPos(x, y, Z);
    }

    public override string ToString() => $"({X},{Y},{Z})";
}

/// <summary>Compass direction in the XY plane. Also used as a building's facing (where its front points).</summary>
public enum Dir : byte
{
    North = 0,
    East = 1,
    South = 2,
    West = 3,
}

/// <summary>Sides of a building in its own frame, before rotation.</summary>
public enum Side : byte
{
    Front = 0,
    Right = 1,
    Back = 2,
    Left = 3,
}

public static class DirExtensions
{
    private static readonly GridPos[] Offsets = { new(0, -1, 0), new(1, 0, 0), new(0, 1, 0), new(-1, 0, 0) };

    public static GridPos Offset(this Dir dir) => Offsets[(int)dir];
    public static Dir Opposite(this Dir dir) => (Dir)(((int)dir + 2) & 3);
    public static Dir RotateCW(this Dir dir, int quarterTurns = 1) => (Dir)((((int)dir + quarterTurns) % 4 + 4) % 4);

    /// <summary>World direction a local side faces when the building faces <paramref name="facing"/>.</summary>
    public static Dir ToWorld(this Side side, Dir facing) => (Dir)(((int)side + (int)facing) & 3);
}
