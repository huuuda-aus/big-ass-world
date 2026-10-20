using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using Godot;
using Realm.Core.Diagnostics;
using Realm.Game.Bridge;
using Realm.Game.World;

namespace Realm.Game.Spike;

/// <summary>
/// Terrain3D streaming spike and first traversal soak harness (ADR 0005): flies a camera in a straight line at
/// constant speed over procedurally generated regions that are streamed in and out, records every frame time,
/// and writes a report of percentiles, hitches and the streaming steps that coincide with them.
/// </summary>
/// <remarks>
/// Run: <c>godot --path src/Realm.Game -- --speed=120 --distance=9000 --report=/tmp/report.json</c>.
/// Options (all <c>--name=value</c>): seed, speed (m/s), distance (m), region-size, spacing, collision (0, 1, 3),
/// view-distance (m), unload-margin (m), start-x (m), in-flight, commits-per-frame, removals-per-frame, budget-ms, update-per-add,
/// update-per-remove, altitude, report.
/// </remarks>
public partial class StreamingSpike : Node3D
{
    private const int WarmupFrames = 60;
    private const int TopHitchesReported = 12;
    private const float CameraPitchRadians = -0.25f;
    private const float FarPlaneMeters = 6000f;
    private const double MicrosecondsPerMillisecond = 1000.0;

    private Camera3D _camera = null!; // Safe: created in _Ready before any other method runs.
    private TerrainBridge _bridge = null!; // Safe: created in _Ready before _Process starts.
    private RegionStreamer _streamer = null!; // Safe: created in _Ready before _Process starts.
    private readonly List<double> _frameMs = new();
    private readonly List<long> _frameNumbers = new();
    private Dictionary<string, string> _args = new();
    private double _speed;
    private double _distance;
    private double _travelled;
    private double _preloadMs;
    private ulong _lastTicks;
    private long _frame;
    private int _gc0;
    private int _gc1;
    private int _gc2;
    private long _allocatedAtStart;

    /// <inheritdoc />
    public override void _Ready()
    {
        _args = ParseArguments(OS.GetCmdlineUserArgs());
        _speed = Number("speed", 120);
        _distance = Number("distance", 6000);
        int regionSize = (int)Number("region-size", 512);
        float spacing = (float)Number("spacing", 2);
        int collision = (int)Number("collision", 0);
        ulong seed = (ulong)Number("seed", 1);

        DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
        Engine.MaxFps = 0;

        _camera = new Camera3D { Far = FarPlaneMeters, Current = true };
        AddChild(_camera);
        AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-50, 30, 0), ShadowEnabled = false });
        AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Sky,
                Sky = new Sky { SkyMaterial = new ProceduralSkyMaterial() },
                AmbientLightSource = Godot.Environment.AmbientSource.Sky,
            },
        });

        _bridge = TerrainBridge.Create(new TerrainSettings(regionSize, spacing, (TerrainCollisionMode)collision), _camera);
        _bridge.Node.Set("show_checkered", true); // no textures yet: a checker pattern makes motion visible
        AddChild(_bridge.Node);

        var source = new SpikeTerrainSource(seed, regionSize, spacing);
        var options = new StreamingOptions(
            LoadDistanceMeters: Number("view-distance", 600),
            UnloadDistanceMeters: Number("view-distance", 600) + Number("unload-margin", 300),
            MaxInFlight: (int)Number("in-flight", 2),
            MaxCommitsPerFrame: (int)Number("commits-per-frame", 1),
            MaxRemovalsPerFrame: (int)Number("removals-per-frame", 1),
            CommitBudgetMs: Number("budget-ms", 2.0),
            UpdatePerAdd: Flag("update-per-add", true),
            UpdatePerRemove: Flag("update-per-remove", true),
            BuildOnWorker: Flag("build-on-worker", false));
        _streamer = new RegionStreamer(_bridge, location => new RegionData(source.Generate(location), null), options);

        // Fly east along the middle of region row 0, kept inside the fixed region grid.
        float extent = _bridge.RegionExtentMeters;
        float limit = (TerrainBridge.MaxRegionCoordinate - 1) * extent;
        float startX = _args.ContainsKey("start-x") ? (float)Number("start-x", 0) : Math.Max(-limit, -(float)(_distance / 2));
        if (startX + _distance > limit)
        {
            GD.PushWarning($"Flight of {_distance} m does not fit the region grid; shortening it to {limit - startX} m.");
            _distance = limit - startX;
        }

        _camera.Position = new Vector3(startX, (float)Number("altitude", 250), extent / 2);
        _camera.Rotation = new Vector3(CameraPitchRadians, -Mathf.Pi / 2, 0);

        var preload = Stopwatch.StartNew();
        _streamer.LoadAllBlocking(_camera.Position);
        preload.Stop();
        _preloadMs = preload.Elapsed.TotalMilliseconds;
        GD.Print($"[spike] Terrain3D {TerrainBridge.RequiredVersion}, region {regionSize} x {spacing} m = {extent} m, preload {_streamer.LoadedCount} regions in {_preloadMs:F0} ms");

        _gc0 = GC.CollectionCount(0);
        _gc1 = GC.CollectionCount(1);
        _gc2 = GC.CollectionCount(2);
        _allocatedAtStart = GC.GetTotalAllocatedBytes();
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        ulong now = Time.GetTicksUsec();
        if (_lastTicks != 0 && _frame > WarmupFrames)
        {
            _frameMs.Add((now - _lastTicks) / MicrosecondsPerMillisecond);
            _frameNumbers.Add(_frame);
        }

        _lastTicks = now;
        _frame++;

        double step = _speed * delta;
        _camera.Position += new Vector3((float)step, 0, 0);
        if (_frame > WarmupFrames)
        {
            _travelled += step;
        }

        _streamer.Update(_camera.Position, _frame);

        if (_travelled >= _distance)
        {
            Finish();
        }
    }

    private void Finish()
    {
        SetProcess(false);
        FrameStatsSummary stats = FrameStats.Summarise(_frameMs);
        List<StreamEvent> events = _streamer.Events;
        var adds = events.Where(e => e.Kind == StreamEventKind.Add).Select(e => e.Milliseconds).ToList();
        var removes = events.Where(e => e.Kind == StreamEventKind.Remove).Select(e => e.Milliseconds).ToList();
        var updates = events.Where(e => e.Kind == StreamEventKind.Update).Select(e => e.Milliseconds).ToList();

        // The length recorded under frame number N spans the _Process call whose streaming steps are tagged N plus the
        // rendering that follows it, so the steps that explain a long frame are those with the same number.
        var worst = Enumerable.Range(0, _frameMs.Count)
            .OrderByDescending(i => _frameMs[i])
            .Take(TopHitchesReported)
            .Select(i => new
            {
                frame = _frameNumbers[i],
                ms = Math.Round(_frameMs[i], 2),
                streaming = events.Where(e => e.Frame == _frameNumbers[i])
                    .Select(e => $"{e.Kind} {(e.Kind == StreamEventKind.Update ? string.Empty : e.Location.ToString())} {e.Milliseconds:F2} ms".Replace("  ", " "))
                    .ToList(),
            })
            .ToList();

        var report = new
        {
            engine = Engine.GetVersionInfo()["string"].AsString(),
            terrain3d = TerrainBridge.RequiredVersion,
            config = _args,
            regionExtentMeters = _bridge.RegionExtentMeters,
            preloadMs = Math.Round(_preloadMs, 1),
            flight = new { speed = _speed, distance = Math.Round(_travelled, 1), seconds = Math.Round(_frameMs.Sum() / 1000.0, 1), frames = stats.Count },
            frameMs = new
            {
                mean = Math.Round(stats.MeanMs, 3),
                p50 = Math.Round(stats.P50Ms, 3),
                p95 = Math.Round(stats.P95Ms, 3),
                p99 = Math.Round(stats.P99Ms, 3),
                p999 = Math.Round(stats.P999Ms, 3),
                max = Math.Round(stats.MaxMs, 3),
                hitches = stats.HitchCount,
                hitchThresholdMs = Math.Round(stats.HitchThresholdMs, 2),
            },
            streaming = new
            {
                adds = Summary(adds),
                removes = Summary(removes),
                updates = Summary(updates),
                generateMsWorker = Summary(_streamer.GenerateTimesMs),
                loadedAtEnd = _streamer.LoadedCount,
                terrainRegionCount = _bridge.RegionCount,
                discarded = _streamer.DiscardedCount,
                phases = new
                {
                    buildImageAndRegionMsTotal = Math.Round(_bridge.TotalBuildMs, 2),
                    addRegionCallMsTotal = Math.Round(_bridge.TotalAddCallMs, 2),
                    removeRegionCallMsTotal = Math.Round(_bridge.TotalRemoveCallMs, 2),
                },
            },
            gc = new
            {
                gen0 = GC.CollectionCount(0) - _gc0,
                gen1 = GC.CollectionCount(1) - _gc1,
                gen2 = GC.CollectionCount(2) - _gc2,
                allocatedMb = Math.Round((GC.GetTotalAllocatedBytes() - _allocatedAtStart) / 1048576.0, 1),
            },
            godotStaticMemoryMb = Math.Round(OS.GetStaticMemoryUsage() / 1048576.0, 1),
            worstFrames = worst,
        };

        string json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        GD.Print(json);
        if (_args.TryGetValue("report", out string? path) && !string.IsNullOrEmpty(path))
        {
            File.WriteAllText(path, json);
        }

        GetTree().Quit();
    }

    private static object Summary(IReadOnlyList<double> values) => values.Count == 0
        ? new { count = 0 }
        : new
        {
            count = values.Count,
            mean = Math.Round(values.Average(), 3),
            max = Math.Round(values.Max(), 3),
            total = Math.Round(values.Sum(), 2),
        };

    private double Number(string name, double fallback) =>
        _args.TryGetValue(name, out string? text) && double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v)
            ? v
            : fallback;

    private bool Flag(string name, bool fallback) =>
        _args.TryGetValue(name, out string? text) ? text is "true" or "1" : fallback;

    private static Dictionary<string, string> ParseArguments(string[] args)
    {
        var parsed = new Dictionary<string, string>();
        foreach (string arg in args)
        {
            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            int eq = arg.IndexOf('=', StringComparison.Ordinal);
            if (eq > 2)
            {
                parsed[arg[2..eq]] = arg[(eq + 1)..];
            }
        }

        return parsed;
    }
}
