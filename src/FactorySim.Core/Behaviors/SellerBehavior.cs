using FactorySim.Content;
using FactorySim.View;

namespace FactorySim.Behaviors;

public sealed class SellerParams
{
    /// <summary>Local price multiplier (stacks with the global "sell.multiplier" stat).</summary>
    public double Multiplier { get; init; } = 1;

    /// <summary>Share of the value paid for raw materials: processing is how you make money.</summary>
    public double RawMultiplier { get; init; } = 0.25;
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
        double raw = ctx.Content.Items.TryGetValue(item.Type, out var def) && def.Raw ? p.RawMultiplier : 1;
        BigNum payout = item.TotalValue * (p.Multiplier * raw * e.ValueFactor * ctx.Stat(StatIds.SellMultiplier));
        s.Earned += payout;
        s.Units += item.Count;
        ctx.Sell(e, item, payout);
        return true;
    }

    protected override EntityStatus GetStatus(Entity e, SellerParams p, SellerState s) => new(true, 0, "selling");

    /// <summary>Uncapped: depots are the main long-term money sink.</summary>
    public override UpgradeTrack DefaultUpgrade(BuildingDef def) =>
        new() { MaxLevel = null, ValuePerLevel = 0.1, CostFactor = 2, CostGrowth = 1.9 };

    protected override void Describe(Entity e, SellerParams p, SellerState s, List<InfoLine> into)
    {
        into.Add(new InfoLine("Price bonus", $"×{p.Multiplier * e.ValueFactor:0.##}"));
        into.Add(new InfoLine("Raw materials", $"{p.RawMultiplier:0%} of value"));
        into.Add(new InfoLine("Earned", "$" + s.Earned.Format()));
        into.Add(new InfoLine("Units sold", s.Units.ToString()));
    }
}
