using FactorySim.Content;

namespace FactorySim.Balance;

/// <summary>
/// One ring of plots: every plot at the same distance from the starting plot, which is also
/// the same price. Tier is the first tier whose best factory earns one plot's price within the
/// target time (null when none does); SecondsThere is how long that takes at that tier.
/// </summary>
public sealed record LandRing(
    int Distance,
    int Plots,
    double PriceEach,
    double RingTotal,
    double Cumulative,
    int? Tier,
    double SecondsThere,
    double SecondsAtLastTier);

/// <summary>
/// What the land costs against what a factory earns. Plots are priced by their distance from the
/// starting plot (see <see cref="Land.PriceOf"/>), so the map falls into rings. For each ring this
/// reports the price and how many minutes of the best factory's income a plot takes at the first
/// tier that can pay for one within the target time, using the tier estimates of
/// <see cref="TierPacing"/>. It is how the land prices in the content file are chosen and checked.
/// </summary>
public static class LandPacing
{
    /// <summary>Ten minutes of the best factory's income: what a plot should ask for.</summary>
    public const double TargetSeconds = 600;

    public static IReadOnlyList<LandRing> Estimate(ContentRegistry content, BalanceAssumptions? assumptions = null, double targetSeconds = TargetSeconds)
    {
        var tiers = TierPacing.Estimate(content, assumptions);
        var land = new Land(content.Map);
        double lastIncome = tiers.Count > 0 ? tiers[^1].IncomePerSecond : 0;

        var rings = new List<LandRing>();
        double cumulative = 0;
        foreach (var group in land.All().Where(p => p != land.Start).GroupBy(land.Distance).OrderBy(g => g.Key))
        {
            double price = land.PriceOf(group.First()).ToDouble();
            double total = price * group.Count();
            cumulative += total;

            int? tier = null;
            double seconds = double.NaN;
            foreach (var t in tiers)
                if (t.IncomePerSecond > 0 && price <= t.IncomePerSecond * targetSeconds)
                {
                    tier = t.Tier;
                    seconds = price / t.IncomePerSecond;
                    break;
                }
            rings.Add(new LandRing(group.Key, group.Count(), price, total, cumulative, tier, seconds,
                lastIncome > 0 ? price / lastIncome : double.PositiveInfinity));
        }
        return rings;
    }
}
