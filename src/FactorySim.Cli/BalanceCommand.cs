using System.Globalization;
using System.Text;
using FactorySim.Balance;
using FactorySim.Content;

namespace FactorySim.Cli;

/// <summary>
/// The balance tool: reports over the content file, so balance changes are checked instead of guessed.
///   balance                     tier pacing, then every item
///   balance tiers               how long each tier takes and what its best factory sells
///   balance land                what each ring of plots costs against what a factory earns
///   balance items               value, use and ore share of every item
///   balance item &lt;item&gt; [rate]  the full production line for one item (default 1/s)
/// Options: --level N (every building at level N), --polish none|products|all,
///          --tier N (unlocked tier for items/item, default the last), --pack extra.json (repeatable).
/// Polish: none, products (what the game does: one polisher before the depot) or all (a
/// hypothetical where the bonus compounds at every stage, which the game does not allow).
/// </summary>
public static class BalanceCommand
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public const string Usage =
        "usage: FactorySim.Cli balance [tiers | land | items | item <item> [rate]]\n" +
        "         [--level N] [--polish none|products|all] [--tier N] [--pack extra.json]";

    public static int Run(string[] args)
    {
        var positional = new List<string>();
        var packs = new List<ContentPack>();
        var assumptions = BalanceAssumptions.Default;
        int? tier = null;

        try
        {
            for (int i = 0; i < args.Length; i++)
            {
                string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} needs a value.");
                switch (args[i])
                {
                    case "--level":
                        assumptions = assumptions with { Level = ParseInt(Next(), "--level", min: 1) };
                        break;
                    case "--polish":
                        assumptions = assumptions with { Polish = ParsePolish(Next()) };
                        break;
                    case "--tier":
                        tier = ParseInt(Next(), "--tier", min: 0);
                        break;
                    case "--pack":
                        packs.Add(ContentRegistry.ParsePack(File.ReadAllText(Next())));
                        break;
                    case "-h" or "--help":
                        Console.WriteLine(Usage);
                        return 0;
                    default:
                        if (args[i].StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException($"Unknown option {args[i]}.");
                        positional.Add(args[i]);
                        break;
                }
            }

            var content = ContentRegistry.LoadDefault(null, packs.ToArray());
            if (tier >= content.Tiers.Count) throw new ArgumentException($"--tier must be below {content.Tiers.Count}.");
            var book = RecipeBook.Create(content, assumptions, tier);
            string mode = positional.Count > 0 ? positional[0] : "all";

            switch (mode)
            {
                case "all":
                    Console.WriteLine(Header(book, "Balance report"));
                    Console.Write(TierReport(content, assumptions));
                    Console.WriteLine();
                    Console.Write(LandReport(content, assumptions));
                    Console.WriteLine();
                    Console.Write(ItemReport(book));
                    break;
                case "tiers":
                    Console.WriteLine(Header(book, "Tier pacing"));
                    Console.Write(TierReport(content, assumptions));
                    break;
                case "land":
                    Console.WriteLine(Header(book, "Land prices"));
                    Console.Write(LandReport(content, assumptions));
                    break;
                case "items":
                    Console.WriteLine(Header(book, "Items"));
                    Console.Write(ItemReport(book));
                    break;
                case "item":
                    if (positional.Count < 2) throw new ArgumentException("balance item needs an item id or name.");
                    double rate = positional.Count > 2 ? ParseRate(positional[2]) : 1;
                    Console.Write(ChainReport(book, ResolveItem(book, positional[1]), rate));
                    break;
                default:
                    throw new ArgumentException($"Unknown report '{mode}'.");
            }
            return 0;
        }
        catch (Exception e) when (e is ArgumentException or IOException or System.Text.Json.JsonException or InvalidOperationException)
        {
            Console.Error.WriteLine(e.Message);
            Console.Error.WriteLine(Usage);
            return 1;
        }
    }

    // ---- Reports ----

    public static string Header(RecipeBook book, string title)
    {
        string polish = book.Assumptions.Polish switch
        {
            PolishMode.None => "no polishing",
            PolishMode.Products => $"products polished (x{Num(book.PolishMultiplier)})",
            _ => $"every stage polished (x{Num(book.PolishMultiplier)} each, not what the game does)",
        };
        return $"== {title}: buildings at level {book.Assumptions.Level}, {polish}, " +
               $"belts carry {Num(book.BeltItemsPerSecond)}/s ==\n";
    }

    /// <summary>How long each tier takes with the best factory its build limits allow.</summary>
    public static string TierReport(ContentRegistry content, BalanceAssumptions assumptions)
    {
        var estimates = TierPacing.Estimate(content, assumptions);
        var book = RecipeBook.Create(content, assumptions);
        var table = new Table("Tier", "Name", ">Income/s", ">Setup", ">Next in", ">Total", "Sells", "Idle raw");
        foreach (var e in estimates)
        {
            string sells = string.Join(", ", e.Products.Take(3).Select(p =>
                $"{book.NameOf(p.Item)} {Percent(e.IncomePerSecond > 0 ? p.IncomePerSecond / e.IncomePerSecond : 0)}"));
            if (e.Products.Count > 3) sells += $", +{e.Products.Count - 3}";
            string idle = string.Join(", ", e.IdleRaw.Where(kv => kv.Value > 0.005).OrderByDescending(kv => kv.Value)
                .Select(kv => $"{book.NameOf(kv.Key)} {Percent(kv.Value)}"));
            table.Add(e.Tier.ToString(Inv), e.Name, Money(e.IncomePerSecond), Money(e.SetupCost),
                double.IsNaN(e.Seconds) ? "-" : Duration(e.Seconds), Duration(e.CumulativeSeconds),
                sells.Length > 0 ? sells : "-", idle.Length > 0 ? idle : "-");
        }

        var sb = new StringBuilder(table.ToString());
        sb.AppendLine("Best factory per tier: every extractor and depot its limits allow, products picked by income.");
        sb.AppendLine("A lower bound: no ramp-up, belt travel, orders, milestone rewards or build time.");
        sb.AppendLine("Belts and mergers are not modelled either, so a flow that needs several belts counts as one.");
        return sb.ToString();
    }

    /// <summary>Every obtainable item: value, sale price, where it is made, what uses it, and which ores its value comes from.</summary>
    /// <summary>What each ring of plots costs, and how long that is at the best factory's income.</summary>
    public static string LandReport(ContentRegistry content, BalanceAssumptions assumptions)
    {
        var map = content.Map;
        var rings = LandPacing.Estimate(content, assumptions);
        var table = new Table("Ring", ">Plots", ">Each", ">Ring total", ">All so far", "Affordable at", ">Then takes", ">At last tier");
        foreach (var r in rings)
        {
            string tier = r.Tier is int t ? $"tier {t} ({content.Tiers[t].Name})" : "no tier";
            table.Add(r.Distance.ToString(Inv), r.Plots.ToString(Inv), Money(r.PriceEach), Money(r.RingTotal), Money(r.Cumulative),
                tier, double.IsNaN(r.SecondsThere) ? "-" : Duration(r.SecondsThere), Duration(r.SecondsAtLastTier));
        }

        var sb = new StringBuilder();
        sb.AppendLine($"Map: {map.Columns} x {map.Rows} plots of {map.PlotSize} x {map.PlotSize} cells, start at column {map.StartColumn + 1}, row {map.StartRow + 1}.");
        sb.AppendLine($"A plot costs {Money(map.PlotPrice.ToDouble())} next to the start and x{Num(map.PriceGrowth)} for every ring further out.");
        sb.AppendLine();
        sb.Append(table);
        sb.AppendLine($"Ring = plots away from the start, all one price. Affordable at = first tier whose best factory earns one plot in {Duration(LandPacing.TargetSeconds)}.");
        sb.AppendLine("Then takes = how long one plot is at that tier's income; At last tier = the same at the last tier's income.");
        return sb.ToString();
    }

    public static string ItemReport(RecipeBook book)
    {
        var table = new Table("Item", ">Tier", ">Value", ">Sells for", "Made in", ">Steps", "Used in", "Value from");
        var items = book.Sources.Values
            .OrderBy(s => s.Tier).ThenBy(s => s.IsExtracted ? 0 : 1).ThenBy(s => s.Value);
        foreach (var s in items)
        {
            string usedIn = book.UsedBy.TryGetValue(s.Item, out var uses)
                ? string.Join(", ", uses.SelectMany(u => u.Recipe.Outputs.Select(o => o.Item)).Distinct().Select(book.NameOf))
                : "-";
            table.Add(book.NameOf(s.Item), s.Tier.ToString(Inv), Money(s.Value), Money(book.SaleValue(s.Item)),
                s.Building.Name, ProductionChain.For(book, s.Item).Depth.ToString(Inv), usedIn, Shares(book, s.RawShare, 3));
        }

        var sb = new StringBuilder(table.ToString());
        var deadEnds = book.Sources.Values.Where(s => !s.IsExtracted && !book.UsedBy.ContainsKey(s.Item)).Select(s => book.NameOf(s.Item)).ToList();
        if (deadEnds.Count > 0) sb.AppendLine($"Only sold, used in nothing: {string.Join(", ", deadEnds)}.");
        sb.AppendLine("Value = worth as it leaves its machine; sells for = at the first depot. Steps = crafting steps from the ore.");
        return sb.ToString();
    }

    /// <summary>The whole production line for one item at a rate: machines, ores, costs.</summary>
    public static string ChainReport(RecipeBook book, string item, double rate)
    {
        var chain = ProductionChain.For(book, item, rate);
        var sb = new StringBuilder();
        sb.Append(Header(book, $"{book.NameOf(item)} at {Num(rate)}/s, tier {book.UnlockedTier} unlocked"));

        var table = new Table("Item", ">Per sec", "Made in", ">Exact", ">Built", ">Belt");
        foreach (var step in chain.Steps)
        {
            string built = step.Built.ToString(Inv);
            if (step.Allowed is int allowed) built += $" of {allowed}";
            table.Add(book.NameOf(step.Item), Num(step.Rate), step.Building.Name, Num(step.Buildings), built, Percent(step.BeltLoad));
        }
        sb.Append(table);

        var overLimit = chain.Steps.Where(s => s.Allowed is int a && s.Built > a).ToList();
        foreach (var s in overLimit)
            sb.AppendLine($"Needs {s.Built} {s.Building.Name} but the tier allows {s.Allowed}: " +
                          $"the most this line can make is {Num(rate * s.Allowed!.Value / s.Buildings)}/s.");
        var overBelt = chain.Steps.Where(s => s.BeltLoad > 1 + 1e-9).Select(s => book.NameOf(s.Item)).ToList();
        if (overBelt.Count > 0) sb.AppendLine($"More than one belt: {string.Join(", ", overBelt)}.");
        if (chain.Byproducts.Count > 0)
            sb.AppendLine("Byproducts: " + string.Join(", ", chain.Byproducts.Select(kv => $"{book.NameOf(kv.Key)} {Num(kv.Value)}/s")) + ".");

        sb.AppendLine();
        var depot = book.Depot?.Name ?? "a depot";
        sb.AppendLine($"Worth {Money(book.Sources[item].Value)} each, sells for {Money(chain.SaleValue)} at {depot}: {Money(chain.IncomePerSecond)}/s.");
        sb.AppendLine($"Build cost: machines {Money(chain.MachineCost)}, with belts and depots {Money(chain.TotalCost)}. " +
                      $"Pays back in {Duration(chain.PaybackSeconds)}.");
        sb.AppendLine($"Value from ores: {Shares(book, book.Sources[item].RawShare, 99)}.");
        sb.AppendLine($"Crafting steps from the ore: {chain.Depth}.");

        if (book.UsedBy.TryGetValue(item, out var uses))
            sb.AppendLine("Used in: " + string.Join(", ", uses.Select(u =>
                $"{string.Join(" + ", u.Recipe.Outputs.Select(o => book.NameOf(o.Item)))} ({u.Building.Name}, tier {u.Tier})")) + ".");
        else
            sb.AppendLine("Used in: nothing, only sold.");
        return sb.ToString();
    }

    // ---- Parsing ----

    /// <summary>Finds an item by id or name, ignoring case; spaces count as underscores.</summary>
    public static string ResolveItem(RecipeBook book, string query)
    {
        string key = query.Trim().Replace(' ', '_');
        var match = book.Content.Items.Values.FirstOrDefault(i =>
            string.Equals(i.Id, key, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(i.Name, query.Trim(), StringComparison.OrdinalIgnoreCase));
        if (match == null)
        {
            var close = book.Content.Items.Values
                .Where(i => i.Id.Contains(key, StringComparison.OrdinalIgnoreCase)
                         || i.Name.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase)
                         || (key.Length >= i.Id.Length && i.Id.Length >= 3 && key.Contains(i.Id, StringComparison.OrdinalIgnoreCase)))
                .Select(i => i.Id).Take(5).ToList();
            throw new ArgumentException($"No item '{query}'." + (close.Count > 0 ? $" Did you mean: {string.Join(", ", close)}?" : ""));
        }
        if (!book.Sources.ContainsKey(match.Id))
            throw new ArgumentException($"{match.Name} cannot be made with tier {book.UnlockedTier} unlocked.");
        return match.Id;
    }

    private static int ParseInt(string text, string option, int min)
    {
        if (!int.TryParse(text, NumberStyles.Integer, Inv, out int value) || value < min)
            throw new ArgumentException($"{option} needs a whole number of at least {min}.");
        return value;
    }

    private static double ParseRate(string text)
    {
        if (!double.TryParse(text, NumberStyles.Float, Inv, out double rate) || !(rate > 0) || double.IsInfinity(rate))
            throw new ArgumentException("The rate must be a positive number of items per second.");
        return rate;
    }

    private static PolishMode ParsePolish(string text) => text.ToLowerInvariant() switch
    {
        "none" or "off" => PolishMode.None,
        "products" => PolishMode.Products,
        "all" or "every" or "everystep" => PolishMode.EveryStep,
        _ => throw new ArgumentException("--polish must be none, products or all."),
    };

    // ---- Formatting ----

    public static string Num(double v)
    {
        if (double.IsNaN(v)) return "-";
        if (double.IsInfinity(v)) return "never";
        if (Math.Abs(v) >= 1000) return ((BigNum)v).Format();
        if (Math.Abs(v) >= 10) return v.ToString("0.#", Inv);
        return v.ToString("0.##", Inv);
    }

    public static string Money(double v) => double.IsFinite(v) ? "$" + Num(v) : Num(v);

    public static string Percent(double share) => (share * 100).ToString(share < 0.1 && share > 0 ? "0.#" : "0", Inv) + "%";

    public static string Duration(double seconds)
    {
        if (double.IsNaN(seconds)) return "-";
        if (double.IsInfinity(seconds)) return "never";
        long s = (long)Math.Round(seconds);
        if (s < 60) return $"{s}s";
        if (s < 3600) return $"{s / 60}m {s % 60}s";
        if (s < 86400) return $"{s / 3600}h {s % 3600 / 60}m";
        return $"{s / 86400}d {s % 86400 / 3600}h";
    }

    private static string Shares(RecipeBook book, IReadOnlyDictionary<string, double> shares, int top)
    {
        var parts = shares.Where(kv => kv.Value > 0.0005).OrderByDescending(kv => kv.Value).ToList();
        string text = string.Join(", ", parts.Take(top).Select(kv => $"{book.NameOf(kv.Key)} {Percent(kv.Value)}"));
        return parts.Count > top ? $"{text}, +{parts.Count - top}" : text;
    }

    /// <summary>Plain-text table; a header starting with '>' right-aligns its column.</summary>
    private sealed class Table
    {
        private readonly string[] _headers;
        private readonly bool[] _right;
        private readonly List<string[]> _rows = new();

        public Table(params string[] headers)
        {
            _right = headers.Select(h => h.StartsWith('>')).ToArray();
            _headers = headers.Select(h => h.TrimStart('>')).ToArray();
        }

        public void Add(params string[] cells) => _rows.Add(cells);

        public override string ToString()
        {
            var widths = _headers.Select((h, c) => Math.Max(h.Length, _rows.Select(r => r[c].Length).DefaultIfEmpty(0).Max())).ToArray();
            var sb = new StringBuilder();
            void Line(string[] cells)
            {
                var parts = cells.Select((cell, c) => c == cells.Length - 1 && !_right[c] ? cell
                    : _right[c] ? cell.PadLeft(widths[c]) : cell.PadRight(widths[c]));
                sb.AppendLine(string.Join("  ", parts).TrimEnd());
            }
            Line(_headers);
            Line(widths.Select(w => new string('-', w)).ToArray());
            foreach (var row in _rows) Line(row);
            return sb.ToString();
        }
    }
}
