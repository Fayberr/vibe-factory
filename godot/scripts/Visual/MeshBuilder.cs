using System;
using System.Collections.Generic;
using Godot;

namespace FactorySim.Client;

/// <summary>A point on an extrusion path with its orientation and travelled distance.</summary>
public readonly record struct PathFrame(Vector3 Position, Vector3 Right, Vector3 Up, float Distance);

/// <summary>
/// Procedural low-poly geometry. Collects triangles per material (one surface each) and
/// commits them into a single ArrayMesh, so a whole model is usually one draw per material.
///
/// Faces are flat-shaded (per-face normals) for the faceted look; extrusions are smooth
/// along the path and flat across the profile. Winding is fixed automatically from the
/// intended normal, so callers only give points in loop order.
/// </summary>
public sealed class MeshBuilder
{
    private readonly Dictionary<Material, SurfaceTool> _surfaces = new();
    private readonly List<Material> _order = new();

    /// <summary>Transform applied to everything added (push/pop with <see cref="With"/>).</summary>
    public Transform3D Xform = Transform3D.Identity;

    private SurfaceTool Surface(Material m)
    {
        if (_surfaces.TryGetValue(m, out var st)) return st;
        st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        st.SetMaterial(m);
        _surfaces[m] = st;
        _order.Add(m);
        return st;
    }

    /// <summary>Runs <paramref name="build"/> with an extra local transform.</summary>
    public MeshBuilder With(Transform3D local, Action<MeshBuilder> build)
    {
        var saved = Xform;
        Xform = Xform * local;
        build(this);
        Xform = saved;
        return this;
    }

    public ArrayMesh Commit()
    {
        var mesh = new ArrayMesh();
        foreach (var m in _order) _surfaces[m].Commit(mesh);
        return mesh;
    }

    // ---- Raw faces -----------------------------------------------------------

    public void Tri(Material m, Vector3 a, Vector3 b, Vector3 c, Vector3 normal,
                    Vector2 ua = default, Vector2 ub = default, Vector2 uc = default,
                    Vector3? na = null, Vector3? nb = null, Vector3? nc = null)
    {
        a = Xform * a; b = Xform * b; c = Xform * c;
        var basis = Xform.Basis;
        normal = (basis * normal).Normalized();
        // Godot treats clockwise (seen from the front) as front-facing.
        if ((b - a).Cross(c - a).Dot(normal) > 0)
        {
            (b, c) = (c, b);
            (ub, uc) = (uc, ub);
            (nb, nc) = (nc, nb);
        }
        var st = Surface(m);
        Emit(st, a, na.HasValue ? (basis * na.Value).Normalized() : normal, ua);
        Emit(st, b, nb.HasValue ? (basis * nb.Value).Normalized() : normal, ub);
        Emit(st, c, nc.HasValue ? (basis * nc.Value).Normalized() : normal, uc);
    }

    private static void Emit(SurfaceTool st, Vector3 p, Vector3 n, Vector2 uv)
    {
        st.SetNormal(n);
        st.SetUV(uv);
        st.AddVertex(p);
    }

    /// <summary>Quad a-b-c-d given in loop order.</summary>
    public void Quad(Material m, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal)
    {
        Tri(m, a, b, c, normal);
        Tri(m, a, c, d, normal);
    }

    /// <summary>Convex polygon (fan triangulated).</summary>
    public void Polygon(Material m, IReadOnlyList<Vector3> pts, Vector3 normal)
    {
        for (int i = 1; i + 1 < pts.Count; i++) Tri(m, pts[0], pts[i], pts[i + 1], normal);
    }

    // ---- Solids ---------------------------------------------------------------

    /// <summary>Box with chamfered edges (bevel 0 = sharp).</summary>
    public void Box(Material m, Vector3 center, Vector3 size, float bevel = 0.02f)
    {
        var h = size / 2;
        float b = Mathf.Min(bevel, Mathf.Min(h.X, Mathf.Min(h.Y, h.Z)) * 0.9f);

        Vector3 P(int axis, int sx, int sy, int sz)
        {
            // Corner point lying on the face perpendicular to `axis`.
            float x = sx * (axis == 0 ? h.X : h.X - b);
            float y = sy * (axis == 1 ? h.Y : h.Y - b);
            float z = sz * (axis == 2 ? h.Z : h.Z - b);
            return center + new Vector3(x, y, z);
        }

        int[] s = { -1, 1 };
        foreach (int sx in s)
            Quad(m, P(0, sx, -1, -1), P(0, sx, 1, -1), P(0, sx, 1, 1), P(0, sx, -1, 1), new Vector3(sx, 0, 0));
        foreach (int sy in s)
            Quad(m, P(1, -1, sy, -1), P(1, 1, sy, -1), P(1, 1, sy, 1), P(1, -1, sy, 1), new Vector3(0, sy, 0));
        foreach (int sz in s)
            Quad(m, P(2, -1, -1, sz), P(2, 1, -1, sz), P(2, 1, 1, sz), P(2, -1, 1, sz), new Vector3(0, 0, sz));
        if (b <= 0) return;

        foreach (int sx in s)
        foreach (int sy in s)
        {
            Quad(m, P(0, sx, sy, -1), P(0, sx, sy, 1), P(1, sx, sy, 1), P(1, sx, sy, -1), new Vector3(sx, sy, 0));
            foreach (int sz in s)
                Tri(m, P(0, sx, sy, sz), P(1, sx, sy, sz), P(2, sx, sy, sz), new Vector3(sx, sy, sz));
        }
        foreach (int sy in s)
        foreach (int sz in s)
            Quad(m, P(1, -1, sy, sz), P(1, 1, sy, sz), P(2, 1, sy, sz), P(2, -1, sy, sz), new Vector3(0, sy, sz));
        foreach (int sx in s)
        foreach (int sz in s)
            Quad(m, P(0, sx, -1, sz), P(0, sx, 1, sz), P(2, sx, 1, sz), P(2, sx, -1, sz), new Vector3(sx, 0, sz));
    }

    /// <summary>Faceted cylinder/frustum standing on <paramref name="baseCenter"/> along +Y.</summary>
    public void Cylinder(Material m, Vector3 baseCenter, float radius, float height, int sides = 10,
                         float topRadius = -1, bool capTop = true, bool capBottom = false, bool smooth = false)
    {
        if (topRadius < 0) topRadius = radius;
        var top = baseCenter + new Vector3(0, height, 0);
        var ring0 = new Vector3[sides];
        var ring1 = new Vector3[sides];
        for (int i = 0; i < sides; i++)
        {
            float a = Mathf.Tau * (i + 0.5f) / sides;
            var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
            ring0[i] = baseCenter + dir * radius;
            ring1[i] = top + dir * topRadius;
        }
        float slope = (radius - topRadius) / Mathf.Max(height, 0.0001f);
        for (int i = 0; i < sides; i++)
        {
            int j = (i + 1) % sides;
            float a = Mathf.Tau * (i + 1f) / sides;
            var n = new Vector3(Mathf.Cos(a), slope, Mathf.Sin(a)).Normalized();
            if (smooth)
            {
                Vector3 N(int k) => new Vector3(Mathf.Cos(Mathf.Tau * (k + 0.5f) / sides), slope, Mathf.Sin(Mathf.Tau * (k + 0.5f) / sides)).Normalized();
                Tri(m, ring0[i], ring1[i], ring1[j], n, na: N(i), nb: N(i), nc: N(j));
                Tri(m, ring0[i], ring1[j], ring0[j], n, na: N(i), nb: N(j), nc: N(j));
            }
            else Quad(m, ring0[i], ring1[i], ring1[j], ring0[j], n);
        }
        if (capTop && topRadius > 0) Polygon(m, ring1, Vector3.Up);
        if (capBottom) Polygon(m, ring0, Vector3.Down);
    }

    /// <summary>Square beam from <paramref name="a"/> to <paramref name="b"/>.</summary>
    public void Beam(Material m, Vector3 a, Vector3 b, float thickness)
    {
        var dir = b - a;
        float len = dir.Length();
        if (len < 1e-4f) return;
        var y = dir / len;
        var x = Mathf.Abs(y.Dot(Vector3.Up)) > 0.95f ? Vector3.Right : Vector3.Up.Cross(y).Normalized();
        var z = x.Cross(y).Normalized();
        var t = new Transform3D(new Basis(x, y, z), (a + b) / 2);
        With(t, mb => mb.Box(m, Vector3.Zero, new Vector3(thickness, len, thickness), thickness * 0.2f));
    }

    /// <summary>Extruded flat polygon (convex, given in XZ, counter-clockwise seen from above) between two heights.</summary>
    public void Slab(Material m, IReadOnlyList<Vector2> xz, float y0, float y1)
    {
        var top = new Vector3[xz.Count];
        var bottom = new Vector3[xz.Count];
        for (int i = 0; i < xz.Count; i++)
        {
            top[i] = new Vector3(xz[i].X, y1, xz[i].Y);
            bottom[i] = new Vector3(xz[i].X, y0, xz[i].Y);
        }
        Polygon(m, top, Vector3.Up);
        var c = Vector3.Zero;
        foreach (var p in bottom) c += p;
        c /= bottom.Length;
        for (int i = 0; i < xz.Count; i++)
        {
            int j = (i + 1) % xz.Count;
            var mid = (bottom[i] + bottom[j]) / 2;
            var n = (mid - c) with { Y = 0 };
            Quad(m, bottom[i], bottom[j], top[j], top[i], n.Normalized());
        }
    }

    // ---- Extrusion ------------------------------------------------------------

    /// <summary>
    /// Sweeps a 2D profile (x = right, y = up) along <paramref name="path"/>. For closed
    /// profiles give points counter-clockwise; for open ones the outside is on the right
    /// of the direction of travel. UV.x runs along the profile, UV.y is path distance.
    /// </summary>
    public void Extrude(Material m, IReadOnlyList<Vector2> profile, IReadOnlyList<PathFrame> path, bool closed, bool caps = true)
    {
        int segs = closed ? profile.Count : profile.Count - 1;
        float u = 0;
        for (int i = 0; i < segs; i++)
        {
            var p0 = profile[i];
            var p1 = profile[(i + 1) % profile.Count];
            var d = p1 - p0;
            var n2 = new Vector2(d.Y, -d.X).Normalized();
            float u1 = u + d.Length();
            for (int k = 0; k + 1 < path.Count; k++)
            {
                var f0 = path[k];
                var f1 = path[k + 1];
                var a = At(f0, p0);
                var b = At(f0, p1);
                var c = At(f1, p1);
                var e = At(f1, p0);
                var n0 = (f0.Right * n2.X + f0.Up * n2.Y).Normalized();
                var n1 = (f1.Right * n2.X + f1.Up * n2.Y).Normalized();
                var avg = (n0 + n1).Normalized();
                Tri(m, a, b, c, avg, new Vector2(u, f0.Distance), new Vector2(u1, f0.Distance), new Vector2(u1, f1.Distance), n0, n0, n1);
                Tri(m, a, c, e, avg, new Vector2(u, f0.Distance), new Vector2(u1, f1.Distance), new Vector2(u, f1.Distance), n0, n1, n1);
            }
            u = u1;
        }

        if (!closed || !caps) return;
        foreach (var (frame, sign) in new[] { (path[0], -1f), (path[^1], 1f) })
        {
            var pts = new Vector3[profile.Count];
            for (int i = 0; i < profile.Count; i++) pts[i] = At(frame, profile[i]);
            var tangent = frame.Up.Cross(frame.Right).Normalized(); // forward along the path
            Polygon(m, pts, tangent * sign);
        }
    }

    private static Vector3 At(PathFrame f, Vector2 p) => f.Position + f.Right * p.X + f.Up * p.Y;

    /// <summary>Builds frames by sampling a curve; orientation follows the tangent with world-up as reference.</summary>
    public static List<PathFrame> Frames(Func<float, Vector3> curve, int steps)
    {
        var frames = new List<PathFrame>(steps + 1);
        float dist = 0;
        Vector3 prev = curve(0);
        for (int i = 0; i <= steps; i++)
        {
            float t = (float)i / steps;
            var p = curve(t);
            dist += p.DistanceTo(prev);
            prev = p;
            const float e = 0.002f;
            var tangent = (curve(Mathf.Min(1, t + e)) - curve(Mathf.Max(0, t - e))).Normalized();
            var right = tangent.Cross(Vector3.Up).Normalized();
            var up = right.Cross(tangent).Normalized();
            frames.Add(new PathFrame(p, right, up, dist));
        }
        return frames;
    }
}
