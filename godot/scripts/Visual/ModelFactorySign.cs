using Godot;
using FactorySim.Behaviors;
using FactorySim.Content;

namespace FactorySim.Client;

/// <summary>
/// The sign (meta.model "sign", idea G3): a board on two posts, with its text floating above it and
/// turned to the camera so it reads from any angle. The text comes from the entity, so
/// <see cref="WorldView"/> adds it with <see cref="SignLabel"/> after building the model.
/// To remove: this file and the "sign" case in <c>ModelFactory.Build</c> and <c>WorldView.BuildRig</c>.
/// </summary>
public static partial class ModelFactory
{
    /// <summary>Height of the floating text above the sign's floor.</summary>
    private const float SignTextHeight = 1.0f;

    private static void Sign(ModelRig rig, BuildingDef def, Color accent)
    {
        Add(rig, rig.Root, Cached($"sign:{def.Id}", () =>
        {
            var mb = new MeshBuilder();
            var steel = Palette.Solid(Palette.Steel, 0.35f, 0.6f);
            mb.Box(Palette.Solid(Palette.Graphite), new Vector3(0, 0.04f, 0), new Vector3(0.84f, 0.08f, 0.22f), 0.02f);
            foreach (float x in new[] { -0.3f, 0.3f })
                mb.Cylinder(steel, new Vector3(x, 0.08f, 0.02f), 0.03f, 0.56f, 8);
            // The board: a dark frame, a light face, an accent strip along the bottom of the face.
            mb.Box(Palette.Solid(Palette.Graphite), new Vector3(0, 0.5f, 0.02f), new Vector3(0.86f, 0.38f, 0.04f), 0.015f);
            mb.Box(Palette.Solid(Palette.Body), new Vector3(0, 0.5f, -0.005f), new Vector3(0.8f, 0.32f, 0.02f), 0.01f);
            mb.Box(Palette.Solid(accent), new Vector3(0, 0.37f, -0.018f), new Vector3(0.8f, 0.05f, 0.01f), 0f);
            // Three grey lines of "writing" on the face, so an empty sign still reads as a sign.
            for (int i = 0; i < 3; i++)
                mb.Box(Palette.Solid(Palette.Panel), new Vector3(-0.06f * i, 0.6f - 0.07f * i, -0.018f), new Vector3(0.6f - 0.12f * i, 0.025f, 0.008f), 0f);
            return mb.Commit();
        }));
        rig.Height = 0.75f;
    }

    /// <summary>The floating text for a sign entity, or null when it has none (or is not a sign).</summary>
    public static Label3D? SignLabel(Entity e)
    {
        if (e.State is not SignState { Text.Length: > 0 } sign) return null;
        return new Label3D
        {
            Name = "SignText",
            Text = sign.Text,
            FontSize = 64,
            PixelSize = 0.008f,
            OutlineSize = 16,
            OutlineModulate = new Color(0.08f, 0.09f, 0.11f),
            Modulate = Colors.White,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            Position = new Vector3(0, SignTextHeight, 0),
        };
    }
}
