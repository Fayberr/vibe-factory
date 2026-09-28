namespace FactorySim.Tests;

public class BigNumTests
{
    [Fact]
    public void Normalizes_mantissa_into_one_to_ten()
    {
        var n = new BigNum(12345, 0);
        Assert.Equal(1.2345, n.Mantissa, 12);
        Assert.Equal(4, n.Exponent);

        var small = (BigNum)0.00042;
        Assert.Equal(4.2, small.Mantissa, 12);
        Assert.Equal(-4, small.Exponent);
        Assert.True(BigNum.Zero.IsZero);
    }

    [Fact]
    public void Arithmetic_matches_doubles_in_range()
    {
        BigNum a = 1500, b = 2.5;
        Assert.Equal(1502.5, (a + b).ToDouble(), 9);
        Assert.Equal(1497.5, (a - b).ToDouble(), 9);
        Assert.Equal(3750, (a * b).ToDouble(), 9);
        Assert.Equal(600, (a / b).ToDouble(), 9);
        Assert.True((a - a).IsZero);
    }

    [Fact]
    public void Scales_far_beyond_double_range()
    {
        var huge = BigNum.Pow(10, 1000);
        Assert.Equal(1000, huge.Exponent);
        Assert.Equal(1, huge.Mantissa, 9);
        Assert.Equal(double.PositiveInfinity, huge.ToDouble());

        var sum = huge + huge;
        Assert.Equal(2, sum.Mantissa, 9);
        Assert.Equal(huge, huge + 1); // negligible addend
        Assert.True(huge * huge > huge);
        Assert.Equal(2000, (huge * huge).Exponent);
    }

    [Fact]
    public void Comparison_handles_signs_and_exponents()
    {
        BigNum[] ordered = { -1e10, -5, 0, 0.5, 3, 1e5, BigNum.Pow(10, 400) };
        for (int i = 0; i < ordered.Length; i++)
        for (int j = 0; j < ordered.Length; j++)
            Assert.Equal(Math.Sign(i.CompareTo(j)), Math.Sign(ordered[i].CompareTo(ordered[j])));
    }

    [Theory]
    [InlineData(0, "0")]
    [InlineData(950, "950")]
    [InlineData(1234.5, "1.23K")]
    [InlineData(4.56e15, "4.56Qa")]
    public void Formats_with_suffixes(double value, string expected) => Assert.Equal(expected, ((BigNum)value).Format());

    [Fact]
    public void Formats_scientific_past_suffixes() => Assert.Equal("1.50e45", new BigNum(1.5, 45).Format());

    [Fact]
    public void String_form_round_trips_exactly()
    {
        var values = new[] { (BigNum)1.0 / 3, BigNum.Pow(1.07, 12345), -(BigNum)123.456, BigNum.Zero };
        foreach (var v in values) Assert.Equal(v, BigNum.Parse(v.ToString()));
    }

    [Fact]
    public void Pow_is_deterministic_repeated_squaring()
    {
        // Same inputs, same bits: the property replays/leaderboards rely on.
        var a = BigNum.Pow(1.6, 57);
        var b = BigNum.Pow(1.6, 57);
        Assert.Equal(a, b);
        Assert.Equal(Math.Pow(1.6, 57), a.ToDouble(), a.ToDouble() * 1e-12);
    }
}
