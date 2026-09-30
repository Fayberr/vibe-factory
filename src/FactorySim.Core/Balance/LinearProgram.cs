namespace FactorySim.Balance;

/// <summary>
/// A small linear program: maximise c·x subject to A·x ≤ b and x ≥ 0, with every b ≥ 0 (so doing nothing
/// is always allowed). Solved by the textbook tableau simplex with Bland's rule, which never cycles and is
/// deterministic: the same numbers always give the same answer. Meant for the balance tool's few dozen
/// variables and handful of limits, not for anything large.
/// </summary>
public static class LinearProgram
{
    /// <summary>Pivot entries smaller than this count as zero (rows are scaled to a largest entry of 1).</summary>
    private const double Eps = 1e-9;

    /// <summary>An improvement smaller than this share of the best value is noise: a quarter next to a
    /// billion still counts, rounding error does not.</summary>
    private const double Gain = 1e-13;

    /// <summary>The best x. <paramref name="a"/> has one row per limit, one column per variable.</summary>
    public static double[] Maximise(double[] c, double[][] a, double[] b)
    {
        int n = c.Length, m = b.Length;
        if (a.Length != m || a.Any(row => row.Length != n)) throw new ArgumentException("A must be b.Length rows of c.Length columns");
        if (b.Any(v => v < 0 || double.IsNaN(v))) throw new ArgumentException("every limit must be zero or more");
        if (n == 0) return Array.Empty<double>();

        // Scale so every row's largest coefficient and the objective's largest are 1: money runs from cents to
        // billions, and the tolerances below should mean the same thing everywhere.
        double cScale = c.Select(Math.Abs).DefaultIfEmpty(0).Max();
        if (cScale <= 0) return new double[n];

        // Tableau: m limit rows over n variables and m slacks, then the right-hand side; the last row is -c.
        int width = n + m + 1;
        var t = new double[m + 1][];
        for (int i = 0; i < m; i++)
        {
            double rowScale = a[i].Select(Math.Abs).DefaultIfEmpty(0).Max();
            if (rowScale <= 0) rowScale = 1;
            t[i] = new double[width];
            for (int j = 0; j < n; j++) t[i][j] = a[i][j] / rowScale;
            t[i][n + i] = 1;
            t[i][^1] = b[i] / rowScale;
        }
        t[m] = new double[width];
        for (int j = 0; j < n; j++) t[m][j] = -c[j] / cScale;

        var basis = Enumerable.Range(n, m).ToArray();
        for (int step = 0; step < 50_000; step++)
        {
            // Bland: the first column that still improves the objective enters...
            int enter = -1;
            for (int j = 0; j < n + m; j++)
                if (t[m][j] < -Gain) { enter = j; break; }
            if (enter < 0) break;

            // ...and the tightest limit leaves, ties to the lowest variable.
            int leave = -1;
            double best = double.PositiveInfinity;
            for (int i = 0; i < m; i++)
            {
                if (t[i][enter] <= Eps) continue;
                double ratio = t[i][^1] / t[i][enter];
                if (ratio < best - Eps || (ratio <= best + Eps && leave >= 0 && basis[i] < basis[leave]))
                    (best, leave) = (ratio, i);
            }
            if (leave < 0) throw new InvalidOperationException("unbounded: some variable has no limit");

            Pivot(t, leave, enter);
            basis[leave] = enter;
        }

        var x = new double[n];
        for (int i = 0; i < m; i++)
            if (basis[i] < n) x[basis[i]] = Math.Max(0, t[i][^1]);
        return x;
    }

    private static void Pivot(double[][] t, int row, int col)
    {
        double p = t[row][col];
        var pr = t[row];
        for (int j = 0; j < pr.Length; j++) pr[j] /= p;
        for (int i = 0; i < t.Length; i++)
        {
            if (i == row) continue;
            double f = t[i][col];
            if (f == 0) continue;
            var r = t[i];
            for (int j = 0; j < r.Length; j++) r[j] -= f * pr[j];
        }
    }
}
