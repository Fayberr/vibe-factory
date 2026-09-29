using FactorySim.Behaviors;

namespace FactorySim;

/// <summary>
/// Resolves port links and the per-tick update order. Rebuilt lazily whenever a
/// building is placed, removed or rotated, never per tick.
/// </summary>
internal static class Topology
{
    public static List<Entity> Rebuild(World world)
    {
        var entities = world.Entities.OrderBy(e => e.Id).ToList();

        foreach (var e in entities)
        {
            Array.Clear(e.Links);
            Array.Clear(e.Fed);
        }
        foreach (var e in entities)
            foreach (int port in e.Def.OutputPorts)
                e.Links[port] = Resolve(world, e, port);
        foreach (var e in entities)
            MarkFed(e);

        if (CutBeltMerges(entities))
        {
            // Belts that fed a side they may not use are cut loose: recount what still feeds what.
            foreach (var e in entities) Array.Clear(e.Fed);
            foreach (var e in entities) MarkFed(e);
        }

        return DownstreamFirst(entities);
    }

    private static void MarkFed(Entity e)
    {
        foreach (int port in e.Def.OutputPorts)
            if (e.Links[port].Target is { } t) t.Fed[e.Links[port].TargetPort] = true;
    }

    /// <summary>
    /// A belt does not merge: a belt hands its items to another belt only through that belt's back, or
    /// through its side when that side is the belt's one and only input (a curve). Any other belt-to-belt
    /// side link is cut, so the feeding belt just backs up. Merging is what mergers are for. Machines and
    /// hubs may still feed a belt from the side. Decided from the links as resolved (before any cut) so the
    /// result does not depend on entity order. Returns true if anything was cut.
    /// </summary>
    private static bool CutBeltMerges(List<Entity> entities)
    {
        List<(Entity From, int Port)>? cuts = null;
        foreach (var e in entities)
        {
            if (e.Behavior is not ConveyorBehavior) continue;
            foreach (int port in e.Def.OutputPorts)
            {
                var link = e.Links[port];
                if (link.Target is not { } t || t.Behavior is not ConveyorBehavior) continue;
                var side = t.Def.Ports[link.TargetPort].Side;
                if (side == Side.Back || side == ConveyorBehavior.CurveSide(t)) continue;
                (cuts ??= new()).Add((e, port));
            }
        }
        if (cuts == null) return false;
        foreach (var (from, port) in cuts) from.Links[port] = default;
        return true;
    }

    private static PortLink Resolve(World world, Entity e, int port)
    {
        var dir = e.PortDir(port);
        var targetCell = e.PortCell(port).Step(dir);
        var target = world.EntityAt(targetCell);
        if (target == null || target == e) return default;

        var from = dir.Opposite();
        foreach (int q in target.Def.InputPorts)
            if (target.PortCell(q) == targetCell && target.PortDir(q) == from)
                return new PortLink(target, q);
        return default;
    }

    /// <summary>
    /// Post-order DFS along output links: every entity comes after the entities it feeds.
    /// Ticking in this order frees space downstream before upstream pushes into it, so a
    /// saturated belt line moves as one without gaps, and items move at most once per tick
    /// (except at one link per belt loop). Iterative to survive very long lines.
    /// </summary>
    private static List<Entity> DownstreamFirst(List<Entity> entities)
    {
        var order = new List<Entity>(entities.Count);
        var visited = new HashSet<int>();
        var stack = new Stack<(Entity Node, int NextPort)>();

        foreach (var root in entities)
        {
            if (!visited.Add(root.Id)) continue;
            stack.Push((root, 0));
            while (stack.Count > 0)
            {
                var (node, next) = stack.Pop();
                bool descended = false;
                for (int i = next; i < node.Links.Length; i++)
                {
                    var t = node.Links[i].Target;
                    if (t == null || !visited.Add(t.Id)) continue;
                    stack.Push((node, i + 1));
                    stack.Push((t, 0));
                    descended = true;
                    break;
                }
                if (!descended) order.Add(node);
            }
        }
        return order;
    }
}
