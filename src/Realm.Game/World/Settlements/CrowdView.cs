using System;
using System.Collections.Generic;
using Godot;
using Realm.Game.Actors;
using Realm.Gen.Settlements.Geometry;
using Realm.Gen.Settlements.Meshing;
using Realm.Gen.Settlements.Model;
using Realm.Sim.Npcs;

namespace Realm.Game.World.Settlements;

/// <summary>
/// Draws the characters of one settlement: capsules for the crowd as a whole (one MultiMesh, so all of them are one
/// draw call), and a real rigged <see cref="HumanoidRig"/> for the handful nearest the camera (CLAUDE.md section
/// 4.6, the sim LOD pillar applied to presentation: full detail up close, the cheap version everywhere else).
/// </summary>
/// <remarks>Main thread only. A child of the <see cref="CityInstance"/>, so positions are in the city's plan frame.</remarks>
public sealed partial class CrowdView : Node3D
{
    private const float Radius = 0.3f;
    private const float Height = 1.7f;
    private const double BridgeMarginMeters = 0.5;
    private const int RiggedPoolSize = 20;
    private const double RiggedRadiusMeters = 30;
    private static readonly Color LoiterColour = new(0.72f, 0.40f, 0.92f);
    private static readonly Color WalkerColour = new(0.95f, 0.65f, 0.25f);
    private static readonly Color GuardColour = new(0.85f, 0.25f, 0.25f);

    private readonly MultiMesh _multi;
    private readonly MultiMesh _noses;
    private readonly List<NpcSnapshot> _snapshots = new();
    private readonly List<(int Index, Vector3 Position)> _visible = new();
    private readonly List<HumanoidRig> _freeRigs = new(RiggedPoolSize);
    private readonly Dictionary<int, HumanoidRig> _rigged = new();
    private readonly List<(double DistanceSquared, int SnapshotIndex)> _nearCandidates = new();
    private readonly HashSet<int> _wantRigged = new();

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
        for (int i = 0; i < RiggedPoolSize; i++)
        {
            var rig = new HumanoidRig(i) { Visible = false };
            AddChild(rig);
            _freeRigs.Add(rig);
        }
    }

    /// <summary>The characters drawn in the last frame, with their positions in the city's frame (index into the crowd, position).</summary>
    public IReadOnlyList<(int Index, Vector3 Position)> Shown => _visible;

    /// <summary>Writes the characters that are outside: a rigged humanoid for the few nearest the viewer, capsules (one draw call) for the rest.</summary>
    /// <param name="crowd">The crowd.</param>
    /// <param name="nowMinutes">The game time in minutes.</param>
    /// <param name="ground">The ground, for each character's height.</param>
    /// <param name="bridges">The city's bridges (a character on one stands on the roadway).</param>
    /// <param name="deltaSeconds">Real seconds since the last frame, for the pace at which people ease out of each other's way, and the rigged humanoids' walk cycle.</param>
    /// <param name="viewer">Where the camera (or player) is, in the city's plan frame — decides who gets a rigged humanoid instead of a capsule.</param>
    public void Show(Crowd crowd, double nowMinutes, double deltaSeconds, IGroundSampler ground, IReadOnlyList<CityBridge> bridges, Vector3 viewer)
    {
        crowd.CollectVisible(nowMinutes, deltaSeconds, _snapshots);
        AssignRiggedPool(viewer);

        int capsuleShown = 0;
        _visible.Clear();
        foreach (NpcSnapshot s in _snapshots)
        {
            int i = s.Id;
            double y = StandingHeight(s.X, s.Z, ground, bridges);
            var position = new Vector3((float)s.X, (float)(y + (Height / 2)), (float)s.Z);
            Color colour = RoleColour(s.Role, s.Id);

            if (_rigged.TryGetValue(i, out HumanoidRig? rig))
            {
                rig.Position = new Vector3((float)s.X, (float)y, (float)s.Z);
                rig.SetColour(colour);
                rig.Animate(deltaSeconds, s.Walking, (float)s.Heading);
            }
            else
            {
                var facing = new Basis(Vector3.Up, (float)s.Heading);
                _multi.SetInstanceTransform(capsuleShown, new Transform3D(facing, position));
                // A nose on the front (the capsule's minus-Z side) shows which way the character faces.
                _noses.SetInstanceTransform(capsuleShown, new Transform3D(facing, position + (facing * new Vector3(0, 0.45f, -0.34f))));
                _multi.SetInstanceColor(capsuleShown, colour);
                _noses.SetInstanceColor(capsuleShown, s.Alert ? new Color(1f, 0.9f, 0.1f) : s.Noticing ? new Color(1f, 1f, 1f) : new Color(0.15f, 0.15f, 0.15f));
                capsuleShown++;
            }

            _visible.Add((i, position));
        }

        _multi.VisibleInstanceCount = capsuleShown;
        _noses.VisibleInstanceCount = capsuleShown;
    }

    private static Color RoleColour(NpcRole role, int id)
    {
        float tint = 0.85f + (((id * 37) % 100) / 100f * 0.3f);
        Color colour = role switch
        {
            NpcRole.Guard => GuardColour,
            NpcRole.Walker => WalkerColour,
            _ => LoiterColour,
        };
        return new Color(colour.R * tint, colour.G * tint, colour.B * tint);
    }

    // Keeps a rigged humanoid on each of the up to RiggedPoolSize characters nearest the viewer, within RiggedRadiusMeters; characters
    // that fall out of that set give their rig back to the pool, and a stable mapping (by character id) avoids swapping rigs between
    // characters every frame as distances jitter slightly.
    private void AssignRiggedPool(Vector3 viewer)
    {
        _nearCandidates.Clear();
        for (int index = 0; index < _snapshots.Count; index++)
        {
            NpcSnapshot s = _snapshots[index];
            double dx = s.X - viewer.X;
            double dz = s.Z - viewer.Z;
            double distanceSquared = (dx * dx) + (dz * dz);
            if (distanceSquared <= RiggedRadiusMeters * RiggedRadiusMeters)
            {
                _nearCandidates.Add((distanceSquared, index));
            }
        }

        _nearCandidates.Sort((a, b) => a.DistanceSquared.CompareTo(b.DistanceSquared));
        _wantRigged.Clear();
        for (int k = 0; k < Math.Min(RiggedPoolSize, _nearCandidates.Count); k++)
        {
            _wantRigged.Add(_snapshots[_nearCandidates[k].SnapshotIndex].Id);
        }

        var releasing = new List<int>();
        foreach (KeyValuePair<int, HumanoidRig> entry in _rigged)
        {
            if (!_wantRigged.Contains(entry.Key))
            {
                entry.Value.Visible = false;
                _freeRigs.Add(entry.Value);
                releasing.Add(entry.Key);
            }
        }

        foreach (int id in releasing)
        {
            _rigged.Remove(id);
        }

        foreach (int id in _wantRigged)
        {
            if (!_rigged.ContainsKey(id) && _freeRigs.Count > 0)
            {
                HumanoidRig rig = _freeRigs[^1];
                _freeRigs.RemoveAt(_freeRigs.Count - 1);
                rig.Visible = true;
                _rigged[id] = rig;
            }
        }
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
