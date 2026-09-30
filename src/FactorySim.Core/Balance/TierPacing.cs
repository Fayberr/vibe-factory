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
    IReadOnlyDictionary<string, double> IdleRaw,
    double DeliverySeconds = 0);

/// <summary>
/// How fast progression goes. For every tier it builds the best factory the tier's build limits
/// allow (every extractor it permits, every building at the assumed level), selling the mix of
/// products that earns the most (<see cref="LinearProgram"/>), and times how long that factory
/// takes to reach the next tier's earnings and price. A rough lower bound: it ignores ramp-up,
/// travel time on belts, orders and milestone rewards, and assumes the factory is complete the
/// moment its tier opens.
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

            double seconds = double.NaN, delivery = 0;
            if (t + 1 < content.Tiers.Count)
            {
                var next = content.Tiers[t + 1];
                double spend = setup - previousSetup + next.Cost.ToDouble(); // rebuilding is refunded in full
                double needed = Math.Max(next.RequiredEarnings.ToDouble() - earned, spend - money);
                seconds = needed <= 0 ? 0 : income > 0 ? needed / income : double.PositiveInfinity;
                delivery = DeliverySeconds(book, next);
                seconds = Math.Max(seconds, delivery);
                earned += income * seconds;
                money += income * seconds - spend;
                total += seconds;
            }
            previousSetup = setup;
            result.Add(new TierEstimate(t, content.Tiers[t].Name, products, income, setup, seconds, total, idle, delivery));
        }
        return result;
    }

    /// <summary>Raw supply per second: every extractor the tier allows, running flat out.</summary>
    private static Dictionary<string, double> RawSupply(RecipeBook book)
    {
        var supply = new Dictionary<string, double>();
        foreach (var b in book.Content.BuildingList)
            if (book.Available(b) && b.Params is MinerParams m && book.Sources.TryGetValue(m.Item, out var src) && src.Building == b)
                supply[m.Item] = supply.GetValueOrDefault(m.Item) + book.AllowedCount(b) * book.RatePerBuilding(src);
        return supply;
    }

    /// <summary>
    /// The shortest time in which the factory of this tier can make and sell everything <paramref name="tier"/>
    /// asks to have delivered, all of it at once: bound by each raw resource (the ore all the goods together
    /// need, over its supply) and by how many units the depots take. Infinite when a good cannot be made.
    /// </summary>
    private static double DeliverySeconds(RecipeBook book, TierDef tier)
    {
        if (tier.Deliver.Length == 0) return 0;
        var supply = RawSupply(book);
        var rawNeeded = new Dictionary<string, double>();
        double units = 0;
        foreach (var need in tier.Deliver)
        {
            if (!book.Sources.ContainsKey(need.Item)) return double.PositiveInfinity;
            foreach (var (raw, perOne) in ProductionChain.For(book, need.Item).RawPerSecond)
                rawNeeded[raw] = rawNeeded.GetValueOrDefault(raw) + need.Count * perOne;
            units += need.Count;
        }
        double seconds = 0;
        foreach (var (raw, amount) in rawNeeded)
            seconds = Math.Max(seconds, supply.GetValueOrDefault(raw) > 0 ? amount / supply[raw] : double.PositiveInfinity);
        double depots = book.Content.BuildingList.Where(b => book.Available(b) && b.Params is SellerParams)
            .Sum(b => book.AllowedCount(b) * book.BeltItemsPerSecond);
        return Math.Max(seconds, depots > 0 ? units / depots : double.PositiveInfinity);
    }

    /// <summary>
    /// The mix of products that earns the most from the tier's raw supply and depots: a linear program with
    /// one variable per product and depot, limited by every raw resource and every depot's belt. Exact where a
    /// greedy pick is not: a side product capped by one resource (chairs by logs) still gets the share of a
    /// shared one (iron) that makes the whole factory earn more.
    /// </summary>
    private static (List<ProductShare> Products, double Setup, Dictionary<string, double> Idle) BestFactory(RecipeBook book)
    {
        var supply = RawSupply(book);
        var raws = supply.Where(kv => kv.Value > 0).Select(kv => kv.Key).OrderBy(r => r, StringComparer.Ordinal).ToList();

        // Selling: each depot takes one belt, so depots cap how many units the factory can sell.
        var depots = book.Content.BuildingList.Where(b => book.Available(b) && b.Params is SellerParams)
            .Select(b => (Def: b, Capacity: book.AllowedCount(b) * book.BeltItemsPerSecond))
            .Where(d => d.Capacity > 0)
            .ToList();

        // Everything the tier can make from the raws it has, and sell.
        var candidates = book.Sources.Keys.OrderBy(id => id, StringComparer.Ordinal)
            .Select(id => (Id: id, Raw: ProductionChain.For(book, id).RawPerSecond))
            .Where(p => book.SaleValue(p.Id) > 0 && p.Raw.All(kv => supply.GetValueOrDefault(kv.Key) > 0))
            .ToList();

        int d = depots.Count, n = candidates.Count * d;
        var value = new double[n];
        var limits = new List<(double[] Row, double Limit)>();
        foreach (var r in raws)
        {
            var row = new double[n];
            for (int p = 0; p < candidates.Count; p++)
                for (int k = 0; k < d; k++) row[p * d + k] = candidates[p].Raw.GetValueOrDefault(r);
            limits.Add((row, supply[r]));
        }
        for (int k = 0; k < d; k++)
        {
            var row = new double[n];
            for (int p = 0; p < candidates.Count; p++) row[p * d + k] = 1;
            limits.Add((row, depots[k].Capacity));
        }
        for (int p = 0; p < candidates.Count; p++)
            for (int k = 0; k < d; k++) value[p * d + k] = book.SaleValue(candidates[p].Id, depots[k].Def);

        var x = LinearProgram.Maximise(value, limits.Select(l => l.Row).ToArray(), limits.Select(l => l.Limit).ToArray());

        var products = new List<ProductShare>();
        var left = new Dictionary<string, double>(supply);
        double sold = x.Sum();
        for (int p = 0; p < candidates.Count; p++)
        {
            double rate = 0, income = 0;
            for (int k = 0; k < d; k++) (rate, income) = (rate + x[p * d + k], income + x[p * d + k] * value[p * d + k]);
            if (rate <= sold * 1e-9) continue; // rounding, not a product
            products.Add(new ProductShare(candidates[p].Id, rate, income));
            foreach (var (r, perOne) in candidates[p].Raw) left[r] -= rate * perOne;
        }
        products.Sort((a, b) => b.IncomePerSecond != a.IncomePerSecond
            ? b.IncomePerSecond.CompareTo(a.IncomePerSecond)
            : string.CompareOrdinal(a.Item, b.Item));

        double setup = products.Sum(p => ProductionChain.For(book, p.Item, p.Rate).TotalCost);
        var idle = supply.Where(kv => kv.Value > 0).ToDictionary(kv => kv.Key, kv => Math.Clamp(left[kv.Key] / kv.Value, 0, 1));
        return (products, setup, idle);
    }
}
