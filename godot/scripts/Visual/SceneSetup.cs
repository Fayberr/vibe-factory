using Godot;

namespace FactorySim.Client;

/// <summary>Sky, light and ground: the static stage the factory is built on.</summary>
public static class SceneSetup
{
    public static void AddLighting(Node3D parent)
    {
        var sky = new ProceduralSkyMaterial
        {
            SkyTopColor = new Color("#5d9be0"),
            SkyHorizonColor = new Color("#c9e2f5"),
            GroundBottomColor = new Color("#3f6a33"),
            GroundHorizonColor = new Color("#c9e2f5"),
            SunAngleMax = 30,
        };
        parent.AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Sky,
                Sky = new Sky { SkyMaterial = sky },
                AmbientLightSource = Godot.Environment.AmbientSource.Sky,
                AmbientLightEnergy = 0.66f,
                ReflectedLightSource = Godot.Environment.ReflectionSource.Sky,
                TonemapMode = Godot.Environment.ToneMapper.Agx,
                TonemapExposure = 0.93f,
                SsaoEnabled = true,
                SsaoRadius = 0.6f,
                SsaoIntensity = 1.6f,
                GlowEnabled = true,
                GlowIntensity = 0.35f,
                GlowBloom = 0.05f,
                GlowHdrThreshold = 1.1f,
            },
        });
        parent.AddChild(new DirectionalLight3D
        {
            Name = "Sun",
            RotationDegrees = new Vector3(-52, -38, 0),
            LightColor = new Color("#fff4e0"),
            LightEnergy = 1.15f,
            ShadowEnabled = true,
            ShadowBlur = 1.5f,
            DirectionalShadowMaxDistance = 70,
        });
    }

    /// <summary>Grass ground with the plot tinted, a concrete kerb around it, and a shader grid.</summary>
    public static ShaderMaterial AddGround(Node3D parent, GridBounds bounds)
    {
        var noise = new NoiseTexture2D
        {
            Width = 512,
            Height = 512,
            Seamless = true,
            Noise = new FastNoiseLite { Frequency = 0.012f, FractalOctaves = 4 },
        };
        var material = new ShaderMaterial { Shader = new Shader { Code = Shaders.Ground } };
        material.SetShaderParameter("noise_tex", noise);
        material.SetShaderParameter("plot_rect", new Vector4(bounds.Min.X, bounds.Min.Y, bounds.Max.X + 1, bounds.Max.Y + 1));
        parent.AddChild(new MeshInstance3D
        {
            Name = "Ground",
            Mesh = new PlaneMesh { Size = new Vector2(600, 600) },
            MaterialOverride = material,
            Position = new Vector3(bounds.Min.X + 16, -0.001f, bounds.Min.Y + 16),
        });

        // Kerb around the buildable plot.
        var mb = new MeshBuilder();
        var kerb = Palette.Solid(Palette.Concrete, 0.9f);
        float x0 = bounds.Min.X, z0 = bounds.Min.Y, x1 = bounds.Max.X + 1, z1 = bounds.Max.Y + 1, w = 0.35f, h = 0.05f;
        mb.Box(kerb, new Vector3((x0 + x1) / 2, h / 2, z0 - w / 2), new Vector3(x1 - x0 + 2 * w, h, w), 0.015f);
        mb.Box(kerb, new Vector3((x0 + x1) / 2, h / 2, z1 + w / 2), new Vector3(x1 - x0 + 2 * w, h, w), 0.015f);
        mb.Box(kerb, new Vector3(x0 - w / 2, h / 2, (z0 + z1) / 2), new Vector3(w, h, z1 - z0), 0.015f);
        mb.Box(kerb, new Vector3(x1 + w / 2, h / 2, (z0 + z1) / 2), new Vector3(w, h, z1 - z0), 0.015f);
        parent.AddChild(new MeshInstance3D { Name = "Kerb", Mesh = mb.Commit() });
        return material;
    }

    /// <summary>Translucent grid plane shown when building on a layer other than the ground.</summary>
    public static MeshInstance3D AddLayerGrid(Node3D parent, GridBounds bounds)
    {
        float w = bounds.Max.X - bounds.Min.X + 1, d = bounds.Max.Y - bounds.Min.Y + 1;
        var grid = new MeshInstance3D
        {
            Name = "LayerGrid",
            Mesh = new PlaneMesh { Size = new Vector2(w, d) },
            MaterialOverride = new ShaderMaterial { Shader = new Shader { Code = Shaders.LayerGrid } },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Position = new Vector3(bounds.Min.X + w / 2, 0, bounds.Min.Y + d / 2),
            Visible = false,
        };
        parent.AddChild(grid);
        return grid;
    }
}
