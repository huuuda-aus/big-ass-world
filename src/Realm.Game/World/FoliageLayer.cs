using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using Realm.Game.Bridge;
using Realm.Gen.Foliage;
using Realm.Gen.Props;

namespace Realm.Game.World;

/// <summary>
/// Trees, bushes and grass. Trees and bushes come from <c>foliage.bin</c> and are drawn per 256 m chunk with one MultiMesh per species and
/// detail level (a 3D model near, a simpler one far, switched by the engine's visibility ranges). Grass tufts are scattered around the
/// camera from the baked density map. A handful of pooled colliders make nearby trunks solid.
/// </summary>
/// <remarks>Main thread only. Chunks are built and freed a few per frame; nothing is allocated per frame in steady state.</remarks>
public sealed partial class FoliageLayer : Node3D
{
    private const double LoadRadiusMeters = 850;
    private const double UnloadRadiusMeters = 1000;
    private const float NearEndMeters = 170;
    private const float FarBeginMeters = 150;
    private const float FarEndMeters = 950;
    private const double GrassRadiusMeters = 64;
    private const double GrassUnloadMeters = 90;
    private const float GrassEndMeters = 70;
    private const int ChunkBuildsPerFrame = 2;
    private const int GrassBuildsPerFrame = 2;
    private const double RefreshMeters = 24;
    private const int ColliderPool = 48;
    private const double ColliderRadiusMeters = 16;
    private const double ColliderPeriodSeconds = 0.2;
    private const float ColliderHeightMeters = 4;
    private const double TintSpread = 0.14;

    private readonly FoliageData _data;
    private readonly ulong _seed;
    private readonly TerrainBridge _terrain;
    private readonly (int First, int Count)[] _chunks;
    private readonly Dictionary<int, List<Node>> _loaded = new();
    private readonly Dictionary<int, List<Node>> _grass = new();
    private readonly Mesh[][] _meshes; // per species: near, far
    private readonly Material[] _materials; // per species
    private readonly List<StaticBody3D> _colliders = new();
    private readonly List<int> _wanted = new();
    private Vector3 _lastRefresh = new(float.MaxValue, 0, float.MaxValue);
    private double _colliderClock;

    private FoliageLayer(FoliageData data, ulong seed, TerrainBridge terrain)
    {
        Name = "Foliage";
        _data = data;
        _seed = seed;
        _terrain = terrain;
        _chunks = IndexChunks(data);
        _meshes = new Mesh[data.Species.Count][];
        _materials = new Material[data.Species.Count];
        for (int i = 0; i < data.Species.Count; i++)
        {
            FoliageSpecies s = data.Species[i];
            string style = FoliageMeshes.StyleFor(s.Kind, s.Tags);
            _meshes[i] = new[]
            {
                PropMeshConverter.ToArrayMesh(FoliageMeshes.Build(style, far: false)),
                PropMeshConverter.ToArrayMesh(FoliageMeshes.Build(style, far: true)),
            };
            _materials[i] = new StandardMaterial3D
            {
                VertexColorUseAsAlbedo = true,
                VertexColorIsSrgb = true,
                Roughness = 1f,
                CullMode = s.Kind == "grass" ? BaseMaterial3D.CullModeEnum.Disabled : BaseMaterial3D.CullModeEnum.Back,
            };
        }
    }

    /// <summary>Number of tree and bush chunks currently built.</summary>
    public int LoadedChunks => _loaded.Count;

    /// <summary>Number of grass cells currently built.</summary>
    public int LoadedGrassCells => _grass.Count;

    /// <summary>Reads <c>foliage.bin</c> next to the terrain.</summary>
    /// <param name="directory">The baked terrain directory.</param>
    /// <param name="seed">World seed (for the grass scatter).</param>
    /// <param name="terrain">The terrain, for the ground height under grass.</param>
    /// <returns>The layer, or null when no foliage file was baked.</returns>
    public static FoliageLayer? Open(string directory, ulong seed, TerrainBridge terrain)
    {
        string path = Path.Combine(directory, FoliageFile.FileName);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return new FoliageLayer(FoliageFile.FromBytes(File.ReadAllBytes(path)), seed, terrain);
        }
        catch (InvalidDataException e)
        {
            GD.PushError($"[foliage] {e.Message}");
            return null;
        }
    }

    /// <summary>Loads and frees chunks around the camera and moves the trunk colliders; call once per frame.</summary>
    /// <param name="camera">Camera position in metres.</param>
    /// <param name="delta">Seconds since the last frame.</param>
    public void Update(Vector3 camera, double delta)
    {
        if (new Vector2(camera.X - _lastRefresh.X, camera.Z - _lastRefresh.Z).Length() > RefreshMeters)
        {
            _lastRefresh = camera;
            Refresh(camera);
        }

        BuildPending(camera);
        _colliderClock += delta;
        if (_colliderClock >= ColliderPeriodSeconds)
        {
            _colliderClock = 0;
            MoveColliders(camera);
        }
    }

    private void Refresh(Vector3 camera)
    {
        int side = _data.ChunksPerSide;
        double half = _data.WorldSizeMeters / 2.0;
        int chunk = _data.ChunkMeters;
        int reach = (int)Math.Ceiling(LoadRadiusMeters / chunk) + 1;
        int centreX = (int)Math.Floor((camera.X + half) / chunk);
        int centreZ = (int)Math.Floor((camera.Z + half) / chunk);
        _wanted.Clear();
        for (int cz = Math.Max(0, centreZ - reach); cz <= Math.Min(side - 1, centreZ + reach); cz++)
        {
            for (int cx = Math.Max(0, centreX - reach); cx <= Math.Min(side - 1, centreX + reach); cx++)
            {
                int index = (cz * side) + cx;
                if (_chunks[index].Count > 0 && !_loaded.ContainsKey(index) && ChunkDistance(camera, cx, cz) <= LoadRadiusMeters)
                {
                    _wanted.Add(index);
                }
            }
        }

        _wanted.Sort((a, b) => ChunkDistance(camera, a % side, a / side).CompareTo(ChunkDistance(camera, b % side, b / side)));

        var gone = new List<int>();
        foreach (int index in _loaded.Keys)
        {
            if (ChunkDistance(camera, index % side, index / side) > UnloadRadiusMeters)
            {
                gone.Add(index);
            }
        }

        foreach (int index in gone)
        {
            Free(_loaded, index);
        }

        var goneGrass = new List<int>();
        foreach (int index in _grass.Keys)
        {
            int gs = _data.GrassSide;
            if (GrassDistance(camera, index % gs, index / gs) > GrassUnloadMeters)
            {
                goneGrass.Add(index);
            }
        }

        foreach (int index in goneGrass)
        {
            Free(_grass, index);
        }
    }

    private void BuildPending(Vector3 camera)
    {
        for (int built = 0; built < ChunkBuildsPerFrame && _wanted.Count > 0; built++)
        {
            int index = _wanted[0];
            _wanted.RemoveAt(0);
            if (!_loaded.ContainsKey(index))
            {
                _loaded[index] = BuildChunk(index);
            }
        }

        BuildGrass(camera);
    }

    private List<Node> BuildChunk(int index)
    {
        var nodes = new List<Node>();
        (int first, int count) = _chunks[index];
        int start = first;
        while (start < first + count)
        {
            int species = _data.Instances[start].Species;
            int end = start;
            while (end < first + count && _data.Instances[end].Species == species)
            {
                end++;
            }

            AddSpecies(nodes, species, start, end - start);
            start = end;
        }

        return nodes;
    }

    private void AddSpecies(List<Node> nodes, int species, int start, int count)
    {
        for (int level = 0; level < 2; level++)
        {
            var multi = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                UseColors = true,
                Mesh = _meshes[species][level],
                InstanceCount = count,
            };
            for (int i = 0; i < count; i++)
            {
                FoliageInstance inst = _data.Instances[start + i];
                var basis = new Basis(Vector3.Up, (float)inst.YawRadians).Scaled(Vector3.One * (float)inst.Scale);
                multi.SetInstanceTransform(i, new Transform3D(basis, new Vector3((float)inst.X, (float)inst.Y, (float)inst.Z)));
                float tint = (float)(1 - TintSpread / 2 + (((inst.YawRadians * 7.3) % 1.0 + 1.0) % 1.0 * TintSpread));
                multi.SetInstanceColor(i, new Color(tint, tint, tint));
            }

            var node = new MultiMeshInstance3D
            {
                Multimesh = multi,
                MaterialOverride = _materials[species],
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                VisibilityRangeEnd = level == 0 ? NearEndMeters : FarEndMeters,
                VisibilityRangeBegin = level == 0 ? 0 : FarBeginMeters,
            };
            AddChild(node);
            nodes.Add(node);
        }
    }

    private void BuildGrass(Vector3 camera)
    {
        int gs = _data.GrassSide;
        int cell = _data.GrassCellMeters;
        double half = _data.WorldSizeMeters / 2.0;
        int reach = (int)Math.Ceiling(GrassRadiusMeters / cell) + 1;
        int centreX = (int)Math.Floor((camera.X + half) / cell);
        int centreZ = (int)Math.Floor((camera.Z + half) / cell);
        int built = 0;
        for (int cz = Math.Max(0, centreZ - reach); cz <= Math.Min(gs - 1, centreZ + reach) && built < GrassBuildsPerFrame; cz++)
        {
            for (int cx = Math.Max(0, centreX - reach); cx <= Math.Min(gs - 1, centreX + reach) && built < GrassBuildsPerFrame; cx++)
            {
                int index = (cz * gs) + cx;
                if (_grass.ContainsKey(index) || _data.GrassTufts[index] == 0 || GrassDistance(camera, cx, cz) > GrassRadiusMeters)
                {
                    continue;
                }

                List<Node>? nodes = TryBuildGrass(index, cx, cz);
                if (nodes is not null)
                {
                    _grass[index] = nodes;
                    built++;
                }
            }
        }
    }

    private List<Node>? TryBuildGrass(int index, int cx, int cz)
    {
        IReadOnlyList<Tuft> tufts = GrassScatter.Cell(_seed, cx, cz, _data.GrassCellMeters, _data.WorldSizeMeters, _data.GrassTufts[index]);
        int species = _data.GrassSpecies[index];
        var transforms = new List<Transform3D>(tufts.Count);
        foreach (Tuft t in tufts)
        {
            float y = _terrain.HeightAt(new Vector3((float)t.X, 0, (float)t.Z));
            if (float.IsNaN(y))
            {
                return null; // the terrain here is not loaded yet; try again next frame
            }

            var basis = new Basis(Vector3.Up, (float)t.YawRadians).Scaled(Vector3.One * (float)t.Scale);
            transforms.Add(new Transform3D(basis, new Vector3((float)t.X, y, (float)t.Z)));
        }

        var multi = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            Mesh = _meshes[species][0],
            InstanceCount = transforms.Count,
        };
        for (int i = 0; i < transforms.Count; i++)
        {
            multi.SetInstanceTransform(i, transforms[i]);
        }

        var node = new MultiMeshInstance3D
        {
            Multimesh = multi,
            MaterialOverride = _materials[species],
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            VisibilityRangeEnd = GrassEndMeters,
        };
        AddChild(node);
        return new List<Node> { node };
    }

    private void MoveColliders(Vector3 camera)
    {
        while (_colliders.Count < ColliderPool)
        {
            var body = new StaticBody3D { CollisionLayer = 0 };
            body.AddChild(new CollisionShape3D { Shape = new CylinderShape3D { Radius = 0.4f, Height = ColliderHeightMeters } });
            AddChild(body);
            _colliders.Add(body);
        }

        int used = 0;
        int side = _data.ChunksPerSide;
        double half = _data.WorldSizeMeters / 2.0;
        int chunk = _data.ChunkMeters;
        int x0 = Math.Max(0, (int)Math.Floor((camera.X - ColliderRadiusMeters + half) / chunk));
        int x1 = Math.Min(side - 1, (int)Math.Floor((camera.X + ColliderRadiusMeters + half) / chunk));
        int z0 = Math.Max(0, (int)Math.Floor((camera.Z - ColliderRadiusMeters + half) / chunk));
        int z1 = Math.Min(side - 1, (int)Math.Floor((camera.Z + ColliderRadiusMeters + half) / chunk));
        for (int cz = z0; cz <= z1; cz++)
        {
            for (int cx = x0; cx <= x1; cx++)
            {
                (int first, int count) = _chunks[(cz * side) + cx];
                for (int i = first; i < first + count && used < ColliderPool; i++)
                {
                    FoliageInstance inst = _data.Instances[i];
                    double radius = _data.Species[inst.Species].TrunkRadiusMeters;
                    double dx = inst.X - camera.X;
                    double dz = inst.Z - camera.Z;
                    if (radius <= 0 || (dx * dx) + (dz * dz) > ColliderRadiusMeters * ColliderRadiusMeters)
                    {
                        continue;
                    }

                    StaticBody3D body = _colliders[used++];
                    ((CylinderShape3D)((CollisionShape3D)body.GetChild(0)).Shape).Radius = (float)(radius * inst.Scale);
                    body.GlobalPosition = new Vector3((float)inst.X, (float)inst.Y + (ColliderHeightMeters / 2), (float)inst.Z);
                    body.CollisionLayer = 1;
                }
            }
        }

        for (int i = used; i < _colliders.Count; i++)
        {
            _colliders[i].CollisionLayer = 0;
        }
    }

    private void Free(Dictionary<int, List<Node>> set, int index)
    {
        foreach (Node node in set[index])
        {
            node.QueueFree();
        }

        set.Remove(index);
    }

    private double ChunkDistance(Vector3 camera, int cx, int cz)
    {
        double half = _data.WorldSizeMeters / 2.0;
        int chunk = _data.ChunkMeters;
        double centreX = -half + ((cx + 0.5) * chunk);
        double centreZ = -half + ((cz + 0.5) * chunk);
        return Math.Sqrt(((centreX - camera.X) * (centreX - camera.X)) + ((centreZ - camera.Z) * (centreZ - camera.Z))) - (chunk * 0.7071);
    }

    private double GrassDistance(Vector3 camera, int cx, int cz)
    {
        double half = _data.WorldSizeMeters / 2.0;
        int cell = _data.GrassCellMeters;
        double centreX = -half + ((cx + 0.5) * cell);
        double centreZ = -half + ((cz + 0.5) * cell);
        return Math.Sqrt(((centreX - camera.X) * (centreX - camera.X)) + ((centreZ - camera.Z) * (centreZ - camera.Z))) - (cell * 0.7071);
    }

    private static (int First, int Count)[] IndexChunks(FoliageData data)
    {
        int side = data.ChunksPerSide;
        var chunks = new (int First, int Count)[side * side];
        double half = data.WorldSizeMeters / 2.0;
        for (int i = 0; i < data.Instances.Count; i++)
        {
            FoliageInstance inst = data.Instances[i];
            int cx = Math.Clamp((int)Math.Floor((inst.X + half) / data.ChunkMeters), 0, side - 1);
            int cz = Math.Clamp((int)Math.Floor((inst.Z + half) / data.ChunkMeters), 0, side - 1);
            int c = (cz * side) + cx;
            chunks[c] = chunks[c].Count == 0 ? (i, 1) : (chunks[c].First, chunks[c].Count + 1);
        }

        return chunks;
    }
}
