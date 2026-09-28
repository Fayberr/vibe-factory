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
    public BigNum UnitValue { get; set; }

    /// <summary>Status effects and one-shot markers (e.g. "polished", "heat"). Null until first used.</summary>
    public Dictionary<string, double>? Tags { get; set; }

    public BigNum TotalValue => UnitValue * Count;

    public bool HasTag(string tag) => Tags != null && Tags.ContainsKey(tag);

    public double GetTag(string tag) => Tags != null && Tags.TryGetValue(tag, out var v) ? v : 0;

    public void SetTag(string tag, double value) => (Tags ??= new Dictionary<string, double>())[tag] = value;

    public override string ToString() => $"{Count}x {Type} @{UnitValue.Format()}";
}
