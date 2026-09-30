using System;
using Godot;

namespace FactorySim.Client;

/// <summary>
/// Game speed on the factory card (ideas H1 and H2): slower and faster in steps from ×0.5 to ×16, and
/// "Run until", which runs as fast as the machine allows until the next tier is ready, money doubles or ten
/// minutes pass, and stops early when a machine stops. The goals live in the core's <see cref="FastForward"/>;
/// <see cref="SimHost"/> runs it.
///
/// To remove: this file, its row on the card and the <c>slower</c>/<c>faster</c> keys in <c>Hud</c> and
/// <c>Keybinds</c>, the fast-forward part of <c>SimHost</c>, and <c>FastForward.cs</c> in the core (see there).
/// The Game window's speed button is older and stays.
/// </summary>
public sealed class SpeedControls
{
    /// <summary>The speed steps; the Game window's button cycles through the same list.</summary>
    public static readonly double[] Speeds = { 0.5, 1, 2, 4, 8, 16 };

    public readonly Control Root;
    private readonly Func<SimHost> _hostOf;
    private SimHost _host => _hostOf();
    private readonly Label _speed;
    private readonly Button _slower, _faster, _stop;
    private readonly MenuButton _run;

    /// <param name="host">The host, looked up when used (the HUD is built before it gets one).</param>
    public SpeedControls(Func<SimHost> host)
    {
        _hostOf = host;
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 4);

        _slower = Small("−", () => Step(-1));
        row.AddChild(_slower);
        _speed = Ui.Label("×1", 14);
        _speed.HorizontalAlignment = HorizontalAlignment.Center;
        _speed.CustomMinimumSize = new Vector2(40, 0);
        row.AddChild(_speed);
        _faster = Small("+", () => Step(1));
        row.AddChild(_faster);

        _run = new MenuButton { Text = "Run until…", FocusMode = Control.FocusModeEnum.None, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, Flat = false };
        _run.AddThemeFontSizeOverride("font_size", 13);
        _run.TooltipText = "Run as fast as possible until something happens. A machine that stops ends it early.";
        var menu = _run.GetPopup();
        menu.AddItem("The next tier is ready", (int)RunUntil.NextTier);
        menu.AddItem("Money doubles", (int)RunUntil.MoneyDoubled);
        menu.AddItem("Ten minutes pass", (int)RunUntil.TenMinutes);
        menu.IdPressed += id => _host.StartFastForward((RunUntil)(int)id);
        row.AddChild(_run);

        _stop = new Button { Text = "Stop", FocusMode = Control.FocusModeEnum.None, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, Visible = false };
        _stop.AddThemeFontSizeOverride("font_size", 13);
        _stop.Pressed += () => _host.StopFastForward("Fast forward stopped");
        row.AddChild(_stop);
        Root = row;
    }

    /// <summary>One step slower (-1) or faster (+1).</summary>
    public void Step(int direction)
    {
        int i = Array.IndexOf(Speeds, _host.TimeScale);
        if (i < 0) i = Array.IndexOf(Speeds, 1.0);
        _host.TimeScale = Speeds[Math.Clamp(i + direction, 0, Speeds.Length - 1)];
        Refresh();
    }

    public static string Format(double speed) => speed < 1 ? $"×{speed:0.#}" : $"×{speed:0}";

    public void Refresh()
    {
        var running = _host.Running;
        _speed.Text = running != null ? "max" : Format(_host.TimeScale);
        _slower.Disabled = running != null || _host.TimeScale <= Speeds[0];
        _faster.Disabled = running != null || _host.TimeScale >= Speeds[^1];
        _run.Visible = running == null;
        _stop.Visible = running != null;
        if (running != null)
        {
            _stop.Text = $"Stop ({SimHost.FormatDuration(running.Elapsed(_host.Sim.World))})";
            _stop.TooltipText = $"Running {running.Describe()}";
        }
    }

    private static Button Small(string text, Action pressed)
    {
        var b = Ui.TextButton(text, pressed);
        b.CustomMinimumSize = new Vector2(30, 0);
        return b;
    }
}
