using System.Collections.Generic;
using Godot;
using FactorySim.Behaviors;
using FactorySim.Content;
using FactorySim.View;

namespace FactorySim.Client;

public enum Highlight
{
    None,
    Hover,
    Selected,
    Danger,
    Moving,
    Upgrade,
}

/// <summary>
/// 3D presentation of the world: one procedural model per building (rebuilt when its
/// neighbourhood changes, so belts curve and bridges get pillars automatically), items
/// drawn with a MultiMesh per shape and interpolated between ticks, machine animation,
/// highlights, and floating income text. Reads the simulation, never writes it.
/// </summary>
public partial class WorldView : Node3D
{
    private sealed class Visual
    {
        public required Entity Entity;
        public required ModelRig Rig;
        public Highlight Highlight;
        public bool Working;
        public float StatusAge = 99;
    }

    private readonly record struct Sprite(long Uid, string Shape, Color Color, float Scale, int EntityZ, float Spin);

    private readonly Dictionary<int, Visual> _visuals = new();
    private readonly HashSet<int> _dirty = new();
    private readonly List<PositionedItem> _positioned = new();
    private readonly List<Sprite> _sprites = new();
    private readonly Dictionary<string, MultiMesh> _itemMeshes = new();
    private readonly Dictionary<string, (Color Color, string Shape)> _itemLook = new();
    private readonly Dictionary<long, float> _yaw = new();
    private Dictionary<long, Vector3> _prev = new(), _curr = new();
    private readonly Dictionary<int, BigNum> _pendingIncome = new();
    private readonly Stack<Label3D> _labelPool = new();
    private double _incomeTimer;

    /// <summary>Float "+$" over depots as they sell (Settings → Gameplay).</summary>
    public bool ShowIncome { get; set; } = true;

    private SimHost _host = null!;
    private Node3D _entities = null!;
    private ShaderMaterial _ground = null!;
    private MeshInstance3D _layerGrid = null!;
    private int _layer;
    private bool _cutaway;
    private float _gridTarget, _grid;

    private static readonly Material HoverOverlay = Shaders.HighlightMaterial(Colors.White, 0.07f, 0.55f);
    private static readonly Material SelectOverlay = Shaders.HighlightMaterial(Palette.Select, 0.14f, 1.2f);
    private static readonly Material DangerOverlay = Shaders.HighlightMaterial(Palette.Danger, 0.35f, 1f);
    private static readonly Material MovingOverlay = Shaders.HighlightMaterial(Colors.White, 0.4f, 0.6f);
    private static readonly Material UpgradeOverlay = Shaders.HighlightMaterial(Palette.Upgrade, 0.14f, 1f);

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
        SceneSetup.AddLighting(this);
        _entities = new Node3D { Name = "Entities" };
        AddChild(_entities);
    }

    // ---- Public API for tools ----------------------------------------------

    public int Layer => _layer;

    /// <summary>Current build height; with cutaway on, everything above it is hidden.</summary>
    public void SetLayer(int layer, bool cutaway)
    {
        _layer = layer;
        _cutaway = cutaway;
        foreach (var v in _visuals.Values) v.Rig.Root.Visible = IsVisible(v.Entity.Pos.Z);
        if (_layerGrid != null)
        {
            _layerGrid.Visible = layer != 0;
            _layerGrid.Position = _layerGrid.Position with { Y = layer * GridMapping.LayerHeight + 0.01f };
        }
    }

    /// <summary>Fades the ground build grid in (building) or out.</summary>
    public void ShowGrid(bool on) => _gridTarget = on ? 1f : 0f;

    public bool IsVisible(int z) => !_cutaway || z <= _layer;

    /// <summary>Graphics preset: Low turns off shadows, ambient occlusion and glow; Medium keeps crisp shadows.</summary>
    public void ApplyQuality(string quality)
    {
        foreach (var child in GetChildren())
        {
            if (child is WorldEnvironment { Environment: { } env })
            {
                env.SsaoEnabled = quality == "High";
                env.GlowEnabled = quality != "Low";
            }
            if (child is DirectionalLight3D sun)
            {
                sun.ShadowEnabled = quality != "Low";
                sun.ShadowBlur = quality == "High" ? 1.5f : 0.5f;
            }
        }
    }

    public Highlight HighlightOf(int entityId) => _visuals.TryGetValue(entityId, out var v) ? v.Highlight : Highlight.None;

    public void SetHighlight(int entityId, Highlight h)
    {
        if (!_visuals.TryGetValue(entityId, out var v) || v.Highlight == h) return;
        v.Highlight = h;
        v.Rig.SetOverlay(OverlayFor(h));
    }

    public void ClearHighlights()
    {
        foreach (var v in _visuals.Values)
        {
            if (v.Highlight == Highlight.None) continue;
            v.Highlight = Highlight.None;
            v.Rig.SetOverlay(null);
        }
    }

    private static Material? OverlayFor(Highlight h) => h switch
    {
        Highlight.Hover => HoverOverlay,
        Highlight.Selected => SelectOverlay,
        Highlight.Danger => DangerOverlay,
        Highlight.Moving => MovingOverlay,
        Highlight.Upgrade => UpgradeOverlay,
        _ => null,
    };

    /// <summary>Nearest visible building hit by a ray (true 3D picking, works across layers).</summary>
    public Entity? Pick(Vector3 origin, Vector3 dir)
    {
        Entity? best = null;
        float bestT = float.MaxValue;
        foreach (var v in _visuals.Values)
        {
            var e = v.Entity;
            if (!IsVisible(e.Pos.Z)) continue;
            var box = Bounds(e, v.Rig.Height);
            if (RayHit(box, origin, dir) is not { } t) continue;
            if (t < bestT)
            {
                bestT = t;
                best = e;
            }
        }
        return best;
    }

    /// <summary>Slab test: distance along the ray to the box, or null.</summary>
    private static float? RayHit(Aabb box, Vector3 o, Vector3 d)
    {
        float tMin = 0, tMax = float.MaxValue;
        for (int axis = 0; axis < 3; axis++)
        {
            float origin = o[axis], dir = d[axis], lo = box.Position[axis], hi = box.End[axis];
            if (Mathf.Abs(dir) < 1e-6f)
            {
                if (origin < lo || origin > hi) return null;
                continue;
            }
            float t1 = (lo - origin) / dir, t2 = (hi - origin) / dir;
            if (t1 > t2) (t1, t2) = (t2, t1);
            tMin = Mathf.Max(tMin, t1);
            tMax = Mathf.Min(tMax, t2);
            if (tMin > tMax) return null;
        }
        return tMin;
    }

    private static Aabb Bounds(Entity e, float height)
    {
        int minX = int.MaxValue, minY = int.MaxValue, minZ = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        foreach (var c in e.Cells())
        {
            minX = System.Math.Min(minX, c.X); maxX = System.Math.Max(maxX, c.X);
            minY = System.Math.Min(minY, c.Y); maxY = System.Math.Max(maxY, c.Y);
            minZ = System.Math.Min(minZ, c.Z);
        }
        var min = new Vector3(minX + 0.04f, minZ * GridMapping.LayerHeight, minY + 0.04f);
        return new Aabb(min, new Vector3(maxX - minX + 0.92f, height, maxY - minY + 0.92f));
    }

    // ---- Entities ----------------------------------------------------------

    private void RebuildAll()
    {
        foreach (var v in _visuals.Values) v.Rig.Root.QueueFree();
        _visuals.Clear();
        _dirty.Clear();
        RebuildGround();
        foreach (var e in World.Entities) _dirty.Add(e.Id);
        _prev.Clear();
        _curr.Clear();
        SetLayer(_layer, _cutaway);
        FlushDirty();
        SnapshotItems();
    }

    /// <summary>Ground, kerb and height grid for the current plot (it grows with every tier).</summary>
    private void RebuildGround()
    {
        foreach (var child in GetChildren())
            if (child.Name == "Ground" || child.Name == "Kerb" || child.Name == "LayerGrid")
            {
                RemoveChild(child);
                child.QueueFree();
            }
        _ground = SceneSetup.AddGround(this, World.Bounds);
        _layerGrid = SceneSetup.AddLayerGrid(this, World.Bounds);
        _ground.SetShaderParameter("grid_strength", _grid);
        SetLayer(_layer, _cutaway);
    }

    private void OnSimEvent(SimEvent ev)
    {
        switch (ev)
        {
            case EntityPlaced p:
                MarkAround(p.Pos);
                _dirty.Add(p.EntityId);
                break;
            case EntityRemoved r:
                if (_visuals.Remove(r.EntityId, out var gone)) gone.Rig.Root.QueueFree();
                MarkAround(r.Pos);
                break;
            case EntityReoriented o:
                MarkAround(o.Pos);
                _dirty.Add(o.EntityId);
                break;
            case EntityLevelChanged l:
                _dirty.Add(l.EntityId);
                break;
            case TierUnlocked:
                RebuildGround();
                break;
            case ItemSold s:
                _pendingIncome[s.EntityId] = _pendingIncome.GetValueOrDefault(s.EntityId) + s.Payout;
                break;
        }
    }

    /// <summary>Neighbours may change shape (curves) or support (pillars): rebuild the surrounding columns.</summary>
    private void MarkAround(GridPos pos)
    {
        foreach (var e in _visuals.Values)
        {
            var p = e.Entity.Pos;
            if (System.Math.Abs(p.X - pos.X) <= 2 && System.Math.Abs(p.Y - pos.Y) <= 2) _dirty.Add(e.Entity.Id);
        }
    }

    private void FlushDirty()
    {
        if (_dirty.Count == 0) return;
        World.EnsureTopology();
        foreach (int id in _dirty)
        {
            if (_visuals.Remove(id, out var old)) old.Rig.Root.QueueFree();
            if (World.GetEntity(id) is not { } e) continue;
            var v = new Visual { Entity = e, Rig = BuildRig(e), Highlight = old?.Highlight ?? Highlight.None };
            if (v.Highlight != Highlight.None) v.Rig.SetOverlay(OverlayFor(v.Highlight));
            _visuals[id] = v;
            _entities.AddChild(v.Rig.Root);
        }
        _dirty.Clear();
    }

    private ModelRig BuildRig(Entity e)
    {
        var rig = ModelFactory.Build(e.Def, TransportPath.ShapeOf(e));
        ModelFactory.ApplyLevel(rig, e.Level, (float)e.SpeedFactor);
        rig.Root.Position = GridMapping.CellFloor(e.Pos);
        rig.Root.Rotation = new Vector3(0, GridMapping.Yaw(e.Facing), 0);
        rig.Root.Visible = IsVisible(e.Pos.Z);

        // Elevated buildings standing over empty ground get a support column.
        if (e.Pos.Z > 0 && ColumnEmptyBelow(e))
        {
            var pillar = new MeshInstance3D { Mesh = ModelFactory.PillarMesh(e.Pos.Z) };
            rig.Root.AddChild(pillar);
            rig.Geometry.Add(pillar);
        }
        return rig;
    }

    private bool ColumnEmptyBelow(Entity e)
    {
        for (int z = e.Pos.Z - 1; z >= 0; z--)
            if (World.EntityAt(e.Pos with { Z = z }) != null) return false;
        return true;
    }

    /// <summary>Builds a standalone model (ghosts, thumbnails) for a def in a given shape.</summary>
    public static ModelRig BuildPreview(BuildingDef def, PathShape shape) => ModelFactory.Build(def, shape, effects: false);

    // ---- Items --------------------------------------------------------------

    private void SnapshotItems()
    {
        if (_host?.Sim == null) return;
        (_prev, _curr) = (_curr, _prev);
        _curr.Clear();
        _sprites.Clear();

        TransportPath.CollectAll(World, _positioned);
        foreach (var p in _positioned)
        {
            var (color, shape) = Look(p.Item.Type);
            var pos = GridMapping.ToGodot(p.Point) + new Vector3(0, ModelFactory.DeckHeight + ItemMeshes.Lift(shape), 0);
            _curr[p.Item.Uid] = pos;
            float scale = 1f + 0.16f * Mathf.Log(p.Item.Count) / Mathf.Log(2);
            int z = World.GetEntity(p.EntityId)?.Pos.Z ?? 0;
            float spin = shape == "rock" ? (p.Item.Uid * 2654435761u % 628) / 100f : 0f;
            _sprites.Add(new Sprite(p.Item.Uid, shape, color, scale, z, spin));
        }
    }

    private (Color, string) Look(string type)
    {
        if (_itemLook.TryGetValue(type, out var look)) return look;
        var def = World.Content.Items.GetValueOrDefault(type);
        look = (Palette.Parse(def?.Meta.GetValueOrDefault("color"), Colors.Magenta), def?.Meta.GetValueOrDefault("shape") ?? "box");
        return _itemLook[type] = look;
    }

    private MultiMesh ItemMultiMesh(string shape)
    {
        if (_itemMeshes.TryGetValue(shape, out var mm)) return mm;
        mm = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            Mesh = ItemMeshes.Get(shape),
            InstanceCount = 256,
            VisibleInstanceCount = 0,
        };
        AddChild(new MultiMeshInstance3D { Name = $"Items_{shape}", Multimesh = mm });
        return _itemMeshes[shape] = mm;
    }

    private void DrawItems(float alpha)
    {
        var counts = new Dictionary<string, int>();
        foreach (var s in _sprites)
        {
            if (!IsVisible(s.EntityZ)) continue;
            var mm = ItemMultiMesh(s.Shape);
            int n = counts.GetValueOrDefault(s.Shape);
            if (n >= mm.InstanceCount)
            {
                // Growing clears instance data; everything is rewritten this frame anyway.
                mm.InstanceCount = (int)System.Numerics.BitOperations.RoundUpToPowerOf2((uint)(n + 1));
            }

            var curr = _curr[s.Uid];
            var pos = curr;
            if (_prev.TryGetValue(s.Uid, out var prev))
            {
                pos = prev.Lerp(curr, alpha);
                var d = curr - prev;
                if (d.LengthSquared() > 1e-6f) _yaw[s.Uid] = Mathf.Atan2(-d.Z, d.X);
            }
            float yaw = (_yaw.TryGetValue(s.Uid, out var y) ? y : 0f) + s.Spin;
            var basis = new Basis(Vector3.Up, yaw).Scaled(Vector3.One * s.Scale);
            mm.SetInstanceTransform(n, new Transform3D(basis, pos));
            mm.SetInstanceColor(n, s.Color);
            counts[s.Shape] = n + 1;
        }
        foreach (var (shape, mm) in _itemMeshes) mm.VisibleInstanceCount = counts.GetValueOrDefault(shape);
        if (_yaw.Count > 4 * _curr.Count + 1024) _yaw.Clear(); // forget sold items
    }

    // ---- Frame ----------------------------------------------------------------

    public override void _Process(double delta)
    {
        if (_host?.Sim == null) return;
        FlushDirty();
        float dt = (float)delta;

        Shaders.Deck.SetShaderParameter("speed", 2f * (float)World.Stat(StatIds.ConveyorSpeed) * _host.TimeScale);
        DrawItems(_host.Sim.Alpha);

        foreach (var v in _visuals.Values)
        {
            var rig = v.Rig;
            if (!rig.Animated && rig.StatusLamp == null) continue;
            v.StatusAge += dt;
            if (v.StatusAge > 0.2f)
            {
                v.StatusAge = 0;
                var status = v.Entity.Behavior.GetStatus(v.Entity);
                v.Working = status.Working;
                rig.SetStatus(status.Working ? Palette.Ok : status.Detail == "idle" ? new Color("#f2c14e") : Palette.Danger);
            }
            if (rig.Animated && rig.Root.Visible) rig.Animate(dt * _host.TimeScale, v.Working);
        }

        _grid = Mathf.MoveToward(_grid, _gridTarget, dt * 4f);
        _ground?.SetShaderParameter("grid_strength", _grid);

        _incomeTimer += delta;
        if (_incomeTimer > 0.7)
        {
            _incomeTimer = 0;
            if (ShowIncome)
                foreach (var (id, amount) in _pendingIncome)
                    if (_visuals.TryGetValue(id, out var v) && v.Rig.Root.Visible) FloatText(v, "+$" + amount.Format());
            _pendingIncome.Clear();
        }
    }

    private void FloatText(Visual v, string text)
    {
        var label = _labelPool.Count > 0 ? _labelPool.Pop() : NewLabel();
        label.Text = text;
        label.Visible = true;
        var start = v.Rig.Root.Position + new Vector3(0, v.Rig.Height + 0.1f, 0);
        label.Position = start;
        label.Modulate = new Color(Palette.Ok, 1f);
        label.OutlineModulate = new Color(0.05f, 0.25f, 0.1f, 1f);
        var tween = CreateTween();
        tween.TweenProperty(label, "position", start + new Vector3(0, 0.8f, 0), 1.3).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
        tween.Parallel().TweenProperty(label, "modulate:a", 0f, 0.6).SetDelay(0.7);
        tween.Parallel().TweenProperty(label, "outline_modulate:a", 0f, 0.6).SetDelay(0.7);
        tween.TweenCallback(Callable.From(() =>
        {
            label.Visible = false;
            _labelPool.Push(label);
        }));
    }

    private Label3D NewLabel()
    {
        var label = new Label3D
        {
            FontSize = 44,
            PixelSize = 0.006f,
            OutlineSize = 10,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true,
            FixedSize = false,
        };
        AddChild(label);
        return label;
    }
}
