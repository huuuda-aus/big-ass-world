using System;
using Godot;
using Realm.Game.Bridge;

namespace Realm.Game.World;

/// <summary>
/// A tape-measure strip laid on the terrain for judging scale: alternating one-metre segments, a red mark and a
/// number every ten metres. Built once from sampled terrain heights, so it follows the ground it was laid on.
/// </summary>
public sealed partial class MeasureTape : Node3D
{
    private const float SegmentMeters = 1f;
    private const int MarkEverySegments = 10;
    private const int ProbeEveryMeters = 10;
    private const float WidthMeters = 0.7f;
    private const float LiftMeters = 0.06f;
    private const float LabelPixelSize = 0.006f;
    private static readonly Color Light = new(0.95f, 0.85f, 0.25f);
    private static readonly Color Dark = new(0.12f, 0.12f, 0.12f);
    private static readonly Color Mark = new(0.9f, 0.1f, 0.1f);

    /// <summary>Lays a tape along a direction.</summary>
    /// <param name="bridge">Terrain to sample.</param>
    /// <param name="start">Start in world metres (height is taken from the terrain).</param>
    /// <param name="direction">Horizontal unit direction.</param>
    /// <param name="lengthMeters">Tape length; rounded down to whole segments.</param>
    /// <param name="startMetres">Distance printed on the first label (where this piece lies along a longer tape).</param>
    /// <returns>The tape node to add to the scene, or null when terrain is not loaded under all of it yet.</returns>
    public static MeasureTape? Lay(TerrainBridge bridge, Vector3 start, Vector3 direction, int lengthMeters, int startMetres = 0)
    {
        ArgumentNullException.ThrowIfNull(bridge);
        for (int probe = 0; probe <= lengthMeters; probe += ProbeEveryMeters)
        {
            Vector3 at = start + (direction * probe);
            if (float.IsNaN(bridge.HeightAt(new Vector3(at.X, 0, at.Z))))
            {
                return null;
            }
        }

        var tape = new MeasureTape();
        var side = new Vector3(-direction.Z, 0, direction.X) * (WidthMeters / 2);
        var surface = new SurfaceTool();
        surface.Begin(Mesh.PrimitiveType.Triangles);

        Vector3 previous = Sample(bridge, start, direction, 0, start.Y);
        for (int i = 0; i < lengthMeters; i++)
        {
            Vector3 next = Sample(bridge, start, direction, (i + 1) * SegmentMeters, previous.Y);
            bool mark = i % MarkEverySegments == 0;
            Color colour = mark ? Mark : (i % 2 == 0 ? Light : Dark);
            AddQuad(surface, previous - side, previous + side, next + side, next - side, colour);
            previous = next;
        }

        surface.GenerateNormals();
        tape.AddChild(new MeshInstance3D
        {
            Mesh = surface.Commit(),
            MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                VertexColorUseAsAlbedo = true,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            },
        });

        for (int metres = 0; metres <= lengthMeters; metres += MarkEverySegments)
        {
            Vector3 at = Sample(bridge, start, direction, metres, start.Y) + (Vector3.Up * 0.9f) + (side * 1.8f);
            tape.AddChild(new Label3D
            {
                Text = $"{startMetres + metres} m",
                Position = at,
                PixelSize = LabelPixelSize,
                FontSize = 48,
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                NoDepthTest = false,
                Modulate = Colors.White,
                OutlineSize = 12,
                TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
            });
        }

        return tape;
    }

    private static Vector3 Sample(TerrainBridge bridge, Vector3 start, Vector3 direction, float distance, float fallbackHeight)
    {
        Vector3 flat = start + (direction * distance);
        float ground = bridge.HeightAt(new Vector3(flat.X, 0, flat.Z));
        return new Vector3(flat.X, (float.IsNaN(ground) ? fallbackHeight : ground) + LiftMeters, flat.Z);
    }

    private static void AddQuad(SurfaceTool s, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color colour)
    {
        s.SetColor(colour);
        foreach (Vector3 v in new[] { a, b, c, a, c, d })
        {
            s.AddVertex(v);
        }
    }
}
