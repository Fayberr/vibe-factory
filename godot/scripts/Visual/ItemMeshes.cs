using System.Collections.Generic;
using Godot;

namespace FactorySim.Client;

/// <summary>Meshes for items on belts, chosen by the item's "shape" meta. Coloured per instance.</summary>
public static class ItemMeshes
{
    private static readonly Dictionary<string, Mesh> Cache = new();

    /// <summary>Height of the mesh's centre above the belt deck.</summary>
    public static float Lift(string shape) => shape switch
    {
        "ingot" => 0.05f,
        "rock" => 0.1f,
        "plank" or "plate" or "pane" or "chip" => 0.025f,
        "log" or "coil" => 0.075f,
        "pile" => 0.01f,
        "barrel" => 0.01f,
        "motor" => 0.08f,
        "gem" => 0.1f,
        "toy" or "robot" => 0.01f,
        _ => 0.11f,
    };

    private static bool Shiny(string shape) => shape is "ingot" or "coil" or "motor" or "gem" or "pane";

    public static Mesh Get(string shape)
    {
        if (Cache.TryGetValue(shape, out var mesh)) return mesh;
        var mat = new StandardMaterial3D { VertexColorUseAsAlbedo = true, Roughness = Shiny(shape) ? 0.32f : 0.85f, Metallic = Shiny(shape) ? 0.55f : 0f };
        var mb = new MeshBuilder();
        switch (shape)
        {
            case "ingot":
                Ingot(mb, mat);
                break;
            case "rock":
                Rock(mb, mat);
                break;
            case "log":
                LyingCylinder(mb, mat, Vector3.Zero, 0.07f, 0.28f, 8);
                break;
            case "pile":
                // Heap of sand: low faceted cone.
                mb.Cylinder(mat, Vector3.Zero, 0.13f, 0.11f, 9, topRadius: 0.03f);
                break;
            case "barrel":
                mb.Cylinder(mat, Vector3.Zero, 0.085f, 0.22f, 10);
                foreach (float y in new[] { 0.04f, 0.18f }) mb.Cylinder(mat, new Vector3(0, y, 0), 0.092f, 0.025f, 10);
                break;
            case "plank":
                mb.Box(mat, Vector3.Zero, new Vector3(0.3f, 0.04f, 0.1f), 0.008f);
                break;
            case "plate":
                mb.Box(mat, Vector3.Zero, new Vector3(0.21f, 0.04f, 0.21f), 0.01f);
                break;
            case "pane":
                mb.Box(mat, Vector3.Zero, new Vector3(0.22f, 0.025f, 0.18f), 0.004f);
                mb.Box(mat, new Vector3(0, 0.016f, 0), new Vector3(0.24f, 0.012f, 0.02f), 0f);
                break;
            case "coil":
                Coil(mb, mat);
                break;
            case "box":
                Crate(mb, mat);
                break;
            case "toy":
                Toy(mb, mat);
                break;
            case "chip":
                mb.Box(mat, Vector3.Zero, new Vector3(0.18f, 0.035f, 0.16f), 0.006f);
                for (int i = 0; i < 4; i++)
                foreach (int side in new[] { -1, 1 })
                    mb.Box(mat, new Vector3(-0.06f + i * 0.04f, -0.01f, side * 0.095f), new Vector3(0.018f, 0.02f, 0.03f), 0f);
                break;
            case "motor":
                LyingCylinder(mb, mat, Vector3.Zero, 0.075f, 0.18f, 12);
                LyingCylinder(mb, mat, new Vector3(0.12f, 0, 0), 0.018f, 0.08f, 6);
                mb.Box(mat, new Vector3(0, -0.07f, 0), new Vector3(0.16f, 0.025f, 0.14f), 0.005f);
                break;
            case "gem":
                mb.Cylinder(mat, Vector3.Zero, 0.1f, 0.05f, 8, topRadius: 0.06f);
                mb.Cylinder(mat, new Vector3(0, -0.1f, 0), 0.001f, 0.1f, 8, topRadius: 0.1f, capTop: false);
                break;
            case "robot":
                Robot(mb, mat);
                break;
            default:
                mb.Box(mat, Vector3.Zero, new Vector3(0.22f, 0.22f, 0.22f), 0.03f);
                break;
        }
        return Cache[shape] = mb.Commit();
    }

    private static void LyingCylinder(MeshBuilder mb, Material m, Vector3 center, float radius, float length, int sides) =>
        mb.With(new Transform3D(new Basis(Vector3.Forward, Mathf.Pi / 2), center),
            b => b.Cylinder(m, new Vector3(0, -length / 2, 0), radius, length, sides, capBottom: true));

    /// <summary>Spool of wire: a drum with raised flanges.</summary>
    private static void Coil(MeshBuilder mb, Material m)
    {
        LyingCylinder(mb, m, Vector3.Zero, 0.06f, 0.14f, 10);
        LyingCylinder(mb, m, new Vector3(-0.075f, 0, 0), 0.085f, 0.02f, 10);
        LyingCylinder(mb, m, new Vector3(0.075f, 0, 0), 0.085f, 0.02f, 10);
    }

    /// <summary>Slatted wooden crate.</summary>
    private static void Crate(MeshBuilder mb, Material m)
    {
        mb.Box(m, Vector3.Zero, new Vector3(0.2f, 0.2f, 0.2f), 0.012f);
        foreach (int s in new[] { -1, 1 })
        {
            mb.Box(m, new Vector3(0, s * 0.085f, 0.102f), new Vector3(0.21f, 0.03f, 0.01f), 0f);
            mb.Box(m, new Vector3(0, s * 0.085f, -0.102f), new Vector3(0.21f, 0.03f, 0.01f), 0f);
        }
    }

    /// <summary>Little toy car: body, cabin and four wheels.</summary>
    private static void Toy(MeshBuilder mb, Material m)
    {
        mb.Box(m, new Vector3(0, 0.07f, 0), new Vector3(0.22f, 0.07f, 0.13f), 0.02f);
        mb.Box(m, new Vector3(-0.02f, 0.13f, 0), new Vector3(0.11f, 0.06f, 0.11f), 0.02f);
        foreach (int sx in new[] { -1, 1 })
        foreach (int sz in new[] { -1, 1 })
            mb.With(new Transform3D(new Basis(Vector3.Right, Mathf.Pi / 2), new Vector3(sx * 0.07f, 0.035f, sz * 0.07f)),
                b => b.Cylinder(m, new Vector3(0, -0.015f, 0), 0.035f, 0.03f, 8, capBottom: true));
    }

    /// <summary>Boxy robot: legs, torso, arms and a head with an antenna.</summary>
    private static void Robot(MeshBuilder mb, Material m)
    {
        foreach (int s in new[] { -1, 1 })
        {
            mb.Box(m, new Vector3(0, 0.04f, s * 0.035f), new Vector3(0.05f, 0.08f, 0.04f), 0.008f);
            mb.Box(m, new Vector3(0, 0.14f, s * 0.085f), new Vector3(0.035f, 0.1f, 0.03f), 0.008f);
        }
        mb.Box(m, new Vector3(0, 0.14f, 0), new Vector3(0.09f, 0.12f, 0.13f), 0.015f);
        mb.Box(m, new Vector3(0, 0.25f, 0), new Vector3(0.08f, 0.08f, 0.09f), 0.015f);
        mb.Box(m, new Vector3(0, 0.305f, 0), new Vector3(0.01f, 0.04f, 0.01f), 0f);
    }

    private static void Ingot(MeshBuilder mb, Material m)
    {
        // Trapezoid bar, long axis along X (belts rotate it to face the travel direction).
        Vector3 B(float x, float z) => new(x, -0.05f, z);
        Vector3 T(float x, float z) => new(x, 0.05f, z);
        float bx = 0.16f, bz = 0.08f, tx = 0.125f, tz = 0.055f;
        mb.Quad(m, T(-tx, -tz), T(tx, -tz), T(tx, tz), T(-tx, tz), Vector3.Up);
        mb.Quad(m, B(-bx, -bz), B(bx, -bz), T(tx, -tz), T(-tx, -tz), new Vector3(0, 0.4f, -1));
        mb.Quad(m, B(bx, bz), B(-bx, bz), T(-tx, tz), T(tx, tz), new Vector3(0, 0.4f, 1));
        mb.Quad(m, B(bx, -bz), B(bx, bz), T(tx, tz), T(tx, -tz), new Vector3(1, 0.4f, 0));
        mb.Quad(m, B(-bx, bz), B(-bx, -bz), T(-tx, -tz), T(-tx, tz), new Vector3(-1, 0.4f, 0));
    }

    private static void Rock(MeshBuilder mb, Material m)
    {
        // Jittered icosahedron: 20 flat facets read as a chunk of ore.
        float t = (1 + Mathf.Sqrt(5)) / 2;
        var v = new[]
        {
            new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
            new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
            new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1),
        };
        float[] jitter = { 1.0f, 0.86f, 1.08f, 0.93f, 1.12f, 0.9f, 1.0f, 1.1f, 0.88f, 1.04f, 0.95f, 1.07f };
        for (int i = 0; i < v.Length; i++)
        {
            var p = v[i].Normalized() * jitter[i] * 0.125f;
            v[i] = new Vector3(p.X, p.Y * 0.78f, p.Z);
        }
        int[,] f =
        {
            { 0, 11, 5 }, { 0, 5, 1 }, { 0, 1, 7 }, { 0, 7, 10 }, { 0, 10, 11 }, { 1, 5, 9 }, { 5, 11, 4 },
            { 11, 10, 2 }, { 10, 7, 6 }, { 7, 1, 8 }, { 3, 9, 4 }, { 3, 4, 2 }, { 3, 2, 6 }, { 3, 6, 8 },
            { 3, 8, 9 }, { 4, 9, 5 }, { 2, 4, 11 }, { 6, 2, 10 }, { 8, 6, 7 }, { 9, 8, 1 },
        };
        for (int i = 0; i < f.GetLength(0); i++)
        {
            Vector3 a = v[f[i, 0]], b = v[f[i, 1]], c = v[f[i, 2]];
            var n = (b - a).Cross(c - a).Normalized();
            if (n.Dot(a + b + c) < 0) n = -n;
            mb.Tri(m, a, b, c, n);
        }
    }
}
