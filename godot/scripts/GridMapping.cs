using Godot;
using FactorySim.View;

namespace FactorySim.Client;

/// <summary>
/// The single place where simulation space meets Godot space.
/// Grid X → Godot X, grid Y (south) → Godot Z, grid Z (layer) → Godot Y.
/// </summary>
public static class GridMapping
{
    public const float LayerHeight = 1.0f;

    public static Vector3 ToGodot(GridPoint p) => new(p.X, p.Z * LayerHeight, p.Y);

    /// <summary>Centre of a cell's floor.</summary>
    public static Vector3 CellFloor(GridPos c) => new(c.X + 0.5f, c.Z * LayerHeight, c.Y + 0.5f);

    /// <summary>
    /// Grid offset (building-local, facing north) → Godot-local offset. Rotating the parent by
    /// <see cref="Yaw"/> then matches <see cref="GridPos.Rotate"/>.
    /// </summary>
    public static Vector3 LocalOffset(GridPos local) => new(local.X, local.Z * LayerHeight, local.Y);

    /// <summary>Rotation about Godot's up axis that points a north-facing model toward <paramref name="facing"/>.</summary>
    public static float Yaw(Dir facing) => -(int)facing * Mathf.Pi / 2f;

    public static Color ParseColor(string? hex, Color fallback) =>
        !string.IsNullOrEmpty(hex) && Color.HtmlIsValid(hex) ? Color.FromHtml(hex) : fallback;
}
