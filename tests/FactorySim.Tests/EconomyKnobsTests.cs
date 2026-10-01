using FactorySim.Balance;
using FactorySim.Behaviors;
using FactorySim.Content;

namespace FactorySim.Tests;

/// <summary>
/// The two whole-economy knobs in base.json: "priceScale" (the game's length: every price, nothing
/// that goods are worth) and a tier's "valueBoost" (what the tier's own new goods are worth).
/// </summary>
public class EconomyKnobsTests
{
    private static readonly ContentRegistry C = TestUtil.Content;

    private static void Close(double expected, double actual) =>
        Assert.Equal(expected, actual, Math.Abs(expected) * 1e-12);

    private static ContentRegistry Priced(double scale) =>
        ContentRegistry.LoadDefault(null, ContentRegistry.ParsePack($$"""{ "priceScale": {{scale}} }"""));

    [Fact]
    public void Price_scale_multiplies_every_price_and_no_value()
    {
        var x3 = Priced(3);

        Close(C.StartingMoney.ToDouble() * 3, x3.StartingMoney.ToDouble());
        Close(C.Map.PlotPrice.ToDouble() * 3, x3.Map.PlotPrice.ToDouble());
        foreach (var b in C.BuildingList)
        {
            var scaled = x3.Buildings[b.Id];
            Close(b.Cost.ToDouble() * 3, scaled.Cost.ToDouble());
            if (b.Upgrade != null)
                Close(b.Upgrade.UpgradeCost(b, 4).ToDouble() * 3, scaled.Upgrade!.UpgradeCost(scaled, 4).ToDouble());
        }
        for (int t = 0; t < C.Tiers.Count; t++)
        {
            Close(C.Tiers[t].Cost.ToDouble() * 3, x3.Tiers[t].Cost.ToDouble());
            Close(C.Tiers[t].RequiredEarnings.ToDouble() * 3, x3.Tiers[t].RequiredEarnings.ToDouble());
            Assert.Equal(C.Tiers[t].Deliver, x3.Tiers[t].Deliver);
        }
        for (int i = 0; i < C.Milestones.Count; i++)
        {
            var (m, scaled) = (C.Milestones[i], x3.Milestones[i]);
            Close(m.Reward.ToDouble() * 3, scaled.Reward.ToDouble());
            // Money goals ask for more money; goals that count goods still count the same goods.
            Close(m.Kind == "earned" ? m.Target * 3 : m.Target, scaled.Target);
        }

        foreach (var (item, info) in C.ItemValue) Assert.Equal(info, x3.ItemValue[item]);
        Assert.Equal(C.ContractBundles.Count, x3.ContractBundles.Count);
    }

    [Fact]
    public void Price_scale_stretches_the_whole_game_by_the_same_factor()
    {
        // Income is the same and every price is k times, so a player who upgrades on the same
        // terms (k times the payback time) reaches every tier after k times as long.
        var normal = TierPacing.Estimate(C, new BalanceAssumptions { UpgradePaybackSeconds = 1800 });
        var x4 = TierPacing.Estimate(Priced(4), new BalanceAssumptions { UpgradePaybackSeconds = 4 * 1800 });

        for (int t = 0; t < normal.Count; t++)
        {
            Assert.Equal(normal[t].Level, x4[t].Level);
            Close(normal[t].IncomePerSecond, x4[t].IncomePerSecond);
            Close(normal[t].SetupCost * 4, x4[t].SetupCost);
        }
        double ratio = x4[^1].CumulativeSeconds / normal[^1].CumulativeSeconds;
        Assert.InRange(ratio, 3.95, 4.0001);
    }

    [Fact]
    public void The_base_game_is_at_price_scale_one()
    {
        var pack = ContentRegistry.ParsePack(ContentRegistry.BasePackJson());
        Assert.Equal(1, pack.PriceScale);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-2")]
    public void Price_scale_must_be_above_zero(string scale)
    {
        var ex = Assert.Throws<ContentException>(() =>
            ContentRegistry.LoadDefault(null, ContentRegistry.ParsePack($$"""{ "priceScale": {{scale}} }""")));
        Assert.Contains("priceScale", ex.Message);
    }

    /// <summary>The base content with one tier's value boost replaced.</summary>
    private static ContentRegistry WithBoost(string tier, double boost)
    {
        var pack = ContentRegistry.ParsePack(ContentRegistry.BasePackJson());
        int index = pack.Tiers.FindIndex(t => t.Name == tier);
        var old = pack.Tiers[index];
        pack.Tiers[index] = new TierDef
        {
            Name = old.Name, Description = old.Description, Cost = old.Cost, RequiredEarnings = old.RequiredEarnings,
            Deliver = old.Deliver, ValueBoost = boost,
        };
        return ContentRegistry.Build(BehaviorRegistry.CreateDefault(), new[] { pack });
    }

    [Fact]
    public void A_value_boost_multiplies_the_tiers_own_goods()
    {
        int robotics = C.Tiers.ToList().FindIndex(t => t.Name == "Robotics");
        double boost = C.Tiers[robotics].ValueBoost;
        Assert.True(boost > 1);
        var plain = WithBoost("Robotics", 1);

        // A robot is first made at Robotics: its recipe is worth the boost more, from the same parts.
        Assert.Equal(robotics, C.ItemValue["robot"].Tier);
        Assert.Equal(plain.Recipes["make_robot"].ValueMultiplier * boost, C.Recipes["make_robot"].ValueMultiplier, 9);
        Assert.Equal(plain.ItemValue["robot"].Value * boost, C.ItemValue["robot"].Value, 6);

        // Older goods keep their value, and so does everything made before the tier.
        foreach (var (item, info) in plain.ItemValue.Where(kv => kv.Value.Tier < robotics))
            Assert.Equal(info.Value, C.ItemValue[item].Value, 9);
    }

    [Fact]
    public void A_newer_machine_making_an_older_good_is_not_boosted()
    {
        // Bulk machines come at a later tier but make early goods: the good keeps its tier's value.
        var bulk = C.BuildingList.Where(b => b.Params is ProcessorParams && b.Tier >= 4)
            .SelectMany(b => ((ProcessorParams)b.Params!).Recipes.Select(r => (Building: b, Recipe: C.Recipes[r])))
            .Where(x => x.Recipe.Outputs.All(o => C.ItemValue[o.Item].Tier < x.Building.Tier))
            .ToList();
        Assert.NotEmpty(bulk);

        var pack = ContentRegistry.ParsePack(ContentRegistry.BasePackJson());
        foreach (var (_, recipe) in bulk)
            Assert.Equal(pack.Recipes.Single(r => r.Id == recipe.Id).ValueMultiplier, recipe.ValueMultiplier, 9);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_value_boost_must_be_above_zero(double boost)
    {
        var ex = Assert.Throws<ContentException>(() => WithBoost("Robotics", boost));
        Assert.Contains("valueBoost", ex.Message);
    }

    [Fact]
    public void Each_late_tier_earns_several_times_the_tier_before()
    {
        // Measured with every building as built: the boosts and extractor limits, not upgrades.
        var pacing = TierPacing.Estimate(C, new BalanceAssumptions { Level = 1 });
        for (int t = C.Tiers.ToList().FindIndex(t => t.Name == "Robotics"); t < pacing.Count; t++)
            Assert.InRange(pacing[t].IncomePerSecond / pacing[t - 1].IncomePerSecond, 3, 12);
    }
}
