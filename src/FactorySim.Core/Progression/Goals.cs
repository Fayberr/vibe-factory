using FactorySim.Content;

namespace FactorySim;

/// <summary>Saved progress for one sustained production-rate milestone.</summary>
public sealed class RateMilestoneState
{
    public long LastProduced { get; set; }
    public double Credit { get; set; }
    public int HeldSeconds { get; set; }
}

/// <summary>
/// Contracts and milestones: the goals layered over free building. Checked once per
/// simulated second, deterministic (contracts draw from the world's seeded RNG), saved.
/// </summary>
public sealed partial class Simulation
{
    private static readonly long[] NiceSteps = { 5, 10, 15, 20, 25, 30, 40, 50, 60, 75, 100, 125, 150, 200, 250, 300, 400, 500, 750, 1000 };

    private void UpdateGoals()
    {
        var board = World.Contracts;
        for (int i = board.Open.Count - 1; i >= 0; i--)
        {
            var c = board.Open[i];
            if (c.ExpiresAtTick > World.Tick) continue;
            board.Open.RemoveAt(i);
            board.Expired++;
            if (Events.Enabled) Events.Add(new ContractExpired(World.Tick, c));
        }
        // Orders start once the factory sells something, then one arrives every half minute.
        if (board.Open.Count < ContractBoard.Slots && World.Tick >= board.NextOfferTick && !World.Stats.TotalEarned.IsZero)
        {
            if (NewContract() is { } offer)
            {
                board.Open.Add(offer);
                if (Events.Enabled) Events.Add(new ContractOffered(World.Tick, offer));
            }
            board.NextOfferTick = World.Tick + 30 * TicksPerSecond;
        }

        foreach (var m in Content.Milestones)
        {
            if (m.Kind == "produced_rate" && !World.Milestones.Contains(m.Id)) UpdateRateMilestone(m);
            if (World.Milestones.Contains(m.Id) || MilestoneProgress(m) < m.Target) continue;
            World.Milestones.Add(m.Id);
            Reward(m.Reward);
            if (Events.Enabled) Events.Add(new MilestoneReached(World.Tick, m.Id, m.Name, m.Reward));
        }
    }

    /// <summary>How far along a milestone is, in its own unit (compare with <see cref="MilestoneDef.Target"/>).</summary>
    public double MilestoneProgress(MilestoneDef m)
    {
        var s = World.Stats;
        return m.Kind switch
        {
            "earned" => s.TotalEarned.ToDouble(),
            "sold" => m.Item == null ? s.Sold.Values.Sum() : s.Sold.GetValueOrDefault(m.Item),
            "produced" => m.Item == null ? s.Produced.Values.Sum() : s.Produced.GetValueOrDefault(m.Item),
            "produced_rate" => World.RateMilestones.GetValueOrDefault(m.Id)?.HeldSeconds ?? 0,
            "built" => m.Building == null ? World.EntityCount : World.CountOf(m.Building),
            "level" => World.EntityCount == 0 ? 0 : World.Entities.Max(e => e.Level),
            "contracts" => World.Contracts.Completed,
            "tier" => World.UnlockedTier,
            _ => 0,
        };
    }

    /// <summary>
    /// Advances a sustained output goal. Up to one second of required output can carry forward,
    /// enough for recipes whose crafts straddle second boundaries but not enough for a banked burst.
    /// </summary>
    private void UpdateRateMilestone(MilestoneDef m)
    {
        var state = World.RateMilestones.GetValueOrDefault(m.Id);
        if (state == null)
            World.RateMilestones[m.Id] = state = new RateMilestoneState();
        long produced = World.Stats.Produced.GetValueOrDefault(m.Item!);
        long made = Math.Max(0, produced - state.LastProduced);
        state.LastProduced = produced;
        state.Credit = Math.Min(m.Rate * 2, state.Credit + made) - m.Rate;
        if (state.Credit < -1e-9)
        {
            state.Credit = 0;
            state.HeldSeconds = 0;
        }
        else
        {
            state.Credit = Math.Max(0, state.Credit);
            state.HeldSeconds++;
        }
    }

    /// <summary>Sold items count toward the open order for that item.</summary>
    internal void Deliver(string item, long count)
    {
        var board = World.Contracts;
        var c = board.Open.Find(x => x.Item == item);
        if (c == null) return;
        c.Delivered = Math.Min(c.Quantity, c.Delivered + count);
        if (!c.IsComplete) return;
        board.Open.Remove(c);
        board.Completed++;
        board.EarnedFromContracts += c.Reward;
        Reward(c.Reward);
        board.NextOfferTick = Math.Min(board.NextOfferTick, World.Tick + 5 * TicksPerSecond);
        if (Events.Enabled) Events.Add(new ContractCompleted(World.Tick, c));
    }

    private void Reward(BigNum amount)
    {
        World.AddMoney(amount);
        World.Stats.TotalEarned += amount; // counts toward tiers, not toward the income rate
    }

    /// <summary>
    /// A fresh order: a processed item the player can make at their tier (recent tiers are
    /// likelier), sized to roughly a minute of current income, due in 6 to 12 minutes, paying
    /// about twice its market value on top of the sales. Small parts (screws, rods) are worth so
    /// little that a minute of income would be thousands of them, so an item is only offered while
    /// an order for it stays a sane size; the parts come back as orders while income is small.
    /// </summary>
    private Contract? NewContract()
    {
        var board = World.Contracts;
        double income = Math.Max(0.5, World.Stats.IncomePerSecond().ToDouble());
        var candidates = Content.ItemValue
            .Where(kv => kv.Value.Tier <= World.UnlockedTier && Content.Items[kv.Key] is { Raw: false, Byproduct: false, Science: false } && kv.Value.Value > 0)
            .Where(kv => board.Open.TrueForAll(c => c.Item != kv.Key))
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .ToList();
        if (candidates.Count == 0) return null;

        // A minute and a half of income is the biggest an order gets; keep the items it would not overshoot.
        var sane = candidates.Where(c => income * 90 / c.Value.Value <= NiceSteps[^1]).ToList();
        candidates = sane.Count > 0 ? sane : new() { candidates.OrderByDescending(c => c.Value.Value).First() };

        double Weight(int tier) => tier == World.UnlockedTier ? 3 : tier == World.UnlockedTier - 1 ? 2 : 1;
        double pick = World.Rng.NextDouble() * candidates.Sum(c => Weight(c.Value.Tier));
        var (item, info) = candidates[^1];
        foreach (var c in candidates)
        {
            pick -= Weight(c.Value.Tier);
            if (pick < 0)
            {
                (item, info) = c;
                break;
            }
        }

        double wanted = income * (45 + 45 * World.Rng.NextDouble()) / info.Value;
        long quantity = NiceSteps.LastOrDefault(n => n <= wanted, 5);
        int minutes = 6 + World.Rng.NextInt(7);
        double bonus = 2 + 0.5 * World.Rng.NextDouble();
        return new Contract
        {
            Id = board.NextId++,
            Item = item,
            Quantity = quantity,
            OfferedAtTick = World.Tick,
            ExpiresAtTick = World.Tick + minutes * 60L * TicksPerSecond,
            Reward = (BigNum)(quantity * info.Value * bonus),
        };
    }

    private CommandResult Reroll(RerollContract c)
    {
        var board = World.Contracts;
        var old = board.Open.Find(x => x.Id == c.ContractId);
        if (old == null) return CommandResult.Fail("That order is gone");
        var fee = old.Reward * 0.1;
        if (!World.Sandbox)
        {
            if (World.Money < fee) return CommandResult.Fail($"Need {fee.Format()} for a new order");
            World.Money -= fee;
        }
        board.Open.Remove(old);
        if (NewContract() is { } offer)
        {
            board.Open.Add(offer);
            if (Events.Enabled) Events.Add(new ContractOffered(World.Tick, offer));
        }
        return CommandResult.Success();
    }
}
