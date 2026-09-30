using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using FactorySim.Content;

namespace FactorySim.Client;

/// <summary>
/// The Bottlenecks window (ideas F3 and F5): the buildings that kept waiting over the last half minute,
/// worst first, each with a Show button, and a switch that marks them all in the world. It only formats
/// <see cref="BottleneckTracker"/>; what counts as waiting lives in the core, shared with the away report.
///
/// To remove: this file, the window and its key in <c>Hud</c>, the two keybinds, and the tracker in SimHost.
/// </summary>
public sealed class BottleneckPanel
{
    public readonly Control Root;
    private readonly VBoxContainer _list = new();
    private readonly CheckButton _overlay;
    private readonly Action<int> _show;
    private readonly List<(TextureRect Rect, string Key)> _pictures = new();
    private string _shown = "";

    /// <summary>Whether the world pins are on; the HUD reads this and passes the marks to the world view.</summary>
    public bool OverlayOn
    {
        get => _overlay.ButtonPressed;
        set => _overlay.SetPressedNoSignal(value);
    }

    public event Action<bool>? OverlayToggled;

    public BottleneckPanel(Action<int> show, Func<string> overlayKey)
    {
        _show = show;
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 0);

        var intro = Ui.Label($"Buildings that spent at least half of the last {BottleneckTracker.WindowSeconds} seconds waiting, " +
                             "the most time lost first.", 12, UiTheme.Muted);
        intro.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        intro.CustomMinimumSize = new Vector2(380, 0);
        col.AddChild(Ui.Pad(intro, 16, 8));

        _overlay = new CheckButton { Text = "Mark them in the world", FocusMode = Control.FocusModeEnum.None };
        _overlay.AddThemeFontSizeOverride("font_size", 13);
        _overlay.TooltipText = $"Yellow pins wait for input, red pins cannot get rid of their output ({overlayKey()})";
        _overlay.Toggled += on => OverlayToggled?.Invoke(on);
        col.AddChild(Ui.Pad(_overlay, 12, 2));
        col.AddChild(new HSeparator());

        _list.AddThemeConstantOverride("separation", 0);
        col.AddChild(_list);
        col.AddChild(Ui.Pad(new Control(), 0, 4));
        Root = col;
    }

    public void Refresh(Simulation sim, BottleneckTracker tracker, Thumbnails thumbs)
    {
        var problems = tracker.Problems(sim.World);
        // Rebuild only when what is shown changes (shares in 5% steps), so a Show button is not replaced under the cursor.
        string key = tracker.Samples < 3 ? "warming" : string.Join("|", problems.Select(p =>
            $"{p.Building}/{p.Reason}/{p.Detail}/{p.Count}/{(int)(p.IdleShare * 20)}/{p.ExampleId}"));
        if (key != _shown)
        {
            _shown = key;
            foreach (var child in _list.GetChildren()) child.QueueFree();
            _pictures.Clear();
            if (tracker.Samples < 3)
                _list.AddChild(Ui.Pad(Ui.Label("Watching the factory...", 13, UiTheme.Muted), 16, 8));
            else if (problems.Count == 0)
                _list.AddChild(Ui.Pad(Ui.Label("Nothing is waiting for long. The factory keeps up.", 13, Palette.Ok), 16, 8));
            foreach (var problem in problems)
                _list.AddChild(AwayReportPanel.ProblemRow(problem, sim.Content, Picture, _show));
        }

        foreach (var (rect, pictureKey) in _pictures)
            if (GodotObject.IsInstanceValid(rect) && rect.Texture == null) rect.Texture = thumbs.Get(pictureKey);
    }

    private TextureRect Picture(string key, int size)
    {
        var rect = new TextureRect
        {
            CustomMinimumSize = new Vector2(size, size),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        _pictures.Add((rect, key));
        return rect;
    }
}
