using FactorySim.Content;

namespace FactorySim.Behaviors;

public sealed class SellerParams
{
    /// <summary>Local price multiplier (stacks with the global "sell.multiplier" stat).</summary>
    public double Multiplier { get; init; } = 1;
}

/// <summary>Sink: converts every accepted item into money immediately.</summary>
public sealed class SellerBehavior : Behavior<SellerParams, NoState>
{
    public override string Name => "seller";

    protected override void Bind(BuildingDef def, SellerParams p, ContentRegistry content)
    {
        Require(def.InputPorts.Count >= 1, def, "needs at least one input port.");
    }

    protected override bool TryAccept(TickContext ctx, Entity e, SellerParams p, NoState s, ItemStack item, int port, int overflow)
    {
        BigNum payout = item.TotalValue * (p.Multiplier * ctx.Stat(StatIds.SellMultiplier));
        ctx.Sell(e, item, payout);
        return true;
    }
}
