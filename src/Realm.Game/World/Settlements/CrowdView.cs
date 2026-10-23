using System;
using System.Collections.Generic;
using Godot;
using Realm.Gen.Settlements.Geometry;
using Realm.Gen.Settlements.Meshing;
using Realm.Gen.Settlements.Model;
using Realm.Sim.Npcs;

namespace Realm.Game.World.Settlements;

/// <summary>
/// Draws the characters of one settlement as capsules (a placeholder until real figures exist): one MultiMesh, so all of them are one draw
/// call, with the transforms of the characters that are outside written each frame and the rest hidden.
/// </summary>
/// <remarks>Main thread only. A child of the <see cref="CityInstance"/>, so positions are in the city's plan frame.</remarks>
public sealed partial class CrowdView : Node3D
{
    private const float Radius = 0.3f;
    private const float Height = 1.7f;
    private const double BridgeMarginMeters = 0.5;
    private static readonly Color LoiterColour = new(0.72f, 0.40f, 0.92f);
    private static readonly Color WalkerColour = new(0.95f, 0.65f, 0.25f);
    private static readonly Color GuardColour = new(0.85f, 0.25f, 0.25f);

    private readonly MultiMesh _multi;
    private readonly MultiMesh _noses;
    private readonly List<NpcSnapshot> _snapshots = new();
    private readonly List<(int Index, Vector3 Position)> _visible = new();

    /// <summary>Creates the view.</summary>
    /// <param name="capacity">Number of characters it can show.</param>
    public CrowdView(int capacity)
    {
        Name = "Crowd";
        _multi = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            Mesh = new CapsuleMesh { Radius = Radius, Height = Height, RadialSegments = 8, Rings = 2 },
            InstanceCount = capacity,
            VisibleInstanceCount = 0,
        };
        _noses = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            Mesh = new BoxMesh { Size = new Vector3(0.16f, 0.16f, 0.34f) },
            InstanceCount = capacity,
            VisibleInstanceCount = 0,
        };
        AddChild(new MultiMeshInstance3D
        {
            Multimesh = _noses,
            MaterialOverride = new StandardMaterial3D { VertexColorUseAsAlbedo = true, Roughness = 1f },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
        AddChild(new MultiMeshInstance3D
        {
            Multimesh = _multi,
            MaterialOverride = new StandardMaterial3D { VertexColorUseAsAlbedo = true, Roughness = 1f },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
    }

    /// <summary>The characters drawn in the last frame, with their positions in the city's frame (index into the crowd, position).</summary>
    public IReadOnlyList<(int Index, Vector3 Position)> Shown => _visible;

    /// <summary>Writes the characters that are outside into the MultiMesh.</summary>
    /// <param name="crowd">The crowd.</param>
    /// <param name="nowMinutes">The game time in minutes.</param>
    /// <param name="ground">The ground, for each character's height.</param>
    /// <param name="bridges">The city's bridges (a character on one stands on the roadway).</param>
    /// <param name="deltaSeconds">Real seconds since the last frame, for the pace at which people ease out of each other's way.</param>
    public void Show(Crowd crowd, double nowMinutes, double deltaSeconds, IGroundSampler ground, IReadOnlyList<CityBridge> bridges)
    {
        crowd.CollectVisible(nowMinutes, deltaSeconds, _snapshots);
        int shown = 0;
        _visible.Clear();
        foreach (NpcSnapshot s in _snapshots)
        {
            int i = s.Id;
            double y = StandingHeight(s.X, s.Z, ground, bridges);
            var position = new Vector3((float)s.X, (float)(y + (Height / 2)), (float)s.Z);
            var facing = new Basis(Vector3.Up, (float)s.Heading);
            _multi.SetInstanceTransform(shown, new Transform3D(facing, position));
            // A nose on the front (the capsule's minus-Z side) shows which way the character faces.
            _noses.SetInstanceTransform(shown, new Transform3D(facing, position + (facing * new Vector3(0, 0.45f, -0.34f))));
            _visible.Add((i, position));
            float tint = 0.85f + (((s.Id * 37) % 100) / 100f * 0.3f);
            Color colour = s.Role switch
            {
                NpcRole.Guard => GuardColour,
                NpcRole.Walker => WalkerColour,
                _ => LoiterColour,
            };
            _multi.SetInstanceColor(shown, new Color(colour.R * tint, colour.G * tint, colour.B * tint));
            _noses.SetInstanceColor(shown, s.Alert ? new Color(1f, 0.9f, 0.1f) : s.Noticing ? new Color(1f, 1f, 1f) : new Color(0.15f, 0.15f, 0.15f));
            shown++;
        }

        _multi.VisibleInstanceCount = shown;
        _noses.VisibleInstanceCount = shown;
    }

    // The ground, or the roadway of a bridge when the point is on one.
    private static double StandingHeight(double x, double z, IGroundSampler ground, IReadOnlyList<CityBridge> bridges)
    {
        foreach (CityBridge b in bridges)
        {
            Vec2 d = b.To - b.From;
            double length = d.Length;
            if (length < 1e-6)
            {
                continue;
            }

            Vec2 along = d * (1 / length);
            Vec2 offset = new Vec2(x, z) - b.From;
            double t = Vec2.Dot(offset, along) / length;
            double across = Math.Abs(Vec2.Cross(along, offset));
            if (t >= 0 && t <= 1 && across <= (b.WidthMeters / 2) + BridgeMarginMeters)
            {
                return ArchBridgeMesher.RoadwayHeight(b, ground, t);
            }
        }

        return ground.HeightAt(x, z);
    }
}
