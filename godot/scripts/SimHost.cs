using System;
using System.Collections.Generic;
using Godot;
using FactorySim.Content;
using FactorySim.Editing;
using FactorySim.Persistence;
using FactorySim.Samples;

namespace FactorySim.Client;

/// <summary>
/// Owns the <see cref="Simulation"/> and drives it from Godot's frame loop. Also handles
/// saving, loading and offline catch-up. Other nodes read the world through
/// <see cref="Sim"/> and change it only through <see cref="Execute"/>.
/// </summary>
public partial class SimHost : Node
{
    private const string SavePath = "user://factory_save.json";
    private const double AutosaveSeconds = 30;
    /// <summary>Enough for a first drill → smelter → depot line with a little to spare.</summary>
    private static readonly BigNum StartingMoney = 150;

    private readonly List<SimEvent> _events = new();
    private double _sinceSave;

    public ContentRegistry Content { get; private set; } = null!;
    public Simulation Sim { get; private set; } = null!;

    /// <summary>Undo/redo for building edits in the current world.</summary>
    public EditHistory History { get; private set; } = null!;

    /// <summary>Periodic and on-quit saving; scripted tests turn it off so they never touch player saves.</summary>
    public bool Autosave { get; set; } = true;

    /// <summary>Simulation speed multiplier (1 = real time).</summary>
    public int TimeScale { get; set; } = 1;

    /// <summary>Raised after a new/loaded world replaces the current one.</summary>
    public event Action? WorldReplaced;

    /// <summary>Raised for every simulation event, after the frame's ticks ran.</summary>
    public event Action<SimEvent>? EventRaised;

    /// <summary>Raised when at least one tick ran this frame (argument: tick count).</summary>
    public event Action<int>? TicksAdvanced;

    /// <summary>Human-readable notices (command errors, offline reports).</summary>
    public event Action<string>? Notice;

    /// <summary>Loads content and the last save (or starts fresh). Call after listeners are wired.</summary>
    public void Start(bool loadSave = true)
    {
        Content = ContentRegistry.LoadDefault();
        if (!loadSave || !TryLoad()) NewGame(withDemo: false);
    }

    public override void _Process(double delta)
    {
        if (Sim == null) return;
        int ticks = Sim.Advance(delta * TimeScale, maxTicks: 20 * TimeScale);

        Sim.Events.Drain(_events);
        foreach (var ev in _events) EventRaised?.Invoke(ev);
        if (ticks > 0) TicksAdvanced?.Invoke(ticks);

        _sinceSave += delta;
        if (Autosave && _sinceSave >= AutosaveSeconds) Save(quiet: true);
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest && Sim != null && Autosave) Save(quiet: true);
    }

    /// <summary>Runs a non-undoable command (tier unlocks) and reports failures.</summary>
    public CommandResult Execute(Command command)
    {
        var result = Sim.Execute(command);
        if (!result.Ok && result.Error != null) Notice?.Invoke(result.Error);
        return result;
    }

    public void Notify(string text) => Notice?.Invoke(text);

    private void Replace(Simulation sim)
    {
        Sim = sim;
        History = new EditHistory(sim);
        Sim.Events.Clear();
        WorldReplaced?.Invoke();
    }

    public void NewGame(bool withDemo)
    {
        var sim = Simulation.CreateNew(Content, StartingMoney, seed: (uint)Random.Shared.Next());
        if (withDemo) DemoLayout.Build(sim, new GridPos(8, 8, 0));
        Replace(sim);
    }

    public void Save(bool quiet = false)
    {
        _sinceSave = 0;
        using var file = FileAccess.Open(SavePath, FileAccess.ModeFlags.Write);
        if (file == null)
        {
            Notice?.Invoke($"Save failed: {FileAccess.GetOpenError()}");
            return;
        }
        file.StoreString(SaveSystem.Serialize(Sim, DateTimeOffset.UtcNow));
        if (!quiet) Notice?.Invoke("Saved.");
    }

    public bool TryLoad()
    {
        if (!FileAccess.FileExists(SavePath)) return false;
        try
        {
            using var file = FileAccess.Open(SavePath, FileAccess.ModeFlags.Read);
            var result = SaveSystem.Deserialize(file.GetAsText(), Content);
            var sim = result.Simulation;
            foreach (var w in result.Warnings) GD.PushWarning(w);

            string? welcome = null;
            if (result.SavedAtUtc is DateTimeOffset savedAt)
            {
                double away = (DateTimeOffset.UtcNow - savedAt).TotalSeconds;
                if (away > 5)
                {
                    var report = sim.CatchUp(away);
                    if (!report.Earned.IsZero)
                    welcome = $"Welcome back! {FormatDuration(away)} offline: earned ${report.Earned.Format()} " +
                              $"({report.IncomePerSecond.Format()}/s)";
                }
            }
            Replace(sim);
            if (welcome != null) Notice?.Invoke(welcome);
            return true;
        }
        catch (Exception ex)
        {
            GD.PushError($"Could not load save: {ex.Message}");
            Notice?.Invoke("Save could not be loaded; starting fresh.");
            return false;
        }
    }

    /// <summary>Debug helper: pretend the game was closed for <paramref name="seconds"/>.</summary>
    public void SimulateOffline(double seconds)
    {
        var report = Sim.CatchUp(seconds);
        Notice?.Invoke($"Simulated {FormatDuration(seconds)} offline: +{report.Earned.Format()} " +
                       $"({report.SimulatedTicks} ticks run, {report.ExtrapolatedTicks} extrapolated).");
        TicksAdvanced?.Invoke(1);
    }

    private static string FormatDuration(double seconds) =>
        seconds >= 3600 ? $"{seconds / 3600:F1} h" : seconds >= 60 ? $"{seconds / 60:F0} min" : $"{seconds:F0} s";
}
