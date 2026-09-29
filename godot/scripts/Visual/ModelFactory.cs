using System;
using System.Collections.Generic;
using Godot;
using FactorySim.Content;
using FactorySim.View;

namespace FactorySim.Client;

/// <summary>
/// Builds stylised low-poly models procedurally from a building def (its "model" meta,
/// accent colour, footprint and ports). No imported assets: every shape is generated,
/// cached as a mesh, and shared between instances.
///
/// Local frame: building faces north (front = −Z), anchor cell floor centre at the origin.
/// </summary>
public static partial class ModelFactory
{
    public const float DeckHeight = 0.12f;

    private static readonly Dictionary<string, Mesh> Cache = new();

    public static ModelRig Build(BuildingDef def, PathShape shape, bool effects = true)
    {
        var rig = new ModelRig { Root = new Node3D { Name = def.Id } };
        var accent = Palette.Parse(def.MetaOr("accent", ""), Palette.Steel);
        switch (def.MetaOr("model", "box"))
        {
            case "belt":
                Belt(rig, shape);
                break;
            case "ramp":
                Belt(rig, shape.Kind == PathKind.None ? ShapeFromPorts(def) : shape);
                rig.Height = 1.25f;
                break;
            case "splitter":
            case "merger":
                Hub(rig, def, accent);
                break;
            case "drill":
                Drill(rig, def, accent, effects);
                break;
            case "furnace":
                Furnace(rig, def, accent, effects);
                break;
            case "forge":
                Forge(rig, def, accent, effects);
                break;
            case "polisher":
                Polisher(rig, def, accent);
                break;
            case "depot":
                Depot(rig, def, accent);
                break;
            case "lab":
                Lab(rig, def, accent);
                break;
            case "treefarm":
                TreeFarm(rig, def, accent, effects);
                break;
            case "quarry":
                Quarry(rig, def, accent);
                break;
            case "pump":
                Pump(rig, def, accent);
                break;
            case "sawmill":
                Sawmill(rig, def, accent, effects);
                break;
            case "press":
                Press(rig, def, accent);
                break;
            case "refinery":
                Refinery(rig, def, accent, effects);
                break;
            case "assembler":
                Assembler(rig, def, accent);
                break;
            case "launchpad":
                LaunchPad(rig, def, accent, effects);
                break;
            default:
                Add(rig, rig.Root, Cached($"box:{def.Id}", () =>
                {
                    var mb = new MeshBuilder();
                    Plinth(mb);
                    mb.Box(Palette.Solid(Palette.Body), new Vector3(0, 0.45f, 0), new Vector3(0.8f, 0.6f, 0.8f), 0.05f);
                    Sockets(mb, def);
                    return mb.Commit();
                }));
                rig.Height = 0.8f;
                break;
        }
        return rig;
    }

    /// <summary>Shape a transport def has when standing alone (thumbnails, ghosts).</summary>
    public static PathShape ShapeFromPorts(BuildingDef def)
    {
        if (def.InputPorts.Count == 0 || def.OutputPorts.Count == 0) return new PathShape(PathKind.None);
        int inPort = def.InputPorts[0];
        foreach (int p in def.InputPorts)
            if (def.Ports[p].Side == Side.Back) { inPort = p; break; }
        return new PathShape(PathKind.Straight, def.Ports[inPort].Cell.Z, def.Ports[def.OutputPorts[0]].Cell.Z);
    }

    // ---- Helpers --------------------------------------------------------------

    private static Mesh Cached(string key, Func<Mesh> build)
    {
        if (!Cache.TryGetValue(key, out var mesh)) Cache[key] = mesh = build();
        return mesh;
    }

    private static MeshInstance3D Add(ModelRig rig, Node3D parent, Mesh mesh, Transform3D? xform = null)
    {
        var mi = new MeshInstance3D { Mesh = mesh };
        if (xform is { } t) mi.Transform = t;
        parent.AddChild(mi);
        rig.Geometry.Add(mi);
        return mi;
    }

    private static Vector3 ToGodot(GridPoint p) => new(p.X, p.Z * GridMapping.LayerHeight, p.Y);

    private static Vector3 SideVector(Side side) => side switch
    {
        Side.Front => new Vector3(0, 0, -1),
        Side.Back => new Vector3(0, 0, 1),
        Side.Left => new Vector3(-1, 0, 0),
        _ => new Vector3(1, 0, 0),
    };

    /// <summary>Graphite base with a light trim line: the shared footing of every machine.</summary>
    private static void Plinth(MeshBuilder mb)
    {
        mb.Box(Palette.Solid(Palette.Graphite), new Vector3(0, 0.05f, 0), new Vector3(0.96f, 0.1f, 0.96f), 0.025f);
        mb.Box(Palette.Solid(Palette.Rail), new Vector3(0, 0.115f, 0), new Vector3(0.93f, 0.03f, 0.93f), 0.012f);
    }

    /// <summary>A framed opening on each port face, with a blue (in) or orange (out) marker, at belt height.</summary>
    private static void Sockets(MeshBuilder mb, BuildingDef def)
    {
        foreach (var port in def.Ports)
        {
            var d = SideVector(port.Side);
            var c = GridMapping.LocalOffset(port.Cell);
            bool alongZ = d.X == 0;
            Vector3 Size(float perp, float h, float depth) => alongZ ? new Vector3(perp, h, depth) : new Vector3(depth, h, perp);

            mb.Box(Palette.Solid(Palette.BodyShade), c + d * 0.435f + new Vector3(0, 0.24f, 0), Size(0.68f, 0.24f, 0.09f), 0.02f);
            mb.Box(Palette.Solid(Palette.Dark), c + d * 0.475f + new Vector3(0, 0.225f, 0), Size(0.56f, 0.16f, 0.03f), 0f);
            var marker = port.Kind == PortKind.In ? Palette.PortIn : Palette.PortOut;
            mb.Box(Palette.Glow(marker, 1.2f), c + d * 0.478f + new Vector3(0, 0.335f, 0), Size(0.5f, 0.024f, 0.02f), 0f);
        }
    }

    private static StandardMaterial3D StatusLamp(ModelRig rig, Node3D parent, Vector3 at)
    {
        var mat = Palette.GlowInstance(Palette.Ok, 1.6f);
        var lamp = new MeshInstance3D { Mesh = Cached("lamp", () => new SphereMesh { Radius = 0.045f, Height = 0.09f, RadialSegments = 10, Rings = 5 }), MaterialOverride = mat, Position = at };
        parent.AddChild(lamp);
        rig.StatusLamp = mat;
        return mat;
    }

    private static void Smoke(ModelRig rig, Node3D parent, Vector3 at, float scale = 1f)
    {
        var smoke = new CpuParticles3D
        {
            Position = at,
            Emitting = false,
            Amount = 14,
            Lifetime = 2.4,
            LocalCoords = false,
            Mesh = SmokeMesh,
            EmissionShape = CpuParticles3D.EmissionShapeEnum.Sphere,
            EmissionSphereRadius = 0.04f,
            Direction = Vector3.Up,
            Spread = 14,
            Gravity = new Vector3(0.12f, 0.28f, 0.05f),
            InitialVelocityMin = 0.25f,
            InitialVelocityMax = 0.4f,
            ScaleAmountMin = 0.55f * scale,
            ScaleAmountMax = 0.9f * scale,
            ScaleAmountCurve = SmokeScale,
            ColorRamp = SmokeFade,
        };
        parent.AddChild(smoke);
        rig.Emitters.Add(smoke);
    }

    private static QuadMesh? _smokeMesh;
    private static QuadMesh SmokeMesh => _smokeMesh ??= new QuadMesh
    {
        Size = new Vector2(0.32f, 0.32f),
        Material = new StandardMaterial3D
        {
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
            BillboardKeepScale = true,
            VertexColorUseAsAlbedo = true,
            AlbedoTexture = new GradientTexture2D
            {
                Width = 64,
                Height = 64,
                Fill = GradientTexture2D.FillEnum.Radial,
                FillFrom = new Vector2(0.5f, 0.5f),
                FillTo = new Vector2(0.5f, 0f),
                Gradient = new Gradient { Colors = new[] { new Color(1, 1, 1, 1), new Color(1, 1, 1, 0) }, Offsets = new[] { 0f, 1f } },
            },
        },
    };

    private static Curve? _smokeScale;
    private static Curve SmokeScale => _smokeScale ??= MakeCurve((0, 0.35f), (1, 1.6f));

    private static Gradient? _smokeFade;
    private static Gradient SmokeFade => _smokeFade ??= new Gradient
    {
        Colors = new[] { new Color(0.93f, 0.94f, 0.95f, 0f), new Color(0.9f, 0.91f, 0.93f, 0.55f), new Color(0.85f, 0.86f, 0.88f, 0f) },
        Offsets = new[] { 0f, 0.15f, 1f },
    };

    private static Curve MakeCurve(params (float X, float Y)[] points)
    {
        var c = new Curve { MaxValue = 2f };
        foreach (var (x, y) in points) c.AddPoint(new Vector2(x, y));
        return c;
    }

    // ---- Belts ----------------------------------------------------------------

    // Belt cross-section: narrower than a cell so machines read as the bigger masses.
    private static readonly Vector2[] ChassisProfile = { new(-0.31f, 0f), new(0.31f, 0f), new(0.31f, 0.115f), new(-0.31f, 0.115f) };

    private static readonly Vector2[] LeftRail =
    {
        new(-0.365f, 0f), new(-0.285f, 0f), new(-0.285f, 0.17f), new(-0.3f, 0.2f),
        new(-0.325f, 0.213f), new(-0.35f, 0.2f), new(-0.365f, 0.17f),
    };

    private static readonly Vector2[] RightRail =
    {
        new(0.285f, 0f), new(0.365f, 0f), new(0.365f, 0.17f), new(0.35f, 0.2f),
        new(0.325f, 0.213f), new(0.3f, 0.2f), new(0.285f, 0.17f),
    };

    private static readonly Vector2[] DeckProfile = { new(0.285f, DeckHeight), new(-0.285f, DeckHeight) };

    /// <summary>Belt mesh for a path shape: graphite chassis, rounded light rails, animated deck.</summary>
    public static Mesh BeltMesh(PathShape shape) => Cached($"belt:{shape.Kind}:{shape.StartZ}:{shape.EndZ}", () =>
    {
        var mb = new MeshBuilder();
        int steps = shape.Kind is PathKind.CurveLeft or PathKind.CurveRight ? 12 : shape.IsRamp ? 16 : 1;
        var frames = MeshBuilder.Frames(t => ToGodot(TransportPath.SampleLocal(shape, t)), steps);
        mb.Extrude(Palette.Solid(Palette.Graphite), ChassisProfile, frames, closed: true);
        mb.Extrude(Palette.Solid(Palette.Rail, 0.5f), LeftRail, frames, closed: true);
        mb.Extrude(Palette.Solid(Palette.Rail, 0.5f), RightRail, frames, closed: true);
        mb.Extrude(Shaders.Deck, DeckProfile, frames, closed: false);

        if (shape.IsRamp)
        {
            // Posts under the raised part so ramps read as built structures.
            foreach (float t in new[] { 0.5f, shape.EndZ > shape.StartZ ? 0.88f : 0.12f })
            {
                var p = ToGodot(TransportPath.SampleLocal(shape, t));
                if (p.Y < 0.2f) continue;
                PillarInto(mb, new Vector3(p.X, 0, p.Z), p.Y);
            }
        }
        return mb.Commit();
    });

    private static void Belt(ModelRig rig, PathShape shape)
    {
        if (shape.Kind == PathKind.None) shape = new PathShape(PathKind.Straight);
        rig.Belts.Add(Add(rig, rig.Root, BeltMesh(shape)));
        rig.Height = 0.25f;
    }

    // ---- Levels ---------------------------------------------------------------

    /// <summary>
    /// Shows a building's level: belts get coloured rails, everything else a coloured band around
    /// its base (bronze → silver → gold → cyan → violet), and belt decks run at the level's speed.
    /// </summary>
    public static void ApplyLevel(ModelRig rig, int level, float beltSpeed)
    {
        foreach (var belt in rig.Belts) belt.SetInstanceShaderParameter("belt_speed", beltSpeed);
        if (Palette.LevelTrim(level) is not { } trim) return;
        if (rig.Belts.Count > 0)
        {
            var rails = Palette.Solid(Palette.Rail, 0.5f);
            var tinted = Palette.Solid(trim, 0.35f, 0.55f);
            foreach (var belt in rig.Belts)
                for (int i = 0; i < belt.Mesh.GetSurfaceCount(); i++)
                    if (belt.Mesh.SurfaceGetMaterial(i) == rails) belt.SetSurfaceOverrideMaterial(i, tinted);
            return;
        }
        Add(rig, rig.Root, Cached($"trim:{trim.ToHtml()}", () =>
        {
            var mb = new MeshBuilder();
            var m = Palette.Solid(trim, 0.35f, 0.55f);
            const float half = 0.49f, t = 0.05f, y = 0.1f, h = 0.05f;
            mb.Box(m, new Vector3(0, y, -half + t / 2), new Vector3(2 * half, h, t), 0.01f);
            mb.Box(m, new Vector3(0, y, half - t / 2), new Vector3(2 * half, h, t), 0.01f);
            mb.Box(m, new Vector3(-half + t / 2, y, 0), new Vector3(t, h, 2 * half - 2 * t), 0.01f);
            mb.Box(m, new Vector3(half - t / 2, y, 0), new Vector3(t, h, 2 * half - 2 * t), 0.01f);
            return mb.Commit();
        }));
    }

    /// <summary>Support column from the floor (y=0) up to <paramref name="height"/>.</summary>
    public static void PillarInto(MeshBuilder mb, Vector3 foot, float height)
    {
        var light = Palette.Solid(Palette.BodyShade);
        mb.Box(Palette.Solid(Palette.Graphite), foot + new Vector3(0, 0.03f, 0), new Vector3(0.28f, 0.06f, 0.28f), 0.015f);
        mb.Box(light, foot + new Vector3(0, height / 2, 0), new Vector3(0.14f, height, 0.14f), 0.02f);
        mb.Box(light, foot + new Vector3(0, height - 0.025f, 0), new Vector3(0.3f, 0.05f, 0.24f), 0.012f);
    }

    /// <summary>Column under an elevated building, spanning <paramref name="levels"/> empty layers.</summary>
    public static Mesh PillarMesh(int levels) => Cached($"pillar:{levels}", () =>
    {
        var mb = new MeshBuilder();
        PillarInto(mb, new Vector3(0, -levels * GridMapping.LayerHeight, 0), levels * GridMapping.LayerHeight);
        return mb.Commit();
    });

    // ---- Hubs -----------------------------------------------------------------

    private static void Hub(ModelRig rig, BuildingDef def, Color accent)
    {
        Add(rig, rig.Root, Cached($"hub:{def.Id}", () =>
        {
            var mb = new MeshBuilder();
            mb.Box(Palette.Solid(Palette.Graphite), new Vector3(0, 0.04f, 0), new Vector3(0.96f, 0.08f, 0.96f), 0.025f);
            mb.Box(Palette.Solid(Palette.BodyShade), new Vector3(0, 0.1f, 0), new Vector3(0.9f, 0.04f, 0.9f), 0.015f);
            mb.Cylinder(Palette.Solid(Palette.Belt), new Vector3(0, DeckHeight, 0), 0.2f, 0.004f, 16);

            // Corner posts and short curbs that funnel items into the openings.
            foreach (int sx in new[] { -1, 1 })
            foreach (int sz in new[] { -1, 1 })
            {
                mb.Box(Palette.Solid(Palette.Body), new Vector3(sx * 0.39f, 0.18f, sz * 0.39f), new Vector3(0.15f, 0.13f, 0.15f), 0.03f);
                mb.Box(Palette.Solid(Palette.Rail), new Vector3(sx * 0.3f, 0.15f, sz * 0.44f), new Vector3(0.14f, 0.06f, 0.05f), 0.01f);
                mb.Box(Palette.Solid(Palette.Rail), new Vector3(sx * 0.44f, 0.15f, sz * 0.3f), new Vector3(0.05f, 0.06f, 0.14f), 0.01f);
            }

            // Direction arrows: outward for outputs, inward for inputs.
            var arrow = Palette.Glow(accent, 0.6f);
            foreach (var port in def.Ports)
            {
                var d = SideVector(port.Side);
                bool outward = port.Kind == PortKind.Out;
                var dir = outward ? d : -d;
                var center = d * 0.27f;
                Arrow(mb, arrow, new Vector2(center.X, center.Z), new Vector2(dir.X, dir.Z), DeckHeight + 0.002f, DeckHeight + 0.012f);
            }
            return mb.Commit();
        }));
        rig.Height = 0.3f;
    }

    private static void Arrow(MeshBuilder mb, Material m, Vector2 c, Vector2 d, float y0, float y1)
    {
        var p = new Vector2(-d.Y, d.X);
        mb.Slab(m, new[] { c + d * 0.09f, c - d * 0.02f + p * 0.085f, c - d * 0.02f - p * 0.085f }, y0, y1);
        mb.Slab(m, new[] { c - d * 0.02f + p * 0.028f, c - d * 0.12f + p * 0.028f, c - d * 0.12f - p * 0.028f, c - d * 0.02f - p * 0.028f }, y0, y1);
    }

    // ---- Machines -------------------------------------------------------------

    private static void Drill(ModelRig rig, BuildingDef def, Color accent, bool effects)
    {
        const float cz = -0.12f; // tower axis, a bit forward of centre
        Add(rig, rig.Root, Cached($"drill:{def.Id}", () =>
        {
            var mb = new MeshBuilder();
            Plinth(mb);
            var body = Palette.Solid(Palette.Body);
            var lattice = Palette.Solid(accent, 0.55f, 0.2f);

            // Engine block at the back with vents and an exhaust.
            mb.Box(body, new Vector3(0, 0.3f, 0.26f), new Vector3(0.8f, 0.34f, 0.34f), 0.045f);
            mb.Box(Palette.Solid(accent), new Vector3(0, 0.485f, 0.26f), new Vector3(0.82f, 0.035f, 0.36f), 0.01f);
            for (int i = 0; i < 3; i++)
                mb.Box(Palette.Solid(Palette.Dark), new Vector3(-0.2f + i * 0.2f, 0.3f, 0.435f), new Vector3(0.12f, 0.16f, 0.02f), 0f);
            mb.Cylinder(Palette.Solid(Palette.Steel), new Vector3(0.28f, 0.5f, 0.3f), 0.045f, 0.22f, 8);
            mb.Cylinder(Palette.Solid(Palette.Dark), new Vector3(0.28f, 0.72f, 0.3f), 0.055f, 0.03f, 8);

            // Tapered lattice tower.
            float y0 = 0.13f, y1 = 1.18f;
            Vector3 Leg(int sx, int sz, float y)
            {
                float t = (y - y0) / (y1 - y0);
                float hx = Mathf.Lerp(0.3f, 0.075f, t), hz = Mathf.Lerp(0.22f, 0.065f, t);
                return new Vector3(sx * hx, y, cz + sz * hz);
            }
            int[] s = { -1, 1 };
            foreach (int sx in s)
            foreach (int sz in s)
                mb.Beam(lattice, Leg(sx, sz, y0), Leg(sx, sz, y1), 0.045f);
            float[] levels = { 0.13f, 0.45f, 0.78f, 1.05f };
            for (int l = 0; l < levels.Length; l++)
            {
                float y = levels[l];
                mb.Beam(lattice, Leg(-1, -1, y), Leg(1, -1, y), 0.03f);
                mb.Beam(lattice, Leg(-1, 1, y), Leg(1, 1, y), 0.03f);
                mb.Beam(lattice, Leg(-1, -1, y), Leg(-1, 1, y), 0.03f);
                mb.Beam(lattice, Leg(1, -1, y), Leg(1, 1, y), 0.03f);
                if (l + 1 < levels.Length)
                {
                    float y2 = levels[l + 1];
                    mb.Beam(lattice, Leg(-1, -1, y), Leg(1, -1, y2), 0.022f);
                    mb.Beam(lattice, Leg(1, -1, y), Leg(-1, -1, y2), 0.022f);
                    mb.Beam(lattice, Leg(1, -1, y), Leg(1, 1, y2), 0.022f);
                    mb.Beam(lattice, Leg(-1, -1, y), Leg(-1, 1, y2), 0.022f);
                }
            }
            // Crown block.
            mb.Box(body, new Vector3(0, 1.22f, cz), new Vector3(0.26f, 0.1f, 0.22f), 0.025f);
            // Output chute.
            mb.Box(Palette.Solid(Palette.BodyShade), new Vector3(0, 0.2f, -0.41f), new Vector3(0.38f, 0.14f, 0.16f), 0.03f);
            Sockets(mb, def);
            return mb.Commit();
        }));

        // Pulley wheel on the crown (spins) and the drill string (bobs).
        var wheel = new Node3D { Position = new Vector3(0, 1.33f, cz) };
        rig.Root.AddChild(wheel);
        Add(rig, wheel, Cached("drill:wheel", () =>
        {
            var mb = new MeshBuilder();
            mb.With(new Transform3D(new Basis(Vector3.Forward, Mathf.Pi / 2), Vector3.Zero), b =>
            {
                b.Cylinder(Palette.Solid(Palette.Dark), new Vector3(0, -0.035f, 0), 0.1f, 0.07f, 12);
                b.Cylinder(Palette.Solid(Palette.Hazard), new Vector3(0, -0.04f, 0), 0.055f, 0.08f, 6);
            });
            return mb.Commit();
        }));
        rig.Spinners.Add((wheel, Vector3.Right, 5f));

        var rod = new Node3D { Position = new Vector3(0, 0, cz) };
        rig.Root.AddChild(rod);
        Add(rig, rod, Cached("drill:rod", () =>
        {
            var mb = new MeshBuilder();
            mb.Cylinder(Palette.Solid(Palette.Steel, 0.35f, 0.6f), new Vector3(0, 0.2f, 0), 0.035f, 0.95f, 8);
            mb.Box(Palette.Solid(Palette.Hazard), new Vector3(0, 0.75f, 0), new Vector3(0.12f, 0.08f, 0.12f), 0.015f);
            return mb.Commit();
        }));
        rig.Bobbers.Add((rod, rod.Position, new Vector3(0, 0.07f, 0), 1.3f)); // bob on the tower's axis, not the plinth's centre

        StatusLamp(rig, rig.Root, new Vector3(-0.3f, 0.54f, 0.3f));
        if (effects) Smoke(rig, rig.Root, new Vector3(0.28f, 0.76f, 0.3f), 0.6f);
        rig.Height = 1.35f;
    }

    private static void Furnace(ModelRig rig, BuildingDef def, Color accent, bool effects)
    {
        Add(rig, rig.Root, Cached($"furnace:{def.Id}", () =>
        {
            var mb = new MeshBuilder();
            Plinth(mb);
            var body = Palette.Solid(Palette.Body);
            mb.Box(body, new Vector3(0, 0.41f, 0.02f), new Vector3(0.82f, 0.56f, 0.76f), 0.05f);
            mb.Box(Palette.Solid(Palette.Panel), new Vector3(0, 0.715f, 0.02f), new Vector3(0.86f, 0.05f, 0.8f), 0.02f);
            mb.Box(Palette.Solid(accent), new Vector3(0, 0.6f, 0.02f), new Vector3(0.84f, 0.035f, 0.78f), 0.01f);

            // Side vents.
            foreach (int sx in new[] { -1, 1 })
                for (int i = 0; i < 3; i++)
                    mb.Box(Palette.Solid(Palette.Dark), new Vector3(sx * 0.412f, 0.36f + i * 0.07f, 0.05f), new Vector3(0.02f, 0.03f, 0.42f), 0f);

            // Firebox frame on the front; the glowing pane is a separate animated mesh.
            mb.Box(Palette.Solid(Palette.Graphite), new Vector3(0, 0.42f, -0.36f), new Vector3(0.52f, 0.2f, 0.06f), 0.02f);

            // Twin stacks with collars and rims.
            foreach (int sx in new[] { -1, 1 })
            {
                var b = new Vector3(sx * 0.22f, 0.74f, 0.2f);
                mb.Cylinder(Palette.Solid(Palette.BodyShade), b, 0.085f, 0.46f, 10);
                mb.Cylinder(Palette.Solid(Palette.Graphite), b + new Vector3(0, 0.18f, 0), 0.097f, 0.05f, 10);
                mb.Cylinder(Palette.Solid(Palette.Graphite), b + new Vector3(0, 0.44f, 0), 0.1f, 0.05f, 10);
                mb.Cylinder(Palette.Solid(Palette.Dark), b + new Vector3(0, 0.49f, 0), 0.07f, 0.002f, 10);
            }
            Sockets(mb, def);
            return mb.Commit();
        }));

        var glow = Palette.GlowInstance(accent, 3.2f);
        Add(rig, rig.Root, Cached("furnace:pane", () =>
        {
            var mb = new MeshBuilder();
            mb.Box(Palette.Solid(Colors.White), new Vector3(0, 0.42f, -0.39f), new Vector3(0.42f, 0.12f, 0.02f), 0f);
            return mb.Commit();
        })).MaterialOverride = glow;
        rig.Glows.Add((glow, 3.2f));

        StatusLamp(rig, rig.Root, new Vector3(0, 0.77f, -0.2f));
        if (effects)
            foreach (int sx in new[] { -1, 1 })
                Smoke(rig, rig.Root, new Vector3(sx * 0.22f, 1.26f, 0.2f));
        rig.Height = 1.25f;
    }

    private static void Forge(ModelRig rig, BuildingDef def, Color accent, bool effects)
    {
        Add(rig, rig.Root, Cached($"forge:{def.Id}", () =>
        {
            var mb = new MeshBuilder();
            Plinth(mb);
            var body = Palette.Solid(Palette.Body);
            var band = Palette.Solid(Palette.Graphite);
            mb.Cylinder(body, new Vector3(0, 0.13f, 0.02f), 0.37f, 0.5f, 12, capTop: false);
            mb.Cylinder(band, new Vector3(0, 0.22f, 0.02f), 0.382f, 0.045f, 12);
            mb.Cylinder(band, new Vector3(0, 0.58f, 0.02f), 0.382f, 0.045f, 12);
            mb.Cylinder(Palette.Solid(Palette.BodyShade), new Vector3(0, 0.63f, 0.02f), 0.37f, 0.2f, 12, topRadius: 0.15f);
            mb.Cylinder(Palette.Solid(Palette.Graphite), new Vector3(0, 0.83f, 0.02f), 0.15f, 0.06f, 12);

            // Chimney with collars.
            var cb = new Vector3(0.33f, 0.13f, 0.33f);
            mb.Cylinder(Palette.Solid(Palette.BodyShade), cb, 0.075f, 1.05f, 10);
            foreach (float y in new[] { 0.35f, 0.7f, 1.0f })
                mb.Cylinder(band, cb + new Vector3(0, y, 0), 0.088f, 0.045f, 10);

            // Feed pipes.
            var pipe = Palette.Solid(Palette.Steel, 0.4f, 0.5f);
            mb.Beam(pipe, new Vector3(-0.4f, 0.15f, 0.36f), new Vector3(-0.4f, 0.5f, 0.36f), 0.07f);
            mb.Beam(pipe, new Vector3(-0.4f, 0.5f, 0.36f), new Vector3(-0.2f, 0.5f, 0.2f), 0.07f);
            Sockets(mb, def);
            return mb.Commit();
        }));

        var glow = Palette.GlowInstance(accent, 2.6f);
        Add(rig, rig.Root, Cached("forge:ring", () =>
        {
            var mb = new MeshBuilder();
            mb.Cylinder(Palette.Solid(Colors.White), new Vector3(0, 0.4f, 0.02f), 0.376f, 0.07f, 12, capTop: false);
            mb.Cylinder(Palette.Solid(Colors.White), new Vector3(0, 0.89f, 0.02f), 0.1f, 0.02f, 12);
            return mb.Commit();
        })).MaterialOverride = glow;
        rig.Glows.Add((glow, 2.6f));

        StatusLamp(rig, rig.Root, new Vector3(-0.3f, 0.2f, -0.35f));
        if (effects) Smoke(rig, rig.Root, new Vector3(0.33f, 1.22f, 0.33f), 1.2f);
        rig.Height = 1.2f;
    }

    private static void Polisher(ModelRig rig, BuildingDef def, Color accent)
    {
        rig.Belts.Add(Add(rig, rig.Root, BeltMesh(new PathShape(PathKind.Straight))));
        Add(rig, rig.Root, Cached($"polisher:{def.Id}", () =>
        {
            var mb = new MeshBuilder();
            var body = Palette.Solid(Palette.Body);
            foreach (int sx in new[] { -1, 1 })
            {
                mb.Box(Palette.Solid(Palette.Graphite), new Vector3(sx * 0.43f, 0.04f, 0), new Vector3(0.14f, 0.08f, 0.3f), 0.02f);
                mb.Box(body, new Vector3(sx * 0.43f, 0.33f, 0), new Vector3(0.09f, 0.56f, 0.18f), 0.02f);
            }
            mb.Box(body, new Vector3(0, 0.64f, 0), new Vector3(0.98f, 0.13f, 0.26f), 0.035f);
            mb.Box(Palette.Glow(accent, 1.5f), new Vector3(0, 0.64f, -0.135f), new Vector3(0.8f, 0.03f, 0.01f), 0f);
            mb.Box(Palette.Glow(accent, 1.5f), new Vector3(0, 0.64f, 0.135f), new Vector3(0.8f, 0.03f, 0.01f), 0f);
            return mb.Commit();
        }));

        // Rotating brush roller across the belt.
        var brush = new Node3D { Position = new Vector3(0, 0.37f, 0), Rotation = new Vector3(0, 0, Mathf.Pi / 2) };
        rig.Root.AddChild(brush);
        Add(rig, brush, Cached($"polisher:brush:{def.Id}", () =>
        {
            var mb = new MeshBuilder();
            mb.Cylinder(Palette.Solid(accent, 0.8f), new Vector3(0, -0.36f, 0), 0.11f, 0.72f, 10);
            mb.Cylinder(Palette.Solid(Palette.Graphite), new Vector3(0, -0.4f, 0), 0.04f, 0.8f, 6);
            for (int i = 0; i < 5; i++)
            {
                float a = Mathf.Tau * i / 5;
                mb.Box(Palette.Solid(accent.Darkened(0.25f)), new Vector3(Mathf.Cos(a) * 0.105f, 0, Mathf.Sin(a) * 0.105f), new Vector3(0.03f, 0.7f, 0.03f), 0f);
            }
            return mb.Commit();
        }));
        rig.Spinners.Add((brush, Vector3.Up, 9f));
        rig.Height = 0.75f;
    }

    private static void Depot(ModelRig rig, BuildingDef def, Color accent)
    {
        Add(rig, rig.Root, Cached($"depot:{def.Id}", () =>
        {
            var mb = new MeshBuilder();
            Plinth(mb);
            var body = Palette.Solid(Palette.Body);
            mb.Box(body, new Vector3(0, 0.33f, 0), new Vector3(0.84f, 0.4f, 0.84f), 0.04f);
            mb.Box(Palette.Glow(accent, 0.9f), new Vector3(0, 0.54f, 0), new Vector3(0.86f, 0.03f, 0.86f), 0f);

            // Gabled roof along X.
            var roofPath = MeshBuilder.Frames(t => new Vector3(Mathf.Lerp(-0.47f, 0.47f, t), 0.555f, 0), 1);
            mb.Extrude(Palette.Solid(Palette.Graphite), new[] { new Vector2(-0.48f, 0), new Vector2(0.48f, 0), new Vector2(0, 0.24f) }, roofPath, closed: true);

            // Coin post.
            mb.Cylinder(Palette.Solid(Palette.Steel), new Vector3(0, 0.62f, 0), 0.03f, 0.28f, 6);
            Sockets(mb, def);

            // A canopy over each input, so the side that takes items in reads at a glance.
            foreach (var port in def.Ports)
            {
                var d = SideVector(port.Side);
                Vector3 Across(float along, float h, float depth) => d.X == 0 ? new Vector3(along, h, depth) : new Vector3(depth, h, along);
                mb.Box(Palette.Solid(accent.Darkened(0.2f)), d * 0.58f + new Vector3(0, 0.43f, 0), Across(0.72f, 0.05f, 0.26f), 0.015f);
                mb.Box(Palette.Glow(Palette.PortIn, 1.3f), d * 0.715f + new Vector3(0, 0.43f, 0), Across(0.72f, 0.055f, 0.025f), 0f);
                foreach (float side in new[] { -0.32f, 0.32f })
                    mb.Box(Palette.Solid(Palette.Steel), d * 0.68f + Across(side, 0, 0) + new Vector3(0, 0.25f, 0), Across(0.04f, 0.36f, 0.04f), 0f);
            }
            return mb.Commit();
        }));

        var coin = new Node3D { Position = new Vector3(0, 1.04f, 0) };
        rig.Root.AddChild(coin);
        var gold = Palette.Solid(new Color("#f2c14e"), 0.28f, 0.7f);
        Add(rig, coin, Cached("depot:coin", () =>
        {
            var mb = new MeshBuilder();
            mb.With(new Transform3D(new Basis(Vector3.Right, Mathf.Pi / 2), Vector3.Zero), b =>
            {
                b.Cylinder(gold, new Vector3(0, -0.025f, 0), 0.17f, 0.05f, 18, capBottom: true);
                b.Cylinder(Palette.Solid(new Color("#d9a53a"), 0.3f, 0.7f), new Vector3(0, -0.03f, 0), 0.12f, 0.06f, 18, capBottom: true);
            });
            return mb.Commit();
        }));
        foreach (float z in new[] { -0.031f, 0.031f })
        {
            coin.AddChild(new Label3D
            {
                Text = "$",
                FontSize = 72,
                PixelSize = 0.0028f,
                Position = new Vector3(0, 0, z),
                Rotation = new Vector3(0, z > 0 ? 0 : Mathf.Pi, 0),
                Modulate = new Color("#7a5510"),
                OutlineSize = 0,
                Shaded = true,
            });
        }
        rig.Spinners.Add((coin, Vector3.Up, 1.6f));
        rig.Height = 1.2f;
    }
}
