using System.Text.Json;
using FactorySim.Content;
using FactorySim.Persistence;

namespace FactorySim.Editing;

public sealed record BlueprintEntry(string Def, GridPos Offset, Dir Facing, int Level = 1, string? Recipe = null);

/// <summary>
/// A reusable arrangement of buildings relative to an origin: the clipboard for
/// copy/paste, the payload of move/undo, and a shareable string (JSON). Later also the
/// input for "compress this layout into one condensed machine".
/// </summary>
public sealed class Blueprint
{
    public List<BlueprintEntry> Entries { get; init; } = new();

    public int Count => Entries.Count;

    /// <summary>Captures entities with offsets relative to <paramref name="origin"/>.</summary>
    public static Blueprint FromEntities(IEnumerable<Entity> entities, GridPos origin) => new()
    {
        Entries = entities.OrderBy(e => e.Id)
            .Select(e => new BlueprintEntry(e.Def.Id, e.Pos - origin, e.Facing, e.Level, e.Behavior.Selection(e))).ToList(),
    };

    /// <summary>
    /// Suggested paste origin: centre of the XY bounds at the lowest layer, so a pasted
    /// blueprint is centred on the cursor.
    /// </summary>
    public static GridPos CenterOf(IEnumerable<Entity> entities)
    {
        var list = entities.ToList();
        if (list.Count == 0) return GridPos.Zero;
        int minX = list.Min(e => e.Pos.X), maxX = list.Max(e => e.Pos.X);
        int minY = list.Min(e => e.Pos.Y), maxY = list.Max(e => e.Pos.Y);
        return new GridPos((minX + maxX) / 2, (minY + maxY) / 2, list.Min(e => e.Pos.Z));
    }

    /// <summary>World placements when pasted at <paramref name="at"/>, rotated clockwise by <paramref name="quarterTurns"/>.</summary>
    public IEnumerable<(string Def, GridPos Pos, Dir Facing, int Level, string? Recipe)> Placements(GridPos at, int quarterTurns = 0)
    {
        foreach (var e in Entries) yield return (e.Def, at + e.Offset.Rotate(quarterTurns), e.Facing.RotateCW(quarterTurns), e.Level, e.Recipe);
    }

    /// <summary>Price of pasting: every building at its captured level.</summary>
    public BigNum Cost(ContentRegistry content)
    {
        BigNum total = BigNum.Zero;
        foreach (var e in Entries)
            if (content.Buildings.TryGetValue(e.Def, out var def)) total += def.Upgrade?.Invested(def, e.Level) ?? def.Cost;
        return total;
    }

    public string ToJson() => JsonSerializer.Serialize(this, Json.Options);

    public static Blueprint FromJson(string json) =>
        JsonSerializer.Deserialize<Blueprint>(json, Json.Options) ?? new Blueprint();
}
