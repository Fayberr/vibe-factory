using System;
using Godot;
using FactorySim.Guide;

namespace FactorySim.Client;

/// <summary>
/// Presents <see cref="Tutorial.Steps"/>: a window with the current step, which moves on by
/// itself once the step is done (or with Next for tips), and a pulsing outline around the
/// control the step is about (a hotbar slot, the Upgrade button, the height ladder).
/// </summary>
public sealed class TutorialPanel
{
    public readonly HudWindow Window;

    /// <summary>Outline drawn over the control the current step points at. Add it above everything else.</summary>
    public readonly Panel Highlight;

    private readonly Label _counter = Ui.Label("", 12, UiTheme.Muted);
    private readonly ProgressBar _bar = new() { CustomMinimumSize = new Vector2(0, 4), ShowPercentage = false };
    private readonly Label _title = Ui.Label("", 19);
    private readonly Label _text = Ui.Label("", 14);
    private readonly Label _status = Ui.Label("", 13, UiTheme.Muted);
    private readonly Button _next;
    private readonly Func<string, Control?> _focus;

    private int _step = -1;
    private double _doneIn = -1;
    private double _checkIn;
    private double _pulse;

    /// <summary>Raised when the player finishes or skips the tutorial.</summary>
    public event Action? Ended;

    public TutorialPanel(Func<string, Control?> focus)
    {
        _focus = focus;
        Window = new HudWindow("Tutorial", Icon.Help, 380) { EscCloses = false };
        Window.Closed += () => End();

        var head = new HBoxContainer();
        var tag = Ui.Label("FIRST FACTORY", 11, UiTheme.Muted);
        tag.AddThemeFontOverride("font", UiTheme.Bold);
        head.AddChild(tag);
        head.AddChild(Ui.Spacer());
        head.AddChild(_counter);
        Window.Body.AddChild(Ui.Pad(head, 14, 6));
        _bar.MaxValue = Tutorial.Steps.Count;
        Window.Body.AddChild(_bar);

        var body = new VBoxContainer();
        _title.AddThemeFontOverride("font", UiTheme.Bold);
        body.AddChild(_title);
        _text.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _text.CustomMinimumSize = new Vector2(350, 0);
        body.AddChild(_text);
        body.AddChild(_status);
        Window.Body.AddChild(Ui.Pad(body, 14, 10));

        var actions = new HBoxContainer();
        actions.AddThemeConstantOverride("separation", 0);
        var skip = new Button { ThemeTypeVariation = "FlatButton", Text = "Skip tutorial", FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(150, 44) };
        skip.Pressed += () => End();
        actions.AddChild(skip);
        _next = new Button { ThemeTypeVariation = "PrimaryButton", Text = "Next", FocusMode = Control.FocusModeEnum.None, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 44) };
        _next.Pressed += Next;
        actions.AddChild(_next);
        Window.Body.AddChild(actions);

        Highlight = new Panel { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        var outline = UiTheme.Box(new Color(0, 0, 0, 0), 12, 0, 0);
        outline.BorderColor = UiTheme.Primary;
        outline.SetBorderWidthAll(3);
        outline.ShadowColor = new Color(UiTheme.Primary, 0.45f);
        outline.ShadowSize = 10;
        Highlight.AddThemeStyleboxOverride("panel", outline);
    }

    public bool Active => _step >= 0;

    public TutorialStep? Current => Active ? Tutorial.Steps[_step] : null;

    /// <summary>For scripted tests.</summary>
    public Button NextButton => _next;

    public void Start()
    {
        _step = 0;
        ShowStep();
        Window.Visible = true;
    }

    public void Next()
    {
        if (!Active) return;
        if (_step + 1 >= Tutorial.Steps.Count)
        {
            End();
            return;
        }
        _step++;
        ShowStep();
    }

    private void End()
    {
        if (!Active) return;
        _step = -1;
        Window.Visible = false;
        Highlight.Visible = false;
        Ended?.Invoke();
    }

    private void ShowStep()
    {
        var step = Tutorial.Steps[_step];
        _doneIn = -1;
        _checkIn = 0;
        _counter.Text = $"Step {_step + 1} of {Tutorial.Steps.Count}";
        _bar.Value = _step;
        _title.Text = step.Title;
        _text.Text = step.Text;
        bool last = _step + 1 == Tutorial.Steps.Count;
        _next.Visible = step.Done == null;
        _next.Text = last ? "Finish" : "Next";
        _status.Text = step.Done == null ? "" : "Your turn: do the step above.";
        _status.AddThemeColorOverride("font_color", UiTheme.Muted);
        Window.Fit();
    }

    /// <summary>Checks the current step against the world and keeps the outline on its target. Call every frame.</summary>
    public void Update(World world, double delta)
    {
        if (!Active) return;
        var step = Tutorial.Steps[_step];
        if (step.Done != null)
        {
            if (_doneIn < 0)
            {
                _checkIn -= delta;
                if (_checkIn <= 0)
                {
                    _checkIn = 0.2;
                    if (step.Done(world))
                    {
                        _doneIn = 1.0; // a moment to see it worked
                        _status.Text = "Done!";
                        _status.AddThemeColorOverride("font_color", UiTheme.Money);
                        _bar.Value = _step + 1;
                    }
                }
            }
            else if ((_doneIn -= delta) <= 0) Next();
        }

        var target = step.Focus != null && _doneIn < 0 ? _focus(step.Focus) : null;
        if (target != null && target.IsVisibleInTree())
        {
            var rect = target.GetGlobalRect().Grow(5);
            Highlight.Position = rect.Position;
            Highlight.Size = rect.Size;
            _pulse += delta;
            Highlight.Modulate = new Color(1, 1, 1, 0.55f + 0.45f * Mathf.Sin((float)_pulse * 5f));
            Highlight.Visible = true;
        }
        else Highlight.Visible = false;
    }
}
