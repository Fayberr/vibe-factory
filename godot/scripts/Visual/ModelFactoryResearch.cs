using Godot;
using FactorySim.Content;

namespace FactorySim.Client;

/// <summary>The research trial's lab (meta.model "lab"): a bench with a glowing flask and a turning ring.</summary>
public static partial class ModelFactory
{
    private static void Lab(ModelRig rig, BuildingDef def, Color accent)
    {
        Add(rig, rig.Root, Cached($"lab:{def.Id}", () =>
        {
            var mb = new MeshBuilder();
            Plinth(mb);
            var body = Palette.Solid(Palette.Body);
            mb.Box(body, new Vector3(0, 0.27f, 0), new Vector3(0.82f, 0.34f, 0.82f), 0.04f);
            mb.Box(Palette.Solid(Palette.BodyShade), new Vector3(0, 0.46f, 0), new Vector3(0.88f, 0.04f, 0.88f), 0.015f);
            mb.Box(Palette.Solid(accent), new Vector3(0, 0.3f, -0.415f), new Vector3(0.6f, 0.05f, 0.01f), 0f);
            // Flask: a wide glass body narrowing into a neck, on a steel stand.
            var glass = Palette.Solid(Palette.Glass, 0.1f, 0.2f);
            mb.Cylinder(Palette.Solid(Palette.Graphite), new Vector3(0, 0.48f, 0), 0.26f, 0.04f, 16);
            mb.Cylinder(glass, new Vector3(0, 0.52f, 0), 0.24f, 0.26f, 16, topRadius: 0.09f, smooth: true);
            mb.Cylinder(glass, new Vector3(0, 0.78f, 0), 0.07f, 0.2f, 12, smooth: true);
            mb.Cylinder(Palette.Solid(Palette.Graphite), new Vector3(0, 0.96f, 0), 0.085f, 0.04f, 12);
            Sockets(mb, def);
            return mb.Commit();
        }));

        // The liquid glows while the lab studies.
        var glow = Palette.GlowInstance(accent, 2.4f);
        Add(rig, rig.Root, Cached("lab:liquid", () =>
        {
            var mb = new MeshBuilder();
            mb.Cylinder(Palette.Solid(Colors.White), new Vector3(0, 0.525f, 0), 0.245f, 0.12f, 16, topRadius: 0.18f, smooth: true);
            return mb.Commit();
        })).MaterialOverride = glow;
        rig.Glows.Add((glow, 2.4f));

        // A ring turning around the neck.
        var ring = new Node3D { Position = new Vector3(0, 0.86f, 0) };
        rig.Root.AddChild(ring);
        Add(rig, ring, Cached($"lab:ring:{def.Id}", () =>
        {
            var mb = new MeshBuilder();
            var steel = Palette.Solid(Palette.Steel, 0.3f, 0.7f);
            for (int i = 0; i < 3; i++)
            {
                float a = Mathf.Tau * i / 3;
                var at = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 0.2f;
                mb.Beam(steel, at * 0.4f, at, 0.02f);
                mb.Box(Palette.Solid(accent), at, new Vector3(0.06f, 0.06f, 0.06f), 0.01f);
            }
            return mb.Commit();
        }));
        rig.Spinners.Add((ring, Vector3.Up, 1.2f));
        StatusLamp(rig, rig.Root, new Vector3(0.33f, 0.5f, 0.33f));
        rig.Height = 1.0f;
    }
}
