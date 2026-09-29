namespace FactorySim;

/// <summary>
/// A bundle of identical units travelling through the factory. One ItemStack is
/// owned by exactly one container (belt, machine buffer) at a time and may be
/// mutated in place by in-line effects.
///
/// <see cref="Count"/> is the main scaling lever: stack-size upgrades let one simulated
/// object stand for many units, so throughput can grow without the simulation cost
/// growing with it.
/// </summary>
public sealed class ItemStack
{
    /// <summary>Stable id, unique per world. Lets renderers interpolate the same item across ticks.</summary>
    public long Uid { get; set; }

    public string Type { get; set; } = "";
    public long Count { get; set; } = 1;
    /// <summary>What one unit is worth as it is: the value its maker gave it.</summary>
    public BigNum UnitValue { get; set; }

    /// <summary>
    /// Extra multiplier from in-line effects (the polisher), paid when the item is sold.
    /// Crafting values its inputs by <see cref="UnitValue"/>, so a bonus never carries into
    /// the next product: polish the finished item or lose it.
    /// </summary>
    public double ValueBonus { get; set; } = 1;

    /// <summary>Status effects and one-shot markers (e.g. "polished", "heat"). Null until first used.</summary>
    public Dictionary<string, double>? Tags { get; set; }

    /// <summary>Plain value of the whole bundle, bonus excluded: what a machine consumes.</summary>
    public BigNum TotalValue => UnitValue * Count;

    /// <summary>What the bundle fetches at a depot, in-line bonus included.</summary>
    public BigNum SaleValue => UnitValue * ValueBonus * Count;

    public bool HasTag(string tag) => Tags != null && Tags.ContainsKey(tag);

    public double GetTag(string tag) => Tags != null && Tags.TryGetValue(tag, out var v) ? v : 0;

    public void SetTag(string tag, double value) => (Tags ??= new Dictionary<string, double>())[tag] = value;

    public override string ToString() => $"{Count}x {Type} @{SaleValue.Format()}";
}
