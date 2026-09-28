using System.Globalization;

namespace FactorySim;

/// <summary>
/// Arbitrary-magnitude number for money and item values: <c>Mantissa × 10^Exponent</c>
/// with <c>1 ≤ |Mantissa| &lt; 10</c> (or zero). Values scale far past double's 1e308,
/// which idle/tycoon progression reaches quickly.
///
/// Determinism: only IEEE-754 basic arithmetic (correctly rounded on every .NET
/// platform) and a precomputed power-of-ten table are used (never Math.Log10/Pow),
/// so results are bit-identical across machines. That keeps replays and server-side
/// verification of leaderboard runs possible.
/// </summary>
public readonly struct BigNum : IEquatable<BigNum>, IComparable<BigNum>
{
    public readonly double Mantissa;
    public readonly long Exponent;

    public static readonly BigNum Zero = default;
    public static readonly BigNum One = new(1, 0, normalized: true);

    // Pow10[k] = 10^k built by repeated multiplication (deterministic).
    private static readonly double[] Pow10 = BuildPow10();
    private const int MaxDigits = 17; // beyond this gap the smaller addend can't affect the sum

    private BigNum(double mantissa, long exponent, bool normalized)
    {
        Mantissa = mantissa;
        Exponent = exponent;
    }

    public BigNum(double mantissa, long exponent)
    {
        this = Normalize(mantissa, exponent);
    }

    public static implicit operator BigNum(double value) => Normalize(value, 0);
    public static implicit operator BigNum(long value) => Normalize(value, 0);

    public bool IsZero => Mantissa == 0;
    public int Sign => Math.Sign(Mantissa);

    private static double[] BuildPow10()
    {
        var t = new double[309];
        t[0] = 1;
        for (int k = 1; k < t.Length; k++) t[k] = t[k - 1] * 10;
        return t;
    }

    private static BigNum Normalize(double m, long e)
    {
        if (m == 0) return Zero;
        if (!double.IsFinite(m)) throw new ArgumentOutOfRangeException(nameof(m), $"BigNum mantissa must be finite, got {m}.");
        double a = Math.Abs(m);
        if (a >= 10)
        {
            // Binary search the table for the shift instead of calling Log10.
            int lo = 1, hi = 308;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) >> 1;
                if (a >= Pow10[mid]) lo = mid; else hi = mid - 1;
            }
            m /= Pow10[lo];
            e += lo;
            if (Math.Abs(m) >= 10) { m /= 10; e++; } // rounding can land exactly on 10
        }
        else if (a < 1)
        {
            int shift = 0;
            while (Math.Abs(m) < 1 && shift < 400) { m *= 10; shift++; }
            e -= shift;
            if (Math.Abs(m) >= 10) { m /= 10; e++; }
        }
        return new BigNum(m, e, normalized: true);
    }

    public static BigNum operator +(BigNum a, BigNum b)
    {
        if (a.IsZero) return b;
        if (b.IsZero) return a;
        long d = a.Exponent - b.Exponent;
        if (d > MaxDigits) return a;
        if (d < -MaxDigits) return b;
        return d >= 0
            ? Normalize(a.Mantissa + b.Mantissa / Pow10[d], a.Exponent)
            : Normalize(a.Mantissa / Pow10[-d] + b.Mantissa, b.Exponent);
    }

    public static BigNum operator -(BigNum a) => a.IsZero ? Zero : new BigNum(-a.Mantissa, a.Exponent, normalized: true);
    public static BigNum operator -(BigNum a, BigNum b) => a + (-b);

    public static BigNum operator *(BigNum a, BigNum b) =>
        a.IsZero || b.IsZero ? Zero : Normalize(a.Mantissa * b.Mantissa, a.Exponent + b.Exponent);

    public static BigNum operator /(BigNum a, BigNum b)
    {
        if (b.IsZero) throw new DivideByZeroException("BigNum division by zero.");
        return a.IsZero ? Zero : Normalize(a.Mantissa / b.Mantissa, a.Exponent - b.Exponent);
    }

    /// <summary>base^n for integer n ≥ 0 via repeated squaring (deterministic).</summary>
    public static BigNum Pow(BigNum @base, long n)
    {
        if (n < 0) throw new ArgumentOutOfRangeException(nameof(n), "Exponent must be ≥ 0.");
        BigNum result = One, b = @base;
        while (n > 0)
        {
            if ((n & 1) != 0) result *= b;
            b *= b;
            n >>= 1;
        }
        return result;
    }

    public int CompareTo(BigNum other)
    {
        if (Mantissa == other.Mantissa && Exponent == other.Exponent) return 0;
        int sa = Math.Sign(Mantissa), sb = Math.Sign(other.Mantissa);
        if (sa != sb) return sa < sb ? -1 : 1;
        if (Exponent != other.Exponent) return (Exponent < other.Exponent) == (sa > 0) ? -1 : 1;
        return Mantissa < other.Mantissa ? -1 : 1;
    }

    public static bool operator <(BigNum a, BigNum b) => a.CompareTo(b) < 0;
    public static bool operator >(BigNum a, BigNum b) => a.CompareTo(b) > 0;
    public static bool operator <=(BigNum a, BigNum b) => a.CompareTo(b) <= 0;
    public static bool operator >=(BigNum a, BigNum b) => a.CompareTo(b) >= 0;
    public static bool operator ==(BigNum a, BigNum b) => a.Equals(b);
    public static bool operator !=(BigNum a, BigNum b) => !a.Equals(b);

    public static BigNum Max(BigNum a, BigNum b) => a >= b ? a : b;
    public static BigNum Min(BigNum a, BigNum b) => a <= b ? a : b;

    public bool Equals(BigNum other) => Mantissa == other.Mantissa && Exponent == other.Exponent;
    public override bool Equals(object? obj) => obj is BigNum b && Equals(b);
    public override int GetHashCode() => HashCode.Combine(Mantissa, Exponent);

    /// <summary>Lossy conversion; ±Infinity beyond double range.</summary>
    public double ToDouble()
    {
        if (IsZero) return 0;
        if (Exponent > 308) return Mantissa > 0 ? double.PositiveInfinity : double.NegativeInfinity;
        if (Exponent < -308) return 0;
        return Exponent >= 0 ? Mantissa * Pow10[Exponent] : Mantissa / Pow10[-Exponent];
    }

    private static readonly string[] Suffixes = { "", "K", "M", "B", "T", "Qa", "Qi", "Sx", "Sp", "Oc", "No", "Dc" };

    /// <summary>Human-readable: 950, 1.23K, 4.56Qa, then scientific (7.89e45).</summary>
    public string Format(int digits = 2)
    {
        if (IsZero) return "0";
        var inv = CultureInfo.InvariantCulture;
        string sign = Mantissa < 0 ? "-" : "";
        double m = Math.Abs(Mantissa);
        if (Exponent < 0) return sign + new BigNum(m, Exponent).ToDouble().ToString("G" + (digits + 1), inv);
        long group = Exponent / 3;
        if (group < Suffixes.Length)
        {
            double scaled = m * Pow10[Exponent - group * 3];
            // Plain amounts drop trailing zeros (12, 12.5); suffixed ones keep fixed digits (1.20K).
            string text = group == 0
                ? scaled.ToString("0." + new string('#', digits), inv)
                : scaled.ToString("F" + digits, inv);
            return sign + text + Suffixes[group];
        }
        return sign + m.ToString("F" + digits, inv) + "e" + Exponent.ToString(inv);
    }

    /// <summary>Exact round-trippable text form, e.g. "1.5e3". Used by saves.</summary>
    public override string ToString() =>
        Mantissa.ToString("R", CultureInfo.InvariantCulture) + "e" + Exponent.ToString(CultureInfo.InvariantCulture);

    public static BigNum Parse(string text)
    {
        int i = text.LastIndexOfAny(new[] { 'e', 'E' });
        if (i < 0) return double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
        double m = double.Parse(text.AsSpan(0, i), NumberStyles.Float, CultureInfo.InvariantCulture);
        long e = long.Parse(text.AsSpan(i + 1), NumberStyles.Integer, CultureInfo.InvariantCulture);
        return new BigNum(m, e);
    }
}
