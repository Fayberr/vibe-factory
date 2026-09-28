using System.Collections.Generic;
using System.Globalization;
using Godot;
using FactorySim.View;

namespace FactorySim.Client;

/// <summary>
/// Low-poly 3D presentation of the world. Entity meshes are created/destroyed from
/// simulation events; items are drawn every frame through one MultiMesh and
/// interpolated between ticks. Purely a reader of simulation state.
/// </summary>
public partial class WorldView : Node3D
{
    private const float BeltTop = 0.08f;
    private const float ItemSize = 0.26f;

    private sealed class EntityVisual
    {
        public required Entity Entity;
        public required Node3D Root;
        public StandardMaterial3D? StatusMaterial;
    }

    private readonly record struct ItemSprite(long Uid, Color Color, float Scale, int EntityZ);

    private readonly Dictionary<int, EntityVisual> _visuals = new();
    private readonly Dictionary<Color, StandardMaterial3D> _materials = new();
    private readonly Dictionary<string, Color> _itemColors = new();
    private readonly List<PositionedItem> _positioned = new();
    private readonly List<ItemSprite> _sprites = new();
    private Dictionary<long, Vector3> _prev = new(), _curr = new();

    private SimHost _host = null!;
    private MultiMesh _itemMesh = null!;
    private MeshInstance3D _ground = null!;
    private MeshInstance3D _grid = null!;
    private int _layer;
    private bool _cutaway;
    private double _statusTimer;

    private World World => _host.Sim.World;

    public void Init(SimHost host)
    {
        _host = host;
        host.WorldReplaced += RebuildAll;
        host.EventRaised += OnSimEvent;
        host.TicksAdvanced += _ => SnapshotItems();
    }

    public override void _Ready()
    {
        _itemMesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            Mesh = new BoxMesh
            {
                Size = new Vector3(ItemSize, ItemSize, ItemSize),
                Material = new StandardMaterial3D { VertexColorUseAsAlbedo = true, Roughness = 0.5f },
            },
            InstanceCount = 256,
            VisibleInstanceCount = 0,
        };
        AddChild(new MultiMeshInstance3D { Name = "Items", Multimesh = _itemMesh });

        _ground = new MeshInstance3D { Name = "Ground" };
        _grid = new MeshInstance3D { Name = "Grid", CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        AddChild(_ground);
        AddChild(_grid);
    }

    /// <summary>Current build layer; with cutaway on, everything above it is hidden.</summary>
    public void SetLayer(int layer, bool cutaway)
    {
        _layer = layer;
        _cutaway = cutaway;
        foreach (var v in _visuals.Values) v.Root.Visible = IsVisible(v.Entity.Pos.Z);
        _ground.Visible = layer >= 0;
        RebuildGrid();
    }

    private bool IsVisible(int z) => !_cutaway || z <= _layer;

    // ---- Entities ----------------------------------------------------------

    private void RebuildAll()
    {
        foreach (var v in _visuals.Values) v.Root.QueueFree();
        _visuals.Clear();
        foreach (var e in World.Entities) AddVisual(e);
        RebuildGround();
        RebuildGrid();
        _prev.Clear();
        _curr.Clear();
        SnapshotItems();
    }

    private void OnSimEvent(SimEvent ev)
    {
        switch (ev)
        {
            case EntityPlaced p when World.GetEntity(p.EntityId) is { } placed:
                AddVisual(placed);
                break;
            case EntityRemoved r:
                RemoveVisual(r.EntityId);
                break;
            case EntityReoriented o when World.GetEntity(o.EntityId) is { } moved:
                RemoveVisual(o.EntityId);
                AddVisual(moved);
                break;
        }
    }

    private void RemoveVisual(int id)
    {
        if (!_visuals.Remove(id, out var v)) return;
        v.Root.QueueFree();
    }

    private void AddVisual(Entity e)
    {
        var def = e.Def;
        var root = new Node3D
        {
            Name = $"E{e.Id}_{def.Id}",
            Position = GridMapping.CellFloor(e.Pos),
            Rotation = new Vector3(0, GridMapping.Yaw(e.Facing), 0),
            Visible = IsVisible(e.Pos.Z),
        };
        var color = GridMapping.ParseColor(def.MetaOr("color", ""), new Color(0.5f, 0.5f, 0.5f));
        var visual = new EntityVisual { Entity = e, Root = root };

        switch (def.MetaOr("shape", "box"))
        {
            case "belt":
                AddBelt(root, color);
                break;
            case "ramp":
                AddRamp(root, def, color);
                break;
            default:
                visual.StatusMaterial = AddMachine(root, def, color);
                break;
        }

        AddChild(root);
        _visuals[e.Id] = visual;
    }

    private void AddBelt(Node3D root, Color color)
    {
        root.AddChild(Mesh(new BoxMesh { Size = new Vector3(0.94f, BeltTop, 0.94f) }, color, new Vector3(0, BeltTop / 2, 0)));
        // Flat arrow pointing to the local front (-Z).
        var arrow = Mesh(new PrismMesh { Size = new Vector3(0.34f, 0.3f, 0.02f) }, color.Lightened(0.35f), new Vector3(0, BeltTop + 0.01f, 0));
        arrow.Rotation = new Vector3(-Mathf.Pi / 2, 0, 0);
        root.AddChild(arrow);
    }

    private void AddRamp(Node3D root, Content.BuildingDef def, Color color)
    {
        // Slope derived from the ports' footprint layers: in at z0 (back edge), out at z1 (front edge).
        int z0 = def.Ports[def.InputPorts[0]].Cell.Z;
        int z1 = def.Ports[def.OutputPorts[0]].Cell.Z;
        float rise = (z1 - z0) * GridMapping.LayerHeight;
        float length = Mathf.Sqrt(1 + rise * rise);
        var slab = Mesh(new BoxMesh { Size = new Vector3(0.94f, BeltTop, length) }, color,
            new Vector3(0, (z0 + z1) * 0.5f * GridMapping.LayerHeight + BeltTop / 2, 0));
        slab.Rotation = new Vector3(Mathf.Atan2(rise, 1), 0, 0);
        root.AddChild(slab);

        // Side walls so ramps read as a solid piece from the iso view.
        foreach (float x in new[] { -0.49f, 0.49f })
        {
            var wall = Mesh(new BoxMesh { Size = new Vector3(0.04f, 0.25f, length) }, color.Darkened(0.3f),
                new Vector3(x, (z0 + z1) * 0.5f * GridMapping.LayerHeight + 0.12f, 0));
            wall.Rotation = slab.Rotation;
            root.AddChild(wall);
        }
    }

    private StandardMaterial3D AddMachine(Node3D root, Content.BuildingDef def, Color color)
    {
        // Bounding box of the footprint in the building's local (north-facing) frame.
        int minX = int.MaxValue, maxX = int.MinValue, minY = int.MaxValue, maxY = int.MinValue, minZ = int.MaxValue, maxZ = int.MinValue;
        foreach (var c in def.Footprint)
        {
            minX = System.Math.Min(minX, c.X); maxX = System.Math.Max(maxX, c.X);
            minY = System.Math.Min(minY, c.Y); maxY = System.Math.Max(maxY, c.Y);
            minZ = System.Math.Min(minZ, c.Z); maxZ = System.Math.Max(maxZ, c.Z);
        }
        float perLayer = float.TryParse(def.MetaOr("height", "0.8"), NumberStyles.Float, CultureInfo.InvariantCulture, out var h) ? h : 0.8f;
        float height = perLayer * (maxZ - minZ + 1) * GridMapping.LayerHeight;
        var size = new Vector3(maxX - minX + 1 - 0.08f, height, maxY - minY + 1 - 0.08f);
        var center = new Vector3((minX + maxX) * 0.5f, minZ * GridMapping.LayerHeight + height / 2, (minY + maxY) * 0.5f);
        root.AddChild(Mesh(new BoxMesh { Size = size }, color, center));

        // Front marker so orientation is readable.
        root.AddChild(Mesh(new BoxMesh { Size = new Vector3(0.4f, height * 0.45f, 0.06f) }, color.Darkened(0.45f),
            new Vector3(center.X, minZ * GridMapping.LayerHeight + height * 0.4f, minY - 0.5f + 0.02f)));

        // Status light on top; its material is per-entity so it can change colour.
        var statusMat = new StandardMaterial3D { AlbedoColor = Colors.Gray, EmissionEnabled = true, Emission = Colors.Black };
        root.AddChild(new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 0.07f, Height = 0.14f },
            MaterialOverride = statusMat,
            Position = new Vector3(center.X, minZ * GridMapping.LayerHeight + height + 0.07f, center.Z),
        });
        return statusMat;
    }

    private MeshInstance3D Mesh(Mesh mesh, Color color, Vector3 position) =>
        new() { Mesh = mesh, MaterialOverride = Material(color), Position = position };

    private StandardMaterial3D Material(Color color)
    {
        if (!_materials.TryGetValue(color, out var m))
            _materials[color] = m = new StandardMaterial3D { AlbedoColor = color, Roughness = 0.85f };
        return m;
    }

    // ---- Ground & grid ------------------------------------------------------

    private void RebuildGround()
    {
        var b = World.Bounds;
        float w = b.Max.X - b.Min.X + 1, d = b.Max.Y - b.Min.Y + 1;
        _ground.Mesh = new PlaneMesh { Size = new Vector2(w, d) };
        _ground.MaterialOverride = Material(new Color(0.17f, 0.2f, 0.19f));
        _ground.Position = new Vector3(b.Min.X + w / 2, -0.002f, b.Min.Y + d / 2);
    }

    private void RebuildGrid()
    {
        if (_host?.Sim == null) return;
        var b = World.Bounds;
        float y = _layer * GridMapping.LayerHeight + 0.004f;
        var mesh = new ImmediateMesh();
        var mat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            AlbedoColor = _layer == 0 ? new Color(1, 1, 1, 0.08f) : new Color(0.4f, 0.8f, 1f, 0.25f),
        };
        mesh.SurfaceBegin(Godot.Mesh.PrimitiveType.Lines, mat);
        for (int x = b.Min.X; x <= b.Max.X + 1; x++)
        {
            mesh.SurfaceAddVertex(new Vector3(x, y, b.Min.Y));
            mesh.SurfaceAddVertex(new Vector3(x, y, b.Max.Y + 1));
        }
        for (int z = b.Min.Y; z <= b.Max.Y + 1; z++)
        {
            mesh.SurfaceAddVertex(new Vector3(b.Min.X, y, z));
            mesh.SurfaceAddVertex(new Vector3(b.Max.X + 1, y, z));
        }
        mesh.SurfaceEnd();
        _grid.Mesh = mesh;
    }

    // ---- Items --------------------------------------------------------------

    /// <summary>Captures item positions after ticks ran; frames interpolate previous → current.</summary>
    private void SnapshotItems()
    {
        (_prev, _curr) = (_curr, _prev);
        _curr.Clear();
        _sprites.Clear();

        TransportPath.CollectAll(World, _positioned);
        foreach (var p in _positioned)
        {
            var pos = GridMapping.ToGodot(p.Point) + new Vector3(0, BeltTop + ItemSize / 2, 0);
            _curr[p.Item.Uid] = pos;
            float scale = 1f + 0.18f * Mathf.Log(p.Item.Count) / Mathf.Log(2);
            int z = World.GetEntity(p.EntityId)?.Pos.Z ?? 0;
            _sprites.Add(new ItemSprite(p.Item.Uid, ItemColor(p.Item.Type), scale, z));
        }
    }

    private Color ItemColor(string type)
    {
        if (_itemColors.TryGetValue(type, out var c)) return c;
        var def = World.Content.Items.GetValueOrDefault(type);
        c = GridMapping.ParseColor(def?.Meta.GetValueOrDefault("color"), Colors.Magenta);
        _itemColors[type] = c;
        return c;
    }

    public override void _Process(double delta)
    {
        if (_host?.Sim == null) return;
        DrawItems(_host.Sim.Alpha);

        _statusTimer += delta;
        if (_statusTimer >= 0.15)
        {
            _statusTimer = 0;
            UpdateStatusLights();
        }
    }

    private void DrawItems(float alpha)
    {
        if (_sprites.Count > _itemMesh.InstanceCount)
            _itemMesh.InstanceCount = (int)System.Numerics.BitOperations.RoundUpToPowerOf2((uint)_sprites.Count);

        int n = 0;
        foreach (var s in _sprites)
        {
            if (!IsVisible(s.EntityZ)) continue;
            var curr = _curr[s.Uid];
            var pos = _prev.TryGetValue(s.Uid, out var prev) ? prev.Lerp(curr, alpha) : curr;
            _itemMesh.SetInstanceTransform(n, new Transform3D(Basis.Identity.Scaled(Vector3.One * s.Scale), pos));
            _itemMesh.SetInstanceColor(n, s.Color);
            n++;
        }
        _itemMesh.VisibleInstanceCount = n;
    }

    private void UpdateStatusLights()
    {
        foreach (var v in _visuals.Values)
        {
            if (v.StatusMaterial == null) continue;
            var status = v.Entity.Behavior.GetStatus(v.Entity);
            var c = status.Working ? new Color(0.3f, 1f, 0.4f)
                : status.Detail == "idle" ? new Color(0.45f, 0.45f, 0.45f)
                : new Color(1f, 0.35f, 0.25f);
            v.StatusMaterial.AlbedoColor = c;
            v.StatusMaterial.Emission = c * (status.Working ? 0.8f : 0.3f);
        }
    }
}
