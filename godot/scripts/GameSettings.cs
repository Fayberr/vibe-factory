using System;
using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace FactorySim.Client;

/// <summary>
/// Player preferences, stored in user://settings.json and applied on start and whenever
/// they change: audio volumes, display (window mode, vsync, quality, UI scale) and gameplay.
/// </summary>
public sealed class GameSettings
{
    private const string Path = "user://settings.json";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    public float MasterVolume { get; set; } = 0.8f;
    public float MusicVolume { get; set; } = 0.5f;
    public float EffectsVolume { get; set; } = 0.8f;
    public float InterfaceVolume { get; set; } = 0.7f;

    public bool Fullscreen { get; set; }
    public bool VSync { get; set; } = true;

    /// <summary>"Low", "Medium" or "High".</summary>
    public string Quality { get; set; } = "High";

    public float UiScale { get; set; } = 1f;

    /// <summary>Seconds between autosaves; 0 = off.</summary>
    public int AutosaveSeconds { get; set; } = 60;

    public bool TutorialDone { get; set; }

    /// <summary>Silence everything while another window has focus.</summary>
    public bool MuteInBackground { get; set; } = true;

    /// <summary>Frame rate cap; 0 = none (vsync still applies).</summary>
    public int MaxFps { get; set; }

    public bool ShowFps { get; set; }

    /// <summary>Keyboard and edge panning speed: 0.6 slow, 1 normal, 1.6 fast.</summary>
    public float PanSpeed { get; set; } = 1f;

    public bool InvertZoom { get; set; }

    /// <summary>Pan when the mouse touches the window's edge.</summary>
    public bool EdgePan { get; set; }

    /// <summary>"+$" floating over depots as they sell.</summary>
    public bool ShowIncomePopups { get; set; } = true;

    /// <summary>The key hints above the hotbar.</summary>
    public bool ShowKeyHints { get; set; } = true;

    /// <summary>Pop-ups for new, finished and missed orders and reached goals.</summary>
    public bool ShowNotifications { get; set; } = true;

    /// <summary>Rebound keys (action → key name); actions not listed use their default.</summary>
    public Dictionary<string, string> Keys { get; set; } = new();

    public static GameSettings Load()
    {
        if (!FileAccess.FileExists(Path)) return new GameSettings();
        using var f = FileAccess.Open(Path, FileAccess.ModeFlags.Read);
        try { return JsonSerializer.Deserialize<GameSettings>(f.GetAsText(), Json) ?? new GameSettings(); }
        catch (JsonException) { return new GameSettings(); }
    }

    public void Save()
    {
        try
        {
            SafeFile.Write(ProjectSettings.GlobalizePath(Path), JsonSerializer.Serialize(this, Json), keepBackup: false);
        }
        catch (Exception ex)
        {
            GD.PushWarning($"Could not save settings: {ex.Message}");
        }
    }

    /// <summary>The HUD is laid out for at least 1280×720; the interface size shrinks to fit smaller windows.</summary>
    public const float MinWidth = 1280, MinHeight = 720;

    /// <summary>Interface size, capped so the HUD still fits the window. Call again when the window resizes.</summary>
    public void ApplyScale(Window root)
    {
        var px = root.Size;
        float fit = px.X > 0 && px.Y > 0 ? Mathf.Min(px.X / MinWidth, px.Y / MinHeight) : 1;
        float scale = Mathf.Max(0.5f, Mathf.Min(Mathf.Clamp(UiScale, 0.8f, 1.5f), fit));
        // Changing the factor resizes the viewport (and raises size_changed), so only on a real change.
        if (!Mathf.IsEqualApprox(root.ContentScaleFactor, scale)) root.ContentScaleFactor = scale;
    }

    /// <summary>Applies display and window settings (audio volumes are applied by the AudioManager).</summary>
    public void ApplyDisplay(Window root)
    {
        if (DisplayServer.GetName() == "headless") return;
        DisplayServer.WindowSetMode(Fullscreen ? DisplayServer.WindowMode.Fullscreen : DisplayServer.WindowMode.Windowed);
        DisplayServer.WindowSetVsyncMode(VSync ? DisplayServer.VSyncMode.Enabled : DisplayServer.VSyncMode.Disabled);
        ApplyScale(root);
        root.Msaa3D = Quality switch
        {
            "Low" => Viewport.Msaa.Disabled,
            "Medium" => Viewport.Msaa.Msaa2X,
            _ => Viewport.Msaa.Msaa4X,
        };
        root.Scaling3DScale = Quality == "Low" ? 0.75f : 1f;
        Engine.MaxFps = Mathf.Max(0, MaxFps);
    }
}
