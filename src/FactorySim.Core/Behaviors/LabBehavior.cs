using System.Text.Json.Serialization;
using FactorySim.Content;
using FactorySim.View;

namespace FactorySim.Behaviors;

public sealed class LabParams
{
    /// <summary>Pack items this lab takes. Empty = every item marked <c>science</c>.</summary>
    public string[] Items { get; init; } = Array.Empty<string>();

    /// <summary>Ticks to study one pack at level 1 (80 = one every 4 seconds).</summary>
    public int Interval { get; init; } = 80;

    /// <summary>Packs held waiting to be studied before the lab refuses more.</summary>
    public int Capacity { get; init; } = 10;

    [JsonIgnore] internal HashSet<string> Accepted { get; set; } = new();
    [JsonIgnore] internal Dictionary<string, string> ItemNames { get; set; } = new();
}

public sealed class LabState
{
    // Keys are never removed, so enumeration order (and the save) is a pure function of history.
    public Dictionary<string, long> Held { get; set; } = new();

    public double Work { get; set; }

    /// <summary>Packs this lab has put into the bank.</summary>
    public long Banked { get; set; }
}

/// <summary>
/// Research sink: takes packs off a belt, studies one per <see cref="LabParams.Interval"/>, and adds
/// it to the world's research bank (<see cref="World.Science"/>), from which upgrades priced in
/// packs are bought. It needs no research to be picked, so a lab never backs up its belt for want
/// of a choice: it only refuses packs while it holds a full load.
/// </summary>
public sealed class LabBehavior : Behavior<LabParams, LabState>
{
    public override string Name => "lab";

    protected override void Bind(BuildingDef def, LabParams p, ContentRegistry content)
    {
        Require(def.InputPorts.Count >= 1, def, "needs at least one input port.");
        Require(p.Interval > 0 && p.Capacity > 0, def, "interval and capacity must be > 0.");
        foreach (var id in p.Items)
            Require(content.Items.ContainsKey(id), def, $"unknown item '{id}'.");
        var accepted = p.Items.Length > 0
            ? p.Items
            : content.Items.Values.Where(i => i.Science).Select(i => i.Id).ToArray();
        p.Accepted = accepted.ToHashSet();
        p.ItemNames = accepted.ToDictionary(id => id, id => content.Items[id].Name);
    }

    protected override void Tick(TickContext ctx, Entity e, LabParams p, LabState s)
    {
        string? next = Next(s);
        if (next == null)
        {
            s.Work = 0;
            return;
        }
        s.Work += e.SpeedFactor;
        while (s.Work >= p.Interval && next != null)
        {
            s.Work -= p.Interval;
            s.Held[next]--;
            s.Banked++;
            ctx.World.AddScience(next, 1);
            next = Next(s);
        }
        if (next == null) s.Work = 0;
    }

    private static string? Next(LabState s)
    {
        foreach (var (item, count) in s.Held)
            if (count > 0) return item;
        return null;
    }

    private static long HeldTotal(LabState s)
    {
        long total = 0;
        foreach (var count in s.Held.Values) total += count;
        return total;
    }

    protected override bool TryAccept(TickContext ctx, Entity e, LabParams p, LabState s, ItemStack item, int port, int overflow)
    {
        if (!p.Accepted.Contains(item.Type) || HeldTotal(s) >= p.Capacity) return false;
        s.Held[item.Type] = s.Held.GetValueOrDefault(item.Type) + item.Count;
        return true;
    }

    /// <summary>Exact: a lab's load only shrinks as it studies, so a pack it would take now it would take later.</summary>
    protected override bool? WouldAccept(TickContext ctx, Entity e, LabParams p, LabState s, ItemStack item, int port, int inTicks) =>
        p.Accepted.Contains(item.Type) && HeldTotal(s) < p.Capacity;

    protected override EntityStatus GetStatus(Entity e, LabParams p, LabState s) =>
        Next(s) is { } item
            ? new EntityStatus(true, (float)Math.Min(1, s.Work / p.Interval), $"studying {p.ItemNames.GetValueOrDefault(item, item)}")
            : new EntityStatus(false, 0, "waiting for packs", IdleReason.Starved);

    /// <summary>A few levels of speed, for players who want more research from one lab.</summary>
    public override UpgradeTrack DefaultUpgrade(BuildingDef def) =>
        new() { MaxLevel = 5, SpeedPerLevel = 0.5, CostFactor = 1.5, CostGrowth = 2 };

    protected override void Describe(Entity e, LabParams p, LabState s, List<InfoLine> into)
    {
        into.Add(new InfoLine("Studies", p.ItemNames.Count == 0 ? "nothing (no packs in the content)" : string.Join(", ", p.ItemNames.Values)));
        double perMinute = 60.0 * Simulation.TicksPerSecond * e.SpeedFactor / p.Interval;
        into.Add(new InfoLine("Speed", $"{perMinute:0.#} packs/min"));
        into.Add(new InfoLine("Holding", $"{HeldTotal(s)}/{p.Capacity}"));
        into.Add(new InfoLine("Banked", s.Banked.ToString()));
    }

    /// <summary>Packs the lab no longer studies (their item was removed from the content) are dropped.</summary>
    protected override void CheckLoaded(Entity e, LabParams p, LabState s, List<string> warnings)
    {
        foreach (var (item, count) in s.Held.ToList())
            if (count > 0 && !p.Accepted.Contains(item))
            {
                s.Held[item] = 0;
                warnings.Add($"{e.Def.Name} #{e.Id}: dropped {count} '{item}' it no longer studies.");
            }
    }
}
