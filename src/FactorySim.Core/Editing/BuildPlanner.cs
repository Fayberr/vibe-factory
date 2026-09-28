using FactorySim.Behaviors;
using FactorySim.Content;

namespace FactorySim.Editing;

public enum PlanAction : byte
{
    /// <summary>PlaceBuilding with Replace: builds here, swapping out compatible pieces.</summary>
    Place,

    /// <summary>The same piece is already here but points elsewhere: re-aim it.</summary>
    Rotate,

    /// <summary>Leave what is here alone (same piece, a machine the line runs into, a pricier belt piece).</summary>
    Keep,
}

/// <summary>
/// One step of a planned placement. <paramref name="Incoming"/> is the direction items travel
/// into this cell along the drag (null for the first cell) so previews can show curves.
/// <paramref name="Bridge"/> marks the pieces of an automatic bridge.
/// </summary>
public readonly record struct PlanStep(PlanAction Action, BuildingDef Def, GridPos Pos, Dir Facing, Dir? Incoming, bool Bridge = false);

/// <summary>Planned steps plus how many crossings were bridged and why others could not be.</summary>
public sealed class BuildPlan
{
    public List<PlanStep> Steps { get; } = new();
    public int Bridges { get; internal set; }
    public string? BridgeProblem { get; internal set; }

    public IEnumerable<PlanStep> Changes => Steps.Where(s => s.Action != PlanAction.Keep);
}

/// <summary>
/// Turns a click or drag into placement steps, frontend-independent:
///  • a click places one building (on top of a compatible one, keeping its direction);
///  • a drag lays an L-shaped line; belt pieces face along it, re-aim belts already there and
///    never downgrade pricier pieces (a belt dragged over a polisher keeps the polisher);
///  • a belt dragged across other belt lines bridges over them: ramp up before the crossing,
///    belts one level up above it, ramp down after it.
/// The ids of the pieces used for bridges are parameters so content can rename them.
/// </summary>
public sealed class BuildPlanner
{
    private readonly Simulation _sim;
    private readonly List<Entity> _scratch = new();

    public string BeltId { get; init; } = "conveyor";
    public string RampUpId { get; init; } = "ramp_up";
    public string RampDownId { get; init; } = "ramp_down";

    public BuildPlanner(Simulation sim) => _sim = sim;

    private World World => _sim.World;

    // ---- Paths --------------------------------------------------------------

    /// <summary>Cells from <paramref name="start"/> to <paramref name="end"/> as an L (first leg along X or Y), at the start's height.</summary>
    public static List<GridPos> LPath(GridPos start, GridPos end, bool firstLegX)
    {
        var cells = new List<GridPos> { start };
        var p = start;
        void Walk(bool alongX, int target)
        {
            while ((alongX ? p.X : p.Y) != target)
            {
                p = alongX ? p with { X = p.X + Math.Sign(target - p.X) } : p with { Y = p.Y + Math.Sign(target - p.Y) };
                cells.Add(p);
            }
        }
        if (firstLegX) { Walk(true, end.X); Walk(false, end.Y); }
        else { Walk(false, end.Y); Walk(true, end.X); }
        return cells;
    }

    public static Dir Toward(GridPos from, GridPos to)
    {
        var d = to - from;
        return d.X > 0 ? Dir.East : d.X < 0 ? Dir.West : d.Y > 0 ? Dir.South : Dir.North;
    }

    private static bool Perpendicular(Dir a, Dir b) => (((int)a ^ (int)b) & 1) == 1;

    /// <summary>Pieces laid by dragging and faced along the drag: single-cell conveyors (belts, polishers).</summary>
    public bool IsLineTool(BuildingDef def) =>
        def.Footprint.Length == 1 && World.Content.Behaviors.Get(def.Behavior) is ConveyorBehavior;

    /// <summary>Single-cell transport a belt can bridge over: belts, polishers, splitters, mergers.</summary>
    private bool IsCrossable(Entity e) =>
        e.Def.Footprint.Length == 1 && World.Content.Behaviors.Get(e.Def.Behavior) is ConveyorBehavior or RouterBehavior;

    /// <summary>Height to anchor <paramref name="def"/> at when building at <paramref name="height"/>: its lowest input sits there
    /// (a ramp down placed at height 1 comes down to the ground; placed on the ground it spans ground and level 1).</summary>
    public int AnchorHeight(BuildingDef def, int height)
    {
        int inputZ = def.Ports.Where(p => p.Kind == PortKind.In).Select(p => p.Cell.Z).DefaultIfEmpty(0).Min();
        return Math.Clamp(height - inputZ, World.Bounds.Min.Z, World.Bounds.Max.Z);
    }

    /// <summary>Height a belt continues at after <paramref name="def"/>: where its output leaves (a ramp up's top).</summary>
    public int OutputHeight(BuildingDef def, int anchorZ) =>
        anchorZ + def.Ports.Where(p => p.Kind == PortKind.Out).Select(p => p.Cell.Z).DefaultIfEmpty(0).Max();

    /// <summary>Build height that reproduces <paramref name="e"/>'s placement (inverse of <see cref="AnchorHeight"/>).</summary>
    public static int HeightOf(Entity e) =>
        e.Pos.Z + e.Def.Ports.Where(p => p.Kind == PortKind.In).Select(p => p.Cell.Z).DefaultIfEmpty(0).Min();

    /// <summary>Moves items between heights (its ports sit on different levels).</summary>
    public static bool IsRamp(BuildingDef def) => def.Ports.Select(p => p.Cell.Z).Distinct().Count() > 1;

    // ---- Planning -----------------------------------------------------------

    /// <summary>A single click at <paramref name="anchor"/>.</summary>
    public BuildPlan Click(BuildingDef def, GridPos anchor, Dir facing)
    {
        var plan = new BuildPlan();
        // Dropped onto exactly one building (a polisher onto a belt): keep that building's direction.
        if (World.CanPlaceReplacing(def, anchor, facing, _scratch).Ok && _scratch.Count == 1) facing = _scratch[0].Facing;
        facing = FaceFeeder(def, anchor, facing);
        var here = World.EntityAt(anchor);
        bool same = here != null && here.Def == def && here.Pos == anchor;
        plan.Steps.Add(new PlanStep(same && here!.Facing == facing ? PlanAction.Keep : same ? PlanAction.Rotate : PlanAction.Place,
            def, anchor, facing, null));
        return plan;
    }

    /// <summary>
    /// A building whose only port is one input (a depot) does nothing facing away from the
    /// belt that runs into its cell, so it turns to take that belt in. <paramref name="facing"/>
    /// is kept when it already does, or when nothing feeds the cell.
    /// </summary>
    public Dir FaceFeeder(BuildingDef def, GridPos anchor, Dir facing)
    {
        if (def.Footprint.Length != 1 || def.Ports.Length != 1 || def.InputPorts.Count != 1) return facing;
        var side = def.Ports[0].Side;
        var cell = anchor + def.Ports[0].Cell;
        bool FedFrom(Dir d)
        {
            var e = World.EntityAt(cell.Step(d));
            if (e == null) return false;
            foreach (int p in e.Def.OutputPorts)
                if (e.PortCell(p).Step(e.PortDir(p)) == cell) return true;
            return false;
        }
        if (FedFrom(side.ToWorld(facing))) return facing;
        for (int f = 0; f < 4; f++)
            if (FedFrom(side.ToWorld((Dir)f))) return (Dir)f;
        return facing;
    }

    /// <summary>A drag along <paramref name="cells"/> (from <see cref="LPath"/>). Non-line tools keep <paramref name="facing"/>.</summary>
    public BuildPlan Drag(BuildingDef def, IReadOnlyList<GridPos> cells, Dir facing)
    {
        if (cells.Count == 1) return Click(def, cells[0], facing);
        var plan = new BuildPlan();
        bool line = IsLineTool(def);
        int n = cells.Count;
        var dirs = new Dir[n];
        for (int i = 0; i < n; i++)
            dirs[i] = !line ? facing : i + 1 < n ? Toward(cells[i], cells[i + 1]) : Toward(cells[i - 1], cells[i]);

        var crossing = new bool[n];
        for (int i = 0; i < n; i++)
        {
            Dir? incoming = i == 0 ? null : Toward(cells[i - 1], cells[i]);
            var action = Classify(def, line, cells[i], dirs[i], interior: i > 0 && i < n - 1, out crossing[i]);
            plan.Steps.Add(new PlanStep(action, def, cells[i], dirs[i], line ? incoming : null));
        }
        if (line && def.Id == BeltId) AddBridges(plan, cells, dirs, crossing);
        return plan;
    }

    private PlanAction Classify(BuildingDef def, bool line, GridPos cell, Dir dir, bool interior, out bool crossing)
    {
        crossing = false;
        var occ = World.EntityAt(cell);
        if (occ == null) return PlanAction.Place;
        if (occ.Def == def && occ.Pos == cell)
        {
            if (occ.Facing == dir) return PlanAction.Keep;
            if (line && interior && IsCrossable(occ) && Perpendicular(occ.Facing, dir)) crossing = true;
            return crossing ? PlanAction.Keep : line ? PlanAction.Rotate : PlanAction.Place;
        }
        if (line && interior && IsCrossable(occ) && Perpendicular(occ.Facing, dir))
        {
            crossing = true;
            return PlanAction.Keep;
        }
        // Only upgrade what a drag passes over (polishers over belts), never swap a pricier piece out.
        bool replaceable = Array.IndexOf(def.Replaces, occ.Def.Group) >= 0 && occ.Def.Group.Length > 0;
        return replaceable && def.Cost > occ.Def.Cost ? PlanAction.Place : PlanAction.Keep;
    }

    private void AddBridges(BuildPlan plan, IReadOnlyList<GridPos> cells, Dir[] dirs, bool[] crossing)
    {
        int n = cells.Count;
        var runs = new List<(int A, int B)>();
        for (int i = 0; i < n; i++)
        {
            if (!crossing[i]) continue;
            int j = i;
            while (j + 1 < n && crossing[j + 1]) j++;
            // Two crossings one tile apart share a bridge (no room for a ramp down and up in between).
            if (runs.Count > 0 && i - runs[^1].B <= 2) runs[^1] = (runs[^1].A, j);
            else runs.Add((i, j));
            i = j;
        }

        foreach (var (a, b) in runs)
        {
            if (BridgeProblem(cells, dirs, a, b) is { } problem)
            {
                plan.BridgeProblem ??= problem;
                continue;
            }
            var d = dirs[a];
            var up = World.Content.Buildings[RampUpId];
            var down = World.Content.Buildings[RampDownId];
            var belt = World.Content.Buildings[BeltId];
            plan.Steps[a - 1] = RampStep(up, cells[a - 1], d);
            for (int k = a; k <= b; k++)
            {
                var above = cells[k].Above();
                bool there = World.EntityAt(above) is { } e && e.Def == belt && e.Facing == d;
                plan.Steps[k] = new PlanStep(there ? PlanAction.Keep : PlanAction.Place, belt, above, d, d, Bridge: true);
            }
            plan.Steps[b + 1] = RampStep(down, cells[b + 1], d);
            plan.Bridges++;
        }
    }

    /// <summary>Runs the plan's changes through <paramref name="execute"/> (the simulation or an undo history).</summary>
    public static (int Changed, int Failed, string? FirstError) Apply(BuildPlan plan, Func<Command, CommandResult> execute)
    {
        int changed = 0, failed = 0;
        string? error = null;
        foreach (var s in plan.Changes)
        {
            var r = execute(s.Action == PlanAction.Rotate
                ? new RotateBuilding(s.Pos, s.Facing)
                : new PlaceBuilding(s.Def.Id, s.Pos, s.Facing, Replace: true));
            if (r.Ok) changed++;
            else
            {
                failed++;
                error ??= r.Error;
            }
        }
        return (changed, failed, error);
    }

    private PlanStep RampStep(BuildingDef ramp, GridPos at, Dir d)
    {
        bool there = World.EntityAt(at) is { } e && e.Def == ramp && e.Pos == at && e.Facing == d;
        return new PlanStep(there ? PlanAction.Keep : PlanAction.Place, ramp, at, d, d, Bridge: true);
    }

    /// <summary>Why the crossing run [a..b] cannot be bridged, or null when it can.</summary>
    private string? BridgeProblem(IReadOnlyList<GridPos> cells, Dir[] dirs, int a, int b)
    {
        var content = World.Content.Buildings;
        if (!content.TryGetValue(RampUpId, out var up) || !content.TryGetValue(RampDownId, out var down) || !content.ContainsKey(BeltId))
            return "No ramps to bridge with";
        if (_sim.LockReason(up) is { } locked) return locked;
        var d = dirs[a];
        // The ramp up needs a straight run-in: it only takes items from behind.
        for (int k = Math.Max(0, a - 2); k <= b + 1; k++)
            if (dirs[k] != d) return "Can't bridge at a corner: cross in a straight line";
        if (cells[a].Z + 1 > World.Bounds.Max.Z) return "Too high to bridge";

        foreach (var (ramp, at) in new[] { (up, cells[a - 1]), (down, cells[b + 1]) })
        {
            if (World.EntityAt(at) is { } e && e.Def == ramp && e.Pos == at && e.Facing == d) continue;
            var check = World.CanPlaceReplacing(ramp, at, d, _scratch);
            if (!check.Ok) return $"No room for a ramp: {check.Reason}";
        }
        for (int k = a; k <= b; k++)
        {
            var above = cells[k].Above();
            if (World.EntityAt(above) is { } e && !(e.Def.Id == BeltId && e.Facing == d)) return $"Something is in the way above {cells[k]}";
        }
        return null;
    }
}
