using System;
using System.Collections.Generic;
using Godot;
using Realm.Game.Bridge;
using Realm.Gen.Props;

namespace Realm.Game.World;

/// <summary>
/// A scale-reference strip: a measure tape from a start point to the edge of the world with a house and three trees
/// beside every 100 m (<see cref="StripLayout"/>). Segments are built as the terrain under them loads and freed when
/// the player is far away, so the whole 13 km strip costs a few hundred metres' worth of nodes at a time.
/// </summary>
public sealed partial class ReferenceStrip : Node3D
{
    private const int SegmentMeters = (int)StripLayout.SegmentMeters;
    private const float BuildRadiusMeters = 280f;
    private const float FreeRadiusMeters = 420f;
    private const float WaterLevelMeters = 0.3f; // houses are skipped where the ground is at or below this
    private const int MaxBuildsPerFrame = 1;
    private const int MeshCacheLimit = 200;

    private readonly TerrainBridge _bridge;
    private readonly Vector3 _start;
    private readonly Vector3 _direction;
    private readonly Vector3 _left;
    private readonly ulong _seed;
    private readonly Dictionary<int, Node3D> _built = new();
    private readonly Dictionary<string, ArrayMesh> _meshes = new();
    private readonly List<int> _wanted = new();
    private readonly StandardMaterial3D _material = new() { VertexColorUseAsAlbedo = true, Roughness = 1f };
    private int _builtTotal;

    /// <summary>Creates a strip.</summary>
    /// <param name="bridge">Terrain to sample.</param>
    /// <param name="start">Where the tape starts, in world metres.</param>
    /// <param name="direction">Horizontal unit direction.</param>
    /// <param name="lengthMeters">Length of the strip; the last partial segment is dropped.</param>
    /// <param name="seed">World seed for the layout.</param>
    public ReferenceStrip(TerrainBridge bridge, Vector3 start, Vector3 direction, float lengthMeters, ulong seed)
    {
        _bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
        _start = start;
        _direction = direction;
        _left = new Vector3(direction.Z, 0, -direction.X);
        _seed = seed;
        SegmentCount = Math.Max(0, (int)(lengthMeters / SegmentMeters));
    }

    /// <summary>Number of 100 m segments the strip is made of.</summary>
    public int SegmentCount { get; }

    /// <summary>Segments currently built.</summary>
    public int BuiltCount => _built.Count;

    /// <summary>Segments built over the whole run (a stress-test figure).</summary>
    public int BuiltTotal => _builtTotal;

    /// <summary>Distance from a start point to the edge of a square world along a direction.</summary>
    /// <param name="start">Start in world metres.</param>
    /// <param name="direction">Horizontal unit direction.</param>
    /// <param name="minWorld">Lowest world coordinate on both axes.</param>
    /// <param name="maxWorld">Highest world coordinate on both axes.</param>
    /// <returns>Metres to the edge.</returns>
    public static float DistanceToEdge(Vector3 start, Vector3 direction, float minWorld, float maxWorld)
    {
        float best = float.MaxValue;
        foreach ((float s, float d) in new[] { (start.X, direction.X), (start.Z, direction.Z) })
        {
            if (Mathf.Abs(d) < 1e-6f)
            {
                continue;
            }

            best = Math.Min(best, ((d > 0 ? maxWorld : minWorld) - s) / d);
        }

        return best;
    }

    /// <summary>How far along the strip a position is (negative before the start).</summary>
    /// <param name="position">World position in metres.</param>
    /// <returns>Metres along the strip.</returns>
    public float AlongMeters(Vector3 position) => (position - _start).Dot(_direction);

    /// <summary>Builds segments near the player and frees far ones. Call once per frame from the main thread.</summary>
    /// <param name="player">Player position in world metres.</param>
    public void Update(Vector3 player)
    {
        float along = (player - _start).Dot(_direction);
        int first = Math.Max(0, (int)Math.Floor((along - BuildRadiusMeters) / SegmentMeters));
        int last = Math.Min(SegmentCount - 1, (int)Math.Floor((along + BuildRadiusMeters) / SegmentMeters));

        _wanted.Clear();
        for (int i = first; i <= last; i++)
        {
            if (!_built.ContainsKey(i) && SegmentDistance(i, player) <= BuildRadiusMeters)
            {
                _wanted.Add(i);
            }
        }

        _wanted.Sort((a, b) => SegmentDistance(a, player).CompareTo(SegmentDistance(b, player)));
        int builds = 0;
        foreach (int index in _wanted)
        {
            if (builds >= MaxBuildsPerFrame)
            {
                break;
            }

            if (Build(index))
            {
                builds++;
            }
        }

        List<int>? stale = null;
        foreach (int index in _built.Keys)
        {
            if (SegmentDistance(index, player) > FreeRadiusMeters)
            {
                (stale ??= new List<int>()).Add(index);
            }
        }

        if (stale is not null)
        {
            foreach (int index in stale)
            {
                _built[index].QueueFree();
                _built.Remove(index);
            }
        }
    }

    private float SegmentDistance(int index, Vector3 player)
    {
        Vector3 centre = _start + (_direction * ((index * SegmentMeters) + (SegmentMeters / 2f)));
        return new Vector2(centre.X - player.X, centre.Z - player.Z).Length();
    }

    private bool Build(int index)
    {
        Vector3 origin = _start + (_direction * (index * SegmentMeters));
        MeasureTape? tape = MeasureTape.Lay(_bridge, origin, _direction, SegmentMeters, index * SegmentMeters);
        if (tape is null)
        {
            return false; // terrain not loaded under this segment yet
        }

        var node = new Node3D();
        node.AddChild(tape);
        foreach (StripPlacement p in StripLayout.ForSegment(_seed, index))
        {
            Vector3 at = origin + (_direction * p.AlongMeters) + (_left * p.SideMeters);
            PlaceProp(node, p, at);
        }

        AddChild(node);
        _built[index] = node;
        _builtTotal++;
        return true;
    }

    private void PlaceProp(Node3D parent, StripPlacement placement, Vector3 at)
    {
        bool house = placement.Kind is StripPropKind.Cottage or StripPropKind.TownHouse;
        float half = house ? 3.5f : 0.5f;
        float ground = float.MinValue;
        foreach ((float dx, float dz) in new[] { (-1f, -1f), (1f, -1f), (-1f, 1f), (1f, 1f), (0f, 0f) })
        {
            float h = _bridge.HeightAt(new Vector3(at.X + (dx * half), 0, at.Z + (dz * half)));
            if (!float.IsNaN(h))
            {
                ground = Math.Max(ground, h);
            }
        }

        if (ground == float.MinValue || (house && ground <= WaterLevelMeters) || ground <= -WaterLevelMeters)
        {
            return; // not loaded, or under the sea
        }

        Vector3 towardTape = placement.SideMeters > 0 ? -_left : _left;
        parent.AddChild(new MeshInstance3D
        {
            Mesh = MeshFor(placement),
            MaterialOverride = _material,
            Transform = new Transform3D(Basis.LookingAt(-towardTape, Vector3.Up), new Vector3(at.X, ground, at.Z)),
        });
    }

    private ArrayMesh MeshFor(StripPlacement p)
    {
        string key = $"{p.Kind}:{p.HeightMeters}";
        if (!_meshes.TryGetValue(key, out ArrayMesh? mesh))
        {
            PropMesh model = p.Kind switch
            {
                StripPropKind.Cottage => HouseModels.Cottage(),
                StripPropKind.TownHouse => HouseModels.TownHouse(),
                StripPropKind.Pine => TreeModels.Build(TreeKind.Pine, p.HeightMeters),
                StripPropKind.Oak => TreeModels.Build(TreeKind.Oak, p.HeightMeters),
                StripPropKind.Poplar => TreeModels.Build(TreeKind.Poplar, p.HeightMeters),
                _ => throw new ArgumentOutOfRangeException(nameof(p), p.Kind, "Unknown prop kind."),
            };
            mesh = PropMeshConverter.ToArrayMesh(model);
            if (_meshes.Count >= MeshCacheLimit)
            {
                _meshes.Clear();
            }

            _meshes[key] = mesh;
        }

        return mesh;
    }
}
