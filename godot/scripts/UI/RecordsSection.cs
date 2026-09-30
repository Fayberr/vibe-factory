using System.Collections.Generic;
using System.Linq;
using Godot;

namespace FactorySim.Client;

/// <summary>
/// The records part of the Statistics window (idea G6): factory time, best income, most money and
/// buildings with when they were set, and when each tier was reached. Reads
/// <see cref="PersonalRecords"/>; it writes nothing.
///
/// To remove: this file and its two lines in <c>StatsPanel</c>, and <c>PersonalRecords.cs</c> in the core
/// (see there).
/// </summary>
public sealed class RecordsSection
{
    public readonly Control Root;
    private readonly GridContainer _grid = new() { Columns = 3 };
    private readonly List<Label> _cells = new();

    public RecordsSection()
    {
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 4);
        var title = Ui.Label("Records", 14);
        title.AddThemeFontOverride("font", UiTheme.Bold);
        box.AddChild(title);
        _grid.AddThemeConstantOverride("h_separation", 12);
        _grid.AddThemeConstantOverride("v_separation", 2);
        box.AddChild(_grid);
        Root = box;
    }

    /// <summary>For scripted tests: the rows as "name value when".</summary>
    public IEnumerable<string> Lines()
    {
        for (int i = 0; i + 2 < _cells.Count; i += 3)
            if (_cells[i].Visible) yield return $"{_cells[i].Text} {_cells[i + 1].Text} {_cells[i + 2].Text}".Trim();
    }

    public void Refresh(World world)
    {
        var r = world.Stats.Records;
        var rows = new List<(string Name, string Value, string When)>
        {
            ("Factory time", PersonalRecords.Duration(world.Tick), ""),
            ("Best income", r.BestIncome.IsZero ? "measuring" : $"${r.BestIncome.Format()}/s", At(r.BestIncome.IsZero, r.BestIncomeTick)),
            ("Most money", $"${r.MostMoney.Format()}", At(r.MostMoney.IsZero, r.MostMoneyTick)),
            ("Most buildings", r.MostBuildings.ToString(), At(r.MostBuildings == 0, r.MostBuildingsTick)),
        };
        var tiers = world.Content.Tiers;
        for (int t = 1; t <= world.UnlockedTier && t < tiers.Count; t++)
            rows.Add(($"Tier {t}", tiers[t].Name, r.TierTicks.TryGetValue(t, out long tick) ? At(false, tick) : "reached"));

        while (_cells.Count < rows.Count * 3)
        {
            foreach (var (color, right) in new[] { (UiTheme.Muted, false), (UiTheme.Text, true), (UiTheme.Muted, true) })
            {
                var cell = Ui.Label("", 13, color);
                if (right) cell.HorizontalAlignment = HorizontalAlignment.Right;
                _grid.AddChild(cell);
                _cells.Add(cell);
            }
        }
        for (int i = 0; i < _cells.Count; i++)
        {
            int row = i / 3;
            _cells[i].Visible = row < rows.Count;
            if (row >= rows.Count) continue;
            _cells[i].Text = (i % 3) switch { 0 => rows[row].Name, 1 => rows[row].Value, _ => rows[row].When };
        }
    }

    private static string At(bool none, long tick) => none ? "" : $"at {PersonalRecords.Duration(tick)}";
}
