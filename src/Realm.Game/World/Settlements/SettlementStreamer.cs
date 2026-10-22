using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Godot;
using Realm.Core.Validation;
using Realm.Gen.Settlements.Placement;

namespace Realm.Game.World.Settlements;

/// <summary>
/// Builds a city when the player comes near it and removes it again when they leave. The heavy work (generating the city,
/// every house's interior and exterior mesh) runs on a worker thread; the main thread only adds nodes, a few per frame.
/// </summary>
/// <remarks>
/// Settlements come from <c>settlements.json</c>, baked next to the terrain by <c>realm bake-terrain</c>.
/// Main thread only.
/// </remarks>
public sealed partial class SettlementStreamer : Node3D
{
    /// <summary>A city is built when the player is within its pad radius plus this distance (or the view distance plus 300 m when that is larger), in metres.</summary>
    public const double LoadMarginMeters = 500;

    /// <summary>A city is removed when the player is farther than its pad radius plus this distance (or 300 m beyond the load distance), in metres.</summary>
    public const double UnloadMarginMeters = 800;

    private readonly IReadOnlyList<Settlement> _settlements;
    private readonly IReadOnlyDictionary<string, GroundGrid> _ground;
    private readonly Dictionary<string, CityInstance> _live = new();
    private readonly Dictionary<string, Task<CityBuild>> _building = new();
    private readonly Dictionary<string, ulong> _started = new();

    private SettlementStreamer(IReadOnlyList<Settlement> settlements, IReadOnlyDictionary<string, GroundGrid> ground)
    {
        _settlements = settlements;
        _ground = ground;
        Name = "Settlements";
    }

    /// <summary>The settlements this streamer manages.</summary>
    public IReadOnlyList<Settlement> Settlements => _settlements;

    /// <summary>Cities currently in the world.</summary>
    public IReadOnlyCollection<CityInstance> Cities => _live.Values;

    /// <summary>Cities being generated.</summary>
    public int BuildingCount => _building.Count;

    /// <summary>Reads the baked settlements next to the terrain.</summary>
    /// <param name="directory">The baked terrain directory.</param>
    /// <returns>The streamer, or null when no settlements were baked (the world is then just terrain).</returns>
    public static SettlementStreamer? Open(string directory)
    {
        string path = Path.Combine(directory, SettlementManifest.FileName);
        if (!File.Exists(path))
        {
            return null;
        }

        var report = new ValidationReport();
        SettlementManifest? manifest = SettlementManifest.FromJson(File.ReadAllText(path), report);
        if (manifest is null)
        {
            foreach (ValidationIssue issue in report.Issues)
            {
                GD.PushError(issue.ToString());
            }

            return null;
        }

        // The terrain under each city (so houses can stand on slopes). Without it cities are built on level ground.
        IReadOnlyDictionary<string, GroundGrid> ground = new Dictionary<string, GroundGrid>();
        string groundPath = Path.Combine(directory, GroundGridFile.FileName);
        if (File.Exists(groundPath))
        {
            var groundReport = new ValidationReport();
            ground = GroundGridFile.FromBytes(File.ReadAllBytes(groundPath), groundReport) ?? ground;
            foreach (ValidationIssue issue in groundReport.Issues)
            {
                GD.PushError(issue.ToString());
            }
        }
        else
        {
            GD.PushWarning($"{GroundGridFile.FileName} is missing; cities are built on level ground. Run 'realm bake-terrain'.");
        }

        return new SettlementStreamer(manifest.Settlements, ground);
    }

    /// <summary>Finds the settlement whose wall encloses a world position.</summary>
    /// <param name="x">World X in metres.</param>
    /// <param name="z">World Z in metres.</param>
    /// <returns>The settlement, or null when the position is not inside any wall.</returns>
    public Settlement? InsideWall(double x, double z)
    {
        foreach (Settlement s in _settlements)
        {
            if (s.DistanceTo(x, z) <= s.WallRadiusMeters)
            {
                return s;
            }
        }

        return null;
    }

    /// <summary>One frame of work.</summary>
    /// <param name="player">The player's world position.</param>
    /// <param name="delta">Seconds since the last frame.</param>
    /// <param name="viewMeters">How far terrain is currently loaded around the player; cities load a little beyond that (more when flying).</param>
    public void Process(Vector3 player, double delta, double viewMeters)
    {
        double loadMargin = Math.Max(LoadMarginMeters, viewMeters + 300);
        double unloadMargin = Math.Max(UnloadMarginMeters, loadMargin + 300);
        foreach (Settlement s in _settlements)
        {
            double distance = s.DistanceTo(player.X, player.Z);
            bool live = _live.ContainsKey(s.Id);
            bool pending = _building.ContainsKey(s.Id);
            if (!live && !pending && distance < s.PadRadiusMeters + loadMargin)
            {
                Settlement captured = s;
                _started[s.Id] = Time.GetTicksMsec();
                _ground.TryGetValue(s.Id, out GroundGrid? grid);
                _building[s.Id] = Task.Run(() => CityBuild.Create(captured, grid));
            }
            else if (live && distance > s.PadRadiusMeters + unloadMargin)
            {
                _live[s.Id].QueueFree();
                _live.Remove(s.Id);
            }
        }

        List<string>? done = null;
        foreach ((string id, Task<CityBuild> task) in _building)
        {
            if (!task.IsCompleted)
            {
                continue;
            }

            (done ??= new List<string>()).Add(id);
            if (task.IsFaulted)
            {
                GD.PushError($"Could not build {id}: {task.Exception?.GetBaseException().Message}");
                continue;
            }

            var city = new CityInstance(task.Result);
            AddChild(city);
            _live[id] = city;
            GD.Print($"[city] {id} built: {city.BuildingCount} houses in {Time.GetTicksMsec() - _started[id]} ms");
        }

        if (done is not null)
        {
            foreach (string id in done)
            {
                _building.Remove(id);
            }
        }

        foreach (CityInstance city in _live.Values)
        {
            city.Process(player, delta);
        }
    }
}
