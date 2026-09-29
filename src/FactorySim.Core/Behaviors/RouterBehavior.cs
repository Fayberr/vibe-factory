using System.Text.Json.Serialization;
using FactorySim.Content;
using FactorySim.View;

namespace FactorySim.Behaviors;

public sealed class RouterParams
{
    public int Speed { get; init; } = 100;
    public int Spacing { get; init; } = 250;

    /// <summary>
    /// Most stacks a sorting hub sets aside for full outputs (see <see cref="RouterState.Held"/>). Identical
    /// stacks are kept as one count, so this bounds a number, not memory: at the fastest a hub moves (one
    /// item a tick) it is over 13 hours of nothing but the jammed item before the hub blocks again.
    /// </summary>
    public long HoldLimit { get; init; } = 1_000_000;

    [JsonIgnore] internal IReadOnlyDictionary<string, ItemDef> Items { get; set; } = new Dictionary<string, ItemDef>();
}

public sealed class RouterState
{
    /// <summary>Items in entry order (front-most first).</summary>
    public List<RouterItem> Items { get; set; } = new();

    /// <summary>Round-robin cursor into the def's output ports.</summary>
    public int NextOut { get; set; }

    /// <summary>Input port that gets priority next (fair merging).</summary>
    public int NextIn { get; set; } = -1;

    /// <summary>
    /// Per port: last tick an offer through it was refused. For an input that means it has items waiting;
    /// for an output of a sorting hub that it was full (reset once it takes an item again).
    /// </summary>
    public long[] RefusedAt { get; set; } = Array.Empty<long>();

    /// <summary>
    /// Sorting rule per output, in the def's output order: null takes anything, an item id takes only
    /// that item, <see cref="RouterBehavior.Overflow"/> takes only what no other output will. Null when
    /// nothing is set, which routes exactly as a hub without filters (and keeps saves unchanged).
    /// </summary>
    public string?[]? Filters { get; set; }

    /// <summary>
    /// Per output: tick an item was last sent to it. Only kept while filters are set, where each group
    /// of outputs takes turns on its own (one shared cursor would send every other item to one output).
    /// </summary>
    public long[]? LastPicked { get; set; }

    /// <summary>
    /// Items a sorting hub set aside because their outputs were full while another output was still
    /// taking items: out of <see cref="Items"/>, so the belt behind them keeps moving, and retried every
    /// tick, first in line first. Identical stacks share one entry. Not drawn; the inspector counts them.
    /// Empty on old saves, which load with none held.
    /// </summary>
    public List<HeldItems> Held { get; set; } = new();
}

/// <summary><see cref="Copies"/> identical stacks waiting for output port <see cref="To"/>.</summary>
public sealed class HeldItems
{
    /// <summary>The next stack to leave; the others are copies of it made as it goes.</summary>
    public ItemStack Item { get; set; } = null!;

    public int To { get; set; }

    /// <summary>1 on saves from before identical stacks were merged, where each entry held one.</summary>
    public long Copies { get; set; } = 1;
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
///  • A hub with two or more outputs can sort (<see cref="RouterState.Filters"/>): an item goes to
///    the outputs set to its type, else to the outputs that take anything, else to an overflow
///    output. The round-robin and the skipping of blocked outputs work within that group, and an
///    overflow output also takes what its group refuses. With no filter set every output is in one
///    group, which is the plain splitter.
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
        p.Items = content.Items;
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

    /// <summary>Most distinct kinds of stack held at once (a kind can be any number of copies).</summary>
    private const int MaxHeldKinds = 32;

    /// <summary>How long an output that refused stays counted as full before a held item probes it again.</summary>
    private const int RecheckTicks = Simulation.TicksPerSecond;

    private const long Never = long.MinValue / 2;

    protected override void Tick(TickContext ctx, Entity e, RouterParams p, RouterState s)
    {
        int speed = Speed(ctx, e, p);
        DrainHeld(ctx, e, s, speed);

        var items = s.Items;
        if (items.Count == 0) return;
        int w = 0;

        for (int i = 0; i < items.Count; i++)
        {
            var it = items[i];
            int target = it.Pos + speed;
            if (w > 0) target = Math.Min(target, items[w - 1].Pos - p.Spacing);

            if (it.To < 0 && target >= Length / 2) it.To = PickOutput(ctx, e, s, it.Item.Type);
            if (it.To < 0) target = Math.Min(target, Length / 2); // nowhere to go yet: wait at the centre

            if (w == 0 && target >= Length)
            {
                if (TryExit(ctx, e, s, it.Item, it.To, target - Length)) continue;
                // Every output it may use is full. If another output is still taking items, set this one
                // aside so the items behind it keep going there; otherwise it blocks the belt as it
                // always did, which is also what holds the line back when everything downstream is full.
                if (MayHold(ctx, e, s, it.Item.Type) && Hold(p, s, it.Item, it.To)) continue;
                target = Length;
            }

            if (target < it.Pos) target = it.Pos;
            items[w++] = it with { Pos = target };
        }
        if (w < items.Count) items.RemoveRange(w, items.Count - w);
    }

    /// <summary>Filter value for an output that takes only what the other outputs refuse.</summary>
    public const string Overflow = "@overflow";

    private enum Group : byte { None, Matched, Open, Overflow }

    private static Group GroupOf(RouterState s, int output, string type)
    {
        string? rule = s.Filters is { } f && output < f.Length ? f[output] : null;
        return rule == null ? Group.Open : rule == Overflow ? Group.Overflow : rule == type ? Group.Matched : Group.None;
    }

    /// <summary>Where an item belongs: outputs set to its type, else open ones, else overflow (connected ones only).</summary>
    private static Group HomeOf(Entity e, RouterState s, string type)
    {
        if (s.Filters == null) return Group.Open;
        var home = Group.None;
        var outs = e.Def.OutputPorts;
        for (int i = 0; i < outs.Count; i++)
        {
            if (!e.Link(outs[i]).IsConnected) continue;
            var g = GroupOf(s, i, type);
            if (g != Group.None && (home == Group.None || g < home)) home = g;
        }
        return home;
    }

    private static int PickOutput(TickContext ctx, Entity e, RouterState s, string type)
    {
        var outs = e.Def.OutputPorts;
        if (s.Filters == null)
        {
            for (int k = 0; k < outs.Count; k++)
            {
                int idx = (s.NextOut + k) % outs.Count;
                if (!e.Link(outs[idx]).IsConnected) continue;
                s.NextOut = (idx + 1) % outs.Count;
                return outs[idx];
            }
            return -1;
        }

        // Sorting: the output of the item's group that waited longest, so a group takes turns.
        var home = HomeOf(e, s, type);
        if (home == Group.None) return -1; // no connected output takes it: wait at the centre
        if (s.LastPicked?.Length != outs.Count) s.LastPicked = new long[outs.Count];
        int best = -1;
        for (int k = 0; k < outs.Count; k++)
        {
            int idx = (s.NextOut + k) % outs.Count;
            if (!e.Link(outs[idx]).IsConnected || GroupOf(s, idx, type) != home) continue;
            if (best < 0 || s.LastPicked[idx] < s.LastPicked[best]) best = idx;
        }
        if (best < 0) return -1;
        s.LastPicked[best] = ctx.Tick;
        s.NextOut = (best + 1) % outs.Count;
        return outs[best];
    }

    /// <summary>
    /// Leave through the chosen output, or another connected one of the same group if it is blocked,
    /// or an overflow output. The group is checked again here, since filters may have changed.
    /// </summary>
    private static bool TryExit(TickContext ctx, Entity e, RouterState s, ItemStack item, int to, int overflow)
    {
        var outs = e.Def.OutputPorts;
        int start = 0;
        while (start < outs.Count - 1 && outs[start] != to) start++;
        string type = item.Type;
        var home = HomeOf(e, s, type);
        if (home == Group.None) return false; // nothing takes it any more: wait until a filter or belt changes
        var chosen = GroupOf(s, start, type);
        if ((chosen == home || chosen == Group.Overflow) && Offer(ctx, e, s, to, item, overflow)) return true;
        for (int k = 1; k < outs.Count; k++)
        {
            int idx = (start + k) % outs.Count;
            if (!e.Link(outs[idx]).IsConnected || GroupOf(s, idx, type) != home || !Offer(ctx, e, s, outs[idx], item, overflow)) continue;
            return true;
        }
        if (home == Group.Overflow || s.Filters == null) return false;
        for (int k = 1; k < outs.Count; k++)
        {
            int idx = (start + k) % outs.Count;
            if (!e.Link(outs[idx]).IsConnected || GroupOf(s, idx, type) != Group.Overflow || !Offer(ctx, e, s, outs[idx], item, overflow)) continue;
            return true;
        }
        return false;
    }

    /// <summary>Push out through one port. A sorting hub notes whether the output was full, for <see cref="MayHold"/>.</summary>
    private static bool Offer(TickContext ctx, Entity e, RouterState s, int port, ItemStack item, int overflow)
    {
        bool took = ctx.Push(e, port, item, overflow);
        if (s.Filters != null && port < s.RefusedAt.Length) s.RefusedAt[port] = took ? Never : ctx.Tick;
        return took;
    }

    /// <summary>
    /// Whether an item whose outputs are all full may be set aside: only in a sorting hub (a plain one
    /// has nowhere else to send anything), only if some output takes it at all, and only while an output
    /// it can never use is not known to be full. An output that refused a while ago counts as free once,
    /// as a probe, so a line that stopped on a full output restarts when that output clears.
    /// </summary>
    private static bool MayHold(TickContext ctx, Entity e, RouterState s, string type)
    {
        if (s.Filters == null) return false;
        var home = HomeOf(e, s, type);
        if (home == Group.None) return false;
        var outs = e.Def.OutputPorts;
        int probe = -1;
        for (int i = 0; i < outs.Count; i++)
        {
            int port = outs[i];
            if (!e.Link(port).IsConnected || port >= s.RefusedAt.Length) continue;
            var g = GroupOf(s, i, type);
            if (g == home || g == Group.Overflow) continue; // one of its own, and all of those were full
            if (s.RefusedAt[port] == Never) return true;
            if (probe < 0 && s.RefusedAt[port] < ctx.Tick - RecheckTicks) probe = port;
        }
        if (probe < 0) return false;
        s.RefusedAt[probe] = ctx.Tick; // one probe per recheck while it stays silent
        return true;
    }

    /// <summary>Set an item aside, merged with identical ones for the same output. False when the hold is full.</summary>
    private static bool Hold(RouterParams p, RouterState s, ItemStack item, int to)
    {
        long total = 0;
        foreach (var h in s.Held) total += h.Copies;
        if (total >= p.HoldLimit) return false;
        foreach (var h in s.Held)
            if (h.To == to && Same(h.Item, item))
            {
                h.Copies++;
                return true;
            }
        if (s.Held.Count >= MaxHeldKinds) return false;
        s.Held.Add(new HeldItems { Item = item, To = to });
        return true;
    }

    /// <summary>Stacks that differ only in their id. Tagged stacks are never merged, so a copy needs no tags.</summary>
    private static bool Same(ItemStack a, ItemStack b) =>
        a.Tags == null && b.Tags == null && a.Type == b.Type && a.Count == b.Count && a.UnitValue == b.UnitValue && a.ValueBonus == b.ValueBonus;

    /// <summary>Retry items held aside for a full output, first in line first. A retry that still fails stays held.</summary>
    private static void DrainHeld(TickContext ctx, Entity e, RouterState s, int speed)
    {
        var held = s.Held;
        if (held.Count == 0) return;
        int w = 0;
        for (int i = 0; i < held.Count; i++)
        {
            var h = held[i];
            if (TryExit(ctx, e, s, h.Item, h.To, speed))
            {
                if (--h.Copies <= 0) continue;
                var next = ctx.CreateItem(h.Item.Type, h.Item.Count, h.Item.UnitValue);
                next.ValueBonus = h.Item.ValueBonus;
                h.Item = next;
            }
            held[w++] = h;
        }
        if (w < held.Count) held.RemoveRange(w, held.Count - w);
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

    /// <summary>Where output number <paramref name="output"/> points, as the player sees it ("Front", "Left").</summary>
    public static string OutputName(BuildingDef def, int output) => def.Ports[def.OutputPorts[output]].Side.ToString();

    protected override IReadOnlyList<string?>? Filters(Entity e, RouterParams p, RouterState s) => s.Filters?.ToArray();

    protected override string? SetFilter(Entity e, RouterParams p, RouterState s, int output, string? filter)
    {
        int outs = e.Def.OutputPorts.Count;
        if (outs < 2) return $"{e.Def.Name} has one output, so there is nothing to sort";
        if (output < 0 || output >= outs) return $"{e.Def.Name} has no output {output}";
        if (filter != null && filter != Overflow && !p.Items.ContainsKey(filter)) return $"Unknown item '{filter}'";
        var f = s.Filters ?? new string?[outs];
        if (f.Length != outs) Array.Resize(ref f, outs);
        f[output] = filter;
        s.Filters = Array.TrueForAll(f, x => x == null) ? null : f;
        if (s.Filters == null) s.LastPicked = null;
        return null;
    }

    protected override void CheckLoaded(Entity e, RouterParams p, RouterState s, List<string> warnings)
    {
        if (s.Filters is not { } f) return;
        int outs = e.Def.OutputPorts.Count;
        if (outs < 2)
        {
            s.Filters = null;
            s.LastPicked = null;
            warnings.Add($"Cleared the filters of entity #{e.Id} ({e.Def.Id}): it has one output.");
            return;
        }
        if (f.Length != outs) Array.Resize(ref f, outs);
        for (int i = 0; i < outs; i++)
            if (f[i] is { } rule && rule != Overflow && !p.Items.ContainsKey(rule))
            {
                f[i] = null;
                warnings.Add($"Cleared the {OutputName(e.Def, i).ToLowerInvariant()} filter of entity #{e.Id} ({e.Def.Id}): unknown item '{rule}'.");
            }
        s.Filters = Array.TrueForAll(f, x => x == null) ? null : f;
        if (s.Filters == null) s.LastPicked = null;
    }

    protected override void CollectItems(Entity e, RouterParams p, RouterState s, List<ItemView> into)
    {
        // Held items are not drawn: there can be thousands, and they would only pile up on one spot.
        foreach (var it in s.Items) into.Add(new ItemView(it.Item, it.Pos / (float)Length, it.From, it.To));
    }

    private static long HeldCount(RouterState s)
    {
        long n = 0;
        foreach (var h in s.Held) n += h.Copies;
        return n;
    }

    protected override EntityStatus GetStatus(Entity e, RouterParams p, RouterState s)
    {
        long held = HeldCount(s);
        if (held > 0) return new(true, 0, $"holding {held} item(s) for a full output");
        return new(s.Items.Count > 0, 0, s.Items.Count == 0 ? "empty" : $"{s.Items.Count} item(s)");
    }

    protected override void Describe(Entity e, RouterParams p, RouterState s, List<InfoLine> into)
    {
        int ins = e.Def.InputPorts.Count(e.IsInputFed);
        int outs = e.Def.OutputPorts.Count(o => e.Link(o).IsConnected);
        into.Add(new InfoLine("Connected", $"{ins} in / {outs} out"));
        double speed = Math.Min(p.Speed * e.SpeedFactor, p.Spacing);
        into.Add(new InfoLine("Throughput", $"{speed * Simulation.TicksPerSecond / p.Spacing:0.#} items/s"));
        into.Add(new InfoLine("Inside", s.Items.Count.ToString()));
        foreach (var g in s.Held.GroupBy(h => (h.Item.Type, h.To)))
        {
            string name = p.Items.TryGetValue(g.Key.Type, out var item) ? item.Name : g.Key.Type;
            into.Add(new InfoLine("Held", $"{g.Sum(h => h.Copies)} {name}, {e.Def.Ports[g.Key.To].Side.ToString().ToLowerInvariant()} output full"));
        }
        if (s.Filters is { } f)
            into.Add(new InfoLine("Sorting", string.Join(" · ", f.Select((rule, i) => $"{OutputName(e.Def, i)}: {RuleName(p, rule)}"))));
    }

    /// <summary>How a filter reads to the player: "anything", "overflow" or the item's name.</summary>
    public static string RuleName(RouterParams p, string? rule) => rule switch
    {
        null => "anything",
        Overflow => "overflow",
        _ => p.Items.TryGetValue(rule, out var item) ? item.Name : rule,
    };
}
