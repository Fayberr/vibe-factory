using FactorySim.Behaviors;
using FactorySim.Content;

namespace FactorySim.Balance;

/// <summary>One product in a tier's factory: how much of it is sold and what it earns.</summary>
public sealed record ProductShare(string Item, double Rate, double IncomePerSecond);

/// <summary>
/// A rough estimate of one tier, from unlocking it until the next one opens. Products is what the
/// factory sells, most income first. SetupCost is what the whole factory costs to build at the
/// level it ends the tier at (Level). Seconds runs until the next tier can be unlocked (NaN for the
/// last tier). IdleRaw is the share of each raw resource's supply that nothing uses. StartLevel is
/// the level the factory is rebuilt at when the tier opens; it only differs from Level when the
/// assumptions buy upgrades (<see cref="BalanceAssumptions.UpgradePaybackSeconds"/>).
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
    double DeliverySeconds = 0,
    int Level = 1,
    int StartLevel = 1);

/// <summary>
/// How fast progression goes. For every tier it builds the best factory the tier's build limits
/// allow (every extractor it permits, every building at the assumed level), selling the mix of
/// products that earns the most (<see cref="LinearProgram"/>), and times how long that factory
/// takes to reach the next tier's earnings and price. A rough lower bound: it ignores ramp-up,
/// travel time on belts, orders and milestone rewards, and assumes the factory is complete the
/// moment its tier opens. With an upgrade payback set, it also buys whole-factory upgrades along
/// the way (see <see cref="BalanceAssumptions.UpgradePaybackSeconds"/>).
/// </summary>
public static class TierPacing
{
    /// <summary>Where the upgrade climb stops even if upgrades still pay (uncapped depots).</summary>
    public const int MaxClimbLevel = 100;

    private sealed record Factory(RecipeBook Book, List<ProductShare> Products, double Income, double Setup, Dictionary<string, double> Idle);

    /// <summary>Money so far: all ever earned, cash in hand, and what the standing factory cost (refunded on rebuild).</summary>
    private readonly record struct Wallet(double Earned, double Money, double Invested);

    private sealed record TierRun(Factory Factory, int StartLevel, int Level, double Seconds, double Delivery, Wallet Wallet);

    public static IReadOnlyList<TierEstimate> Estimate(ContentRegistry content, BalanceAssumptions? assumptions = null)
    {
        assumptions ??= BalanceAssumptions.Default;
        var result = new List<TierEstimate>();
        var wallet = new Wallet(0, content.StartingMoney.ToDouble(), 0);
        int baseLevel = Math.Max(1, assumptions.Level), level = baseLevel;
        double total = 0;

        for (int t = 0; t < content.Tiers.Count; t++)
        {
            var built = new Dictionary<int, Factory>();
            Factory At(int l)
            {
                if (built.TryGetValue(l, out var f)) return f;
                var book = RecipeBook.Create(content, assumptions with { Level = l }, t);
                var (products, setup, idle) = BestFactory(book);
                return built[l] = new Factory(book, products, products.Sum(p => p.IncomePerSecond), setup, idle);
            }

            // Upgrades carry over, but a new tier's buildings may be too dear to build at the old level:
            // the factory is rebuilt at whichever level up to the old one reaches the next tier first.
            var next = t + 1 < content.Tiers.Count ? content.Tiers[t + 1] : null;
            TierRun? best = null;
            for (int start = next == null ? level : baseLevel; start <= level; start++)
            {
                var run = Play(At, next, start, wallet, assumptions.UpgradePaybackSeconds);
                if (best == null || run.Seconds < best.Seconds * (1 - 1e-9)) best = run;
            }

            wallet = best!.Wallet;
            level = best.Level;
            if (next != null) total += best.Seconds;
            var factory = best.Factory;
            result.Add(new TierEstimate(t, content.Tiers[t].Name, factory.Products, factory.Income, factory.Setup,
                best.Seconds, total, factory.Idle, best.Delivery, best.Level, best.StartLevel));
        }
        return result;
    }

    /// <summary>
    /// One tier from the moment it opens: the old factory is refunded and the new one built at
    /// <paramref name="level"/>, then it earns until the next tier can be unlocked, buying whole-factory
    /// upgrades along the way when <paramref name="payback"/> is set. Without a next tier it only climbs.
    /// </summary>
    private static TierRun Play(Func<int, Factory> at, TierDef? next, int level, Wallet wallet, double? payback)
    {
        int startLevel = level;
        var factory = at(level);
        double earned = wallet.Earned, money = wallet.Money + wallet.Invested - factory.Setup, invested = factory.Setup;

        bool Pays(Factory from, Factory to) =>
            payback is double limit && to.Income > from.Income && to.Setup - from.Setup <= (to.Income - from.Income) * limit;

        if (next == null)
        {
            // The last tier has no end: climb for as long as upgrades pay back in time.
            while (level < MaxClimbLevel && Pays(factory, at(level + 1))) factory = at(++level);
            return new TierRun(factory, startLevel, level, double.NaN, 0, new Wallet(earned, money, factory.Setup));
        }

        var deliveryAt = new Dictionary<int, double>();
        double DeliveryAt(int l) => deliveryAt.TryGetValue(l, out var d) ? d : deliveryAt[l] = DeliverySeconds(at(l).Book, next);

        // Time to the next tier from a state: its earnings, its price, and its goods (a share of them already made).
        double Remaining(Factory f, double earnedSoFar, double cash, double madeSoFar, double deliverySeconds)
        {
            double owed = Math.Max(next.RequiredEarnings.ToDouble() - earnedSoFar, next.Cost.ToDouble() - cash);
            double earning = owed <= 0 ? 0 : f.Income > 0 ? owed / f.Income : double.PositiveInfinity;
            return Math.Max(earning, (1 - madeSoFar) * deliverySeconds);
        }

        double made = 0, seconds = 0, delivery = DeliveryAt(level);
        void Advance(double dt)
        {
            earned += factory.Income * dt;
            money += factory.Income * dt;
            double d = DeliveryAt(level);
            made = Math.Min(1, made + (d > 0 ? dt / d : 1));
            seconds += dt;
        }

        while (true)
        {
            double left = Remaining(factory, earned, money, made, DeliveryAt(level));
            if (level < MaxClimbLevel && Pays(factory, at(level + 1)))
            {
                var up = at(level + 1);
                double price = up.Setup - factory.Setup;
                double wait = price <= money ? 0 : factory.Income > 0 ? (price - money) / factory.Income : double.PositiveInfinity;
                if (wait < left)
                {
                    // Worth it only if the next tier comes sooner than without it.
                    double d = DeliveryAt(level);
                    double upgraded = wait + Remaining(up, earned + factory.Income * wait, money + factory.Income * wait - price,
                        Math.Min(1, made + (d > 0 ? wait / d : 1)), DeliveryAt(level + 1));
                    if (upgraded < left)
                    {
                        Advance(wait);
                        money -= price;
                        invested = up.Setup;
                        factory = up;
                        level++;
                        continue;
                    }
                }
            }
            if (double.IsInfinity(left)) seconds = double.PositiveInfinity;
            else Advance(left);
            break;
        }
        return new TierRun(factory, startLevel, level, seconds, delivery, new Wallet(earned, money, invested));
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
