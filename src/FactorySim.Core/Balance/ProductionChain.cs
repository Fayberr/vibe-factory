using FactorySim.Content;

namespace FactorySim.Balance;

/// <summary>
/// One item of a production line: how much of it flows (Rate, units per second), how many
/// buildings make it (Buildings is exact, a fraction means one sits partly idle; Built is rounded
/// up), what share of one plain belt the flow fills (BeltLoad, above 1 needs several belts), and
/// for extractors how many the tier allows (Allowed, null for machines).
/// </summary>
public sealed record ChainStep(
    ItemSource Source,
    double Rate,
    double Buildings,
    int Built,
    double BeltLoad,
    int? Allowed)
{
    public string Item => Source.Item;
    public BuildingDef Building => Source.Building;
}

/// <summary>
/// A full production line for one item at a target rate, from the ore up. Steps lists the target
/// first and every ingredient after everything that consumes it. RawPerSecond is what the line
/// takes in, Byproducts the extra outputs of multi-output recipes. MachineCost covers extractors
/// and machines at the assumed level; TotalCost adds a belt allowance per building and the depots
/// that sell the output. Depth is the longest chain of crafting steps from an ore to the target.
/// </summary>
public sealed record ChainResult(
    string Item,
    double Rate,
    IReadOnlyList<ChainStep> Steps,
    IReadOnlyDictionary<string, double> RawPerSecond,
    IReadOnlyDictionary<string, double> Byproducts,
    double SaleValue,
    double IncomePerSecond,
    double MachineCost,
    double TotalCost,
    int Depth)
{
    /// <summary>Seconds of income to earn back what the line cost to build.</summary>
    public double PaybackSeconds => IncomePerSecond > 0 ? TotalCost / IncomePerSecond : double.PositiveInfinity;
}

/// <summary>Works out what a production line needs: the balance tool's core calculation.</summary>
public static class ProductionChain
{
    public static ChainResult For(RecipeBook book, string item, double rate = 1)
    {
        if (!book.Sources.ContainsKey(item))
            throw new ArgumentException($"'{item}' cannot be made at tier {book.UnlockedTier}.", nameof(item));
        if (rate <= 0) throw new ArgumentOutOfRangeException(nameof(rate), "rate must be > 0.");

        var order = ConsumersFirst(book, item);
        var need = new Dictionary<string, double> { [item] = rate };
        var raw = new Dictionary<string, double>();
        var byproducts = new Dictionary<string, double>();
        var steps = new List<ChainStep>();
        double machineCost = 0;
        int buildings = 0;

        foreach (var id in order)
        {
            double n = need.GetValueOrDefault(id);
            if (n <= 0) continue;
            var source = book.Sources[id];

            if (source.Recipe is { } recipe)
            {
                double crafts = n / source.OutputPerCycle;
                foreach (var input in recipe.Inputs)
                    need[input.Item] = need.GetValueOrDefault(input.Item) + crafts * input.Count;
                foreach (var output in recipe.Outputs.Where(o => o.Item != id))
                    byproducts[output.Item] = byproducts.GetValueOrDefault(output.Item) + crafts * output.Count;
            }
            else
            {
                raw[id] = raw.GetValueOrDefault(id) + n;
            }

            double exact = n / book.RatePerBuilding(source);
            int built = (int)Math.Ceiling(exact - 1e-9);
            machineCost += built * book.BuildCost(source.Building);
            buildings += built;
            double beltLoad = book.BeltItemsPerSecond > 0 ? n / book.BeltItemsPerSecond : 0;
            int? allowed = source.IsExtracted ? book.AllowedCount(source.Building) : null;
            steps.Add(new ChainStep(source, n, exact, built, beltLoad, allowed));
        }

        double beltCost = book.Belt != null ? buildings * book.Assumptions.BeltTilesPerBuilding * book.BuildCost(book.Belt) : 0;
        int depots = book.BeltItemsPerSecond > 0 ? (int)Math.Ceiling(rate / book.BeltItemsPerSecond - 1e-9) : 1;
        double depotCost = book.Depot != null ? depots * book.BuildCost(book.Depot) : 0;
        double sale = book.SaleValue(item);

        return new ChainResult(item, rate, steps, raw, byproducts, sale, sale * rate,
            machineCost, machineCost + beltCost + depotCost, Depth(book, item));
    }

    /// <summary>Topological order over the item's ingredients: every item before the ones it is made from.</summary>
    private static List<string> ConsumersFirst(RecipeBook book, string item)
    {
        var postOrder = new List<string>();
        var state = new Dictionary<string, bool>(); // false = on the current path, true = done

        void Visit(string id)
        {
            if (state.TryGetValue(id, out bool done))
            {
                if (!done) throw new InvalidOperationException($"Recipe loop: '{id}' is made from itself.");
                return;
            }
            state[id] = false;
            if (book.Sources[id].Recipe is { } r)
                foreach (var input in r.Inputs) Visit(input.Item);
            state[id] = true;
            postOrder.Add(id);
        }

        Visit(item);
        postOrder.Reverse();
        return postOrder;
    }

    private static int Depth(RecipeBook book, string item) =>
        book.Sources[item].Recipe is { } r ? 1 + r.Inputs.Max(i => Depth(book, i.Item)) : 0;
}
