using System.Collections.Generic;
using Godot;

namespace FactorySim.Client;

/// <summary>
/// Meshes for items on belts, chosen by the item's "shape" meta. Coloured per instance.
/// A belt tile holds four slots (a quarter tile each) and items are drawn facing the travel
/// direction, so no mesh may be longer than its slot: past that, neighbouring items touch and
/// a busy belt looks like one continuous ribbon instead of separate goods.
/// </summary>
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
        "rod" => 0.075f,
        "screw" => 0.06f,
        "gear" => 0.06f,
        "frame" => 0.045f,
        "pile" => 0.01f,
        "barrel" => 0.01f,
        "battery" => 0.09f,
        "motor" => 0.08f,
        "gem" => 0.1f,
        "toy" or "robot" or "satellite" or "space_probe" => 0.01f,
        "drone" => 0.04f,
        "flask" => 0.08f,
        "chair" or "lantern" or "car" or "toolkit" or "pump" => 0.01f,
        "tire" => 0.075f,
        "phone" => 0.012f,
        "plane" => 0.04f,
        // Orbital tier (4.13.0)
        "solar_panel" => 0.02f,
        "module" => 0.07f,
        "station" => 0.045f,
        // Home appliances (4.24.0): built up from the belt deck.
        "kettle" or "tv" or "washer" or "bike" => 0.01f,
        // Fusion tier (4.25.0)
        "fusion_cell" => 0.01f,
        "starship" => 0.036f,
        // Late exports (4.26.0): built up from the belt deck.
        "rover" or "suit" or "maglev" => 0.01f,
        _ => 0.11f,
    };

    /// <summary>Whether a shape reads as bare metal (shiny and reflective) rather than dull goods.</summary>
    public static bool Shiny(string shape) => shape is "ingot" or "coil" or "rod" or "screw" or "gear" or "frame" or "motor" or "gem" or "pane" or "car";

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
                LyingCylinder(mb, mat, Vector3.Zero, 0.06f, 0.17f, 8);
                break;
            case "pile":
                // Heap of sand: low faceted cone.
                mb.Cylinder(mat, Vector3.Zero, 0.09f, 0.1f, 9, topRadius: 0.025f);
                break;
            case "barrel":
                mb.Cylinder(mat, Vector3.Zero, 0.085f, 0.22f, 10);
                foreach (float y in new[] { 0.04f, 0.18f }) mb.Cylinder(mat, new Vector3(0, y, 0), 0.092f, 0.025f, 10);
                break;
            case "plank":
                mb.Box(mat, Vector3.Zero, new Vector3(0.17f, 0.04f, 0.09f), 0.008f);
                break;
            case "plate":
                mb.Box(mat, Vector3.Zero, new Vector3(0.16f, 0.04f, 0.16f), 0.01f);
                break;
            case "pane":
                mb.Box(mat, Vector3.Zero, new Vector3(0.15f, 0.025f, 0.14f), 0.004f);
                mb.Box(mat, new Vector3(0, 0.016f, 0), new Vector3(0.17f, 0.012f, 0.018f), 0f);
                break;
            case "coil":
                Coil(mb, mat);
                break;
            case "rod":
                LyingCylinder(mb, mat, Vector3.Zero, 0.035f, 0.16f, 8);
                break;
            case "screw":
                // Threaded shaft with a hex head: a little longer than a rod, head to the back.
                LyingCylinder(mb, mat, new Vector3(0.02f, 0, 0), 0.017f, 0.13f, 6);
                mb.With(new Transform3D(new Basis(Vector3.Forward, Mathf.Pi / 2), new Vector3(-0.055f, 0, 0)),
                    b => b.Cylinder(mat, new Vector3(0, -0.011f, 0), 0.033f, 0.022f, 6, capBottom: true));
                break;
            case "gear":
                // Toothed wheel: a hub with eight teeth around its rim.
                mb.Cylinder(mat, new Vector3(0, -0.025f, 0), 0.07f, 0.05f, 12);
                mb.Cylinder(mat, new Vector3(0, 0.025f, 0), 0.028f, 0.022f, 8);
                for (int i = 0; i < 8; i++)
                {
                    float a = i * Mathf.Tau / 8;
                    var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                    mb.With(new Transform3D(new Basis(Vector3.Up, -a), dir * 0.078f),
                        b => b.Box(mat, Vector3.Zero, new Vector3(0.03f, 0.05f, 0.024f), 0.004f));
                }
                break;
            case "frame":
                // Welded rectangle: four beams round an open middle.
                mb.Box(mat, new Vector3(0, 0, 0.07f), new Vector3(0.18f, 0.03f, 0.024f), 0.005f);
                mb.Box(mat, new Vector3(0, 0, -0.07f), new Vector3(0.18f, 0.03f, 0.024f), 0.005f);
                mb.Box(mat, new Vector3(0.078f, 0, 0), new Vector3(0.024f, 0.03f, 0.118f), 0.005f);
                mb.Box(mat, new Vector3(-0.078f, 0, 0), new Vector3(0.024f, 0.03f, 0.118f), 0.005f);
                break;
            case "battery":
                // Cell with a terminal on top and a band across it.
                mb.Box(mat, Vector3.Zero, new Vector3(0.13f, 0.16f, 0.09f), 0.012f);
                mb.Cylinder(mat, new Vector3(0, 0.08f, 0), 0.022f, 0.022f, 8);
                mb.Box(mat, new Vector3(0, 0.02f, 0), new Vector3(0.135f, 0.026f, 0.095f), 0.004f);
                break;
            case "box":
                Crate(mb, mat);
                break;
            case "toy":
                Toy(mb, mat);
                break;
            case "chip":
                mb.Box(mat, Vector3.Zero, new Vector3(0.16f, 0.035f, 0.14f), 0.006f);
                for (int i = 0; i < 4; i++)
                foreach (int side in new[] { -1, 1 })
                    mb.Box(mat, new Vector3(-0.054f + i * 0.036f, -0.01f, side * 0.082f), new Vector3(0.016f, 0.02f, 0.026f), 0f);
                break;
            case "motor":
                LyingCylinder(mb, mat, Vector3.Zero, 0.07f, 0.12f, 12);
                LyingCylinder(mb, mat, new Vector3(0.075f, 0, 0), 0.016f, 0.05f, 6);
                mb.Box(mat, new Vector3(0, -0.065f, 0), new Vector3(0.12f, 0.022f, 0.12f), 0.005f);
                break;
            case "gem":
                mb.Cylinder(mat, Vector3.Zero, 0.085f, 0.05f, 8, topRadius: 0.05f);
                mb.Cylinder(mat, new Vector3(0, -0.085f, 0), 0.001f, 0.085f, 8, topRadius: 0.085f, capTop: false);
                break;
            case "robot":
                Robot(mb, mat);
                break;
            case "drone":
                Drone(mb, mat);
                break;
            case "satellite":
                Satellite(mb, mat);
                break;
            case "space_probe":
                mb.Box(mat, Vector3.Zero, new Vector3(0.12f, 0.09f, 0.08f), 0.008f);
                mb.Box(mat, new Vector3(0, 0.015f, 0), new Vector3(0.035f, 0.025f, 0.17f), 0.002f);
                LyingCylinder(mb, mat, new Vector3(-0.06f, 0, 0), 0.022f, 0.05f, 8);
                LyingCylinder(mb, mat, new Vector3(0.06f, 0, 0), 0.018f, 0.05f, 8);
                break;
            case "toolkit":
                mb.Box(mat, Vector3.Zero, new Vector3(0.16f, 0.08f, 0.12f), 0.012f);
                mb.Box(mat, new Vector3(0, 0.055f, 0), new Vector3(0.08f, 0.03f, 0.018f), 0.006f);
                break;
            case "pump":
                LyingCylinder(mb, mat, Vector3.Zero, 0.065f, 0.11f, 12);
                LyingCylinder(mb, mat, new Vector3(0.065f, 0, 0), 0.025f, 0.05f, 8);
                mb.Box(mat, new Vector3(-0.025f, -0.055f, 0), new Vector3(0.13f, 0.025f, 0.11f), 0.006f);
                break;
            case "flask":
                // A science pack: a round-shouldered flask with a neck and a stopper.
                mb.Cylinder(mat, new Vector3(0, -0.08f, 0), 0.08f, 0.1f, 12, topRadius: 0.032f, capBottom: true, smooth: true);
                mb.Cylinder(mat, new Vector3(0, 0.02f, 0), 0.028f, 0.05f, 10, smooth: true);
                mb.Cylinder(mat, new Vector3(0, 0.07f, 0), 0.036f, 0.025f, 10);
                break;
            case "chair":
                Chair(mb, mat);
                break;
            case "lantern":
                Lantern(mb, mat);
                break;
            case "tire":
                Tire(mb, mat);
                break;
            case "phone":
                // Lying screen up: a slab, the screen a step above its rim, a speaker slot at the top.
                mb.Box(mat, Vector3.Zero, new Vector3(0.16f, 0.02f, 0.085f), 0.008f);
                mb.Box(mat, new Vector3(0.006f, 0.011f, 0), new Vector3(0.13f, 0.004f, 0.072f), 0.001f);
                mb.Box(mat, new Vector3(-0.069f, 0.011f, 0), new Vector3(0.006f, 0.004f, 0.03f), 0.001f);
                break;
            case "car":
                Car(mb, mat);
                break;
            case "plane":
                Plane(mb, mat);
                break;
            // Orbital tier (4.13.0): delete these three cases with the tier.
            case "solar_panel":
                // A flat frame with the cells a step above it, split by two thin bars.
                mb.Box(mat, Vector3.Zero, new Vector3(0.17f, 0.018f, 0.13f), 0.004f);
                mb.Box(mat, new Vector3(0, 0.011f, 0), new Vector3(0.155f, 0.004f, 0.115f), 0.001f);
                foreach (float x in new[] { -0.026f, 0.026f })
                    mb.Box(mat, new Vector3(x, 0.014f, 0), new Vector3(0.006f, 0.003f, 0.115f), 0f);
                break;
            case "module":
                // A lying habitat drum with a docking collar at each end and a window band on top.
                LyingCylinder(mb, mat, Vector3.Zero, 0.065f, 0.13f, 14);
                LyingCylinder(mb, mat, new Vector3(0.075f, 0, 0), 0.035f, 0.02f, 10);
                LyingCylinder(mb, mat, new Vector3(-0.075f, 0, 0), 0.035f, 0.02f, 10);
                mb.Box(mat, new Vector3(0, 0.062f, 0), new Vector3(0.08f, 0.012f, 0.03f), 0.003f);
                break;
            case "station":
                // Two drums on a spine with a panel wing to each side.
                LyingCylinder(mb, mat, new Vector3(-0.045f, 0, 0), 0.04f, 0.075f, 12);
                LyingCylinder(mb, mat, new Vector3(0.045f, 0, 0), 0.04f, 0.075f, 12);
                mb.Box(mat, Vector3.Zero, new Vector3(0.02f, 0.02f, 0.2f), 0.002f);
                foreach (float z in new[] { -0.08f, 0.08f })
                    mb.Box(mat, new Vector3(0, 0, z), new Vector3(0.12f, 0.006f, 0.045f), 0.001f);
                break;
            // Home appliances (4.24.0): delete these four cases with the line.
            case "kettle":
                Kettle(mb, mat);
                break;
            case "tv":
                // A flat screen on a foot, facing across the belt, with the glass a step proud of the bezel.
                mb.Box(mat, new Vector3(0, 0.006f, 0), new Vector3(0.05f, 0.012f, 0.08f), 0.003f);
                mb.Box(mat, new Vector3(0, 0.027f, 0), new Vector3(0.014f, 0.03f, 0.014f), 0.002f);
                mb.Box(mat, new Vector3(0, 0.095f, 0), new Vector3(0.02f, 0.1f, 0.17f), 0.004f);
                mb.Box(mat, new Vector3(0.011f, 0.095f, 0), new Vector3(0.003f, 0.086f, 0.156f), 0.001f);
                break;
            case "washer":
                // A white cube with a round door on the front and a control strip above it.
                mb.Box(mat, new Vector3(0, 0.075f, 0), new Vector3(0.14f, 0.15f, 0.14f), 0.01f);
                LyingCylinder(mb, mat, new Vector3(0.072f, 0.065f, 0), 0.046f, 0.008f, 16);
                LyingCylinder(mb, mat, new Vector3(0.076f, 0.065f, 0), 0.034f, 0.006f, 16);
                mb.Box(mat, new Vector3(0.071f, 0.13f, 0), new Vector3(0.006f, 0.018f, 0.11f), 0.002f);
                break;
            case "bike":
                Bike(mb, mat);
                break;
            // Fusion tier (4.25.0): delete these two cases with the tier.
            case "fusion_cell":
                // A caged core: a round core between two plates, held by four corner posts.
                mb.Box(mat, new Vector3(0, 0.01f, 0), new Vector3(0.12f, 0.02f, 0.12f), 0.004f);
                mb.Box(mat, new Vector3(0, 0.13f, 0), new Vector3(0.12f, 0.02f, 0.12f), 0.004f);
                mb.Cylinder(mat, new Vector3(0, 0.02f, 0), 0.036f, 0.1f, 14, smooth: true);
                foreach (float x in new[] { -0.05f, 0.05f })
                    foreach (float z in new[] { -0.05f, 0.05f })
                        mb.Beam(mat, new Vector3(x, 0.02f, z), new Vector3(x, 0.12f, z), 0.012f);
                break;
            case "starship":
                Starship(mb, mat);
                break;
            // Late exports (4.26.0): delete these three cases with the line.
            case "rover":
                Rover(mb, mat);
                break;
            case "suit":
                Suit(mb, mat);
                break;
            case "maglev":
                // A long rounded car with a cone nose and a window band, floating on its magnet skirt.
                mb.Box(mat, new Vector3(0, 0.008f, 0), new Vector3(0.17f, 0.016f, 0.04f), 0.003f);
                mb.Box(mat, new Vector3(-0.01f, 0.043f, 0), new Vector3(0.14f, 0.045f, 0.06f), 0.015f);
                mb.With(new Transform3D(new Basis(Vector3.Forward, Mathf.Pi / 2), new Vector3(0.06f, 0.043f, 0)),
                    b => b.Cylinder(mat, Vector3.Zero, 0.026f, 0.035f, 12, topRadius: 0.006f, smooth: true));
                mb.Box(mat, new Vector3(-0.01f, 0.052f, 0), new Vector3(0.12f, 0.01f, 0.062f), 0.002f);
                break;
            default:
                mb.Box(mat, Vector3.Zero, new Vector3(0.18f, 0.18f, 0.18f), 0.03f);
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
        mb.Box(m, Vector3.Zero, new Vector3(0.16f, 0.16f, 0.16f), 0.012f);
        foreach (int s in new[] { -1, 1 })
        {
            mb.Box(m, new Vector3(0, s * 0.068f, 0.082f), new Vector3(0.17f, 0.026f, 0.01f), 0f);
            mb.Box(m, new Vector3(0, s * 0.068f, -0.082f), new Vector3(0.17f, 0.026f, 0.01f), 0f);
        }
    }

    /// <summary>A convex side profile (x along the belt, y up) extruded across z, from z0 to z1.</summary>
    private static void Prism(MeshBuilder mb, Material m, Vector2[] xy, float z0, float z1)
    {
        var near = new Vector3[xy.Length];
        var far = new Vector3[xy.Length];
        var centre = Vector2.Zero;
        for (int i = 0; i < xy.Length; i++)
        {
            near[i] = new Vector3(xy[i].X, xy[i].Y, z1);
            far[i] = new Vector3(xy[i].X, xy[i].Y, z0);
            centre += xy[i] / xy.Length;
        }
        mb.Polygon(m, near, Vector3.Back);
        mb.Polygon(m, far, Vector3.Forward);
        for (int i = 0; i < xy.Length; i++)
        {
            int j = (i + 1) % xy.Length;
            var edge = xy[j] - xy[i];
            var n = new Vector3(edge.Y, -edge.X, 0).Normalized();
            if (n.Dot(new Vector3((xy[i] + xy[j]).X / 2 - centre.X, (xy[i] + xy[j]).Y / 2 - centre.Y, 0)) < 0) n = -n;
            mb.Quad(m, far[i], far[j], near[j], near[i], n);
        }
    }

    /// <summary>A wheel standing on the belt, rolling along it: its axle runs across (z).</summary>
    private static void Wheel(MeshBuilder mb, Material m, Vector3 center, float radius, float width, int sides) =>
        mb.With(new Transform3D(new Basis(Vector3.Right, Mathf.Pi / 2), center),
            b => b.Cylinder(m, new Vector3(0, -width / 2, 0), radius, width, sides, capBottom: true, smooth: true));

    /// <summary>Kitchen chair facing along the belt: four legs, a seat, and a back of two posts and a rail.</summary>
    private static void Chair(MeshBuilder mb, Material m)
    {
        foreach (int sx in new[] { -1, 1 })
        foreach (int sz in new[] { -1, 1 })
            mb.Box(m, new Vector3(sx * 0.046f, 0.035f, sz * 0.046f), new Vector3(0.014f, 0.07f, 0.014f), 0.003f);
        mb.Box(m, new Vector3(0, 0.077f, 0), new Vector3(0.115f, 0.016f, 0.115f), 0.004f);
        foreach (int sz in new[] { -1, 1 })
            mb.Box(m, new Vector3(-0.05f, 0.12f, sz * 0.046f), new Vector3(0.015f, 0.075f, 0.014f), 0.003f);
        mb.Box(m, new Vector3(-0.05f, 0.165f, 0), new Vector3(0.016f, 0.03f, 0.115f), 0.004f);
    }

    /// <summary>Oil lantern: a base, a glass body between four posts, a pointed roof and a handle.</summary>
    private static void Lantern(MeshBuilder mb, Material m)
    {
        mb.Cylinder(m, Vector3.Zero, 0.05f, 0.018f, 8);
        mb.Cylinder(m, new Vector3(0, 0.018f, 0), 0.034f, 0.085f, 8, smooth: true);
        for (int i = 0; i < 4; i++)
        {
            float a = (i + 0.5f) * Mathf.Tau / 4;
            var at = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 0.04f;
            mb.Beam(m, at + new Vector3(0, 0.018f, 0), at + new Vector3(0, 0.103f, 0), 0.01f);
        }
        mb.Cylinder(m, new Vector3(0, 0.103f, 0), 0.054f, 0.035f, 8, topRadius: 0.012f);
        mb.Beam(m, new Vector3(-0.022f, 0.132f, 0), new Vector3(-0.016f, 0.165f, 0), 0.007f);
        mb.Beam(m, new Vector3(0.022f, 0.132f, 0), new Vector3(0.016f, 0.165f, 0), 0.007f);
        mb.Beam(m, new Vector3(-0.018f, 0.165f, 0), new Vector3(0.018f, 0.165f, 0), 0.007f);
    }

    /// <summary>Tire standing on its tread: a smooth ring with tread blocks and a hub that stands proud.</summary>
    private static void Tire(MeshBuilder mb, Material m)
    {
        Wheel(mb, m, Vector3.Zero, 0.07f, 0.06f, 16);
        Wheel(mb, m, Vector3.Zero, 0.034f, 0.068f, 10);
        mb.With(new Transform3D(new Basis(Vector3.Right, Mathf.Pi / 2), Vector3.Zero), b =>
        {
            for (int i = 0; i < 14; i++)
            {
                float a = i * Mathf.Tau / 14;
                var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                b.With(new Transform3D(new Basis(Vector3.Up, -a), dir * 0.07f),
                    t => t.Box(m, Vector3.Zero, new Vector3(0.01f, 0.062f, 0.016f), 0.002f));
            }
        });
    }

    /// <summary>Car, lower and sleeker than the toy: a wedge of a body, a sloped cabin and four wheels.</summary>
    private static void Car(MeshBuilder mb, Material m)
    {
        Prism(mb, m, new[] { new Vector2(-0.09f, 0.02f), new Vector2(0.09f, 0.02f), new Vector2(0.09f, 0.045f),
                             new Vector2(0.075f, 0.062f), new Vector2(-0.085f, 0.066f), new Vector2(-0.09f, 0.058f) }, -0.05f, 0.05f);
        Prism(mb, m, new[] { new Vector2(-0.062f, 0.064f), new Vector2(0.035f, 0.063f),
                             new Vector2(0.008f, 0.104f), new Vector2(-0.048f, 0.104f) }, -0.042f, 0.042f);
        foreach (int sx in new[] { -1, 1 })
        foreach (int sz in new[] { -1, 1 })
            Wheel(mb, m, new Vector3(sx * 0.056f, 0.026f, sz * 0.05f), 0.026f, 0.022f, 10);
    }

    /// <summary>Airliner: a fuselage with a nose and tail cone, swept wings with two engines, a fin and a tailplane.</summary>
    private static void Plane(MeshBuilder mb, Material m)
    {
        LyingCylinder(mb, m, new Vector3(-0.005f, 0, 0), 0.024f, 0.13f, 10);
        mb.With(new Transform3D(new Basis(Vector3.Forward, Mathf.Pi / 2), new Vector3(0.06f, 0, 0)),
            b => b.Cylinder(m, Vector3.Zero, 0.024f, 0.028f, 10, topRadius: 0.006f));
        mb.With(new Transform3D(new Basis(Vector3.Forward, -Mathf.Pi / 2), new Vector3(-0.07f, 0, 0)),
            b => b.Cylinder(m, Vector3.Zero, 0.024f, 0.02f, 10, topRadius: 0.009f));
        foreach (int sz in new[] { -1, 1 })
        {
            mb.Slab(m, new[] { new Vector2(0.03f, sz * 0.01f), new Vector2(-0.015f, sz * 0.09f),
                               new Vector2(-0.035f, sz * 0.09f), new Vector2(-0.03f, sz * 0.01f) }, -0.01f, -0.002f);
            LyingCylinder(mb, m, new Vector3(0.002f, -0.022f, sz * 0.045f), 0.012f, 0.04f, 8);
            mb.Slab(m, new[] { new Vector2(-0.07f, sz * 0.008f), new Vector2(-0.085f, sz * 0.038f),
                               new Vector2(-0.092f, sz * 0.038f), new Vector2(-0.088f, sz * 0.008f) }, 0.008f, 0.013f);
        }
        Prism(mb, m, new[] { new Vector2(-0.09f, 0.012f), new Vector2(-0.058f, 0.012f),
                             new Vector2(-0.078f, 0.062f), new Vector2(-0.09f, 0.062f) }, -0.003f, 0.003f);
    }

    /// <summary>Electric kettle: a tapering body on a base plate, a lid knob, a spout ahead and a handle behind.</summary>
    /// <summary>Six wheels under a flat body with a solar deck on top and a camera mast at the front.</summary>
    private static void Rover(MeshBuilder mb, Material m)
    {
        foreach (float x in new[] { -0.05f, 0f, 0.05f })
            foreach (float z in new[] { -0.052f, 0.052f })
                Wheel(mb, m, new Vector3(x, 0.022f, z), 0.022f, 0.014f, 10);
        mb.Box(m, new Vector3(0, 0.05f, 0), new Vector3(0.14f, 0.035f, 0.09f), 0.006f);
        mb.Box(m, new Vector3(-0.015f, 0.071f, 0), new Vector3(0.09f, 0.006f, 0.08f), 0.001f);
        mb.Box(m, new Vector3(0.045f, 0.09f, 0), new Vector3(0.008f, 0.05f, 0.008f), 0.001f);
        mb.Box(m, new Vector3(0.048f, 0.12f, 0), new Vector3(0.02f, 0.014f, 0.03f), 0.002f);
    }

    /// <summary>Standing, facing forward: legs, a torso with a life-support pack, arms and a round helmet with a visor.</summary>
    private static void Suit(MeshBuilder mb, Material m)
    {
        foreach (float z in new[] { -0.022f, 0.022f })
            mb.Box(m, new Vector3(0, 0.035f, z), new Vector3(0.035f, 0.07f, 0.03f), 0.006f);
        mb.Box(m, new Vector3(0, 0.1f, 0), new Vector3(0.045f, 0.065f, 0.08f), 0.01f);
        mb.Box(m, new Vector3(-0.035f, 0.105f, 0), new Vector3(0.025f, 0.06f, 0.065f), 0.005f);
        foreach (float z in new[] { -0.05f, 0.05f })
            mb.Beam(m, new Vector3(0, 0.125f, z), new Vector3(0.01f, 0.075f, z * 1.1f), 0.018f);
        mb.Cylinder(m, new Vector3(0, 0.133f, 0), 0.026f, 0.035f, 12, smooth: true);
        mb.Box(m, new Vector3(0.022f, 0.152f, 0), new Vector3(0.008f, 0.016f, 0.03f), 0.002f);
    }

    /// <summary>Lying along the belt, nose forward: a hull drum, a cone nose, an engine skirt and two fins.</summary>
    private static void Starship(MeshBuilder mb, Material m)
    {
        LyingCylinder(mb, m, new Vector3(-0.01f, 0, 0), 0.032f, 0.12f, 14);
        mb.With(new Transform3D(new Basis(Vector3.Forward, Mathf.Pi / 2), new Vector3(0.05f, 0, 0)),
            b => b.Cylinder(m, Vector3.Zero, 0.032f, 0.04f, 14, topRadius: 0.004f));
        LyingCylinder(mb, m, new Vector3(-0.077f, 0, 0), 0.024f, 0.014f, 12);
        foreach (float z in new[] { -0.045f, 0.045f })
            mb.Box(m, new Vector3(-0.052f, 0, z), new Vector3(0.04f, 0.005f, 0.028f), 0.001f);
        mb.Box(m, new Vector3(0.03f, 0.03f, 0), new Vector3(0.022f, 0.008f, 0.026f), 0.002f);
    }

    private static void Kettle(MeshBuilder mb, Material m)
    {
        mb.Cylinder(m, Vector3.Zero, 0.06f, 0.012f, 12);
        mb.Cylinder(m, new Vector3(0, 0.012f, 0), 0.052f, 0.1f, 14, topRadius: 0.042f, smooth: true);
        mb.Cylinder(m, new Vector3(0, 0.112f, 0), 0.03f, 0.008f, 12);
        mb.Cylinder(m, new Vector3(0, 0.12f, 0), 0.01f, 0.012f, 8);
        mb.Beam(m, new Vector3(0.04f, 0.085f, 0), new Vector3(0.078f, 0.11f, 0), 0.016f);
        mb.Beam(m, new Vector3(-0.045f, 0.03f, 0), new Vector3(-0.075f, 0.05f, 0), 0.014f);
        mb.Beam(m, new Vector3(-0.075f, 0.05f, 0), new Vector3(-0.07f, 0.095f, 0), 0.014f);
        mb.Beam(m, new Vector3(-0.07f, 0.095f, 0), new Vector3(-0.04f, 0.105f, 0), 0.014f);
    }

    /// <summary>E-bike rolling along the belt: two wheels, a diamond frame with a battery on the down tube, a saddle and bars.</summary>
    private static void Bike(MeshBuilder mb, Material m)
    {
        const float r = 0.036f;
        var rear = new Vector3(-0.052f, r, 0);
        var front = new Vector3(0.052f, r, 0);
        Wheel(mb, m, rear, r, 0.01f, 14);
        Wheel(mb, m, front, r, 0.01f, 14);
        var seat = new Vector3(-0.022f, 0.09f, 0);
        var head = new Vector3(0.04f, 0.095f, 0);
        var crank = new Vector3(-0.004f, 0.034f, 0);
        mb.Beam(m, rear, seat, 0.008f);
        mb.Beam(m, rear, crank, 0.008f);
        mb.Beam(m, crank, seat, 0.009f);
        mb.Beam(m, seat, head, 0.009f);
        mb.Beam(m, crank, head, 0.012f);
        mb.Beam(m, head, front, 0.008f);
        mb.Box(m, new Vector3(0.018f, 0.06f, 0), new Vector3(0.036f, 0.02f, 0.022f), 0.004f);
        mb.Box(m, seat + new Vector3(-0.004f, 0.012f, 0), new Vector3(0.034f, 0.008f, 0.016f), 0.003f);
        mb.Beam(m, head, head + new Vector3(-0.004f, 0.022f, 0), 0.007f);
        mb.Beam(m, head + new Vector3(-0.004f, 0.022f, -0.03f), head + new Vector3(-0.004f, 0.022f, 0.03f), 0.007f);
    }

    /// <summary>Little toy car: body, cabin and four wheels.</summary>
    private static void Toy(MeshBuilder mb, Material m)
    {
        mb.Box(m, new Vector3(0, 0.06f, 0), new Vector3(0.17f, 0.06f, 0.11f), 0.02f);
        mb.Box(m, new Vector3(-0.015f, 0.11f, 0), new Vector3(0.09f, 0.05f, 0.09f), 0.02f);
        foreach (int sx in new[] { -1, 1 })
        foreach (int sz in new[] { -1, 1 })
            mb.With(new Transform3D(new Basis(Vector3.Right, Mathf.Pi / 2), new Vector3(sx * 0.055f, 0.03f, sz * 0.058f)),
                b => b.Cylinder(m, new Vector3(0, -0.015f, 0), 0.03f, 0.026f, 8, capBottom: true));
    }

    /// <summary>Quadcopter: a body, four arms and four rotor discs.</summary>
    private static void Drone(MeshBuilder mb, Material m)
    {
        mb.Box(m, Vector3.Zero, new Vector3(0.075f, 0.045f, 0.075f), 0.015f);
        foreach (int sx in new[] { -1, 1 })
        foreach (int sz in new[] { -1, 1 })
        {
            var tip = new Vector3(sx * 0.055f, 0.01f, sz * 0.055f);
            mb.Beam(m, Vector3.Zero, tip, 0.016f);
            mb.Cylinder(m, tip + new Vector3(0, 0.014f, 0), 0.03f, 0.008f, 10);
        }
    }

    /// <summary>Satellite: a body, two solar panels and a dish.</summary>
    private static void Satellite(MeshBuilder mb, Material m)
    {
        mb.Box(m, new Vector3(0, 0.07f, 0), new Vector3(0.07f, 0.1f, 0.07f), 0.01f);
        foreach (int sx in new[] { -1, 1 })
        {
            mb.Beam(m, new Vector3(sx * 0.035f, 0.08f, 0), new Vector3(sx * 0.05f, 0.08f, 0), 0.01f);
            mb.Box(m, new Vector3(sx * 0.07f, 0.08f, 0), new Vector3(0.04f, 0.008f, 0.05f), 0f);
        }
        mb.Cylinder(m, new Vector3(0, 0.12f, 0), 0.008f, 0.025f, 6);
        mb.Cylinder(m, new Vector3(0, 0.135f, 0), 0.016f, 0.025f, 10, topRadius: 0.04f);
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
        float bx = 0.09f, bz = 0.055f, tx = 0.07f, tz = 0.04f;
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
            var p = v[i].Normalized() * jitter[i] * 0.08f;
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
