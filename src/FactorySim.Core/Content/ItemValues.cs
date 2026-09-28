using FactorySim.Behaviors;

namespace FactorySim.Content;

/// <summary>
/// Reference economics of a content set: what each item is worth at level 1 when made
/// from raw resources along the recipes, and the earliest tier it can be obtained at.
/// Frontends show it ("worth $X each"); balance tests check the curve across tiers.
/// </summary>
public static class ItemValues
{
    public readonly record struct Info(double Value, int Tier);

    public static IReadOnlyDictionary<string, Info> Compute(ContentRegistry content)
    {
        var value = new Dictionary<string, Info>();
        foreach (var b in content.BuildingList)
            if (b.Params is MinerParams m && (!value.TryGetValue(m.Item, out var v) || b.Tier < v.Tier))
                value[m.Item] = new Info(content.Items[m.Item].BaseValue, b.Tier);

        for (bool changed = true; changed;)
        {
            changed = false;
            foreach (var b in content.BuildingList)
            {
                if (b.Params is not ProcessorParams p) continue;
                foreach (var r in p.Recipes.Select(id => content.Recipes[id]))
                {
                    if (!r.Inputs.All(i => value.ContainsKey(i.Item))) continue;
                    int tier = Math.Max(b.Tier, r.Inputs.Max(i => value[i.Item].Tier));
                    double each = r.Inputs.Sum(i => value[i.Item].Value * i.Count) * r.ValueMultiplier / r.Outputs.Sum(o => o.Count);
                    foreach (var o in r.Outputs)
                    {
                        // Earliest tier wins; within a tier, the most valuable way to make it.
                        if (value.TryGetValue(o.Item, out var old) && (tier > old.Tier || (tier == old.Tier && each <= old.Value + 1e-9))) continue;
                        value[o.Item] = new Info(each, tier);
                        changed = true;
                    }
                }
            }
        }
        return value;
    }
}
