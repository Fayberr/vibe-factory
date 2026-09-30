using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace FactorySim.Client;

/// <summary>A small line graph: values against seconds before now, with the range and time marked.</summary>
public sealed partial class LineGraph : Control
{
    private List<(double Seconds, double Value)> _points = new();
    private double _span = 3600;
    private Func<double, string> _format = v => v.ToString("0.##");
    private Color _color = UiTheme.Accent;

    public void Show(List<(double Seconds, double Value)> points, double spanSeconds, Func<double, string> format, Color color)
    {
        _points = points;
        _span = Math.Max(1, spanSeconds);
        _format = format;
        _color = color;
        QueueRedraw();
    }

    public override void _Draw()
    {
        var font = ThemeDB.FallbackFont;
        const int fontSize = 11;
        const float left = 64, bottom = 18, top = 6, right = 6;
        var plot = new Rect2(left, top, Size.X - left - right, Size.Y - top - bottom);
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(0, 0, 0, 0.22f));

        double max = _points.Count > 0 ? _points.Max(p => p.Value) : 1;
        double min = Math.Min(0, _points.Count > 0 ? _points.Min(p => p.Value) : 0);
        if (max <= min) max = min + 1;
        max *= 1.08;

        for (int i = 0; i <= 3; i++)
        {
            float y = plot.Position.Y + plot.Size.Y * i / 3f;
            DrawLine(new Vector2(plot.Position.X, y), new Vector2(plot.End.X, y), UiTheme.Line, 1);
            double value = max - (max - min) * i / 3;
            DrawString(font, new Vector2(4, y + 4), _format(value), HorizontalAlignment.Left, left - 8, fontSize, UiTheme.Muted);
        }

        string spanText = SimHost.FormatDuration(_span) + " ago";
        DrawString(font, new Vector2(plot.Position.X, Size.Y - 4), spanText, HorizontalAlignment.Left, -1, fontSize, UiTheme.Muted);
        DrawString(font, new Vector2(plot.End.X - 40, Size.Y - 4), "now", HorizontalAlignment.Right, 40, fontSize, UiTheme.Muted);

        if (_points.Count == 0) return;
        Vector2 At((double Seconds, double Value) p) => new(
            plot.Position.X + plot.Size.X * (float)Math.Clamp(1 + p.Seconds / _span, 0, 1),
            plot.End.Y - plot.Size.Y * (float)((p.Value - min) / (max - min)));

        var line = _points.Where(p => p.Seconds >= -_span).Select(At).ToArray();
        if (line.Length >= 2)
        {
            // The area under the line, a quad per segment: drawn directly, so a flat or zero stretch
            // cannot trip polygon triangulation.
            var shade = new Color(_color, 0.14f);
            var colors = new[] { shade, shade, shade, shade };
            for (int i = 1; i < line.Length; i++)
                DrawPrimitive(new[] { line[i - 1], line[i], new Vector2(line[i].X, plot.End.Y), new Vector2(line[i - 1].X, plot.End.Y) }, colors, null);
            DrawPolyline(line, _color, 2, true);
        }
        foreach (var p in line.TakeLast(1)) DrawCircle(p, 3, _color);
    }
}
