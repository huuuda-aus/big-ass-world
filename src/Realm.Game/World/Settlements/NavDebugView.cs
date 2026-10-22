using System;
using Godot;
using Realm.Gen.Settlements.Meshing;
using Realm.Sim.Navigation;

namespace Realm.Game.World.Settlements;

/// <summary>A debug drawing of a settlement's walking graph: edges as lines and nodes as small crosses, coloured by kind, drawn over everything.</summary>
/// <remarks>Main thread only. Built once from the graph; toggled with the N key.</remarks>
public sealed partial class NavDebugView : MeshInstance3D
{
    private const float LiftMeters = 0.4f;
    private const float CrossMeters = 0.5f;
    private static readonly Color StreetColour = new(0.2f, 0.9f, 0.3f);
    private static readonly Color DoorColour = new(1f, 0.85f, 0.1f);
    private static readonly Color GateColour = new(0.3f, 0.6f, 1f);
    private static readonly Color OutsideColour = new(0.8f, 0.4f, 1f);
    private static readonly Color BridgeColour = new(1f, 0.25f, 0.2f);
    private static readonly Color LaneColour = new(0.85f, 0.95f, 0.85f);

    /// <summary>Builds the drawing.</summary>
    /// <param name="graph">The graph, in the city's plan frame.</param>
    /// <param name="ground">The ground, for the height of each node.</param>
    public NavDebugView(NavGraph graph, IGroundSampler ground)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(ground);
        Name = "NavDebug";
        var vertices = new System.Collections.Generic.List<Vector3>();
        var colours = new System.Collections.Generic.List<Color>();

        Vector3 Point(NavNode n) => new((float)n.X, (float)ground.HeightAt(n.X, n.Z) + LiftMeters, (float)n.Z);
        foreach (NavEdge edge in graph.Edges)
        {
            NavNode a = graph.Nodes[edge.A];
            NavNode b = graph.Nodes[edge.B];
            Color colour = edge.IsBridge ? BridgeColour : LaneColour;
            Vector3 pa = Point(a);
            Vector3 pb = Point(b);
            if (edge.IsBridge)
            {
                pa.Y += 0.3f;
                pb.Y += 0.3f;
            }

            vertices.Add(pa);
            vertices.Add(pb);
            colours.Add(colour);
            colours.Add(colour);
        }

        foreach (NavNode n in graph.Nodes)
        {
            Color colour = n.Kind switch
            {
                NavNodeKind.Door => DoorColour,
                NavNodeKind.Gate => GateColour,
                NavNodeKind.Outside => OutsideColour,
                NavNodeKind.BridgeEnd => BridgeColour,
                _ => StreetColour,
            };
            Vector3 p = Point(n);
            foreach (Vector3 offset in new[] { new Vector3(CrossMeters, 0, 0), new Vector3(0, 0, CrossMeters), new Vector3(0, CrossMeters, 0) })
            {
                vertices.Add(p - offset);
                vertices.Add(p + offset);
                colours.Add(colour);
                colours.Add(colour);
            }
        }

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices.ToArray();
        arrays[(int)Mesh.ArrayType.Color] = colours.ToArray();
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Lines, arrays);
        Mesh = mesh;
        MaterialOverride = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            VertexColorUseAsAlbedo = true,
            NoDepthTest = true,
            RenderPriority = 100,
        };
        CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
    }
}
