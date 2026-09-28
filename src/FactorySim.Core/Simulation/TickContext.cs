using FactorySim.Content;

namespace FactorySim;

/// <summary>
/// What a behavior may do during a tick: move items between entities, create items,
/// earn money, read stats, emit events. Deliberately narrower than World (behaviors
/// cannot place or remove buildings mid-tick).
/// </summary>
public sealed class TickContext
{
    /// <summary>
    /// Overflow value for an item that has been ready at the sender's edge since before this
    /// tick: the receiver may place it as far forward as its own speed and spacing allow.
    /// Without this, machine outputs would only enter belts at position 0 and lose compression.
    /// </summary>
    public const int Waiting = int.MaxValue;

    private readonly Simulation _sim;

    internal TickContext(Simulation sim) => _sim = sim;

    public World World => _sim.World;
    public ContentRegistry Content => _sim.World.Content;
    public long Tick => _sim.World.Tick;
    public Rng Rng => _sim.World.Rng;

    public double Stat(string stat) => _sim.World.Stat(stat);

    /// <summary>Max units per bundle (≥ 1).</summary>
    public long MaxStackSize => Math.Max(1, (long)Stat(StatIds.StackSize));

    public ItemStack CreateItem(string type, long count, BigNum unitValue) => _sim.World.CreateItem(type, count, unitValue);

    /// <summary>
    /// Offer <paramref name="item"/> to whatever is connected to output <paramref name="outPort"/>.
    /// Returns true if the receiver took ownership (caller must drop its reference).
    /// </summary>
    public bool Push(Entity from, int outPort, ItemStack item, int overflow = 0)
    {
        var link = from.Links[outPort];
        return link.Target != null && link.Target.Behavior.TryAccept(this, link.Target, item, link.TargetPort, overflow);
    }

    public void Sell(Entity seller, ItemStack item, BigNum payout)
    {
        _sim.World.AddMoney(payout);
        _sim.World.Stats.RecordSale(item.Type, item.Count, payout);
        if (_sim.Events.Enabled) _sim.Events.Add(new ItemSold(Tick, seller.Id, item.Type, item.Count, payout));
    }

    public void RecordProduced(Entity producer, ItemStack item)
    {
        _sim.World.Stats.RecordProduced(item.Type, item.Count);
        if (_sim.Events.Enabled) _sim.Events.Add(new ItemProduced(Tick, producer.Id, item.Type, item.Count));
    }

    public void Emit(SimEvent ev)
    {
        if (_sim.Events.Enabled) _sim.Events.Add(ev);
    }
}
