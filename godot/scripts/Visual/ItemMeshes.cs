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
        _ => 0.11f,
    };

    public static Mesh Get(string shape)
    {
        if (Cache.TryGetValue(shape, out var mesh)) return mesh;
        var mat = new StandardMaterial3D { VertexColorUseAsAlbedo = true, Roughness = shape == "ingot" ? 0.32f : 0.85f, Metallic = shape == "ingot" ? 0.55f : 0f };
        var mb = new MeshBuilder();
        switch (shape)
        {
            case "ingot":
                Ingot(mb, mat);
                break;
            case "rock":
                Rock(mb, mat);
                break;
            default:
                mb.Box(mat, Vector3.Zero, new Vector3(0.22f, 0.22f, 0.22f), 0.03f);
                break;
        }
        return Cache[shape] = mb.Commit();
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
