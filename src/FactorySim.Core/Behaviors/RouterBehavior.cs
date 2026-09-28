using FactorySim.Content;
using FactorySim.View;

namespace FactorySim.Behaviors;

public sealed class RouterParams
{
    public int Speed { get; init; } = 100;
    public int Spacing { get; init; } = 250;
}

public sealed class RouterState
{
    /// <summary>Items in entry order (front-most first).</summary>
    public List<RouterItem> Items { get; set; } = new();

    /// <summary>Round-robin cursor into the def's output ports.</summary>
    public int NextOut { get; set; }

    /// <summary>Input port that gets priority next (fair merging).</summary>
    public int NextIn { get; set; } = -1;

    /// <summary>Per port: last tick an offer through it was refused (i.e. it has items waiting).</summary>
    public long[] RefusedAt { get; set; } = Array.Empty<long>();
}

/// <summary>An item crossing a hub: entered through <see cref="From"/>, leaving through <see cref="To"/> (-1 = not chosen yet).</summary>
public record struct RouterItem(ItemStack Item, int Pos, int From, int To);

/// <summary>
/// Belt hub that routes between several inputs and outputs. Splitters (1 → 3) and
/// mergers (3 → 1) are this behavior with different ports.
///  • Outputs are chosen round-robin at mid-tile among connected outputs; a blocked
///    output is skipped at the exit, so one jammed branch never stalls the others.
///  • Inputs are served fairly: while the preferred input has items waiting, other
///    inputs are refused, then the preference moves on.
/// </summary>
public sealed class RouterBehavior : Behavior<RouterParams, RouterState>
{
    private const int Length = ConveyorBehavior.Length;

    public override string Name => "router";

    protected override void Bind(BuildingDef def, RouterParams p, ContentRegistry content)
    {
        Require(def.InputPorts.Count >= 1 && def.OutputPorts.Count >= 1, def, "needs input and output ports.");
        Require(p.Speed > 0 && p.Spacing > 0, def, "speed and spacing must be > 0.");
        Require(def.Footprint.Length == 1, def, "must be a single cell.");
    }

    public override object CreateState(BuildingDef def)
    {
        var s = new RouterState { RefusedAt = new long[def.Ports.Length] };
        Array.Fill(s.RefusedAt, long.MinValue / 2);
        return s;
    }

    private static int Speed(TickContext ctx, Entity e, RouterParams p) =>
        Math.Clamp((int)(p.Speed * ctx.Stat(StatIds.ConveyorSpeed) * e.SpeedFactor), 1, p.Spacing);

    public override UpgradeTrack DefaultUpgrade(BuildingDef def) =>
        new() { MaxLevel = 9, SpeedPerLevel = 0.5, CostFactor = 1.5, CostGrowth = 2.2 };

    protected override void Tick(TickContext ctx, Entity e, RouterParams p, RouterState s)
    {
        var items = s.Items;
        if (items.Count == 0) return;
        int speed = Speed(ctx, e, p);
        int w = 0;

        for (int i = 0; i < items.Count; i++)
        {
            var it = items[i];
            int target = it.Pos + speed;
            if (w > 0) target = Math.Min(target, items[w - 1].Pos - p.Spacing);

            if (it.To < 0 && target >= Length / 2) it.To = PickOutput(e, s);
            if (it.To < 0) target = Math.Min(target, Length / 2); // nowhere to go yet: wait at the centre

            if (w == 0 && target >= Length)
            {
                if (TryExit(ctx, e, s, ref it, target - Length)) continue;
                target = Length;
            }

            if (target < it.Pos) target = it.Pos;
            items[w++] = it with { Pos = target };
        }
        if (w < items.Count) items.RemoveRange(w, items.Count - w);
    }

    private static int PickOutput(Entity e, RouterState s)
    {
        var outs = e.Def.OutputPorts;
        for (int k = 0; k < outs.Count; k++)
        {
            int idx = (s.NextOut + k) % outs.Count;
            if (!e.Link(outs[idx]).IsConnected) continue;
            s.NextOut = (idx + 1) % outs.Count;
            return outs[idx];
        }
        return -1;
    }

    /// <summary>Leave through the chosen output, or any other connected one if it is blocked.</summary>
    private static bool TryExit(TickContext ctx, Entity e, RouterState s, ref RouterItem it, int overflow)
    {
        if (ctx.Push(e, it.To, it.Item, overflow)) return true;
        var outs = e.Def.OutputPorts;
        int start = outs.ToList().IndexOf(it.To);
        for (int k = 1; k < outs.Count; k++)
        {
            int port = outs[(start + k) % outs.Count];
            if (!e.Link(port).IsConnected || !ctx.Push(e, port, it.Item, overflow)) continue;
            return true;
        }
        return false;
    }

    protected override bool TryAccept(TickContext ctx, Entity e, RouterParams p, RouterState s, ItemStack item, int port, int overflow)
    {
        var items = s.Items;
        int preferred = s.NextIn;
        bool preferredWaiting = preferred >= 0 && preferred != port && e.IsInputFed(preferred) && s.RefusedAt[preferred] >= ctx.Tick - 1;

        int entry = Math.Min(overflow, Speed(ctx, e, p));
        if (items.Count > 0) entry = Math.Min(entry, items[^1].Pos - p.Spacing);

        if (preferredWaiting || entry < 0)
        {
            s.RefusedAt[port] = ctx.Tick;
            return false;
        }

        items.Add(new RouterItem(item, entry, port, -1));
        s.NextIn = NextFedInput(e, port);
        return true;
    }

    private static int NextFedInput(Entity e, int after)
    {
        var ins = e.Def.InputPorts;
        int start = ins.ToList().IndexOf(after);
        for (int k = 1; k <= ins.Count; k++)
        {
            int port = ins[(start + k) % ins.Count];
            if (e.IsInputFed(port)) return port;
        }
        return -1;
    }

    protected override void CollectItems(Entity e, RouterParams p, RouterState s, List<ItemView> into)
    {
        foreach (var it in s.Items) into.Add(new ItemView(it.Item, it.Pos / (float)Length, it.From, it.To));
    }

    protected override EntityStatus GetStatus(Entity e, RouterParams p, RouterState s) =>
        new(s.Items.Count > 0, 0, s.Items.Count == 0 ? "empty" : $"{s.Items.Count} item(s)");

    protected override void Describe(Entity e, RouterParams p, RouterState s, List<InfoLine> into)
    {
        int ins = e.Def.InputPorts.Count(e.IsInputFed);
        int outs = e.Def.OutputPorts.Count(o => e.Link(o).IsConnected);
        into.Add(new InfoLine("Connected", $"{ins} in / {outs} out"));
        double speed = Math.Min(p.Speed * e.SpeedFactor, p.Spacing);
        into.Add(new InfoLine("Throughput", $"{speed * Simulation.TicksPerSecond / p.Spacing:0.#} items/s"));
        into.Add(new InfoLine("Inside", s.Items.Count.ToString()));
    }
}
