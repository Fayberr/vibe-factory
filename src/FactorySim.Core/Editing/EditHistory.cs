namespace FactorySim.Editing;

/// <summary>
/// Undo/redo for building edits. Wraps <see cref="Simulation.Execute"/>: each edit is
/// recorded together with the command that reverts it (computed from the world before the
/// edit runs). Several commands can be grouped into one step, e.g. a dragged belt line.
///
/// Inverses are expressed by cell, not entity id, so they stay valid across undo/redo
/// cycles that recreate buildings. Upgrades are not undoable and are not recorded.
/// </summary>
public sealed class EditHistory
{
    private sealed class Step
    {
        public readonly List<(Command Do, IReadOnlyList<Command> Undo)> Ops = new();
    }

    private readonly Simulation _sim;
    private readonly List<Step> _undo = new();
    private readonly Stack<Step> _redo = new();
    private Step? _open;
    private int _depth;

    public EditHistory(Simulation sim) => _sim = sim;

    public int Limit { get; set; } = 200;
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    /// <summary>Raised whenever the undo/redo stacks change (for UI buttons).</summary>
    public event Action? Changed;

    public CommandResult Execute(Command command)
    {
        var inverse = Inverse(command);
        var result = _sim.Execute(command);
        if (result.Ok && inverse != null) Record(command, inverse);
        return result;
    }

    /// <summary>Starts grouping subsequent commands into one undo step. Nestable.</summary>
    public void BeginGroup()
    {
        if (_depth++ == 0) _open = new Step();
    }

    public void EndGroup()
    {
        if (_depth == 0 || --_depth > 0) return;
        var step = _open;
        _open = null;
        if (step is { Ops.Count: > 0 }) Push(step);
    }

    public CommandResult Undo()
    {
        if (_undo.Count == 0) return CommandResult.Fail("Nothing to undo");
        var step = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        for (int i = step.Ops.Count - 1; i >= 0; i--)
        {
            foreach (var undo in step.Ops[i].Undo)
            {
                var r = _sim.Execute(undo);
                if (!r.Ok)
                {
                    Changed?.Invoke();
                    return CommandResult.Fail($"Undo failed: {r.Error}");
                }
            }
        }
        _redo.Push(step);
        Changed?.Invoke();
        return CommandResult.Success();
    }

    public CommandResult Redo()
    {
        if (_redo.Count == 0) return CommandResult.Fail("Nothing to redo");
        var step = _redo.Pop();
        foreach (var (op, _) in step.Ops)
        {
            var r = _sim.Execute(op);
            if (!r.Ok)
            {
                Changed?.Invoke();
                return CommandResult.Fail($"Redo failed: {r.Error}");
            }
        }
        _undo.Add(step);
        Changed?.Invoke();
        return CommandResult.Success();
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        Changed?.Invoke();
    }

    private void Record(Command command, IReadOnlyList<Command> inverse)
    {
        if (_open != null)
        {
            _open.Ops.Add((command, inverse));
            return;
        }
        var step = new Step();
        step.Ops.Add((command, inverse));
        Push(step);
    }

    private void Push(Step step)
    {
        _undo.Add(step);
        if (_undo.Count > Limit) _undo.RemoveAt(0);
        _redo.Clear();
        Changed?.Invoke();
    }

    /// <summary>Commands that revert <paramref name="command"/>, from the world as it is now (before it runs).</summary>
    private IReadOnlyList<Command>? Inverse(Command command)
    {
        var world = _sim.World;
        switch (command)
        {
            case PlaceBuilding p:
                var undo = new List<Command> { new RemoveBuildings(new[] { p.Pos }) };
                if (p.Replace && world.Content.Buildings.TryGetValue(p.DefId, out var def))
                {
                    var replaced = new List<Entity>();
                    if (world.CanPlaceReplacing(def, p.Pos, p.Facing, replaced).Ok && replaced.Count > 0) undo.Add(Restore(replaced));
                }
                return undo;
            case PlaceBlueprint pb:
                return One(new RemoveBuildings(pb.Blueprint.Placements(pb.At, pb.QuarterTurns).Select(x => x.Pos).ToList()));
            case RemoveBuilding r:
                return world.EntityAt(r.Cell) is { } e ? One(Restore(new[] { e })) : null;
            case RemoveBuildings rs:
                var removed = rs.Cells.Select(world.EntityAt).OfType<Entity>().Distinct().ToList();
                return removed.Count > 0 ? One(Restore(removed)) : null;
            case RotateBuilding rot:
                return world.EntityAt(rot.Cell) is { } turned ? One(new RotateBuilding(turned.Pos, turned.Facing)) : null;
            case MoveBuildings m:
                var moved = m.Cells.Select(world.EntityAt).OfType<Entity>().Distinct().ToList();
                var newAnchors = moved.Select(e => (e.Pos - m.Pivot).Rotate(m.QuarterTurns) + m.Pivot + m.Delta).ToList();
                return One(new MoveBuildings(newAnchors, GridPos.Zero - m.Delta, -m.QuarterTurns, m.Pivot + m.Delta));
            case SetBuildingLevels lv:
                var before = lv.Changes.Select(ch => world.EntityAt(ch.Cell)).OfType<Entity>().Distinct()
                    .Select(e => new LevelChange(e.Pos, e.Level)).ToList();
                return before.Count > 0 ? One(new SetBuildingLevels(before)) : null;
            default:
                return null; // research, tiers and unknown commands are not undoable
        }
    }

    private static IReadOnlyList<Command> One(Command c) => new[] { c };

    private static Command Restore(IEnumerable<Entity> entities) =>
        new PlaceBlueprint(Blueprint.FromEntities(entities, GridPos.Zero), GridPos.Zero);
}
