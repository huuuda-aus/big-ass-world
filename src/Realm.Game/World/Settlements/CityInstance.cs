using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using Realm.Game.Bridge;
using Realm.Gen.Settlements.Geometry;
using Realm.Gen.Settlements.Meshing;
using Realm.Gen.Settlements.Placement;

namespace Realm.Game.World.Settlements;

/// <summary>
/// A city standing in the world. Every house is a cheap closed mesh; houses within <see cref="NearBuildMeters"/> of the player are
/// swapped for a detailed one with real doors, windows, rooms, stairs and collision, and swapped back beyond
/// <see cref="NearDropMeters"/>. Nodes are added a few at a time so no frame is spent creating a whole city.
/// </summary>
/// <remarks>
/// The node sits at the settlement's centre at its ground height and is rotated so that the city's local frame (plan x, height,
/// plan y) lines up with the world. Main thread only; the heavy work happens in <see cref="CityBuild"/> and in near-building tasks.
/// </remarks>
public sealed partial class CityInstance : Node3D
{
    /// <summary>Houses closer than this to the player get their detailed interior built, in metres.</summary>
    public const double NearBuildMeters = 35;

    /// <summary>Detailed houses farther than this from the player are dropped again, in metres.</summary>
    public const double NearDropMeters = 60;

    private const int FarNodesPerFrame = 40;
    private const int MaxNearTasks = 2;
    private const double NearScanSeconds = 0.2;

    private readonly CityBuild _build;
    private readonly Settlement _settlement;
    private readonly MeshInstance3D?[] _far;
    private readonly Node3D?[] _near;
    private readonly bool[] _nearBuilding;
    private readonly ConcurrentQueue<NearResult> _finished = new();
    private readonly List<int> _wanted = new();
    private int _nextFar;
    private int _tasks;
    private double _scanTimer;
    private bool _attached;

    private sealed record NearResult(int Index, ArrayMesh Visual, ConcavePolygonShape3D Shape);

    /// <summary>Creates the instance from a finished build; call <see cref="Attach"/> next frame loop via <see cref="Process"/>.</summary>
    /// <param name="build">The worker-built city.</param>
    public CityInstance(CityBuild build)
    {
        _build = build ?? throw new ArgumentNullException(nameof(build));
        _settlement = build.Settlement;
        _far = new MeshInstance3D?[build.Buildings.Length];
        _near = new Node3D?[build.Buildings.Length];
        _nearBuilding = new bool[build.Buildings.Length];
        Name = _settlement.Id;
        Position = new Vector3((float)_settlement.X, (float)_settlement.GroundHeightMeters, (float)_settlement.Z);
        // Plan rotation by theta counter-clockwise (plan x toward plan y) is a rotation about the vertical axis by -theta.
        Rotation = new Vector3(0, (float)-_settlement.RotationRadians, 0);
    }

    /// <summary>Settlement this city shows.</summary>
    public Settlement Settlement => _settlement;

    /// <summary>Houses currently shown as detailed buildings.</summary>
    public int NearCount { get; private set; }

    /// <summary>Houses whose far mesh node exists.</summary>
    public int FarCount => _nextFar;

    /// <summary>Total houses in the city.</summary>
    public int BuildingCount => _far.Length;

    /// <summary>Whether the ground, wall and landmark have been added.</summary>
    public bool Attached => _attached;

    /// <summary>Does one frame of work: attaches parts, adds far nodes, scans for near houses and finishes near builds.</summary>
    /// <param name="playerWorld">The player's world position.</param>
    /// <param name="delta">Seconds since the last call.</param>
    public void Process(Vector3 playerWorld, double delta)
    {
        if (!_attached)
        {
            Attach();
            return;
        }

        for (int added = 0; added < FarNodesPerFrame && _nextFar < _far.Length; added++)
        {
            AddFar(_nextFar++);
        }

        while (_finished.TryDequeue(out NearResult? result))
        {
            Complete(result);
        }

        _scanTimer += delta;
        if (_scanTimer >= NearScanSeconds)
        {
            _scanTimer = 0;
            Scan(playerWorld);
        }
    }

    private void Attach()
    {
        if (_build.Wall is not null && _build.WallShape is not null)
        {
            AddChild(new MeshInstance3D { Mesh = _build.Wall, MaterialOverride = CityMaterials.Solid, Name = "Wall" });
            var body = new StaticBody3D { Name = "WallBody" };
            body.AddChild(new CollisionShape3D { Shape = _build.WallShape });
            AddChild(body);
        }

        foreach (CityBridgeMesh bridge in _build.Bridges)
        {
            AddChild(new MeshInstance3D { Mesh = bridge.Mesh, MaterialOverride = CityMaterials.Solid, Name = "Bridge" });
            var bridgeBody = new StaticBody3D { Name = "BridgeBody" };
            bridgeBody.AddChild(new CollisionShape3D { Shape = bridge.Shape });
            AddChild(bridgeBody);
        }

        if (_build.Landmark is { } landmark)
        {
            // The landmark mesh has its base at 0; the root puts it on the ground at the square's height.
            var root = new Node3D { Name = "LandmarkRoot", Position = new Vector3(0, (float)_build.LandmarkBaseY, 0) };
            AddChild(root);
            root.AddChild(new MeshInstance3D { Mesh = landmark.Solid, MaterialOverride = CityMaterials.Solid, Name = "Landmark" });
            var body = new StaticBody3D { Name = "LandmarkBody" };
            body.AddChild(new CollisionShape3D { Shape = landmark.SolidShape });
            root.AddChild(body);
            if (landmark.Water is not null)
            {
                root.AddChild(new MeshInstance3D { Mesh = landmark.Water, MaterialOverride = CityMaterials.Water, Name = "Water", CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
            }

            if (landmark.Flames is not null)
            {
                root.AddChild(new MeshInstance3D { Mesh = landmark.Flames, MaterialOverride = CityMaterials.Fire, Name = "Flames", CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
            }

            if (landmark.FireLight is { } at)
            {
                root.AddChild(new OmniLight3D { Position = at, LightColor = new Color(1f, 0.55f, 0.23f), LightEnergy = 3f, OmniRange = 40f, Name = "FireLight" });
            }

            if (landmark.JetOrigin is { } jet)
            {
                root.AddChild(BuildJets(jet, (float)_build.City.Landmark!.Radius));
            }
        }

        _attached = true;
    }

    // Water thrown from the spout in arcs that fall into the basin.
    private static CpuParticles3D BuildJets(Vector3 origin, float radius) => new()
    {
        Name = "Jets",
        Position = origin,
        Amount = 160,
        Lifetime = 1.5,
        Direction = Vector3.Up,
        Spread = 38,
        InitialVelocityMin = radius * 0.5f,
        InitialVelocityMax = radius * 0.7f,
        Gravity = new Vector3(0, -7, 0),
        Mesh = new SphereMesh { Radius = 0.08f, Height = 0.16f, RadialSegments = 4, Rings = 2, Material = new StandardMaterial3D { AlbedoColor = new Color(0.87f, 0.94f, 1f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded } },
    };

    private void AddFar(int index)
    {
        // Meshes are built with the floor at 0; the node sits at the floor's height on the ground.
        var node = new MeshInstance3D
        {
            Mesh = _build.Buildings[index].Far,
            MaterialOverride = CityMaterials.Solid,
            Name = $"House{_build.Buildings[index].Building.Id}",
            Position = new Vector3(0, (float)_build.Buildings[index].Site.BaseY, 0),
        };
        node.Visible = !_nearBuilding[index] || _near[index] is null;
        AddChild(node);
        _far[index] = node;
    }

    // Finds houses near the player that need a detailed build, and detailed houses that are now far away.
    private void Scan(Vector3 playerWorld)
    {
        Vec2 p = _settlement.ToLocal(playerWorld.X, playerWorld.Z);
        _wanted.Clear();
        for (int i = 0; i < _far.Length; i++)
        {
            CityBuilding b = _build.Buildings[i];
            double d = Math.Sqrt(((b.CentreX - p.X) * (b.CentreX - p.X)) + ((b.CentreY - p.Y) * (b.CentreY - p.Y)));
            if (_near[i] is not null && d > NearDropMeters)
            {
                DropNear(i);
            }
            else if (_near[i] is null && !_nearBuilding[i] && d < NearBuildMeters && _far[i] is not null)
            {
                _wanted.Add(i);
            }
        }

        _wanted.Sort((a, c) => Distance(a, p).CompareTo(Distance(c, p)));
        foreach (int index in _wanted)
        {
            if (_tasks >= MaxNearTasks)
            {
                break;
            }

            StartNear(index);
        }
    }

    private double Distance(int index, Vec2 p)
    {
        CityBuilding b = _build.Buildings[index];
        return ((b.CentreX - p.X) * (b.CentreX - p.X)) + ((b.CentreY - p.Y) * (b.CentreY - p.Y));
    }

    private void StartNear(int index)
    {
        _nearBuilding[index] = true;
        _tasks++;
        CityBuilding b = _build.Buildings[index];
        Task.Run(() =>
        {
            BuildingMeshes meshes = BuildingMesher.Near(b.Building, b.Interior, b.Site);
            _finished.Enqueue(new NearResult(index, PropMeshConverter.ToArrayMesh(meshes.Visual), PropMeshConverter.ToCollisionShape(meshes.Collision)));
        });
    }

    private void Complete(NearResult result)
    {
        _tasks--;
        if (!_nearBuilding[result.Index])
        {
            return; // dropped while it was being built
        }

        var root = new Node3D
        {
            Name = $"Near{_build.Buildings[result.Index].Building.Id}",
            Position = new Vector3(0, (float)_build.Buildings[result.Index].Site.BaseY, 0),
        };
        root.AddChild(new MeshInstance3D { Mesh = result.Visual, MaterialOverride = CityMaterials.Solid });
        var body = new StaticBody3D();
        body.AddChild(new CollisionShape3D { Shape = result.Shape });
        root.AddChild(body);
        AddInteriorLights(root, _build.Buildings[result.Index]);
        AddChild(root);
        _near[result.Index] = root;
        NearCount++;
        if (_far[result.Index] is { } far)
        {
            far.Visible = false;
        }
    }

    // One warm lamp per storey in the middle of the house. Lights do not cast shadows, so the range is kept short enough
    // that a lamp lights its own house and little of the street.
    private static void AddInteriorLights(Node3D root, CityBuilding b)
    {
        double reach = 0;
        foreach (Vec2 corner in b.Building.Footprint.Points)
        {
            reach = Math.Max(reach, Math.Sqrt(((corner.X - b.CentreX) * (corner.X - b.CentreX)) + ((corner.Y - b.CentreY) * (corner.Y - b.CentreY))));
        }

        for (int level = 0; level < b.Interior.Floors.Count; level++)
        {
            root.AddChild(new OmniLight3D
            {
                Position = new Vector3((float)b.CentreX, (float)((level * b.Interior.FloorHeight) + (b.Interior.FloorHeight * 0.8)), (float)b.CentreY),
                LightColor = new Color(1f, 0.86f, 0.64f),
                LightEnergy = 1.6f,
                OmniRange = (float)Math.Max(5, reach * 1.1),
                ShadowEnabled = false,
            });
        }
    }

    private void DropNear(int index)
    {
        _near[index]?.QueueFree();
        _near[index] = null;
        _nearBuilding[index] = false;
        NearCount--;
        if (_far[index] is { } far)
        {
            far.Visible = true;
        }
    }
}
