namespace FactorySim.View;

/// <summary>An item and where it currently is in continuous grid space.</summary>
public readonly record struct PositionedItem(ItemStack Item, int EntityId, GridPoint Point);

/// <summary>
/// Engine-agnostic geometry for drawing items on transport entities. A path runs from
/// the edge its (back) input faces to the edge its output faces; because ports carry
/// their footprint cell, ramps automatically get a sloped path (z → z+1 or back).
/// </summary>
public static class TransportPath
{
    public static bool TryGet(Entity e, out GridPoint start, out GridPoint end)
    {
        start = end = default;
        var def = e.Def;
        if (def.InputPorts.Count == 0 || def.OutputPorts.Count == 0) return false;

        int inPort = def.InputPorts[0];
        foreach (int p in def.InputPorts)
        {
            if (def.Ports[p].Side != Side.Back) continue;
            inPort = p;
            break;
        }
        int outPort = def.OutputPorts[0];

        // Input faces *toward* its source, so the entry edge is on that side of the cell.
        start = EdgePoint(e.PortCell(inPort), e.PortDir(inPort));
        end = EdgePoint(e.PortCell(outPort), e.PortDir(outPort));
        return true;
    }

    public static GridPoint EdgePoint(GridPos cell, Dir side)
    {
        var o = side.Offset();
        return new GridPoint(cell.X + 0.5f + o.X * 0.5f, cell.Y + 0.5f + o.Y * 0.5f, cell.Z);
    }

    public static GridPoint Center(GridPos cell) => new(cell.X + 0.5f, cell.Y + 0.5f, cell.Z);

    /// <summary>Collects every visible item in the world with its position.</summary>
    public static void CollectAll(World world, List<PositionedItem> into)
    {
        into.Clear();
        var buffer = new List<ItemView>();
        foreach (var e in world.Entities)
        {
            buffer.Clear();
            e.Behavior.CollectItems(e, buffer);
            if (buffer.Count == 0) continue;
            bool hasPath = TryGet(e, out var a, out var b);
            foreach (var v in buffer)
                into.Add(new PositionedItem(v.Item, e.Id, hasPath ? GridPoint.Lerp(a, b, v.Progress) : Center(e.Pos)));
        }
    }
}
