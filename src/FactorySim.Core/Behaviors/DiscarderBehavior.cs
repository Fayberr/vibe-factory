using FactorySim.Content;
using FactorySim.View;

namespace FactorySim.Behaviors;

public sealed class DiscarderParams
{
}

public sealed class DiscarderState
{
    public long Units { get; set; }
    public BigNum Value { get; set; }
}

/// <summary>Sink: destroys every accepted item without paying for it.</summary>
public sealed class DiscarderBehavior : Behavior<DiscarderParams, DiscarderState>
{
    public override string Name => "discarder";

    protected override void Bind(BuildingDef def, DiscarderParams p, ContentRegistry content)
    {
        Require(def.InputPorts.Count >= 1, def, "needs at least one input port.");
    }

    protected override bool TryAccept(TickContext ctx, Entity e, DiscarderParams p, DiscarderState s, ItemStack item, int port, int overflow)
    {
        s.Units += item.Count;
        s.Value += item.TotalValue;
        return true;
    }

    protected override bool? WouldAccept(TickContext ctx, Entity e, DiscarderParams p, DiscarderState s, ItemStack item, int port, int inTicks) => true;

    protected override EntityStatus GetStatus(Entity e, DiscarderParams p, DiscarderState s) => new(true, 0, "incinerating");

    public override UpgradeTrack DefaultUpgrade(BuildingDef def) => new() { MaxLevel = 1 };

    protected override void Describe(Entity e, DiscarderParams p, DiscarderState s, List<InfoLine> into)
    {
        into.Add(new InfoLine("Units destroyed", s.Units.ToString()));
        into.Add(new InfoLine("Value destroyed", "$" + s.Value.Format()));
    }
}
