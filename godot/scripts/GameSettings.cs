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

    public static GameSettings Load()
    {
        if (!FileAccess.FileExists(Path)) return new GameSettings();
        using var f = FileAccess.Open(Path, FileAccess.ModeFlags.Read);
        try { return JsonSerializer.Deserialize<GameSettings>(f.GetAsText(), Json) ?? new GameSettings(); }
        catch (JsonException) { return new GameSettings(); }
    }

    public void Save()
    {
        using var f = FileAccess.Open(Path, FileAccess.ModeFlags.Write);
        f?.StoreString(JsonSerializer.Serialize(this, Json));
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
    }
}
