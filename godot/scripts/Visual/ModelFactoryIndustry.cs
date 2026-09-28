using Godot;
using FactorySim.Content;

namespace FactorySim.Client;

/// <summary>Models for the extractors and machines of the later tiers (same kit and style as the rest).</summary>
public static partial class ModelFactory
{
    private static readonly Color Bark = new("#7a5230");
    private static readonly Color Leaf = new("#3f8f45");
    private static readonly Color LeafLight = new("#58ad55");
    private static readonly Color Soil = new("#5b4632");
    private static readonly Color Sand = new("#e4cc92");

    /// <summary>Cylinder lying along X, centred on <paramref name="center"/>.</summary>
    private static void CylinderX(MeshBuilder mb, Material m, Vector3 center, float radius, float length, int sides = 10) =>
        mb.With(new Transform3D(new Basis(Vector3.Forward, Mathf.Pi / 2), center),
            b => b.Cylinder(m, new Vector3(0, -length / 2, 0), radius, length, sides, capBottom: true));

    private static void Tree(MeshBuilder mb, Vector3 foot, float scale)
    {
        mb.Cylinder(Palette.Solid(Bark), foot, 0.035f * scale, 0.2f * scale, 6);
        mb.Cylinder(Palette.Solid(Leaf), foot + new Vector3(0, 0.14f * scale, 0), 0.17f * scale, 0.3f * scale, 7, topRadius: 0.02f, capBottom: true);
        mb.Cylinder(Palette.Solid(LeafLight), foot + new Vector3(0, 0.33f * scale, 0), 0.12f * scale, 0.26f * scale, 7, topRadius: 0f, capBottom: true);
    }

    /// <summary>A small circular saw blade in the YZ plane (spins about X).</summary>
    private static Mesh BladeMesh(float radius) => Cached($"blade:{radius}", () =>
    {
        var mb = new MeshBuilder();
        var steel = Palette.Solid(Palette.Steel, 0.3f, 0.7f);
        mb.With(new Transform3D(new Basis(Vector3.Forward, Mathf.Pi / 2), Vector3.Zero), b =>
        {
            b.Cylinder(steel, new Vector3(0, -0.012f, 0), radius, 0.024f, 16, capBottom: true);
            b.Cylinder(Palette.Solid(Palette.Hazard), new Vector3(0, -0.02f, 0), radius * 0.28f, 0.04f, 8, capBottom: true);
            for (int i = 0; i < 12; i++)
            {
                float a = Mathf.Tau * i / 12;
                var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                b.Box(steel, d * radius, new Vector3(0.035f, 0.022f, 0.035f), 0f);
            }
        });
        return mb.Commit();
    });

    // ---- Extractors -----------------------------------------------------------

    private static void TreeFarm(ModelRig rig, BuildingDef def, Color accent, bool effects)
    {
        Add(rig, rig.Root, Cached($"treefarm:{def.Id}", () =>
        {
            var mb = new MeshBuilder();
            Plinth(mb);
            // Planted bed at the back, three trees growing in it.
            mb.Box(Palette.Solid(Soil, 0.95f), new Vector3(0, 0.15f, 0.17f), new Vector3(0.88f, 0.07f, 0.56f), 0.02f);
            Tree(mb, new Vector3(-0.27f, 0.18f, 0.25f), 1.15f);
            Tree(mb, new Vector3(0.04f, 0.18f, 0.32f), 1.35f);
            Tree(mb, new Vector3(0.3f, 0.18f, 0.16f), 1.0f);
            // Felling cabin at the front with a stacked log pile.
            var body = Palette.Solid(Palette.Body);
            mb.Box(body, new Vector3(-0.2f, 0.3f, -0.22f), new Vector3(0.4f, 0.34f, 0.34f), 0.035f);
            mb.Box(Palette.Solid(accent), new Vector3(-0.2f, 0.49f, -0.22f), new Vector3(0.44f, 0.05f, 0.38f), 0.015f);
            mb.Box(Palette.Solid(Palette.Glass, 0.2f), new Vector3(-0.2f, 0.33f, -0.395f), new Vector3(0.26f, 0.1f, 0.01f), 0f);
            foreach (var log in new[] { new Vector3(0.22f, 0.17f, -0.12f), new Vector3(0.22f, 0.17f, -0.2f), new Vector3(0.22f, 0.235f, -0.16f) })
                CylinderX(mb, Palette.Solid(Bark), log, 0.037f, 0.32f, 7);
            Sockets(mb, def);
            return mb.Commit();
        }));

        var saw = new Node3D { Position = new Vector3(0.22f, 0.38f, -0.33f) };
        rig.Root.AddChild(saw);
        Add(rig, saw, BladeMesh(0.1f));
        rig.Spinners.Add((saw, Vector3.Right, 11f));
        StatusLamp(rig, rig.Root, new Vector3(-0.36f, 0.55f, -0.1f));
        if (effects) Smoke(rig, rig.Root, new Vector3(0.22f, 0.4f, -0.33f), 0.35f);
        rig.Height = 1.0f;
    }

    private static void Quarry(ModelRig rig, BuildingDef def, Color accent)
    {
        Add(rig, rig.Root, Cached($"quarry:{def.Id}", () =>
        {
            var mb = new MeshBuilder();
            Plinth(mb);
            // Sand heap at the back and a hopper feeding the output at the front.
            mb.Cylinder(Palette.Solid(Sand, 0.95f), new Vector3(-0.12f, 0.13f, 0.18f), 0.36f, 0.3f, 9, topRadius: 0.06f);
            var frame = Palette.Solid(accent, 0.6f, 0.2f);
            foreach (int sx in new[] { -1, 1 })
            {
                mb.Beam(frame, new Vector3(sx * 0.3f, 0.12f, -0.3f), new Vector3(sx * 0.3f, 0.62f, -0.3f), 0.05f);
                mb.Beam(frame, new Vector3(sx * 0.3f, 0.12f, 0.1f), new Vector3(sx * 0.3f, 0.78f, 0.1f), 0.05f);
                mb.Beam(frame, new Vector3(sx * 0.3f, 0.62f, -0.3f), new Vector3(sx * 0.3f, 0.78f, 0.1f), 0.04f);
            }
            // Square hopper (frustum) under the screen.
            mb.Cylinder(Palette.Solid(Palette.BodyShade), new Vector3(0, 0.2f, -0.18f), 0.14f, 0.26f, 4, topRadius: 0.3f);
            mb.Box(Palette.Solid(Palette.Graphite), new Vector3(0, 0.2f, -0.38f), new Vector3(0.32f, 0.12f, 0.14f), 0.02f);
            Sockets(mb, def);
            return mb.Commit();
        }));

        // Tilted sieve screen that shakes while working.
        var screen = new Node3D { Position = new Vector3(0, 0.69f, -0.1f) };
        rig.Root.AddChild(screen);
        Add(rig, screen, Cached("quarry:screen", () =>
        {
            var mb = new MeshBuilder();
            mb.With(new Transform3D(new Basis(Vector3.Right, -0.38f), Vector3.Zero), b =>
            {
                b.Box(Palette.Solid(Palette.Body), Vector3.Zero, new Vector3(0.56f, 0.06f, 0.48f), 0.02f);
                b.Box(Palette.Solid(Sand, 0.95f), new Vector3(0, 0.035f, 0), new Vector3(0.48f, 0.02f, 0.4f), 0f);
            });
            return mb.Commit();
        }));
        rig.Bobbers.Add((screen, screen.Position, new Vector3(0.025f, 0.01f, 0), 3.5f));
        StatusLamp(rig, rig.Root, new Vector3(0.36f, 0.3f, -0.38f));
        rig.Height = 0.9f;
    }

    /// <summary>Pumpjack: a walking beam rocks on its samson post, driven by a spinning counterweight crank.</summary>
    private static void Pump(ModelRig rig, BuildingDef def, Color accent)
    {
        const float pivotY = 0.82f, pivotZ = 0.05f;
        Add(rig, rig.Root, Cached($"pump:{def.Id}", () =>
        {
            var mb = new MeshBuilder();
            Plinth(mb);
            var post = Palette.Solid(accent, 0.55f, 0.2f);
            foreach (int sx in new[] { -1, 1 })
            {
                mb.Beam(post, new Vector3(sx * 0.2f, 0.12f, pivotZ - 0.2f), new Vector3(sx * 0.05f, pivotY, pivotZ), 0.05f);
                mb.Beam(post, new Vector3(sx * 0.2f, 0.12f, pivotZ + 0.2f), new Vector3(sx * 0.05f, pivotY, pivotZ), 0.05f);
            }
            mb.Box(Palette.Solid(Palette.Graphite), new Vector3(0, pivotY, pivotZ), new Vector3(0.16f, 0.06f, 0.08f), 0.015f);
            // Gearbox and crank housing at the back, wellhead and output at the front.
            mb.Box(Palette.Solid(Palette.Body), new Vector3(0, 0.24f, 0.34f), new Vector3(0.36f, 0.22f, 0.22f), 0.03f);
            mb.Cylinder(Palette.Solid(Palette.Graphite), new Vector3(0, 0.12f, -0.36f), 0.075f, 0.14f, 10);
            mb.Cylinder(Palette.Solid(Palette.Steel, 0.4f, 0.5f), new Vector3(0, 0.26f, -0.36f), 0.05f, 0.05f, 10);
            mb.Box(Palette.Solid(Palette.BodyShade), new Vector3(0.22f, 0.2f, -0.36f), new Vector3(0.2f, 0.14f, 0.14f), 0.025f);
            Sockets(mb, def);
            return mb.Commit();
        }));

        var beam = new Node3D { Position = new Vector3(0, pivotY + 0.04f, pivotZ) };
        rig.Root.AddChild(beam);
        Add(rig, beam, Cached($"pump:beam:{def.Id}", () =>
        {
            var mb = new MeshBuilder();
            var dark = Palette.Solid(Palette.Graphite);
            mb.Box(dark, new Vector3(0, 0, 0.02f), new Vector3(0.08f, 0.08f, 0.86f), 0.015f);
            // Horse head over the wellhead (front, −Z).
            mb.Box(Palette.Solid(accent), new Vector3(0, -0.07f, -0.43f), new Vector3(0.12f, 0.26f, 0.1f), 0.03f);
            mb.Box(Palette.Solid(Palette.Hazard), new Vector3(0, 0.03f, -0.43f), new Vector3(0.13f, 0.04f, 0.11f), 0.01f);
            mb.Box(Palette.Solid(Palette.Steel, 0.35f, 0.6f), new Vector3(0, -0.36f, -0.46f), new Vector3(0.02f, 0.4f, 0.02f), 0f);
            return mb.Commit();
        }));
        rig.Rockers.Add((beam, Vector3.Right, 0.2f, 0.5f));

        var crank = new Node3D { Position = new Vector3(0, 0.3f, 0.34f) };
        rig.Root.AddChild(crank);
        Add(rig, crank, Cached("pump:crank", () =>
        {
            var mb = new MeshBuilder();
            foreach (int sx in new[] { -1, 1 })
            {
                mb.Box(Palette.Solid(Palette.Graphite), new Vector3(sx * 0.21f, 0.08f, 0), new Vector3(0.04f, 0.26f, 0.08f), 0.01f);
                mb.Box(Palette.Solid(Palette.Hazard), new Vector3(sx * 0.21f, 0.2f, 0), new Vector3(0.05f, 0.1f, 0.2f), 0.02f);
            }
            return mb.Commit();
        }));
        rig.Spinners.Add((crank, Vector3.Right, Mathf.Tau * 0.5f));
        StatusLamp(rig, rig.Root, new Vector3(-0.3f, 0.3f, 0.34f));
        rig.Height = 1.1f;
    }

    // ---- Processing -----------------------------------------------------------

    private static void Sawmill(ModelRig rig, BuildingDef def, Color accent, bool effects)
    {
        Add(rig, rig.Root, Cached($"sawmill:{def.Id}", () =>
        {
            var mb = new MeshBuilder();
            Plinth(mb);
            var body = Palette.Solid(Palette.Body);
            // Long saw bench along the item flow, with the blade guard arching over it.
            mb.Box(body, new Vector3(0, 0.25f, 0), new Vector3(0.66f, 0.24f, 0.86f), 0.035f);
            mb.Box(Palette.Solid(Palette.Graphite), new Vector3(0, 0.38f, 0), new Vector3(0.5f, 0.03f, 0.8f), 0.01f);
            mb.Box(Palette.Solid(accent), new Vector3(0, 0.58f, 0.02f), new Vector3(0.18f, 0.2f, 0.42f), 0.04f);
            mb.Box(Palette.Solid(Palette.Hazard), new Vector3(0, 0.69f, 0.02f), new Vector3(0.19f, 0.03f, 0.43f), 0.01f);
            // Freshly cut planks stacked on the bench.
            var plank = Palette.Solid(new Color("#dcb57e"), 0.85f);
            mb.Box(plank, new Vector3(0.14f, 0.41f, -0.28f), new Vector3(0.16f, 0.03f, 0.3f), 0.008f);
            mb.Box(plank, new Vector3(0.15f, 0.44f, -0.27f), new Vector3(0.16f, 0.03f, 0.3f), 0.008f);
            Sockets(mb, def);
            return mb.Commit();
        }));

        var blade = new Node3D { Position = new Vector3(0, 0.46f, 0.02f) };
        rig.Root.AddChild(blade);
        Add(rig, blade, BladeMesh(0.17f));
        rig.Spinners.Add((blade, Vector3.Right, 14f));
        StatusLamp(rig, rig.Root, new Vector3(0.28f, 0.4f, 0.36f));
        if (effects) Smoke(rig, rig.Root, new Vector3(0, 0.5f, -0.2f), 0.35f);
        rig.Height = 0.75f;
    }

    private static void Press(ModelRig rig, BuildingDef def, Color accent)
    {
        Add(rig, rig.Root, Cached($"press:{def.Id}", () =>
        {
            var mb = new MeshBuilder();
            Plinth(mb);
            var body = Palette.Solid(Palette.Body);
            // Bed, two columns and a heavy crown with the hydraulic cylinder.
            mb.Box(Palette.Solid(Palette.BodyShade), new Vector3(0, 0.22f, 0), new Vector3(0.62f, 0.18f, 0.66f), 0.03f);
            mb.Box(Palette.Solid(Palette.Graphite), new Vector3(0, 0.325f, 0), new Vector3(0.42f, 0.03f, 0.46f), 0.01f);
            foreach (int sx in new[] { -1, 1 })
                mb.Box(body, new Vector3(sx * 0.35f, 0.58f, 0), new Vector3(0.14f, 0.92f, 0.3f), 0.03f);
            mb.Box(body, new Vector3(0, 1.06f, 0), new Vector3(0.86f, 0.2f, 0.4f), 0.04f);
            mb.Box(Palette.Solid(accent), new Vector3(0, 1.06f, -0.205f), new Vector3(0.7f, 0.06f, 0.01f), 0f);
            mb.Cylinder(Palette.Solid(Palette.Graphite), new Vector3(0, 1.16f, 0), 0.1f, 0.12f, 10);
            Sockets(mb, def);
            return mb.Commit();
        }));

        var ram = new Node3D { Position = new Vector3(0, 0, 0) };
        rig.Root.AddChild(ram);
        Add(rig, ram, Cached($"press:ram:{def.Id}", () =>
        {
            var mb = new MeshBuilder();
            mb.Cylinder(Palette.Solid(Palette.Steel, 0.3f, 0.7f), new Vector3(0, 0.62f, 0), 0.05f, 0.36f, 10);
            mb.Box(Palette.Solid(accent), new Vector3(0, 0.58f, 0), new Vector3(0.5f, 0.1f, 0.4f), 0.025f);
            mb.Box(Palette.Solid(Palette.Graphite), new Vector3(0, 0.515f, 0), new Vector3(0.42f, 0.03f, 0.34f), 0.01f);
            return mb.Commit();
        }));
        rig.Bobbers.Add((ram, Vector3.Zero, new Vector3(0, 0.08f, 0), 1.25f));
        StatusLamp(rig, rig.Root, new Vector3(0.35f, 1.18f, 0.12f));
        rig.Height = 1.25f;
    }

    private static void Refinery(ModelRig rig, BuildingDef def, Color accent, bool effects)
    {
        Add(rig, rig.Root, Cached($"refinery:{def.Id}", () =>
        {
            var mb = new MeshBuilder();
            Plinth(mb);
            var body = Palette.Solid(Palette.Body);
            var band = Palette.Solid(Palette.Graphite);
            // Two distillation columns with bands and caps.
            foreach (var (x, z, r, h) in new[] { (-0.22f, 0.16f, 0.14f, 1.3f), (0.08f, 0.26f, 0.1f, 0.95f) })
            {
                var foot = new Vector3(x, 0.12f, z);
                mb.Cylinder(body, foot, r, h, 12);
                for (float y = 0.25f; y < h - 0.05f; y += 0.28f)
                    mb.Cylinder(band, foot + new Vector3(0, y, 0), r + 0.012f, 0.04f, 12);
                mb.Cylinder(Palette.Solid(accent), foot + new Vector3(0, h, 0), r, 0.06f, 12, topRadius: r * 0.5f);
            }
            // Horizontal storage tank at the front and connecting pipes.
            CylinderX(mb, Palette.Solid(Palette.BodyShade), new Vector3(0.05f, 0.3f, -0.22f), 0.15f, 0.62f, 12);
            CylinderX(mb, band, new Vector3(-0.15f, 0.3f, -0.22f), 0.158f, 0.04f, 12);
            CylinderX(mb, band, new Vector3(0.25f, 0.3f, -0.22f), 0.158f, 0.04f, 12);
            var pipe = Palette.Solid(Palette.Steel, 0.4f, 0.5f);
            mb.Beam(pipe, new Vector3(-0.22f, 0.9f, 0.02f), new Vector3(-0.22f, 0.9f, -0.12f), 0.05f);
            mb.Beam(pipe, new Vector3(-0.22f, 0.9f, -0.12f), new Vector3(-0.1f, 0.44f, -0.2f), 0.05f);
            mb.Beam(pipe, new Vector3(0.08f, 0.7f, 0.16f), new Vector3(0.14f, 0.44f, -0.16f), 0.05f);
            // Flare stack.
            mb.Cylinder(Palette.Solid(Palette.BodyShade), new Vector3(0.34f, 0.12f, 0.34f), 0.035f, 1.25f, 8);
            mb.Cylinder(band, new Vector3(0.34f, 1.35f, 0.34f), 0.05f, 0.04f, 8);
            Sockets(mb, def);
            return mb.Commit();
        }));

        var flame = Palette.GlowInstance(new Color("#ff9a3c"), 3f);
        Add(rig, rig.Root, Cached("refinery:flame", () =>
        {
            var mb = new MeshBuilder();
            mb.Cylinder(Palette.Solid(Colors.White), new Vector3(0.34f, 1.39f, 0.34f), 0.04f, 0.12f, 6, topRadius: 0f);
            return mb.Commit();
        })).MaterialOverride = flame;
        rig.Glows.Add((flame, 3f));
        StatusLamp(rig, rig.Root, new Vector3(0.34f, 0.3f, -0.05f));
        if (effects) Smoke(rig, rig.Root, new Vector3(0.34f, 1.5f, 0.34f), 0.8f);
        rig.Height = 1.45f;
    }

    /// <summary>Assembly hall with a robot arm on the roof; the accent colour tells the kinds apart.</summary>
    private static void Assembler(ModelRig rig, BuildingDef def, Color accent)
    {
        Add(rig, rig.Root, Cached($"assembler:{def.Id}", () =>
        {
            var mb = new MeshBuilder();
            Plinth(mb);
            var body = Palette.Solid(Palette.Body);
            mb.Box(body, new Vector3(0, 0.37f, 0.02f), new Vector3(0.86f, 0.5f, 0.8f), 0.045f);
            mb.Box(Palette.Solid(accent), new Vector3(0, 0.6f, 0.02f), new Vector3(0.88f, 0.05f, 0.82f), 0.012f);
            // Window band on the front and sides.
            var glass = Palette.Solid(Palette.Glass, 0.15f, 0.2f);
            mb.Box(glass, new Vector3(0, 0.46f, -0.385f), new Vector3(0.6f, 0.1f, 0.01f), 0f);
            foreach (int sx in new[] { -1, 1 })
                mb.Box(glass, new Vector3(sx * 0.435f, 0.46f, 0.02f), new Vector3(0.01f, 0.1f, 0.5f), 0f);
            // Sawtooth roof lights.
            for (int i = 0; i < 2; i++)
            {
                var path = MeshBuilder.Frames(t => new Vector3(Mathf.Lerp(-0.4f, 0.4f, t), 0.625f, 0.12f + i * 0.2f), 1);
                mb.Extrude(Palette.Solid(Palette.BodyShade), new[] { new Vector2(-0.09f, 0), new Vector2(0.09f, 0), new Vector2(0.09f, 0.12f) }, path, closed: true);
            }
            mb.Cylinder(Palette.Solid(Palette.Graphite), new Vector3(-0.2f, 0.625f, -0.18f), 0.09f, 0.06f, 10);
            Sockets(mb, def);
            return mb.Commit();
        }));

        var arm = new Node3D { Position = new Vector3(-0.2f, 0.685f, -0.18f) };
        rig.Root.AddChild(arm);
        Add(rig, arm, Cached($"assembler:arm:{def.Id}", () =>
        {
            var mb = new MeshBuilder();
            var link = Palette.Solid(accent, 0.5f, 0.2f);
            mb.Cylinder(Palette.Solid(Palette.Body), Vector3.Zero, 0.06f, 0.08f, 10);
            mb.Beam(link, new Vector3(0, 0.06f, 0), new Vector3(0.1f, 0.3f, 0), 0.05f);
            mb.Beam(link, new Vector3(0.1f, 0.3f, 0), new Vector3(0.28f, 0.2f, 0), 0.04f);
            mb.Box(Palette.Solid(Palette.Graphite), new Vector3(0.28f, 0.15f, 0), new Vector3(0.06f, 0.07f, 0.08f), 0.01f);
            mb.Box(Palette.Solid(Palette.Hazard), new Vector3(0.1f, 0.3f, 0), new Vector3(0.07f, 0.07f, 0.07f), 0.015f);
            return mb.Commit();
        }));
        rig.Spinners.Add((arm, Vector3.Up, 1.6f));
        StatusLamp(rig, rig.Root, new Vector3(0.3f, 0.72f, -0.25f));
        rig.Height = 1.0f;
    }
}
