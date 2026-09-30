using System.Globalization;
using System.Text;
using FactorySim.Content;
using FactorySim.Persistence;

namespace FactorySim.Cli;

/// <summary>Loads a real save and runs a short, read-only inspection copy to measure its live factory.</summary>
public static class InspectCommand
{
    private const int SampleSeconds = 60;
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public const string Usage = "usage: FactorySim.Cli inspect <save.json>";

    public static int Run(string[] args)
    {
        if (args.Length != 1 || args[0] is "-h" or "--help")
        {
            Console.WriteLine(Usage);
            return args.Length == 1 ? 0 : 1;
        }

        try
        {
            var loaded = SaveSystem.Deserialize(File.ReadAllText(args[0]), ContentRegistry.LoadDefault());
            Console.Write(Report(loaded.Simulation, loaded.Warnings));
            return 0;
        }
        catch (Exception e) when (e is IOException or System.Text.Json.JsonException or NotSupportedException)
        {
            Console.Error.WriteLine(e.Message);
            Console.Error.WriteLine(Usage);
            return 1;
        }
    }

    public static string Report(Simulation sim, IReadOnlyList<string>? warnings = null, int sampleSeconds = SampleSeconds)
    {
        if (sampleSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(sampleSeconds));

        var world = sim.World;
        var content = world.Content;
        BigNum moneyBefore = world.Money;
        BigNum totalEarnedBefore = world.Stats.TotalEarned;
        long tickBefore = world.Tick;
        var soldBefore = new Dictionary<string, long>(world.Stats.Sold);
        var earnedBefore = new Dictionary<string, BigNum>(world.Stats.EarnedByItem);
        var sampler = new IdleSampler();
        long ticks = (long)sampleSeconds * Simulation.TicksPerSecond;
        for (long done = 0; done < ticks;)
        {
            int step = (int)Math.Min(IdleSampler.Stride, ticks - done);
            sim.Step(step);
            sampler.Sample(world);
            done += step;
        }

        var sb = new StringBuilder();
        var tier = content.Tiers[world.UnlockedTier];
        sb.AppendLine($"== Save inspector: {tier.Name} (tier {world.UnlockedTier}) ==");
        sb.AppendLine($"Money {Money(moneyBefore)}  Lifetime {Money(totalEarnedBefore)}  " +
                      $"Age {BalanceCommand.Duration(tickBefore / (double)Simulation.TicksPerSecond)}");
        sb.AppendLine($"Measured the loaded factory for {sampleSeconds}s. The save file was not changed.");
        if (warnings is { Count: > 0 })
            foreach (string warning in warnings) sb.AppendLine($"Load warning: {warning}");

        sb.AppendLine();
        sb.AppendLine("Buildings");
        var buildings = new BalanceCommand.Table("Building", ">Count", "Levels");
        foreach (var group in world.Entities.GroupBy(e => e.Def.Id).OrderBy(g => g.First().Def.Name, StringComparer.Ordinal))
        {
            string levels = string.Join(", ", group.GroupBy(e => e.Level).OrderBy(g => g.Key)
                .Select(g => $"L{g.Key}: {g.Count()}"));
            buildings.Add(group.First().Def.Name, group.Count().ToString(Inv), levels);
        }
        if (world.EntityCount == 0) buildings.Add("(none)", "0", "-");
        sb.Append(buildings);

        sb.AppendLine("Products sold during sample");
        var products = new BalanceCommand.Table("Product", ">Units/s", ">Income/s", ">Lifetime sold");
        var soldItems = world.Stats.Sold.Keys.Union(soldBefore.Keys).OrderBy(ItemName, StringComparer.Ordinal).ToList();
        foreach (string item in soldItems)
        {
            long sold = world.Stats.Sold.GetValueOrDefault(item) - soldBefore.GetValueOrDefault(item);
            BigNum earned = world.Stats.EarnedByItem.GetValueOrDefault(item) - earnedBefore.GetValueOrDefault(item);
            if (sold == 0 && earned.IsZero) continue;
            products.Add(ItemName(item), BalanceCommand.Num(sold / (double)sampleSeconds),
                Money(earned / sampleSeconds), world.Stats.Sold.GetValueOrDefault(item).ToString(Inv));
        }
        if (!products.HasRows) products.Add("(none)", "0", "$0", "0");
        sb.Append(products);

        sb.AppendLine("Buildings waiting during sample");
        var waiting = new BalanceCommand.Table("Building", ">Count", "Reason", ">Idle", "Example");
        foreach (var problem in sampler.Problems(world, max: 12))
        {
            string name = content.Buildings.TryGetValue(problem.Building, out var def) ? def.Name : problem.Building;
            waiting.Add(name, problem.Count.ToString(Inv), problem.Detail,
                BalanceCommand.Percent(problem.IdleShare), $"#{problem.ExampleId} {problem.ExamplePos}");
        }
        if (!waiting.HasRows) waiting.Add("(none)", "0", "-", "0%", "-");
        sb.Append(waiting);

        sb.AppendLine("Locked tiers still ask for");
        var tiers = new BalanceCommand.Table("Tier", "Name", ">Cash", ">Earnings", "Deliveries");
        for (int i = world.UnlockedTier + 1; i < content.Tiers.Count; i++)
        {
            var next = content.Tiers[i];
            BigNum cash = moneyBefore >= next.Cost ? BigNum.Zero : next.Cost - moneyBefore;
            BigNum earnings = totalEarnedBefore >= next.RequiredEarnings
                ? BigNum.Zero : next.RequiredEarnings - totalEarnedBefore;
            string deliveries = next.Deliver.Length == 0 ? "none" : string.Join(", ", next.Deliver.Select(need =>
            {
                long left = Math.Max(0, need.Count - soldBefore.GetValueOrDefault(need.Item));
                return $"{left} {ItemName(need.Item)}";
            }));
            tiers.Add(i.ToString(Inv), next.Name, Money(cash), Money(earnings), deliveries);
        }
        if (!tiers.HasRows) tiers.Add("-", "Every tier unlocked", "$0", "$0", "none");
        sb.Append(tiers);
        return sb.ToString();

        string ItemName(string id) => content.Items.TryGetValue(id, out var item) ? item.Name : id;
    }

    private static string Money(BigNum value) => "$" + value.Format();

}
