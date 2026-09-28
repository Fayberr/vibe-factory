using System.Collections.Generic;
using Godot;
using FactorySim.Content;
using FactorySim.View;

namespace FactorySim.Client;

/// <summary>One translucent preview building.</summary>
public readonly record struct GhostSpec(BuildingDef Def, GridPos Pos, Dir Facing, PathShape Shape, bool Valid);

/// <summary>
/// Pool of translucent model previews (placement, line drags, paste, move). Each ghost is
/// the real model tinted green/red; single-building previews also show port arrows so
/// players see where items enter (blue) and leave (orange).
/// </summary>
public partial class GhostLayer : Node3D
{
    private sealed class Ghost
    {
        public required string Key;
        public required ModelRig Rig;
        public bool Valid = true;
        public MeshInstance3D? Ports;
    }

    private static readonly StandardMaterial3D OkMat = Ghostly(new Color(0.35f, 1f, 0.55f, 0.42f));
    private static readonly StandardMaterial3D BadMat = Ghostly(new Color(1f, 0.3f, 0.3f, 0.45f));
    private static readonly Dictionary<string, Mesh> PortMeshes = new();

    private readonly List<Ghost> _ghosts = new();

    private static StandardMaterial3D Ghostly(Color c) => new()
    {
        AlbedoColor = c,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.PerPixel,
        EmissionEnabled = true,
        Emission = new Color(c, 1f) * 0.35f,
        Roughness = 0.4f,
    };

    public void Show(IReadOnlyList<GhostSpec> specs, bool showPorts)
    {
        for (int i = 0; i < specs.Count; i++)
        {
            var s = specs[i];
            string key = $"{s.Def.Id}:{s.Shape}";
            if (i >= _ghosts.Count || _ghosts[i].Key != key)
            {
                if (i < _ghosts.Count) _ghosts[i].Rig.Root.QueueFree();
                var rig = WorldView.BuildPreview(s.Def, s.Shape);
                rig.MakeGhost(OkMat);
                AddChild(rig.Root);
                var ghost = new Ghost { Key = key, Rig = rig };
                if (i < _ghosts.Count) _ghosts[i] = ghost;
                else _ghosts.Add(ghost);
            }

            var g = _ghosts[i];
            if (g.Valid != s.Valid)
            {
                g.Valid = s.Valid;
                foreach (var geo in g.Rig.Geometry) geo.MaterialOverride = s.Valid ? OkMat : BadMat;
            }
            g.Rig.Root.Visible = true;
            g.Rig.Root.Position = GridMapping.CellFloor(s.Pos);
            g.Rig.Root.Rotation = new Vector3(0, GridMapping.Yaw(s.Facing), 0);

            if (showPorts && g.Ports == null)
            {
                g.Ports = new MeshInstance3D { Mesh = PortArrows(s.Def), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
                g.Rig.Root.AddChild(g.Ports);
            }
            if (g.Ports != null) g.Ports.Visible = showPorts;
        }
        for (int i = specs.Count; i < _ghosts.Count; i++) _ghosts[i].Rig.Root.Visible = false;
    }

    public void HideAll() => Show(System.Array.Empty<GhostSpec>(), false);

    /// <summary>Arrows just outside each port: inward blue for inputs, outward orange for outputs.</summary>
    private static Mesh PortArrows(BuildingDef def)
    {
        if (PortMeshes.TryGetValue(def.Id, out var mesh)) return mesh;
        var mb = new MeshBuilder();
        foreach (var port in def.Ports)
        {
            var d = port.Side switch
            {
                Side.Front => new Vector2(0, -1),
                Side.Back => new Vector2(0, 1),
                Side.Left => new Vector2(-1, 0),
                _ => new Vector2(1, 0),
            };
            var cell = GridMapping.LocalOffset(port.Cell);
            var c = new Vector2(cell.X, cell.Z) + d * 0.72f;
            bool isIn = port.Kind == PortKind.In;
            var dir = isIn ? -d : d;
            var p = new Vector2(-dir.Y, dir.X);
            float y0 = cell.Y + 0.16f, y1 = y0 + 0.03f;
            var m = Palette.Glow(isIn ? Palette.PortIn : Palette.PortOut, 1.4f);
            mb.Slab(m, new[] { c + dir * 0.16f, c + p * 0.13f, c - p * 0.13f }, y0, y1);
            mb.Slab(m, new[] { c + p * 0.045f, c - dir * 0.13f + p * 0.045f, c - dir * 0.13f - p * 0.045f, c - p * 0.045f }, y0, y1);
        }
        return PortMeshes[def.Id] = mb.Commit();
    }
}
