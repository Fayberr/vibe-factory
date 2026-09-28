using FactorySim.Behaviors;
using FactorySim.Content;

namespace FactorySim.Balance;

/// <summary>One product in a tier's factory: how much of it is sold and what it earns.</summary>
public sealed record ProductShare(string Item, double Rate, double IncomePerSecond);

/// <summary>
/// A rough estimate of one tier, from unlocking it until the next one opens. Products is what the
/// factory sells, most income first. SetupCost is what the whole factory costs to build at the
/// assumed level. Seconds runs until the next tier can be unlocked (NaN for the last tier).
/// IdleRaw is the share of each raw resource's supply that nothing uses.
/// </summary>
public sealed record TierEstimate(
    int Tier,
    string Name,
    IReadOnlyList<ProductShare> Products,
    double IncomePerSecond,
    double SetupCost,
    double Seconds,
    double CumulativeSeconds,
    IReadOnlyDictionary<string, double> IdleRaw);

/// <summary>
/// How fast progression goes. For every tier it builds the best factory the tier's build limits
/// allow (every extractor it permits, every building at the assumed level), picking products
/// greedily by income, and times how long that factory takes to reach the next tier's earnings
/// and price. A rough lower bound: it ignores ramp-up, travel time on belts, orders and
/// milestone rewards, and assumes the factory is complete the moment its tier opens.
/// </summary>
public static class TierPacing
{
    public static IReadOnlyList<TierEstimate> Estimate(ContentRegistry content, BalanceAssumptions? assumptions = null)
    {
        assumptions ??= BalanceAssumptions.Default;
        var result = new List<TierEstimate>();
        double earned = 0, money = content.StartingMoney.ToDouble(), previousSetup = 0, total = 0;

        for (int t = 0; t < content.Tiers.Count; t++)
        {
            var book = RecipeBook.Create(content, assumptions, t);
            var (products, setup, idle) = BestFactory(book);
            double income = products.Sum(p => p.IncomePerSecond);

            double seconds = double.NaN;
            if (t + 1 < content.Tiers.Count)
            {
                var next = content.Tiers[t + 1];
                double spend = setup - previousSetup + next.Cost.ToDouble(); // rebuilding is refunded in full
                double needed = Math.Max(next.RequiredEarnings.ToDouble() - earned, spend - money);
                seconds = needed <= 0 ? 0 : income > 0 ? needed / income : double.PositiveInfinity;
                earned += income * seconds;
                money += income * seconds - spend;
                total += seconds;
            }
            previousSetup = setup;
            result.Add(new TierEstimate(t, content.Tiers[t].Name, products, income, setup, seconds, total, idle));
        }
        return result;
    }

    private static (List<ProductShare> Products, double Setup, Dictionary<string, double> Idle) BestFactory(RecipeBook book)
    {
        // Raw supply: every extractor the tier allows.
        var supply = new Dictionary<string, double>();
        foreach (var b in book.Content.BuildingList)
            if (book.Available(b) && b.Params is MinerParams m && book.Sources.TryGetValue(m.Item, out var src) && src.Building == b)
                supply[m.Item] = supply.GetValueOrDefault(m.Item) + book.AllowedCount(b) * book.RatePerBuilding(src);
        var left = new Dictionary<string, double>(supply);

        // Selling: each depot takes one belt, so depots cap how many units the factory can sell.
        var depots = book.Content.BuildingList.Where(b => book.Available(b) && b.Params is SellerParams)
            .Select(b => (Def: b, Capacity: book.AllowedCount(b) * book.BeltItemsPerSecond))
            .OrderByDescending(d => ((SellerParams)d.Def.Params!).Multiplier * book.ValueFactor(d.Def))
            .ToList();
        double capacity = depots.Sum(d => d.Capacity);

        var perUnit = book.Sources.Keys.ToDictionary(id => id, id => ProductionChain.For(book, id).RawPerSecond);
        var rates = new Dictionary<string, double>();

        // Greedy: repeatedly add the product that earns the most from what is left.
        for (int round = 0; round < 64; round++)
        {
            string? best = null;
            double bestRate = 0, bestIncome = 1e-12;
            foreach (var (id, raw) in perUnit)
            {
                double rate = capacity;
                foreach (var (r, perOne) in raw) rate = Math.Min(rate, left.GetValueOrDefault(r) / perOne);
                double income = rate * book.SaleValue(id);
                if (income > bestIncome) (best, bestRate, bestIncome) = (id, rate, income);
            }
            if (best == null) break;
            rates[best] = rates.GetValueOrDefault(best) + bestRate;
            capacity -= bestRate;
            foreach (var (r, perOne) in perUnit[best]) left[r] -= bestRate * perOne;
        }

        // The best goods go to the best-paying depots.
        var products = new List<ProductShare>();
        var slots = depots.Select(d => (d.Def, Left: d.Capacity)).ToList();
        foreach (var (id, rate) in rates.OrderByDescending(kv => book.SaleValue(kv.Key)))
        {
            double remaining = rate, income = 0;
            for (int i = 0; i < slots.Count && remaining > 1e-12; i++)
            {
                double units = Math.Min(remaining, slots[i].Left);
                income += units * book.SaleValue(id, slots[i].Def);
                slots[i] = (slots[i].Def, slots[i].Left - units);
                remaining -= units;
            }
            products.Add(new ProductShare(id, rate, income));
        }
        products.Sort((a, b) => b.IncomePerSecond.CompareTo(a.IncomePerSecond));

        double setup = products.Sum(p => ProductionChain.For(book, p.Item, p.Rate).TotalCost);
        var idle = supply.Where(kv => kv.Value > 0).ToDictionary(kv => kv.Key, kv => Math.Max(0, left[kv.Key]) / kv.Value);
        return (products, setup, idle);
    }
}
