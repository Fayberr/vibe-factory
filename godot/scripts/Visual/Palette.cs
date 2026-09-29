using System.Collections.Generic;
using Godot;

namespace FactorySim.Client;

/// <summary>
/// The art direction in one place: a clean, light industrial look (off-white bodies,
/// graphite bases, black belts) with one accent colour per machine and saturated items
/// so the flow reads at a glance. Materials are cached and shared.
/// </summary>
public static class Palette
{
    public static readonly Color Body = new("#eceef1");
    public static readonly Color BodyShade = new("#cfd4db");
    public static readonly Color Panel = new("#a4acb7");
    public static readonly Color Graphite = new("#3a4049");
    public static readonly Color Dark = new("#262a31");
    public static readonly Color Belt = new("#1b1d22");
    public static readonly Color Rail = new("#e3e7ec");
    public static readonly Color Steel = new("#9ba5b1");
    public static readonly Color Hazard = new("#f5c542");
    public static readonly Color Glass = new("#8fd3ff");
    public static readonly Color Grass = new("#5f9e45");
    public static readonly Color GrassLight = new("#76b456");
    public static readonly Color Concrete = new("#c9ccc4");

    public static readonly Color PortIn = new("#4fb6ff");
    public static readonly Color PortOut = new("#ffab40");
    public static readonly Color Select = new("#2fb8ff");
    public static readonly Color Danger = new("#ff5a5a");
    public static readonly Color Ok = new("#5de08a");

    /// <summary>A building waiting for something to arrive: worth a look, not an alarm.</summary>
    public static readonly Color Waiting = new("#f2c14e");
    public static readonly Color Upgrade = new("#ffd257");

    /// <summary>Trim colour showing a building's level: none, then bronze, silver, gold, cyan, violet as it climbs.</summary>
    public static Color? LevelTrim(int level) => level switch
    {
        <= 1 => null,
        <= 3 => new Color("#d08a4e"),
        <= 6 => new Color("#d9e2ec"),
        <= 10 => new Color("#ffcf40"),
        <= 20 => new Color("#46e0ff"),
        _ => new Color("#c07bff"),
    };

    private static readonly Dictionary<(Color, float, float), StandardMaterial3D> Solids = new();
    private static readonly Dictionary<(Color, float), StandardMaterial3D> Glows = new();

    /// <summary>Opaque, slightly rough material (shared).</summary>
    public static StandardMaterial3D Solid(Color color, float roughness = 0.72f, float metallic = 0f)
    {
        var key = (color, roughness, metallic);
        if (!Solids.TryGetValue(key, out var m))
        {
            Solids[key] = m = new StandardMaterial3D
            {
                AlbedoColor = color,
                Roughness = roughness,
                Metallic = metallic,
                MetallicSpecular = 0.35f,
            };
        }
        return m;
    }

    /// <summary>Self-lit material (shared). Use <see cref="GlowInstance"/> when it must be animated.</summary>
    public static StandardMaterial3D Glow(Color color, float energy = 2f)
    {
        var key = (color, energy);
        if (!Glows.TryGetValue(key, out var m)) Glows[key] = m = GlowInstance(color, energy);
        return m;
    }

    public static StandardMaterial3D GlowInstance(Color color, float energy = 2f) => new()
    {
        AlbedoColor = color,
        EmissionEnabled = true,
        Emission = color,
        EmissionEnergyMultiplier = energy,
        Roughness = 0.4f,
    };

    public static StandardMaterial3D Translucent(Color color, bool unshaded = true) => new()
    {
        AlbedoColor = color,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        ShadingMode = unshaded ? BaseMaterial3D.ShadingModeEnum.Unshaded : BaseMaterial3D.ShadingModeEnum.PerPixel,
        CullMode = BaseMaterial3D.CullModeEnum.Back,
        NoDepthTest = false,
    };

    public static Color Parse(string? hex, Color fallback) =>
        !string.IsNullOrEmpty(hex) && Color.HtmlIsValid(hex) ? Color.FromHtml(hex) : fallback;
}
