using System;
using System.Collections.Generic;
using System.Text.Json;
using Godot;
using FactorySim.Content;
using FactorySim.Editing;
using FactorySim.Persistence;
using FactorySim.Samples;

namespace FactorySim.Client;

/// <summary>What the menu shows about a save slot without loading the factory.</summary>
public sealed class SlotInfo
{
    public string Name { get; set; } = "";
    public DateTimeOffset SavedAtUtc { get; set; }
    public int Tier { get; set; }
    public string TierName { get; set; } = "";
    public double Money { get; set; }
    public double PlaySeconds { get; set; }
    public int Buildings { get; set; }
}

/// <summary>
/// Owns the <see cref="Simulation"/> and drives it from Godot's frame loop. Also handles
/// save slots, loading, pausing and offline catch-up. Other nodes read the world through
/// <see cref="Sim"/> and change it only through <see cref="Execute"/>.
/// Slot 0 means "not saved anywhere" (the menu background, scripted tests).
/// </summary>
public partial class SimHost : Node
{
    public const int SlotCount = 5;
    private const string SaveDir = "user://saves";
    private const string OldSavePath = "user://factory_save.json";

    /// <summary>Enough for a first drill → smelter → depot line with a little to spare.</summary>
    private static readonly BigNum StartingMoney = 200;

    private static readonly JsonSerializerOptions MetaJson = new() { WriteIndented = true };

    private readonly List<SimEvent> _events = new();
    private double _sinceSave;

    public ContentRegistry Content { get; private set; } = null!;
    public Simulation Sim { get; private set; } = null!;

    /// <summary>Undo/redo for building edits in the current world.</summary>
    public EditHistory History { get; private set; } = null!;

    /// <summary>Periodic and on-quit saving; scripted tests turn it off so they never touch player saves.</summary>
    public bool Autosave { get; set; } = true;

    /// <summary>Seconds between autosaves (0 = off). From the settings.</summary>
    public double AutosaveSeconds { get; set; } = 60;

    /// <summary>Slot the current factory saves to (1..SlotCount), or 0.</summary>
    public int Slot { get; private set; }

    /// <summary>Name of the current factory.</summary>
    public string FactoryName { get; set; } = "";

    /// <summary>Real time spent playing this factory.</summary>
    public double PlaySeconds { get; private set; }

    /// <summary>Simulation speed multiplier (1 = real time).</summary>
    public int TimeScale { get; set; } = 1;

    /// <summary>When true the simulation doesn't advance (building still works).</summary>
    public bool Paused { get; set; }

    /// <summary>Raised after a new/loaded world replaces the current one.</summary>
    public event Action? WorldReplaced;

    /// <summary>Raised for every simulation event, after the frame's ticks ran.</summary>
    public event Action<SimEvent>? EventRaised;

    /// <summary>Raised when at least one tick ran this frame (argument: tick count).</summary>
    public event Action<int>? TicksAdvanced;

    /// <summary>Human-readable notices (command errors, offline reports).</summary>
    public event Action<string>? Notice;

    /// <summary>Something the player tried didn't work (for the error sound).</summary>
    public event Action? Failed;

    /// <summary>Loads content. Call after listeners are wired, before any game starts.</summary>
    public void Init()
    {
        Content ??= ContentRegistry.LoadDefault();
        if (Autosave)
        {
            CarryOverRenamedUserData();
            MigrateSingleSave();
        }
    }

    /// <summary>Legacy entry point: straight into the last save (or a fresh factory) without the menu.</summary>
    public void Start(bool loadSave = true)
    {
        Init();
        int last = MostRecentSlot();
        if (!loadSave || last == 0 || !TryLoad(last)) NewGame(withDemo: false);
    }

    public override void _Process(double delta)
    {
        if (Sim == null) return;
        int ticks = Paused ? 0 : Sim.Advance(delta * TimeScale, maxTicks: 20 * TimeScale);

        Sim.Events.Drain(_events);
        foreach (var ev in _events) EventRaised?.Invoke(ev);
        if (ticks > 0) TicksAdvanced?.Invoke(ticks);

        if (Slot == 0) return;
        if (!Paused) PlaySeconds += delta;
        _sinceSave += delta;
        if (Autosave && AutosaveSeconds > 0 && _sinceSave >= AutosaveSeconds) Save(quiet: true);
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest && Sim != null && Autosave && Slot > 0) Save(quiet: true);
    }

    /// <summary>Runs a non-undoable command (tier unlocks, orders) and reports failures.</summary>
    public CommandResult Execute(Command command)
    {
        var result = Sim.Execute(command);
        if (!result.Ok && result.Error != null) Fail(result.Error);
        return result;
    }

    public void Notify(string text) => Notice?.Invoke(text);

    public void Fail(string text)
    {
        Notice?.Invoke(text);
        Failed?.Invoke();
    }

    private void Replace(Simulation sim)
    {
        Sim = sim;
        History = new EditHistory(sim);
        Sim.Events.Clear();
        Paused = false;
        _sinceSave = 0;
        WorldReplaced?.Invoke();
    }

    /// <summary>A new factory. With <paramref name="slot"/> 0 it is never saved (menu background, tests).</summary>
    public void NewGame(bool withDemo, int slot = 0, string? name = null)
    {
        Content ??= ContentRegistry.LoadDefault();
        var sim = Simulation.CreateNew(Content, StartingMoney, seed: (uint)Random.Shared.Next());
        if (withDemo) DemoLayout.Build(sim, new GridPos(8, 8, 0));
        Slot = slot;
        FactoryName = name ?? (slot > 0 ? $"Factory {slot}" : "");
        PlaySeconds = 0;
        Replace(sim);
        if (slot > 0) Save(quiet: true);
    }

    /// <summary>The menu's live backdrop: the demo factory, in sandbox, never saved.</summary>
    public void StartBackdrop()
    {
        NewGame(withDemo: true);
        Sim.World.Sandbox = true;
        Sim.World.Goals = false;
    }

    // ---- Slots ----------------------------------------------------------------

    // Operating-system paths: slot files go through SafeFile (crash-safe writes with a backup).
    private static string SlotPath(int slot) => ProjectSettings.GlobalizePath($"{SaveDir}/slot{slot}.json");
    private static string MetaPath(int slot) => ProjectSettings.GlobalizePath($"{SaveDir}/slot{slot}.meta.json");

    public void Save(bool quiet = false)
    {
        if (Slot == 0 || Sim == null) return;
        _sinceSave = 0;
        try
        {
            SafeFile.Write(SlotPath(Slot), SaveSystem.Serialize(Sim, DateTimeOffset.UtcNow));
        }
        catch (Exception ex)
        {
            GD.PushError($"Save failed: {ex}");
            Fail($"Save failed: {ex.Message}");
            return;
        }
        var w = Sim.World;
        var info = new SlotInfo
        {
            Name = FactoryName,
            SavedAtUtc = DateTimeOffset.UtcNow,
            Tier = w.UnlockedTier,
            TierName = Content.Tiers[w.UnlockedTier].Name,
            Money = w.Money.ToDouble(),
            PlaySeconds = PlaySeconds,
            Buildings = w.EntityCount,
        };
        WriteMeta(Slot, info);
        if (!quiet) Notice?.Invoke("Saved.");
    }

    /// <summary>The slot list's summary. Losing it only costs the name and play time, so no backup.</summary>
    private static void WriteMeta(int slot, SlotInfo info)
    {
        try
        {
            SafeFile.Write(MetaPath(slot), JsonSerializer.Serialize(info, MetaJson), keepBackup: false);
        }
        catch (Exception ex)
        {
            GD.PushWarning($"Could not write the slot summary: {ex.Message}");
        }
    }

    public static SlotInfo? ReadSlot(int slot)
    {
        if (!SafeFile.Exists(SlotPath(slot))) return null;
        try
        {
            return SafeFile.Read(MetaPath(slot), text => JsonSerializer.Deserialize<SlotInfo>(text))?.Value
                   ?? new SlotInfo { Name = $"Factory {slot}" };
        }
        catch (Exception)
        {
            return new SlotInfo { Name = $"Factory {slot}" };
        }
    }

    /// <summary>The slot saved most recently, or 0 when there are none.</summary>
    public static int MostRecentSlot()
    {
        int best = 0;
        DateTimeOffset when = DateTimeOffset.MinValue;
        for (int s = 1; s <= SlotCount; s++)
            if (ReadSlot(s) is { } info && info.SavedAtUtc >= when)
            {
                best = s;
                when = info.SavedAtUtc;
            }
        return best;
    }

    public static void DeleteSlot(int slot)
    {
        foreach (var path in new[] { SlotPath(slot), MetaPath(slot) })
        {
            try { SafeFile.Delete(path); }
            catch (Exception ex) { GD.PushWarning($"Could not delete {path}: {ex.Message}"); }
        }
    }

    public bool TryLoad(int slot)
    {
        Content ??= ContentRegistry.LoadDefault();
        string path = SlotPath(slot);
        if (!SafeFile.Exists(path)) return false;
        try
        {
            // A damaged save falls back to the one before it. A save from a newer version of the
            // game does not: loading the older backup would let the next save overwrite the newer one.
            var read = SafeFile.Read(path, json => SaveSystem.Deserialize(json, Content),
                                     fallBackOn: ex => ex is not NotSupportedException)!;
            var result = read.Value;
            var sim = result.Simulation;
            foreach (var w in result.Warnings) GD.PushWarning(w);

            string? restored = null;
            if (read.FromBackup)
            {
                GD.PushWarning($"Save slot {slot} was damaged ({read.PrimaryError?.Message}); loaded its backup.");
                restored = "The last save was damaged, so the one before it was loaded.";
                try { SafeFile.RestoreBackup(path); }
                catch (Exception ex) { GD.PushWarning($"Could not restore the backup over the damaged save: {ex.Message}"); }
            }

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
            var info = ReadSlot(slot);
            Slot = slot;
            FactoryName = info?.Name ?? $"Factory {slot}";
            PlaySeconds = info?.PlaySeconds ?? 0;
            Replace(sim);
            if (restored != null) Notice?.Invoke(restored);
            if (welcome != null) Notice?.Invoke(welcome);
            return true;
        }
        catch (Exception ex)
        {
            GD.PushError($"Could not load save: {ex.Message}");
            Fail("That save could not be loaded.");
            return false;
        }
    }

    /// <summary>Before slots there was a single save file: it becomes slot 1.</summary>
    private static void MigrateSingleSave()
    {
        if (!FileAccess.FileExists(OldSavePath) || SafeFile.Exists(SlotPath(1))) return;
        DirAccess.MakeDirRecursiveAbsolute(SaveDir);
        var err = DirAccess.RenameAbsolute(ProjectSettings.GlobalizePath(OldSavePath), SlotPath(1));
        if (err != Error.Ok) return;
        WriteMeta(1, new SlotInfo { Name = "My factory", SavedAtUtc = DateTimeOffset.UtcNow });
    }

    /// <summary>
    /// The game used to be called "Factory Sim", and Godot keeps user data in a folder named
    /// after the game. Copy the save and settings from the old folder once so nothing is lost.
    /// </summary>
    private static void CarryOverRenamedUserData()
    {
        try
        {
            string current = OS.GetUserDataDir();
            string? parent = System.IO.Path.GetDirectoryName(current);
            if (parent == null) return;
            string old = System.IO.Path.Combine(parent, "Factory Sim");
            if (!System.IO.Directory.Exists(old)) return;
            System.IO.Directory.CreateDirectory(current);
            foreach (string name in new[] { "factory_save.json", "hotbar.json" })
            {
                string from = System.IO.Path.Combine(old, name), to = System.IO.Path.Combine(current, name);
                if (System.IO.File.Exists(from) && !System.IO.File.Exists(to)) System.IO.File.Copy(from, to);
            }
        }
        catch (Exception ex)
        {
            GD.PushWarning($"Could not carry over data from the old game folder: {ex.Message}");
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

    public static string FormatDuration(double seconds) =>
        seconds >= 3600 ? $"{seconds / 3600:F1} h" : seconds >= 60 ? $"{seconds / 60:F0} min" : $"{seconds:F0} s";
}
