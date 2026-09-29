using FactorySim.Content;
using FactorySim.View;

namespace FactorySim.Behaviors;

public sealed class ConveyorParams
{
    /// <summary>Conveyor units per tick (a tile is <see cref="ConveyorBehavior.Length"/> units).</summary>
    public int Speed { get; init; } = 100;

    /// <summary>Minimum distance between item centres, in conveyor units.</summary>
    public int Spacing { get; init; } = 250;

    /// <summary>Optional in-line effect applied to each item entering this tile (upgraders, heaters…).</summary>
    public ItemEffect? Effect { get; init; }
}

/// <summary>
/// An in-line modification of passing items. Covers value multipliers ("upgraders")
/// and status effects (heat, cooling, coatings) via tags.
/// </summary>
public sealed class ItemEffect
{
    /// <summary>Tag written on the item (value = times applied).</summary>
    public string Tag { get; init; } = "";

    public double ValueMultiplier { get; init; } = 1;

    /// <summary>If true, items that already carry <see cref="Tag"/> pass unchanged.</summary>
    public bool Once { get; init; } = true;

    /// <summary>Only affect items carrying this tag (e.g. "heated" before "quenched").</summary>
    public string? RequiresTag { get; init; }

    /// <summary>Only affect these item types (null = all).</summary>
    public string[]? Items { get; init; }

    /// <summary>Applies the effect; <paramref name="bonus"/> is an extra value multiplier from the building's level.</summary>
    public bool Apply(ItemStack item, double bonus = 1)
    {
        if (Once && item.HasTag(Tag)) return false;
        if (RequiresTag != null && !item.HasTag(RequiresTag)) return false;
        if (Items != null && Array.IndexOf(Items, item.Type) < 0) return false;
        if (ValueMultiplier * bonus != 1) item.ValueBonus *= ValueMultiplier * bonus;
        item.SetTag(Tag, item.GetTag(Tag) + 1);
        return true;
    }
}

public sealed class ConveyorState
{
    /// <summary>Items on this tile, front-most first (descending <see cref="BeltItem.Pos"/>).</summary>
    public List<BeltItem> Items { get; set; } = new();
}

/// <summary>An item's position along one conveyor tile, 0 (entry edge) … Length (exit edge).</summary>
public record struct BeltItem(ItemStack Item, int Pos);

/// <summary>
/// Belt tile with continuous, fixed-point item positions. Straight belts, ramps and
/// in-line upgraders are all this behavior with different footprints, ports and params.
///
/// Items enter from the back at the position they overflowed into (exact speed across
/// tiles), or from the left/right at mid-tile (a machine or hub side-loading; a belt only feeds
/// another belt's side when that makes it a curve, see <c>Topology</c>). The front item hands off
/// through the single output port; others queue behind it at <c>Spacing</c>.
/// </summary>
public sealed class ConveyorBehavior : Behavior<ConveyorParams, ConveyorState>
{
    public const int Length = 1000;

    public override string Name => "conveyor";

    protected override void Bind(BuildingDef def, ConveyorParams p, ContentRegistry content)
    {
        Require(def.OutputPorts.Count == 1, def, "needs exactly one output port.");
        Require(def.InputPorts.Count >= 1, def, "needs at least one input port.");
        Require(p.Speed > 0, def, "speed must be > 0.");
        Require(p.Spacing > 0 && p.Spacing <= Length, def, $"spacing must be in 1..{Length}.");
        Require(p.Effect == null || p.Effect.Tag.Length > 0, def, "effect needs a tag.");
    }

    /// <summary>Level 9 is 5× the base speed: for a belt built at a fifth of its spacing, the physical limit.</summary>
    public override UpgradeTrack DefaultUpgrade(BuildingDef def) =>
        new() { MaxLevel = 9, SpeedPerLevel = 0.5, CostFactor = 3, CostGrowth = 2 };

    /// <summary>Effective speed; capped at Spacing so at most one item crosses an edge per tick.</summary>
    public static int EffectiveSpeed(TickContext ctx, Entity e, ConveyorParams p) =>
        Math.Clamp((int)(p.Speed * ctx.Stat(StatIds.ConveyorSpeed) * e.SpeedFactor), 1, p.Spacing);

    protected override void Tick(TickContext ctx, Entity e, ConveyorParams p, ConveyorState s)
    {
        var items = s.Items;
        if (items.Count == 0) return;

        int speed = EffectiveSpeed(ctx, e, p);
        int outPort = e.Def.OutputPorts[0];
        int w = 0; // write index: compacts the list as items leave

        for (int i = 0; i < items.Count; i++)
        {
            var it = items[i];
            int target = it.Pos + speed;
            if (w == 0)
            {
                // Front-most remaining item: may leave through the output.
                if (target >= Length)
                {
                    if (ctx.Push(e, outPort, it.Item, target - Length)) continue;
                    target = Length;
                }
            }
            else
            {
                target = Math.Min(target, items[w - 1].Pos - p.Spacing);
            }

            if (target < it.Pos) target = it.Pos; // never move backwards
            items[w++] = it with { Pos = target };
        }

        if (w < items.Count) items.RemoveRange(w, items.Count - w);
    }

    /// <summary>
    /// A belt whose back input is not fed but exactly one side input is becomes a curve:
    /// items from that side enter at the start of the tile (full length, like the back)
    /// instead of merging in at mid-tile. Returns that side, or null for a straight belt.
    /// </summary>
    public static Side? CurveSide(Entity e)
    {
        Side? side = null;
        int fedSides = 0;
        foreach (int port in e.Def.InputPorts)
        {
            if (!e.IsInputFed(port)) continue;
            var s = e.Def.Ports[port].Side;
            if (s == Side.Back) return null;
            if (s is Side.Left or Side.Right)
            {
                side = s;
                fedSides++;
            }
        }
        return fedSides == 1 ? side : null;
    }

    /// <summary>
    /// Whether a belt that hands its items to <paramref name="target"/> at input <paramref name="port"/> is
    /// taken. Belts do not merge (see <c>Topology</c>): a belt takes another belt at its back, or at a side
    /// only while nothing else feeds it (which makes it a curve). Anything that is not a belt takes items
    /// at every input. Reads the last resolved topology.
    /// </summary>
    public static bool TakesBeltAt(Entity target, int port)
    {
        if (target.Behavior is not ConveyorBehavior) return true;
        if (target.Def.Ports[port].Side == Side.Back) return true;
        foreach (int q in target.Def.InputPorts)
            if (target.IsInputFed(q)) return false;
        return true;
    }

    protected override bool TryAccept(TickContext ctx, Entity e, ConveyorParams p, ConveyorState s, ItemStack item, int port, int overflow)
    {
        var items = s.Items;
        int index;
        int entry;
        var side = e.Def.Ports[port].Side;

        if (side == Side.Back || side == CurveSide(e))
        {
            entry = Math.Min(overflow, EffectiveSpeed(ctx, e, p));
            if (items.Count > 0) entry = Math.Min(entry, items[^1].Pos - p.Spacing);
            if (entry < 0) return false;
            index = items.Count;
        }
        else
        {
            // Side-load at mid-tile: needs a Spacing-wide gap on both sides.
            entry = Length / 2;
            index = 0;
            while (index < items.Count && items[index].Pos >= entry) index++;
            if (index > 0 && items[index - 1].Pos - entry < p.Spacing) return false;
            if (index < items.Count && entry - items[index].Pos < p.Spacing) return false;
        }

        p.Effect?.Apply(item, e.ValueFactor);
        items.Insert(index, new BeltItem(item, entry));
        return true;
    }

    protected override void CollectItems(Entity e, ConveyorParams p, ConveyorState s, List<ItemView> into)
    {
        foreach (var it in s.Items) into.Add(new ItemView(it.Item, it.Pos / (float)Length));
    }

    protected override EntityStatus GetStatus(Entity e, ConveyorParams p, ConveyorState s) =>
        new(s.Items.Count > 0, 0, s.Items.Count == 0 ? "empty" : $"{s.Items.Count} item(s)");

    protected override void Describe(Entity e, ConveyorParams p, ConveyorState s, List<InfoLine> into)
    {
        double speed = Math.Min(p.Speed * e.SpeedFactor, p.Spacing);
        into.Add(new InfoLine("Speed", $"{speed * Simulation.TicksPerSecond / Length:0.##} tiles/s"));
        into.Add(new InfoLine("Throughput", $"{speed * Simulation.TicksPerSecond / p.Spacing:0.#} items/s"));
        if (p.Effect != null)
            into.Add(new InfoLine("Effect", $"×{p.Effect.ValueMultiplier * e.ValueFactor:0.##} value{(p.Effect.Once ? ", once per item" : "")}, paid on sale"));
        into.Add(new InfoLine("On belt", s.Items.Count.ToString()));
    }
}
