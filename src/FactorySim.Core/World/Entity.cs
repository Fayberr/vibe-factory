using FactorySim.Behaviors;
using FactorySim.Content;

namespace FactorySim;

/// <summary>Resolved connection from an output port to a neighbour's input port.</summary>
public readonly record struct PortLink(Entity? Target, int TargetPort)
{
    public bool IsConnected => Target != null;
}

/// <summary>
/// A placed building. Identity, placement and a behavior-owned <see cref="State"/> object;
/// everything else (what it does, how fast) comes from its <see cref="Def"/>.
/// </summary>
public sealed class Entity
{
    public int Id { get; }
    public BuildingDef Def { get; }
    public IBehavior Behavior { get; }

    /// <summary>Anchor cell (local (0,0,0) of the footprint).</summary>
    public GridPos Pos { get; internal set; }

    /// <summary>Direction the building's front points.</summary>
    public Dir Facing { get; internal set; }

    /// <summary>Behavior-owned, JSON-serializable state.</summary>
    public object State { get; internal set; }

    /// <summary>Per port index; only output ports are ever connected. Rebuilt by Topology.</summary>
    internal PortLink[] Links;

    internal Entity(int id, BuildingDef def, IBehavior behavior, GridPos pos, Dir facing, object state)
    {
        Id = id;
        Def = def;
        Behavior = behavior;
        Pos = pos;
        Facing = facing;
        State = state;
        Links = new PortLink[def.Ports.Length];
    }

    public IEnumerable<GridPos> Cells() => CellsFor(Def, Pos, Facing);

    public static IEnumerable<GridPos> CellsFor(BuildingDef def, GridPos pos, Dir facing)
    {
        foreach (var local in def.Footprint) yield return pos + local.Rotate(facing);
    }

    /// <summary>World cell the port sits on.</summary>
    public GridPos PortCell(int port) => Pos + Def.Ports[port].Cell.Rotate(Facing);

    /// <summary>World direction the port faces (outputs deliver that way, inputs receive from it).</summary>
    public Dir PortDir(int port) => Def.Ports[port].Side.ToWorld(Facing);

    public PortLink Link(int port) => Links[port];

    public override string ToString() => $"#{Id} {Def.Id} @{Pos} facing {Facing}";
}
