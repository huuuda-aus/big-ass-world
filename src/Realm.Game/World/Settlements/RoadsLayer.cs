using System.Collections.Generic;
using System.IO;
using Godot;
using Realm.Core.Validation;
using Realm.Game.Bridge;
using Realm.Gen.Props;
using Realm.Gen.Settlements.Meshing;
using Realm.Gen.Settlements.Roads;
using Realm.Gen.Textures;

namespace Realm.Game.World.Settlements;

/// <summary>
/// The roads of the world. The road surface is painted into the terrain colour map and graded into the terrain, so the only
/// things this layer adds are the bridges, built once at start (there are only a handful).
/// </summary>
/// <remarks>Main thread only.</remarks>
public sealed partial class RoadsLayer : Node3D
{
    private const double SameBridgeMeters = 6;
    private const float TimberTextureScale = 0.5f; // one wood texture covers 2 m

    private RoadsLayer(RoadManifest manifest)
    {
        Manifest = manifest;
        Name = "Roads";
    }

    /// <summary>The roads that were baked.</summary>
    public RoadManifest Manifest { get; }

    /// <summary>Number of bridges built.</summary>
    public int BridgeCount { get; private set; }

    /// <summary>Reads <c>roads.json</c> next to the terrain and builds the bridges.</summary>
    /// <param name="directory">The baked terrain directory.</param>
    /// <param name="seed">World seed, for the wood texture.</param>
    /// <returns>The layer, or null when no roads were baked.</returns>
    public static RoadsLayer? Open(string directory, ulong seed)
    {
        string path = Path.Combine(directory, RoadManifest.FileName);
        if (!File.Exists(path))
        {
            return null;
        }

        var report = new ValidationReport();
        RoadManifest? manifest = RoadManifest.FromJson(File.ReadAllText(path), report);
        if (manifest is null)
        {
            foreach (ValidationIssue issue in report.Issues)
            {
                GD.PushError(issue.ToString());
            }

            return null;
        }

        var layer = new RoadsLayer(manifest);
        layer.BuildBridges(seed);
        return layer;
    }

    private void BuildBridges(ulong seed)
    {
        StandardMaterial3D timber = TimberMaterial(seed);
        var built = new List<(double X, double Z)>();
        foreach (Road road in Manifest.Roads)
        {
            foreach (RoadBridge bridge in road.Bridges)
            {
                // Roads that share a crossing would stack identical bridges; keep one.
                if (built.Exists(b => ((b.X - bridge.X) * (b.X - bridge.X)) + ((b.Z - bridge.Z) * (b.Z - bridge.Z)) < SameBridgeMeters * SameBridgeMeters))
                {
                    continue;
                }

                built.Add((bridge.X, bridge.Z));
                var mesh = BridgeMesher.Build(bridge);
                (PropMesh wood, PropMesh stone) = BridgeMesher.BuildParts(bridge);
                var node = new Node3D
                {
                    Name = $"{road.Id}_bridge_{BridgeCount}",
                    Position = new Vector3((float)bridge.X, 0, (float)bridge.Z),
                    // A plan angle (x toward z) counter-clockwise is a turn about the vertical axis by the negative angle.
                    Rotation = new Vector3(0, (float)-bridge.YawRadians, 0),
                };
                node.AddChild(new MeshInstance3D { Mesh = PropMeshConverter.ToArrayMesh(wood), MaterialOverride = timber });
                node.AddChild(new MeshInstance3D { Mesh = PropMeshConverter.ToArrayMesh(stone), MaterialOverride = CityMaterials.Solid });
                var body = new StaticBody3D();
                body.AddChild(new CollisionShape3D { Shape = PropMeshConverter.ToCollisionShape(mesh) });
                node.AddChild(body);
                AddChild(node);
                BridgeCount++;
            }
        }
    }

    // The timber is textured with a generated wood grain projected from all three axes (no UVs needed), so the planks run along the deck;
    // the vertex colour tints it brown. Nearest filtering keeps it crisp like the rest of the retro look.
    private static StandardMaterial3D TimberMaterial(ulong seed)
    {
        SurfaceTexture wood = SurfaceTextureGenerator.Wood(seed);
        Image image = Image.CreateFromData(wood.Size, wood.Size, false, Image.Format.Rgba8, wood.AlbedoHeight);
        image.GenerateMipmaps();
        return new StandardMaterial3D
        {
            AlbedoTexture = ImageTexture.CreateFromImage(image),
            VertexColorUseAsAlbedo = true,
            VertexColorIsSrgb = true,
            Uv1Triplanar = true,
            Uv1Scale = Vector3.One * TimberTextureScale,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
            Roughness = 1f,
        };
    }

    /// <summary>All road centre lines as world positions, for drawing on the map.</summary>
    /// <returns>One list of (x, z) per road.</returns>
    public IReadOnlyList<IReadOnlyList<(double X, double Z)>> Lines()
    {
        var lines = new List<IReadOnlyList<(double, double)>>();
        foreach (Road road in Manifest.Roads)
        {
            var line = new List<(double, double)>();
            foreach (RoadPoint p in road.Points)
            {
                line.Add((p.X, p.Z));
            }

            lines.Add(line);
        }

        return lines;
    }
}
