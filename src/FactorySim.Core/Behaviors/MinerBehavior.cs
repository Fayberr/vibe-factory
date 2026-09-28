using FactorySim.Content;
using FactorySim.View;

namespace FactorySim.Behaviors;

public sealed class MinerParams
{
    public string Item { get; init; } = "";

    /// <summary>Ticks of work per cycle at rate 1.</summary>
    public int Interval { get; init; } = 20;

    /// <summary>Units produced per cycle.</summary>
    public int Amount { get; init; } = 1;

    [System.Text.Json.Serialization.JsonIgnore] internal string ItemName { get; set; } = "";
}

public sealed class MinerState
{
    public double Work { get; set; }

    /// <summary>Produced bundle waiting for the output to accept it.</summary>
    public ItemStack? Output { get; set; }
}

/// <summary>
/// Source: spawns raw items. Work accumulates at the "miner.rate" stat per tick; when
/// the rate exceeds one cycle per tick, extra cycles are merged into one bundle (up to
/// the stack size), so rate upgrades stay meaningful without a hard cap.
/// </summary>
public sealed class MinerBehavior : Behavior<MinerParams, MinerState>
{
    public override string Name => "miner";

    protected override void Bind(BuildingDef def, MinerParams p, ContentRegistry content)
    {
        Require(content.Items.ContainsKey(p.Item), def, $"unknown item '{p.Item}'.");
        Require(p.Interval > 0 && p.Amount > 0, def, "interval and amount must be > 0.");
        Require(def.OutputPorts.Count >= 1, def, "needs an output port.");
        p.ItemName = content.Items[p.Item].Name;
    }

    protected override void Tick(TickContext ctx, Entity e, MinerParams p, MinerState s)
    {
        bool fresh = false;
        if (s.Output == null)
        {
            s.Work += ctx.Stat(StatIds.MinerRate) * e.SpeedFactor;
            if (s.Work >= p.Interval)
            {
                long cyclesAllowed = Math.Max(1, ctx.MaxStackSize / p.Amount);
                long cycles = Math.Min((long)(s.Work / p.Interval), cyclesAllowed);
                s.Work = Math.Min(s.Work - cycles * p.Interval, (double)p.Interval * cyclesAllowed);
                var item = ctx.CreateItem(p.Item, cycles * p.Amount, ctx.Content.Items[p.Item].BaseValue);
                ctx.RecordProduced(e, item);
                s.Output = item;
                fresh = true;
            }
        }

        // A bundle made this tick starts at the edge; one that has been waiting may catch up.
        if (s.Output != null && ctx.Push(e, e.Def.OutputPorts[0], s.Output, fresh ? 0 : TickContext.Waiting)) s.Output = null;
    }

    protected override void Describe(Entity e, MinerParams p, MinerState s, List<InfoLine> into)
    {
        into.Add(new InfoLine("Produces", p.ItemName));
        into.Add(new InfoLine("Rate", $"{p.Amount * Simulation.TicksPerSecond * e.SpeedFactor / p.Interval:0.##}/s"));
    }

    public override UpgradeTrack DefaultUpgrade(BuildingDef def) =>
        new() { MaxLevel = 25, SpeedPerLevel = 0.5, CostFactor = 1.2, CostGrowth = 1.65 };

    protected override EntityStatus GetStatus(Entity e, MinerParams p, MinerState s) =>
        s.Output == null
            ? new EntityStatus(true, (float)Math.Min(1, s.Work / p.Interval), $"mining {p.ItemName}")
            : new EntityStatus(false, 1, "output blocked");
}
