using System;
using System.Collections.Generic;
using Godot;
using FactorySim.Content;

namespace FactorySim.Client;

/// <summary>
/// Renders an icon for every building and item by photographing its procedural model in
/// an off-screen viewport, so menus always match the 3D look, and new content gets icons
/// for free.
/// </summary>
public partial class Thumbnails : Node
{
    private const int Size = 192;
    private readonly Dictionary<string, Texture2D> _textures = new();
    private bool _started;

    /// <summary>Raised when an icon becomes available (a building id, or "item:" + item id).</summary>
    public event Action<string>? Updated;

    public Texture2D? Get(string defId) => _textures.GetValueOrDefault(defId);

    public Texture2D? GetItem(string itemId) => _textures.GetValueOrDefault("item:" + itemId);

    public async void RenderAll(IReadOnlyList<BuildingDef> defs, IEnumerable<ItemDef> items)
    {
        if (_started || DisplayServer.GetName() == "headless") return;
        _started = true;

        var viewport = new SubViewport
        {
            Size = new Vector2I(Size, Size),
            TransparentBg = true,
            OwnWorld3D = true,
            Msaa3D = Viewport.Msaa.Msaa4X,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
        };
        AddChild(viewport);
        viewport.AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.ClearColor,
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color("#c9d6e6"),
                AmbientLightEnergy = 0.9f,
                TonemapMode = Godot.Environment.ToneMapper.Agx,
            },
        });
        viewport.AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-50, -30, 0), LightEnergy = 1.3f });
        var camera = new Camera3D { Fov = 26, Current = true };
        viewport.AddChild(camera);
        var stage = new Node3D();
        viewport.AddChild(stage);

        async System.Threading.Tasks.Task Photograph(string key, Node3D root, IEnumerable<GeometryInstance3D> geometry, float minRadius)
        {
            stage.AddChild(root);
            var box = new Aabb();
            bool first = true;
            foreach (var g in geometry)
            {
                if (g is not MeshInstance3D { Mesh: { } mesh } mi) continue;
                var b = mi.GlobalTransform * mesh.GetAabb();
                box = first ? b : box.Merge(b);
                first = false;
            }
            var center = box.GetCenter();
            float radius = Mathf.Max(box.Size.Length() * 0.5f, minRadius);
            var dir = new Vector3(1, 0.85f, 1.1f).Normalized();
            camera.Position = center + dir * (radius / Mathf.Sin(Mathf.DegToRad(camera.Fov / 2)) * 0.98f);
            camera.LookAt(center, Vector3.Up);

            for (int i = 0; i < 2; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            var image = viewport.GetTexture()?.GetImage();
            if (image != null && !image.IsEmpty())
            {
                _textures[key] = ImageTexture.CreateFromImage(image);
                Updated?.Invoke(key);
            }
            root.QueueFree();
        }

        foreach (var def in defs)
        {
            var rig = ModelFactory.Build(def, ModelFactory.ShapeFromPorts(def), effects: false);
            rig.Root.Rotation = new Vector3(0, GridMapping.Yaw(Dir.East), 0);
            await Photograph(def.Id, rig.Root, rig.Geometry, 0.4f);
        }
        foreach (var item in items)
        {
            string shape = item.Meta.GetValueOrDefault("shape") ?? "box";
            var mesh = new MeshInstance3D
            {
                Mesh = ItemMeshes.Get(shape),
                MaterialOverride = new StandardMaterial3D
                {
                    AlbedoColor = Palette.Parse(item.Meta.GetValueOrDefault("color"), Colors.Magenta),
                    Roughness = shape is "ingot" or "coil" or "motor" or "gem" ? 0.35f : 0.75f,
                    Metallic = shape is "ingot" or "coil" or "motor" ? 0.45f : 0f,
                },
                Rotation = new Vector3(0, 0.5f, 0),
            };
            var holder = new Node3D();
            holder.AddChild(mesh);
            await Photograph("item:" + item.Id, holder, new[] { mesh }, 0.12f);
        }
        viewport.QueueFree();
    }
}
