using FactorySim.Behaviors;

namespace FactorySim.View;

/// <summary>An item and where it currently is in continuous grid space.</summary>
public readonly record struct PositionedItem(ItemStack Item, int EntityId, GridPoint Point);

public enum PathKind : byte
{
    None,

    /// <summary>Back edge → front edge; rises from StartZ to EndZ along an S-curve (ramps).</summary>
    Straight,

    /// <summary>Quarter circle from the left edge to the front edge.</summary>
    CurveLeft,

    /// <summary>Quarter circle from the right edge to the front edge.</summary>
    CurveRight,

    /// <summary>Hub (splitter/merger): entry edge → centre → exit edge, per item.</summary>
    Hub,
}

public readonly record struct PathShape(PathKind Kind, float StartZ = 0, float EndZ = 0)
{
    public bool IsRamp => Kind == PathKind.Straight && StartZ != EndZ;
}

/// <summary>
/// Engine-agnostic geometry of transport entities, shared by item positioning and by
/// renderers that build belt meshes — so items always ride exactly on the drawn belt.
///
/// Local frame: building facing north, origin at the anchor cell's floor centre,
/// +X = right, −Y = front, +Z = up (layers). <see cref="ToWorld"/> converts to grid space.
/// </summary>
public static class TransportPath
{
    public static PathShape ShapeOf(Entity e)
    {
        var def = e.Def;
        switch (e.Behavior)
        {
            case RouterBehavior:
                return new PathShape(PathKind.Hub);
            case ConveyorBehavior when def.InputPorts.Count > 0 && def.OutputPorts.Count > 0:
                int inPort = def.InputPorts[0];
                foreach (int p in def.InputPorts)
                    if (def.Ports[p].Side == Side.Back) { inPort = p; break; }
                float z0 = def.Ports[inPort].Cell.Z, z1 = def.Ports[def.OutputPorts[0]].Cell.Z;
                if (z0 == z1)
                {
                    var curve = ConveyorBehavior.CurveSide(e);
                    if (curve == Side.Left) return new PathShape(PathKind.CurveLeft, z0, z1);
                    if (curve == Side.Right) return new PathShape(PathKind.CurveRight, z0, z1);
                }
                return new PathShape(PathKind.Straight, z0, z1);
            default:
                return new PathShape(PathKind.None);
        }
    }

    /// <summary>Smoothstep used for ramp height, so ramps are S-shaped chutes that meet flat belts level.</summary>
    public static float RampEase(float t) => t * t * (3 - 2 * t);

    /// <summary>Point at progress <paramref name="t"/> (0..1) along a non-hub path, in the local frame.</summary>
    public static GridPoint SampleLocal(PathShape s, float t)
    {
        switch (s.Kind)
        {
            case PathKind.CurveLeft:
            case PathKind.CurveRight:
                float a = t * MathF.PI / 2;
                float x = -0.5f + 0.5f * MathF.Sin(a);
                return new GridPoint(s.Kind == PathKind.CurveLeft ? x : -x, -0.5f + 0.5f * MathF.Cos(a), s.StartZ);
            default:
                return new GridPoint(0, 0.5f - t, s.StartZ + (s.EndZ - s.StartZ) * RampEase(t));
        }
    }

    /// <summary>Hub path: from the <paramref name="fromSide"/> edge to the centre, then out to <paramref name="toSide"/>.</summary>
    public static GridPoint SampleHubLocal(Side fromSide, Side? toSide, float t)
    {
        var center = new GridPoint(0, 0, 0);
        return t < 0.5f
            ? GridPoint.Lerp(EdgeLocal(fromSide), center, t * 2)
            : toSide is { } to ? GridPoint.Lerp(center, EdgeLocal(to), (t - 0.5f) * 2) : center;
    }

    /// <summary>Midpoint of a cell edge in the local frame.</summary>
    public static GridPoint EdgeLocal(Side side) => side switch
    {
        Side.Front => new GridPoint(0, -0.5f, 0),
        Side.Back => new GridPoint(0, 0.5f, 0),
        Side.Left => new GridPoint(-0.5f, 0, 0),
        _ => new GridPoint(0.5f, 0, 0),
    };

    /// <summary>Local frame → continuous grid space for this entity's anchor and facing.</summary>
    public static GridPoint ToWorld(Entity e, GridPoint local)
    {
        float x = local.X, y = local.Y;
        for (int i = 0; i < (int)e.Facing; i++) (x, y) = (-y, x);
        return new GridPoint(e.Pos.X + 0.5f + x, e.Pos.Y + 0.5f + y, e.Pos.Z + local.Z);
    }

    public static GridPoint Center(GridPos cell) => new(cell.X + 0.5f, cell.Y + 0.5f, cell.Z);

    /// <summary>World position of an item reported by <see cref="IBehavior.CollectItems"/>.</summary>
    public static GridPoint ItemPoint(Entity e, PathShape shape, in ItemView v)
    {
        switch (shape.Kind)
        {
            case PathKind.None:
                return Center(e.Pos);
            case PathKind.Hub:
                var from = v.FromPort >= 0 ? e.Def.Ports[v.FromPort].Side : Side.Back;
                Side? to = v.ToPort >= 0 ? e.Def.Ports[v.ToPort].Side : null;
                return ToWorld(e, SampleHubLocal(from, to, v.Progress));
            default:
                return ToWorld(e, SampleLocal(shape, v.Progress));
        }
    }

    /// <summary>Collects every visible item in the world with its position.</summary>
    public static void CollectAll(World world, List<PositionedItem> into)
    {
        into.Clear();
        world.EnsureTopology();
        var buffer = new List<ItemView>();
        foreach (var e in world.Entities)
        {
            buffer.Clear();
            e.Behavior.CollectItems(e, buffer);
            if (buffer.Count == 0) continue;
            var shape = ShapeOf(e);
            foreach (var v in buffer) into.Add(new PositionedItem(v.Item, e.Id, ItemPoint(e, shape, v)));
        }
    }
}
