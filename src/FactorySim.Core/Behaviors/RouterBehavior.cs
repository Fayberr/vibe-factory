using System.Text.Json.Serialization;
using FactorySim.Content;
using FactorySim.View;

namespace FactorySim.Behaviors;

public sealed class RouterParams
{
    public int Speed { get; init; } = 100;
    public int Spacing { get; init; } = 250;

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

    /// <summary>Per port: last tick an offer through it was refused (i.e. it has items waiting).</summary>
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
    /// Per port: ticks in a row an item was refused through it. An item it cannot send stops the lane
    /// behind it, so an output that keeps refusing holds the whole hub up and that is what the status
    /// and the panel report. A single refusal in busy traffic is not a jam and does not count.
    /// </summary>
    [JsonIgnore] public int[] RefusedTicks { get; set; } = Array.Empty<int>();

    /// <summary>Per port: an item was refused through it on the tick now running.</summary>
    [JsonIgnore] public bool[] RefusedNow { get; set; } = Array.Empty<bool>();

    /// <summary>Ticks in a row an item in the hub could not leave at all, because no connected output takes it.</summary>
    [JsonIgnore] public int NoOutputTicks { get; set; }

    /// <summary>That happened on the tick now running.</summary>
    [JsonIgnore] public bool NoOutputNow { get; set; }

    /// <summary>Type of the last item no connection would take, for the status text.</summary>
    [JsonIgnore] public string? NoOutputItem { get; set; }
}

/// <summary>An item crossing a hub: entered through <see cref="From"/>, leaving through <see cref="To"/> (-1 = not chosen yet).</summary>
public record struct RouterItem(ItemStack Item, int Pos, int From, int To);

/// <summary>
/// Belt hub that routes between several inputs and outputs. Splitters (1 → 3) and
/// mergers (3 → 1) are this behavior with different ports.
///  • An item rides from its entry edge to the middle of the hub, picks a way out there and leaves from
///    there. The lane stops at the middle, so an item whose output is full waits in the middle of the
///    building instead of running on to the cell edge; a lane that reached the edge parked such an item
///    on the neighbour's own first item, which read as "it has already left".
///  • Outputs are chosen round-robin at the middle among connected outputs; a blocked
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

    /// <summary>
    /// Middle of a hub's lane: where an item chooses its way out, and where it waits when that way is not
    /// clear. An item rides in from its entry edge to here and may not pass it until the hub knows the
    /// receiver will have room by the time the item reaches the edge (<see cref="CanLeave"/>), and then it
    /// rolls the rest of the way out at once (<see cref="RollSpeed"/>). Two things follow, and both are the
    /// point: an item the hub cannot send stands in the middle of the building instead of on the cell edge
    /// it shares with the belt that is refusing it (drawn there it sat on the belt's own first item, and
    /// the picture read as "it has already left" while the hub still held it), and the item it does send
    /// leaves the middle as one continuous move rather than hopping the last half tile in a single step.
    /// The lane behind the middle still holds the rest of the queue, up to the entry edge.
    /// </summary>
    private const int Middle = Length / 2;

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

    protected override void Tick(TickContext ctx, Entity e, RouterParams p, RouterState s)
    {
        int ports = e.Def.Ports.Length;
        if (s.RefusedTicks.Length != ports)
        {
            s.RefusedTicks = new int[ports];
            s.RefusedNow = new bool[ports];
        }
        for (int i = 0; i < ports; i++)
        {
            s.RefusedTicks[i] = s.RefusedNow[i] ? s.RefusedTicks[i] + 1 : 0;
            s.RefusedNow[i] = false;
        }
        s.NoOutputTicks = s.NoOutputNow ? s.NoOutputTicks + 1 : 0;
        s.NoOutputNow = false;
        if (s.NoOutputTicks == 0) s.NoOutputItem = null;

        var items = s.Items;
        if (items.Count == 0) return;
        int speed = Speed(ctx, e, p);
        int roll = RollSpeed(speed);
        int w = 0;

        // A hub held up for a second with its front item still past the middle is in a state the lane cannot
        // reach any more: a save written before the lane stopped at the middle (see Middle), or a receiver
        // that closed up halfway through an exit roll. Lay the lane out again so the item comes back in.
        // Nothing is dropped, and an output that cannot answer ahead (see CanLeave) is left alone, so an item
        // waiting on a machine keeps the place in the lane it always had.
        if (JammedOutput(e, s) >= 0 && items[0].Pos > Middle
            && CanLeave(ctx, e, s, items[0], TicksToExit(items[0].Pos, roll)) == false)
            Relane(items);

        for (int i = 0; i < items.Count; i++)
        {
            var it = items[i];
            int target = it.Pos + (it.Pos > Middle ? roll : speed);
            if (w > 0) target = Math.Min(target, items[w - 1].Pos - p.Spacing);

            if (it.To < 0 && target >= Middle)
            {
                it.To = PickOutput(ctx, e, s, it.Item.Type);
                if (it.To < 0)
                {
                    // Nothing connected takes it at all: it waits at the middle and holds up the belt behind.
                    s.NoOutputNow = true;
                    s.NoOutputItem = it.Item.Type;
                }
            }
            if (it.To < 0) target = Math.Min(target, Middle); // nowhere to go yet: wait at the middle

            if (w == 0 && it.To >= 0 && it.Pos >= Middle)
            {
                // The head decides at the middle whether it may go: if the way out will be clear by the time
                // the item would reach the edge, it rolls out to the edge and hands over there, where the
                // belt's first place is, so the hand-over is one continuous move. If not, it waits in the
                // middle, which is what the player asked for: "when an item reaches the middle ... it checks
                // if the target direction is already full or blocked; if yes it stays in the middle".
                bool? clear = CanLeave(ctx, e, s, it, TicksToExit(it.Pos, roll));
                if (clear == false)
                {
                    if (it.To < s.RefusedNow.Length) s.RefusedNow[it.To] = true; // the output holding the hub up
                    target = Math.Min(target, Middle);
                }
                else
                {
                    target = it.Pos + roll; // committed: leave the middle at the exit roll
                    if (target >= Length && TryExit(ctx, e, s, ref it, target - Length)) continue;
                }
            }

            if (target < it.Pos) target = it.Pos;
            items[w++] = it with { Pos = target };
        }
        if (w < items.Count) items.RemoveRange(w, items.Count - w);
    }

    /// <summary>Speed of an item that has committed to leaving: out of the middle to the edge.</summary>
    private static int RollSpeed(int speed) => speed * 2;

    /// <summary>
    /// Ticks until an item moving at <paramref name="roll"/> from <paramref name="pos"/> would reach the
    /// exit, which is when a receiver has to be ready for it.
    /// </summary>
    private static int TicksToExit(int pos, int roll) => Math.Max(1, (Length - pos + roll - 1) / roll);

    /// <summary>
    /// Lays the lane out again from the middle back to the entry, evenly, keeping the items in order and
    /// dropping none. Used on a lane holding an item past the middle while its output answers that it cannot
    /// take it.
    /// </summary>
    private static void Relane(List<RouterItem> items)
    {
        int n = items.Count;
        for (int i = 0; i < n; i++)
            items[i] = items[i] with { Pos = n == 1 ? Middle : Middle - i * (Middle / (n - 1)) };
    }

    /// <summary>
    /// Whether the item would get out now, asking each output in the order <see cref="TryExit"/> would try
    /// them, without sending anything. False means "wait in the middle". Null means no candidate could say
    /// (a receiver that cannot answer ahead), and then the item commits and tries out at the edge as it
    /// always did.
    /// </summary>
    private static bool? CanLeave(TickContext ctx, Entity e, RouterState s, in RouterItem it, int inTicks)
    {
        var outs = e.Def.OutputPorts;
        string type = it.Item.Type;
        var home = HomeOf(e, s, type);
        if (home == Group.None) return false; // no connected output may take it: wait in the middle
        int start = 0;
        while (start < outs.Count - 1 && outs[start] != it.To) start++;

        bool unknown = false;
        for (int k = 0; k < outs.Count; k++)
        {
            int idx = (start + k) % outs.Count;
            var group = GroupOf(s, idx, type);
            // The same candidates TryExit tries, in the same order: the chosen output (or an overflow one),
            // then the rest of its group, then any overflow output.
            bool candidate = k == 0
                ? group == home || group == Group.Overflow
                : group == home || (home != Group.Overflow && s.Filters != null && group == Group.Overflow);
            if (!candidate) continue;
            var answer = Ask(ctx, e, outs[idx], it.Item, inTicks);
            if (answer == true) return true;
            if (answer == null) unknown = true;
        }
        // Nobody can say whether it would get out: commit and let it try at the edge, as it always did.
        return unknown ? null : false;
    }

    /// <summary>What one output says about an item arriving in <paramref name="inTicks"/> ticks.</summary>
    private static bool? Ask(TickContext ctx, Entity e, int port, ItemStack item, int inTicks) =>
        e.Link(port).IsConnected ? ctx.WouldPush(e, port, item, inTicks) : false;

    /// <summary>Filter value for an output that takes only what the other outputs refuse.</summary>
    public const string Overflow = "@overflow";

    private enum Group : byte { None, Matched, Open, Overflow }

    private static Group GroupOf(RouterState s, int output, string type)
    {
        string? rule = s.Filters is { } f && output < f.Length ? f[output] : null;
        return rule == null ? Group.Open : rule == Overflow ? Group.Overflow : rule == type ? Group.Matched : Group.None;
    }

    /// <summary>Where an item belongs: outputs set to its type, else open ones, else overflow (connected ones only).</summary>
    private static Group HomeOf(Entity e, RouterState s, string type) => HomeGroup(e, s, type, connectedOnly: true);

    /// <summary>
    /// The group an item belongs to. With <paramref name="connectedOnly"/> false the belts are ignored,
    /// which answers "where would this item go if the belt were attached" and is how a missing belt is
    /// told apart from a full one.
    /// </summary>
    private static Group HomeGroup(Entity e, RouterState s, string type, bool connectedOnly)
    {
        if (s.Filters == null) return Group.Open;
        var home = Group.None;
        var outs = e.Def.OutputPorts;
        for (int i = 0; i < outs.Count; i++)
        {
            if (connectedOnly && !e.Link(outs[i]).IsConnected) continue;
            var g = GroupOf(s, i, type);
            if (g != Group.None && (home == Group.None || g < home)) home = g;
        }
        return home;
    }

    /// <summary>
    /// The output this item needs that has no belt on it, or -1. A rule can be set on a port with nothing
    /// attached: never placed, or placed facing the other way, which is no link at all. From the outside
    /// that looks exactly like a hub whose belt is full, so the hub says which of the two it is. Only a
    /// group that is set for this item counts, so a plain splitter whose belt is full is never told about
    /// a belt-less port it never asked for.
    /// </summary>
    private static int MissingOutput(Entity e, RouterState s, string type)
    {
        if (s.Filters == null) return -1; // nothing was asked for: a full belt is the thing to report
        var wanted = HomeGroup(e, s, type, connectedOnly: false);
        if (wanted == Group.None) return -1; // no output is set for this item at all: a filter problem
        var outs = e.Def.OutputPorts;
        for (int i = 0; i < outs.Count; i++)
            if (GroupOf(s, i, type) == wanted && !e.Link(outs[i]).IsConnected) return i;
        return -1;
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
    private static bool TryExit(TickContext ctx, Entity e, RouterState s, ref RouterItem it, int overflow)
    {
        var outs = e.Def.OutputPorts;
        int start = 0;
        while (start < outs.Count - 1 && outs[start] != it.To) start++;
        string type = it.Item.Type;
        var home = HomeOf(e, s, type);
        if (home == Group.None)
        {
            // Nothing connected takes it any more: wait until a filter or belt changes.
            s.NoOutputNow = true;
            return false;
        }
        var chosen = GroupOf(s, start, type);
        if ((chosen == home || chosen == Group.Overflow) && TryPush(ctx, e, s, it.To, it.Item, overflow)) return true;
        for (int k = 1; k < outs.Count; k++)
        {
            int idx = (start + k) % outs.Count;
            if (!e.Link(outs[idx]).IsConnected || GroupOf(s, idx, type) != home) continue;
            if (TryPush(ctx, e, s, outs[idx], it.Item, overflow)) return true;
        }
        if (home == Group.Overflow || s.Filters == null) return false;
        for (int k = 1; k < outs.Count; k++)
        {
            int idx = (start + k) % outs.Count;
            if (!e.Link(outs[idx]).IsConnected || GroupOf(s, idx, type) != Group.Overflow) continue;
            if (TryPush(ctx, e, s, outs[idx], it.Item, overflow)) return true;
        }
        return false;
    }

    /// <summary>
    /// Send the item out through one port. A refusal is remembered per output: an item that cannot leave
    /// stops the lane behind it, so an output that keeps refusing is what the panel names when the hub
    /// looks stuck, and the player can see which belt to look at.
    /// </summary>
    private static bool TryPush(TickContext ctx, Entity e, RouterState s, int port, ItemStack item, int overflow)
    {
        if (ctx.Push(e, port, item, overflow)) return true;
        if (port >= 0 && port < s.RefusedNow.Length) s.RefusedNow[port] = true;
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
        foreach (var it in s.Items) into.Add(new ItemView(it.Item, it.Pos / (float)Length, it.From, it.To));
    }

    /// <summary>Ticks an output must keep refusing items before the hub calls itself held up (1 second).</summary>
    public const int JamTicks = 20;

    /// <summary>
    /// The output that is holding the hub up, as an output number, or -1. Only an output that keeps
    /// refusing counts: one refusal in busy traffic is not a jam.
    /// </summary>
    public static int JammedOutput(Entity e, RouterState s)
    {
        var outs = e.Def.OutputPorts;
        for (int i = 0; i < outs.Count; i++)
            if (outs[i] < s.RefusedTicks.Length && s.RefusedTicks[outs[i]] >= JamTicks) return i;
        return -1;
    }

    /// <summary>
    /// What is wrong with one output, to hang on its name in the panel: no belt at all, or a belt that
    /// keeps refusing. Empty when the output is fine, so nothing changes for a hub that is running.
    /// </summary>
    public static string OutputNote(Entity e, RouterState s, int output)
    {
        if (!e.Link(e.Def.OutputPorts[output]).IsConnected) return " (no belt)";
        return JammedOutput(e, s) == output ? " (belt full)" : "";
    }

    /// <summary>
    /// A hub that cannot pass something says so, because "5 item(s)" on a stopped line reads exactly like
    /// a busy one. See <see cref="JammedOutput"/>: the point is that a full belt downstream is the reason
    /// the whole line stopped, and a player should not have to guess that from a frozen picture.
    /// </summary>
    protected override EntityStatus GetStatus(Entity e, RouterParams p, RouterState s)
    {
        int jam = JammedOutput(e, s);
        bool nothingTakes = s.NoOutputTicks >= JamTicks && s.NoOutputItem != null;
        if (jam < 0 && !nothingTakes)
            return new(s.Items.Count > 0, 0, s.Items.Count == 0 ? "empty" : $"{s.Items.Count} item(s)");

        // A belt that is not attached is the one cause a player cannot see, so it is named first, for the
        // item stuck at the exit and for the one stuck in the middle with no output at all.
        string? front = s.Items.Count > 0 ? s.Items[0].Item.Type : null;
        foreach (string? type in new[] { front, nothingTakes ? s.NoOutputItem : null })
        {
            if (type == null) continue;
            if (MissingOutput(e, s, type) is var missing && missing >= 0)
                return new(false, 0, $"{OutputName(e.Def, missing)} has no belt", IdleReason.Blocked);
        }
        if (jam >= 0) return new(false, 0, $"{OutputName(e.Def, jam)} blocked", IdleReason.Blocked);

        string stuck = (nothingTakes ? s.NoOutputItem : front) ?? "?";
        string name = p.Items.TryGetValue(stuck, out var item) ? item.Name : stuck;
        return new(false, 0, $"nothing takes {name}", IdleReason.Blocked);
    }

    protected override void Describe(Entity e, RouterParams p, RouterState s, List<InfoLine> into)
    {
        int ins = e.Def.InputPorts.Count(e.IsInputFed);
        int outs = e.Def.OutputPorts.Count(o => e.Link(o).IsConnected);
        into.Add(new InfoLine("Connected", $"{ins} in / {outs} out"));
        double speed = Math.Min(p.Speed * e.SpeedFactor, p.Spacing);
        into.Add(new InfoLine("Throughput", $"{speed * Simulation.TicksPerSecond / p.Spacing:0.#} items/s"));
        into.Add(new InfoLine("Inside", s.Items.Count.ToString()));
        if (s.Filters is { } f)
            into.Add(new InfoLine("Sorting", string.Join(" · ", f.Select((rule, i) =>
                $"{OutputName(e.Def, i)}: {RuleName(p, rule)}{OutputNote(e, s, i)}"))));
    }

    /// <summary>How a filter reads to the player: "anything", "overflow" or the item's name.</summary>
    public static string RuleName(RouterParams p, string? rule) => rule switch
    {
        null => "anything",
        Overflow => "overflow",
        _ => p.Items.TryGetValue(rule, out var item) ? item.Name : rule,
    };
}
