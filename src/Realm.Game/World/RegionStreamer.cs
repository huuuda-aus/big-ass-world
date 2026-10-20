using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Godot;
using Realm.Core.Geometry;
using Realm.Game.Bridge;

namespace Realm.Game.World;

/// <summary>
/// Keeps the Terrain3D regions around the camera loaded and the rest removed (CLAUDE.md section 4.9): regions
/// are generated on worker threads, then committed to the terrain on the main thread within a per-frame budget.
/// </summary>
/// <remarks>
/// Main thread only for <see cref="Update"/>; the generator runs on thread-pool threads and must be thread-safe.
/// A region wanted but not loaded is requested nearest first; results for regions that became unwanted while
/// they were generated are discarded. Hysteresis (unload radius above load radius) stops regions flickering at
/// a boundary.
/// </remarks>
public sealed class RegionStreamer
{
    private readonly TerrainBridge _bridge;
    private readonly Func<Vector2I, RegionData> _generate;
    private StreamingOptions _options;
    private readonly HashSet<Vector2I> _loaded = new();
    private readonly HashSet<Vector2I> _pending = new();
    private readonly ConcurrentQueue<(Vector2I Location, RegionData? Data, PreparedRegion? Prepared)> _ready = new();
    private readonly List<double> _generateMs = new();
    private readonly object _generateGate = new();
    private bool _needsUpdate;

    /// <summary>Creates a streamer.</summary>
    /// <param name="bridge">The terrain to fill.</param>
    /// <param name="generate">Produces the data of a region (heights, optional colours); called on worker threads.</param>
    /// <param name="options">Radii and budgets.</param>
    public RegionStreamer(TerrainBridge bridge, Func<Vector2I, RegionData> generate, StreamingOptions options)
    {
        ArgumentNullException.ThrowIfNull(bridge);
        ArgumentNullException.ThrowIfNull(generate);
        ArgumentNullException.ThrowIfNull(options);
        ValidateOptions(options);

        _bridge = bridge;
        _generate = generate;
        _options = options;
    }

    /// <summary>Radii and budgets; may be replaced between frames (for example when switching to a longer view distance).</summary>
    public StreamingOptions Options
    {
        get => _options;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            ValidateOptions(value);
            _options = value;
        }
    }

    /// <summary>Timed main-thread steps in order.</summary>
    public List<StreamEvent> Events { get; } = new();

    /// <summary>Regions currently loaded.</summary>
    public int LoadedCount => _loaded.Count;

    /// <summary>Regions being generated.</summary>
    public int PendingCount => _pending.Count;

    /// <summary>Results thrown away because the region was no longer wanted when it was ready.</summary>
    public int DiscardedCount { get; private set; }

    /// <summary>Worker-thread generation times in milliseconds.</summary>
    public IReadOnlyList<double> GenerateTimesMs
    {
        get
        {
            lock (_generateGate)
            {
                return _generateMs.ToArray();
            }
        }
    }

    private static void ValidateOptions(StreamingOptions options)
    {
        if (options.UnloadDistanceMeters <= options.LoadDistanceMeters)
        {
            throw new ArgumentException("UnloadDistanceMeters must be larger than LoadDistanceMeters.", nameof(options));
        }
    }

    /// <summary>Whether a region is loaded into the terrain.</summary>
    /// <param name="location">The region coordinate.</param>
    /// <returns><c>true</c> when loaded.</returns>
    public bool IsLoaded(Vector2I location) => _loaded.Contains(location);

    /// <summary>Loads every region within the load distance now, blocking; for a loading screen before play starts.</summary>
    /// <param name="cameraPosition">Camera position in metres.</param>
    public void LoadAllBlocking(Vector3 cameraPosition)
    {
        foreach (Vector2I location in WantedByDistance(cameraPosition))
        {
            if (_loaded.Contains(location))
            {
                continue;
            }

            RegionData data = _generate(location);
            _bridge.AddRegion(location, data.Heights, data.Colours, update: false);
            _loaded.Add(location);
        }

        _bridge.UpdateAll();
    }

    /// <summary>Runs one frame of streaming: requests, commits and removals.</summary>
    /// <param name="cameraPosition">Camera position in metres.</param>
    /// <param name="frame">Frame number, recorded in the events.</param>
    public void Update(Vector3 cameraPosition, long frame)
    {
        RequestMissing(cameraPosition);
        Commit(cameraPosition, frame);
        Unload(cameraPosition, frame);
        FlushUpdate(frame);
    }

    // One shared-texture rebuild per frame covers every add and remove that deferred it.
    private void FlushUpdate(long frame)
    {
        if (!_needsUpdate)
        {
            return;
        }

        _needsUpdate = false;
        var watch = Stopwatch.StartNew();
        _bridge.UpdateAll();
        watch.Stop();
        Events.Add(new StreamEvent(frame, StreamEventKind.Update, default, watch.Elapsed.TotalMilliseconds));
    }

    private void RequestMissing(Vector3 camera)
    {
        foreach (Vector2I location in WantedByDistance(camera))
        {
            if (_pending.Count >= Options.MaxInFlight)
            {
                return;
            }

            if (_loaded.Contains(location) || _pending.Contains(location))
            {
                continue;
            }

            _pending.Add(location);
            Vector2I target = location;
            Task.Run(() =>
            {
                var watch = Stopwatch.StartNew();
                RegionData data = _generate(target);
                PreparedRegion? prepared = Options.BuildOnWorker ? _bridge.PrepareRegion(target, data.Heights, data.Colours) : null;
                watch.Stop();
                lock (_generateGate)
                {
                    _generateMs.Add(watch.Elapsed.TotalMilliseconds);
                }

                _ready.Enqueue((target, Options.BuildOnWorker ? null : data, prepared));
            });
        }
    }

    private void Commit(Vector3 camera, long frame)
    {
        int commits = 0;
        var budget = Stopwatch.StartNew();
        while (commits < Options.MaxCommitsPerFrame
               && budget.Elapsed.TotalMilliseconds < Options.CommitBudgetMs
               && _ready.TryDequeue(out (Vector2I Location, RegionData? Data, PreparedRegion? Prepared) result))
        {
            _pending.Remove(result.Location);
            if (DistanceTo(result.Location, camera) > Options.UnloadDistanceMeters)
            {
                DiscardedCount++;
                continue;
            }

            var watch = Stopwatch.StartNew();
            if (result.Prepared is not null)
            {
                _bridge.AddPreparedRegion(result.Prepared, Options.UpdatePerAdd);
            }
            else
            {
                // Safe: when nothing was prepared on the worker, the region data was queued instead.
                _bridge.AddRegion(result.Location, result.Data!.Heights, result.Data.Colours, Options.UpdatePerAdd);
            }

            watch.Stop();
            _loaded.Add(result.Location);
            Events.Add(new StreamEvent(frame, StreamEventKind.Add, result.Location, watch.Elapsed.TotalMilliseconds));
            commits++;
        }

        if (commits > 0 && !Options.UpdatePerAdd)
        {
            _needsUpdate = true;
        }
    }

    private void Unload(Vector3 camera, long frame)
    {
        int removals = 0;
        foreach (Vector2I location in new List<Vector2I>(_loaded))
        {
            if (removals >= Options.MaxRemovalsPerFrame)
            {
                return;
            }

            if (DistanceTo(location, camera) <= Options.UnloadDistanceMeters)
            {
                continue;
            }

            var watch = Stopwatch.StartNew();
            _bridge.RemoveRegion(location, Options.UpdatePerRemove);
            watch.Stop();
            _loaded.Remove(location);
            Events.Add(new StreamEvent(frame, StreamEventKind.Remove, location, watch.Elapsed.TotalMilliseconds));
            removals++;
            if (!Options.UpdatePerRemove)
            {
                _needsUpdate = true;
            }
        }
    }

    // Regions within the load distance that fit Terrain3D's grid, nearest first; the camera's own region is always first.
    private List<Vector2I> WantedByDistance(Vector3 camera)
    {
        var wanted = new List<Vector2I>();
        foreach ((int x, int z, _) in RegionGrid.RegionsWithin(
                     camera.X, camera.Z, Options.LoadDistanceMeters, _bridge.RegionExtentMeters,
                     TerrainBridge.MinRegionCoordinate, TerrainBridge.MaxRegionCoordinate))
        {
            wanted.Add(new Vector2I(x, z));
        }

        return wanted;
    }

    private double DistanceTo(Vector2I location, Vector3 camera) =>
        RegionGrid.DistanceToRegion(camera.X, camera.Z, location.X, location.Y, _bridge.RegionExtentMeters);
}
