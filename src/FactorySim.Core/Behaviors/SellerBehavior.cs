using FactorySim.Content;
using FactorySim.View;

namespace FactorySim.Behaviors;

public sealed class SellerParams
{
    /// <summary>Local price multiplier (stacks with the global "sell.multiplier" stat).</summary>
    public double Multiplier { get; init; } = 1;
}

public sealed class SellerState
{
    public BigNum Earned { get; set; }
    public long Units { get; set; }
}

/// <summary>Sink: converts every accepted item into money immediately.</summary>
public sealed class SellerBehavior : Behavior<SellerParams, SellerState>
{
    public override string Name => "seller";

    protected override void Bind(BuildingDef def, SellerParams p, ContentRegistry content)
    {
        Require(def.InputPorts.Count >= 1, def, "needs at least one input port.");
    }

    protected override bool TryAccept(TickContext ctx, Entity e, SellerParams p, SellerState s, ItemStack item, int port, int overflow)
    {
        BigNum payout = item.TotalValue * (p.Multiplier * ctx.Stat(StatIds.SellMultiplier));
        s.Earned += payout;
        s.Units += item.Count;
        ctx.Sell(e, item, payout);
        return true;
    }

    protected override EntityStatus GetStatus(Entity e, SellerParams p, SellerState s) => new(true, 0, "selling");

    protected override void Describe(Entity e, SellerParams p, SellerState s, List<InfoLine> into)
    {
        into.Add(new InfoLine("Earned", "$" + s.Earned.Format()));
        into.Add(new InfoLine("Units sold", s.Units.ToString()));
    }
}
