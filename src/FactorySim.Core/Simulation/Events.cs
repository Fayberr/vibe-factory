namespace FactorySim;

/// <summary>
/// Something that happened in the simulation. The sim only appends to the
/// <see cref="EventQueue"/>; hosts (renderer, audio, analytics, network) drain it
/// on their own schedule. Nothing in the sim depends on who listens.
/// </summary>
public abstract record SimEvent(long Tick);

public sealed record EntityPlaced(long Tick, int EntityId, string DefId, GridPos Pos, Dir Facing) : SimEvent(Tick);
public sealed record EntityRemoved(long Tick, int EntityId, string DefId, GridPos Pos) : SimEvent(Tick);
public sealed record EntityReoriented(long Tick, int EntityId, GridPos Pos, Dir Facing) : SimEvent(Tick);
public sealed record ItemProduced(long Tick, int EntityId, string Item, long Count) : SimEvent(Tick);
public sealed record ItemSold(long Tick, int EntityId, string Item, long Count, BigNum Payout) : SimEvent(Tick);
public sealed record CraftCompleted(long Tick, int EntityId, string Recipe, long Crafts) : SimEvent(Tick);
public sealed record UpgradePurchased(long Tick, string UpgradeId, int Level, BigNum Cost) : SimEvent(Tick);

/// <summary>Bounded buffer of pending events. Oldest events are dropped if nobody drains it.</summary>
public sealed class EventQueue
{
    private readonly List<SimEvent> _pending = new();

    /// <summary>Disable to skip allocation entirely (headless runs, offline catch-up).</summary>
    public bool Enabled { get; set; } = true;

    public int Capacity { get; set; } = 50_000;
    public long Dropped { get; private set; }

    public int Count => _pending.Count;

    internal void Add(SimEvent ev)
    {
        if (_pending.Count >= Capacity)
        {
            int drop = Capacity / 2;
            _pending.RemoveRange(0, drop);
            Dropped += drop;
        }
        _pending.Add(ev);
    }

    /// <summary>Moves all pending events into <paramref name="into"/> (cleared first) and empties the queue.</summary>
    public void Drain(List<SimEvent> into)
    {
        into.Clear();
        into.AddRange(_pending);
        _pending.Clear();
    }

    public void Clear() => _pending.Clear();
}
