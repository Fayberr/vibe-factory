using System.Text;

namespace FactorySim.Cli;

/// <summary>Text renderer for one layer: a second frontend proving the core is engine-agnostic.</summary>
public static class AsciiView
{
    public static string RenderLayer(World world, int z)
    {
        if (world.EntityCount == 0) return "(empty)\n";
        var cells = world.Entities.SelectMany(e => e.Cells()).ToList();
        int minX = cells.Min(c => c.X), maxX = cells.Max(c => c.X);
        int minY = cells.Min(c => c.Y), maxY = cells.Max(c => c.Y);

        var sb = new StringBuilder();
        sb.Append($"layer z={z}\n   ");
        for (int x = minX; x <= maxX; x++) sb.Append(Math.Abs(x) % 10);
        sb.Append('\n');
        for (int y = minY; y <= maxY; y++)
        {
            sb.Append($"{y,2} ");
            for (int x = minX; x <= maxX; x++) sb.Append(Glyph(world, new GridPos(x, y, z)));
            sb.Append('\n');
        }
        return sb.ToString();
    }

    private static char Glyph(World world, GridPos cell)
    {
        var e = world.EntityAt(cell);
        if (e == null) return '.';
        if (e.Pos.Z != cell.Z) return ':'; // upper part of a multi-layer building (ramp)
        var glyph = e.Def.MetaOr("glyph", "?");
        if (glyph == "belt") return e.Facing switch { Dir.North => '^', Dir.East => '>', Dir.South => 'v', _ => '<' };
        return glyph[0];
    }
}
