namespace FactorySim;

/// <summary>
/// Seeded PRNG (mulberry32). Its whole state is one uint stored in the save, so
/// anything random (anomalies, contracts, loot rolls) replays identically.
/// Never use System.Random or wall-clock time inside the simulation.
/// </summary>
public sealed class Rng
{
    public uint State { get; set; }

    public Rng(uint seed) => State = seed;

    /// <summary>Uniform double in [0, 1).</summary>
    public double NextDouble()
    {
        unchecked
        {
            State += 0x6D2B79F5u;
            uint t = State;
            t = (t ^ (t >> 15)) * (t | 1u);
            t ^= t + (t ^ (t >> 7)) * (t | 61u);
            return (t ^ (t >> 14)) / 4294967296.0;
        }
    }

    /// <summary>Uniform integer in [0, n).</summary>
    public int NextInt(int n) => (int)(NextDouble() * n);
}
