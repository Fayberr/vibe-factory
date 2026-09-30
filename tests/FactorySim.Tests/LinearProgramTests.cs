using FactorySim.Balance;

namespace FactorySim.Tests;

/// <summary>The balance tool's small linear program solver (maximise c·x, A·x ≤ b, x ≥ 0).</summary>
public class LinearProgramTests
{
    [Fact]
    public void Solves_the_textbook_example()
    {
        // max 3x + 5y, x ≤ 4, 2y ≤ 12, 3x + 2y ≤ 18: the optimum is x = 2, y = 6 (36).
        var x = LinearProgram.Maximise(new[] { 3.0, 5 }, new[] { new[] { 1.0, 0 }, new[] { 0.0, 2 }, new[] { 3.0, 2 } }, new[] { 4.0, 12, 18 });
        Assert.Equal(2, x[0], 9);
        Assert.Equal(6, x[1], 9);
    }

    [Fact]
    public void Shares_a_resource_where_a_greedy_pick_would_not()
    {
        // Plates: 1 iron, $2. Chairs: 1 iron and 2 logs, $3. 10 iron, 4 logs. Greedy takes all the iron for
        // plates ($20); the best is 2 chairs from the logs and 8 plates from the rest ($22).
        var x = LinearProgram.Maximise(new[] { 2.0, 3 }, new[] { new[] { 1.0, 1 }, new[] { 0.0, 2 } }, new[] { 10.0, 4 });
        Assert.Equal(8, x[0], 9);
        Assert.Equal(2, x[1], 9);
    }

    [Fact]
    public void Money_from_cents_to_billions_and_ties_come_out_the_same_every_time()
    {
        var c = new[] { 0.25, 1e9, 1e9 };
        var a = new[] { new[] { 1.0, 1, 1 }, new[] { 0.0, 1e6, 1e6 } };
        var b = new[] { 5.0, 3e6 };
        var first = LinearProgram.Maximise(c, a, b);
        Assert.Equal(3, first[1] + first[2], 9);
        Assert.Equal(2, first[0], 9);
        Assert.Equal(first, LinearProgram.Maximise(c, a, b));
    }

    [Fact]
    public void Nothing_worth_making_makes_nothing()
    {
        Assert.Equal(new double[2], LinearProgram.Maximise(new[] { 0.0, -1 }, new[] { new[] { 1.0, 1 } }, new[] { 3.0 }));
        Assert.Equal(new double[2], LinearProgram.Maximise(new[] { 1.0, 1 }, new[] { new[] { 1.0, 1 } }, new[] { 0.0 }));
        Assert.Throws<InvalidOperationException>(() => LinearProgram.Maximise(new[] { 1.0 }, new[] { new[] { 0.0 } }, new[] { 1.0 }));
    }
}
