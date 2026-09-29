using FactorySim.Behaviors;

namespace FactorySim.Editing;

/// <summary>
/// Belt routing for drags: instead of a fixed L, the belt finds its own way from where the drag
/// starts to where it ends, with as many turns as it needs.
/// </summary>
public sealed partial class BuildPlanner
{
    /// <summary>Extra cost of a turn, in cells: a route prefers straight runs, but not at any price.</summary>
    public double TurnCost { get; init; } = 0.6;

    /// <summary>Extra cost of bridging over belt lines, in cells: ramps are pricier and busier than a short detour.</summary>
    public double BridgeCost { get; init; } = 3;

    /// <summary>Most belt lines side by side that one bridge crosses.</summary>
    public int MaxBridgeSpan { get; init; } = 4;

    private const int NoDir = 4;

    /// <summary>A search state: where the belt is, which way it moved in, and whether it must go straight on (off a ramp).</summary>
    private readonly record struct RouteNode(GridPos Cell, int Dir, bool Straight);

    /// <summary>
    /// The belt route for a drag from <paramref name="start"/> to <paramref name="end"/> (both at the start's
    /// height): the fewest cells, then the fewest turns. It goes around buildings, over perpendicular belt
    /// lines (<see cref="Drag"/> bridges them) and past, never through, the cells other buildings output
    /// into. A drag that starts on a building leaves through one of its outputs; one that ends on a building
    /// or a belt enters through one of its inputs, so dragging from a machine to a machine connects them.
    /// Null when there is no way; the caller can fall back to <see cref="LPath"/>.
    /// </summary>
    public List<GridPos>? Route(GridPos start, GridPos end, bool firstLegX, int maxNodes = 60000)
    {
        int z = start.Z;
        end = end with { Z = z };
        if (start == end) return null;
        // Belts may only run over land the player owns.
        bool InPlot(GridPos p) => World.OwnsCell(p);
        if (!InPlot(start) || !InPlot(end)) return null;

        var from = World.EntityAt(start);
        var to = World.EntityAt(end);
        if (from != null && from == to) return null;

        // Goals: the free end cell (any direction), or an input of what the drag ends on, entered moving into it.
        var goals = new Dictionary<GridPos, List<int>>();
        if (to == null) goals[end] = new List<int>();
        else
        {
            World.EnsureTopology();
            foreach (int q in to.Def.InputPorts)
            {
                var cell = to.PortCell(q);
                if (cell.Z != z) continue;
                if (!ConveyorBehavior.TakesBeltAt(to, q)) continue; // a belt would not take a belt on that side
                if (!goals.TryGetValue(cell, out var dirs)) goals[cell] = dirs = new List<int>();
                dirs.Add((int)to.PortDir(q).Opposite());
            }
        }
        if (goals.Count == 0) return null;

        // Cells something else outputs into: a belt there would pick up its items (or merge its line).
        var fed = new HashSet<GridPos>();
        foreach (var e in World.Entities)
        {
            if (e == from) continue;
            foreach (int p in e.Def.OutputPorts)
            {
                var target = e.PortCell(p).Step(e.PortDir(p));
                if (target.Z == z && World.EntityAt(target) == null) fed.Add(target);
            }
        }

        bool Free(GridPos p) => InPlot(p) && World.EntityAt(p) == null && (!fed.Contains(p) || goals.ContainsKey(p));
        double H(GridPos p) => goals.Keys.Min(g => Math.Abs(g.X - p.X) + Math.Abs(g.Y - p.Y));
        bool IsGoal(RouteNode n) => n.Dir != NoDir && goals.TryGetValue(n.Cell, out var d) && (d.Count == 0 || d.Contains(n.Dir));

        var rampUp = World.Content.Buildings.GetValueOrDefault(RampUpId);
        var rampDown = World.Content.Buildings.GetValueOrDefault(RampDownId);
        bool canBridge = rampUp != null && rampDown != null && _sim.LockReason(rampUp) == null && z + 1 <= World.Bounds.Max.Z;

        var open = new PriorityQueue<RouteNode, double>();
        var best = new Dictionary<RouteNode, double>();
        var came = new Dictionary<RouteNode, (RouteNode From, GridPos[] Cells)>();
        void Push(RouteNode n, double g, RouteNode? parent, GridPos[] cells)
        {
            if (best.TryGetValue(n, out var old) && old <= g) return;
            best[n] = g;
            if (parent is { } p) came[n] = (p, cells);
            open.Enqueue(n, g + H(n.Cell));
        }

        // Starts: the start cell (free, or a belt the route re-aims), or each output of the building it is on.
        if (from == null || IsLineTool(from.Def)) Push(new RouteNode(start, NoDir, false), 0, null, Array.Empty<GridPos>());
        else
            foreach (int p in from.Def.OutputPorts)
            {
                var cell = from.PortCell(p);
                if (cell.Z == z) Push(new RouteNode(cell, (int)from.PortDir(p), true), 0, null, Array.Empty<GridPos>());
            }

        int expanded = 0;
        while (open.TryDequeue(out var node, out _) && expanded++ < maxNodes)
        {
            double g = best[node];
            if (IsGoal(node)) return Unwind(node, came);
            if (goals.ContainsKey(node.Cell) && node.Dir != NoDir && World.EntityAt(node.Cell) != null) continue; // a building's cell: only an end

            for (int m = 0; m < 4; m++)
            {
                if (node.Straight && m != node.Dir) continue;
                if (node.Dir != NoDir && m == (int)((Dir)node.Dir).Opposite()) continue;
                double step = 1 + (node.Dir != NoDir && m != node.Dir ? TurnCost : 0)
                                + (node.Dir == NoDir && (m is (int)Dir.East or (int)Dir.West) != firstLegX ? 0.001 : 0);
                var next = node.Cell.Step((Dir)m);

                if (goals.TryGetValue(next, out var need) && (need.Count == 0 || need.Contains(m)) && (World.EntityAt(next) != null || Free(next)))
                {
                    Push(new RouteNode(next, m, false), g + step, node, new[] { next });
                    continue;
                }
                if (Free(next))
                {
                    Push(new RouteNode(next, m, false), g + step, node, new[] { next });
                    continue;
                }

                // Over a run of belt lines: a ramp up here (so the belt must already run this way), belts
                // one level up above them, a ramp down on the far side, then straight on.
                if (!canBridge || node.Straight || (node.Dir != m && !(node.Dir == NoDir && from == null)) || World.EntityAt(node.Cell) != null) continue;
                var run = new List<GridPos>();
                var p = next;
                while (run.Count < MaxBridgeSpan && World.EntityAt(p) is { } crossed && IsCrossable(crossed)
                       && Perpendicular(crossed.Facing, (Dir)m)
                       && (World.EntityAt(p.Above()) is not { } above || (above.Def.Id == BeltId && above.Facing == (Dir)m)))
                {
                    run.Add(p);
                    p = p.Step((Dir)m);
                }
                if (run.Count == 0 || !Free(p)) continue;
                if (!World.CanPlace(rampUp!, node.Cell, (Dir)m).Ok || !World.CanPlace(rampDown!, p, (Dir)m).Ok) continue;
                run.Add(p);
                Push(new RouteNode(p, m, true), g + run.Count + BridgeCost, node, run.ToArray());
            }
        }
        return null;
    }

    private static List<GridPos> Unwind(RouteNode goal, Dictionary<RouteNode, (RouteNode From, GridPos[] Cells)> came)
    {
        var segments = new List<GridPos[]>();
        var n = goal;
        while (came.TryGetValue(n, out var link))
        {
            segments.Add(link.Cells);
            n = link.From;
        }
        var cells = new List<GridPos> { n.Cell };
        for (int i = segments.Count - 1; i >= 0; i--) cells.AddRange(segments[i]);
        return cells;
    }

    /// <summary>
    /// The cells of a hand-drawn drag, in order: each new cell under the cursor extends it (gaps are
    /// filled with an L), and moving back over the path takes the cells after that point off again.
    /// </summary>
    public static void ExtendTrail(List<GridPos> trail, GridPos cell)
    {
        if (trail.Count == 0)
        {
            trail.Add(cell);
            return;
        }
        var last = trail[^1];
        if (cell == last) return;
        var fill = LPath(last, cell, Math.Abs(cell.X - last.X) >= Math.Abs(cell.Y - last.Y));
        for (int i = 1; i < fill.Count; i++)
        {
            int seen = trail.IndexOf(fill[i]);
            if (seen >= 0) trail.RemoveRange(seen + 1, trail.Count - seen - 1);
            else trail.Add(fill[i]);
        }
    }
}
