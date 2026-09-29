using FactorySim.Content;

namespace FactorySim;

/// <summary>A plot on the land grid: column (west to east) and row (north to south), both 0-based.</summary>
public readonly record struct PlotId(int Column, int Row)
{
    public override string ToString() => $"plot {Column + 1},{Row + 1}";
}

/// <summary>
/// The land: the world is a fixed grid of square plots (<see cref="MapDef"/>), the player owns some of
/// them and can only build on those. Everyone starts with one plot and buys the rest, always a plot
/// that shares an edge with land they already own (never a diagonal). A plot's price depends only on
/// how far it is from the starting plot, counted in plots along the grid, so the plots next to the
/// start all cost the same and the ones further out cost more.
///
/// The whole grid is the map, and its outer border is where depots stand (see
/// <see cref="BuildingDef.Placement"/>); <see cref="World.Bounds"/> is the map, not the owned land.
/// </summary>
public sealed class Land
{
    private readonly HashSet<PlotId> _owned = new();

    public MapDef Layout { get; private set; }

    public Land(MapDef layout)
    {
        Layout = layout;
        _owned.Add(Start);
    }

    public int PlotSize => Layout.PlotSize;
    public int Columns => Layout.Columns;
    public int Rows => Layout.Rows;

    /// <summary>Width and height of the whole map in cells.</summary>
    public int Width => Columns * PlotSize;
    public int Height => Rows * PlotSize;

    /// <summary>Plots on the map.</summary>
    public int Count => Columns * Rows;

    public PlotId Start => new(Layout.StartColumn, Layout.StartRow);
    public int OwnedCount => _owned.Count;

    /// <summary>Owned plots, in a stable order (row by row) so saves are deterministic.</summary>
    public IEnumerable<PlotId> Owned => _owned.OrderBy(p => p.Row).ThenBy(p => p.Column);

    /// <summary>Every plot on the map, row by row.</summary>
    public IEnumerable<PlotId> All()
    {
        for (int r = 0; r < Rows; r++)
            for (int c = 0; c < Columns; c++)
                yield return new PlotId(c, r);
    }

    public bool OnMap(PlotId p) => p.Column >= 0 && p.Column < Columns && p.Row >= 0 && p.Row < Rows;
    public bool Owns(PlotId p) => _owned.Contains(p);

    /// <summary>The plot a cell lies in (may be off the map for cells outside it).</summary>
    public PlotId PlotAt(int x, int y) => new(FloorDiv(x, PlotSize), FloorDiv(y, PlotSize));

    public bool Owns(int x, int y) => Owns(PlotAt(x, y));

    /// <summary>Inclusive cell rectangle of a plot.</summary>
    public (int MinX, int MinY, int MaxX, int MaxY) CellsOf(PlotId p) =>
        (p.Column * PlotSize, p.Row * PlotSize, (p.Column + 1) * PlotSize - 1, (p.Row + 1) * PlotSize - 1);

    /// <summary>Steps along the grid from the starting plot (0 for the start itself).</summary>
    public int Distance(PlotId p) => Math.Abs(p.Column - Layout.StartColumn) + Math.Abs(p.Row - Layout.StartRow);

    /// <summary>Price of a plot: the base price at distance 1, times the growth for every step beyond.</summary>
    public BigNum PriceOf(PlotId p)
    {
        int d = Distance(p);
        return d <= 0 ? BigNum.Zero : Layout.PlotPrice * BigNum.Pow(Layout.PriceGrowth, d - 1);
    }

    /// <summary>True when an owned plot shares an edge with <paramref name="p"/>.</summary>
    public bool Touches(PlotId p) =>
        _owned.Contains(p with { Column = p.Column - 1 }) || _owned.Contains(p with { Column = p.Column + 1 }) ||
        _owned.Contains(p with { Row = p.Row - 1 }) || _owned.Contains(p with { Row = p.Row + 1 });

    /// <summary>Why a plot cannot be bought (whatever it costs), or null when it can.</summary>
    public string? WhyNot(PlotId p)
    {
        if (!OnMap(p)) return "That is off the map";
        if (Owns(p)) return "You already own this plot";
        if (!Touches(p)) return "Buy the plots in between first: a plot has to share an edge with your land";
        return null;
    }

    /// <summary>Plots that can be bought right now (next to owned land), cheapest first.</summary>
    public IEnumerable<PlotId> Buyable() =>
        All().Where(p => WhyNot(p) == null).OrderBy(PriceOf).ThenBy(p => p.Row).ThenBy(p => p.Column);

    /// <summary>The map as cell bounds, with the given build heights.</summary>
    public GridBounds Bounds(int minZ = 0, int maxZ = 4) =>
        new(new GridPos(0, 0, minZ), new GridPos(Width - 1, Height - 1, maxZ));

    internal bool Add(PlotId p) => OnMap(p) && _owned.Add(p);

    /// <summary>
    /// Makes the grid at least this big and owns every plot in <paramref name="plots"/>. A factory
    /// saved before the land existed can reach past the grid of today, and loading never drops
    /// a building, so the map grows to hold it. The starting plot stays where it is.
    /// </summary>
    internal void Fit(int columns, int rows, IEnumerable<PlotId> plots)
    {
        if (columns > Columns || rows > Rows)
            Layout = new MapDef
            {
                PlotSize = Layout.PlotSize,
                Columns = Math.Max(columns, Columns),
                Rows = Math.Max(rows, Rows),
                StartColumn = Layout.StartColumn,
                StartRow = Layout.StartRow,
                PlotPrice = Layout.PlotPrice,
                PriceGrowth = Layout.PriceGrowth,
            };
        foreach (var p in plots) _owned.Add(p);
    }

    private static int FloorDiv(int a, int b) => a >= 0 ? a / b : -((-a + b - 1) / b);
}
