using System.Collections.Generic;
using Godot;

namespace FactorySim.Client;

/// <summary>
/// A built model plus handles to its moving parts. Animation is driven by the machine's
/// activity (eased toward 1 while working, 0 when idle/blocked), so machines spin up and
/// wind down instead of snapping.
/// </summary>
public sealed class ModelRig
{
    public required Node3D Root;
    public readonly List<GeometryInstance3D> Geometry = new();
    public readonly List<(Node3D Node, Vector3 Axis, float Speed)> Spinners = new();
    public readonly List<(Node3D Node, Vector3 Base, Vector3 Offset, float Frequency)> Bobbers = new();

    /// <summary>Parts that swing back and forth about an axis (pump beams): amplitude in radians.</summary>
    public readonly List<(Node3D Node, Vector3 Axis, float Amplitude, float Frequency)> Rockers = new();
    public readonly List<(StandardMaterial3D Material, float Energy)> Glows = new();
    public readonly List<CpuParticles3D> Emitters = new();

    /// <summary>Belt bodies (chassis, rails, animated deck): level trim tints their rails, speed drives the deck.</summary>
    public readonly List<MeshInstance3D> Belts = new();

    /// <summary>Status lamp material (per instance), or null for models without one.</summary>
    public StandardMaterial3D? StatusLamp;

    /// <summary>Approximate height above the anchor floor, for picking.</summary>
    public float Height = 0.3f;

    /// <summary>True if the model has anything to animate.</summary>
    public bool Animated => Spinners.Count + Bobbers.Count + Rockers.Count + Glows.Count + Emitters.Count > 0;

    private float _activity;
    private float _phase;
    private float _flicker;

    public void Animate(float dt, bool working)
    {
        _activity = Mathf.MoveToward(_activity, working ? 1f : 0f, dt * 1.5f);
        _phase += dt * _activity;
        _flicker += dt;

        foreach (var (node, axis, speed) in Spinners) node.RotateObjectLocal(axis, speed * dt * _activity);
        foreach (var (node, basePos, offset, freq) in Bobbers) node.Position = basePos + offset * Mathf.Sin(_phase * freq * Mathf.Tau);
        foreach (var (node, axis, amplitude, freq) in Rockers) node.Basis = new Basis(axis, amplitude * Mathf.Sin(_phase * freq * Mathf.Tau));
        float flicker = 0.9f + 0.1f * Mathf.Sin(_flicker * 13f) * Mathf.Sin(_flicker * 7.3f);
        foreach (var (mat, energy) in Glows) mat.EmissionEnergyMultiplier = energy * (0.12f + 0.88f * _activity * flicker);
        foreach (var e in Emitters) e.Emitting = _activity > 0.4f;
    }

    public void SetStatus(Color color)
    {
        if (StatusLamp == null) return;
        StatusLamp.AlbedoColor = color;
        StatusLamp.Emission = color;
    }

    /// <summary>Tints every surface (selection/hover/delete feedback); null clears.</summary>
    public void SetOverlay(Material? overlay)
    {
        foreach (var g in Geometry) g.MaterialOverlay = overlay;
    }

    /// <summary>Renders the whole model with one material (ghost previews) and silences effects.</summary>
    public void MakeGhost(Material material)
    {
        foreach (var g in Geometry)
        {
            g.MaterialOverride = material;
            g.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        }
        foreach (var e in Emitters) e.Visible = false;
    }
}
