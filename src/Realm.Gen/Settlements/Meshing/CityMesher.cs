using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Realm.Gen.Props;
using Realm.Gen.Settlements.Geometry;
using Realm.Gen.Settlements.Model;

namespace Realm.Gen.Settlements.Meshing;

/// <summary>
/// Turns the city plan into meshes: the wall (stone with towers, or a wooden palisade, by the settlement's fortification) and the centrepiece. There is no ground or street mesh: houses stand on the natural terrain (roads and paths come later). Ported from
/// the three.js viewer; the gates are an improvement: the viewer left a whole wall edge open, here each gate is a gap of
/// <see cref="GateWidthMeters"/> flanked by two gate towers.
/// </summary>
/// <remarks>Coordinates: the city's local frame (plan x is X, plan y is Z, height is Y). Determinism: pure. Thread-safety: pure.</remarks>
public static class CityMesher
{
    /// <summary>Height of the wall in metres.</summary>
    public const double WallHeight = 7;

    /// <summary>Thickness of the wall in metres.</summary>
    public const double WallThickness = 1.6;

    /// <summary>Width of a gate opening in metres.</summary>
    public const double GateWidthMeters = 8;

    /// <summary>Width of a street in metres.</summary>
    public const double StreetWidthMeters = 6;

    /// <summary>Longest piece of wall built in one go, in metres; shorter pieces follow the slope more closely.</summary>
    private const double DrapeMeters = 8;

    /// <summary>How far the wall and towers are sunk into the ground, in metres, so no gap shows on slopes.</summary>
    private const double WallSink = 2.0;

    /// <summary>How far a wall cut by water runs on into the water, in metres.</summary>
    private const double WaterOverlapMeters = 2.5;

    /// <summary>Spacing of the water tests along a wall, in metres.</summary>
    private const double WaterStepMeters = 2;

    private const float LogMinRadius = 0.18f;
    private const float LogMaxRadius = 0.25f;
    private const double LogSpacingFactor = 1.55; // centre spacing as a multiple of the radius; under 2 means neighbours overlap
    private const float LogTaper = 0.9f;
    private const int LogSides = 8;
    private const int LogRows = 2;
    private const double LogRowOffsetMeters = 0.2;
    private const double LogRowStagger = 0.2;
    private const double LogEndOverlapMeters = 0.2;
    private const double LogSinkMeters = 0.6;
    private const double PalisadeHeight = 3.2;
    private const double PalisadeHeightSpread = 0.8;
    private const double WatchtowerSide = 3.4;
    private const double WatchtowerHeight = 6.5;
    private const double WatchtowerPlatformHeight = 4.8;
    private const double TowerRadius = 2.8;
    private const double GateTowerRadius = 2.4;
    private const double TowerRise = 3;
    private const double GateTowerRise = 4;
    private const int TowerSides = 10;
    private const double SameCorner = 0.01;

    /// <summary>Builds the meshes of a city.</summary>
    /// <param name="city">The generated city.</param>
    /// <param name="citySeed">The seed the city was generated from.</param>
    /// <param name="ground">The ground under the city; level ground when null.</param>
    /// <param name="fortification">What protects the settlement (a stone wall by default).</param>
    /// <param name="water">Where the water is: the wall is cut where it would stand in water. Null for a dry site.</param>
    /// <returns>The meshes.</returns>
    public static CityMeshes Build(City city, int citySeed, IGroundSampler? ground = null, Fortification fortification = Fortification.Stone, IWaterSampler? water = null)
    {
        ArgumentNullException.ThrowIfNull(city);
        ground ??= FlatGround.Instance;
        double landmarkBase = city.Landmark is null ? 0 : ground.HeightAt(city.Landmark.Center.X, city.Landmark.Center.Y);
        return new CityMeshes(
            Wall(city, ground, fortification, water),
            city.Landmark is null ? null : LandmarkMesher.Build(city.Landmark, citySeed, ground, landmarkBase),
            landmarkBase);
    }

    /// <summary>Builds the wall, its towers and the gate towers, following the ground.</summary>
    /// <param name="city">The city.</param>
    /// <param name="ground">The ground under the city; level ground when null.</param>
    /// <param name="fortification">Stone wall with towers (default), wooden palisade with wooden gate towers, or none.</param>
    /// <param name="water">Where the water is. The wall stops at the water (a river or lake is the defence there), running a couple of metres into it, with a tower at each cut end. Null for a dry site.</param>
    /// <returns>The mesh, or null when the city has no wall or the fortification is <see cref="Fortification.None"/>.</returns>
    public static PropMesh? Wall(City city, IGroundSampler? ground = null, Fortification fortification = Fortification.Stone, IWaterSampler? water = null)
    {
        ArgumentNullException.ThrowIfNull(city);
        ground ??= FlatGround.Instance;
        IReadOnlyList<Vec2> wall = city.Wall;
        if (wall.Count < 3 || fortification == Fortification.None)
        {
            return null;
        }

        if (fortification == Fortification.Palisade)
        {
            return Palisade(city, ground, water);
        }

        var b = new PropMeshBuilder();
        HashSet<int> gateVertices = Enumerable.Range(0, wall.Count).Where(i => city.Gates.Any(g => Vec2.Distance(g, wall[i]) < SameCorner)).ToHashSet();
        float height = (float)WallHeight;

        for (int i = 0; i < wall.Count; i++)
        {
            int next = (i + 1) % wall.Count;
            int previous = (i + wall.Count - 1) % wall.Count;
            Vec2 a = wall[i];
            Vec2 c = wall[next];
            double length = Vec2.Distance(a, c);
            Vec2 dir = (c - a).Normalized();

            // Gate vertices have no tower; the wall stops half a gate short of them and a gate tower stands on each side.
            double start = gateVertices.Contains(i) ? GateWidthMeters / 2 : 0;
            double end = length - (gateVertices.Contains(next) ? GateWidthMeters / 2 : 0);
            if (end - start > 0.5)
            {
                foreach ((double u0, double u1, bool cutStart, bool cutEnd) in LandRuns(a, dir, start, end, water))
                {
                    WallRun(b, ground, a + (dir * (u0 - (cutStart ? WaterOverlapMeters : 0))), a + (dir * (u1 + (cutEnd ? WaterOverlapMeters : 0))));
                    if (cutStart)
                    {
                        Tower(b, ground, a + (dir * (u0 - WaterOverlapMeters)), TowerRadius, height + (float)TowerRise);
                    }

                    if (cutEnd)
                    {
                        Tower(b, ground, a + (dir * (u1 + WaterOverlapMeters)), TowerRadius, height + (float)TowerRise);
                    }
                }
            }

            if (gateVertices.Contains(i))
            {
                Vec2 back = (wall[previous] - a).Normalized();
                Tower(b, ground, a + (dir * (GateWidthMeters / 2)), GateTowerRadius, height + (float)GateTowerRise);
                Tower(b, ground, a + (back * (GateWidthMeters / 2)), GateTowerRadius, height + (float)GateTowerRise);
            }
            else if (water is null || !water.IsWater(a.X, a.Y))
            {
                Tower(b, ground, a, TowerRadius, height + (float)TowerRise);
            }
        }

        return b.Build("city.wall");
    }

    // The stretches of a wall edge (u along it from `a`) that stand on land. A stretch that ends because the water starts is flagged as
    // cut at that end. Without water the whole edge is one stretch.
    private static IEnumerable<(double Start, double End, bool CutStart, bool CutEnd)> LandRuns(Vec2 a, Vec2 dir, double start, double end, IWaterSampler? water)
    {
        if (water is null)
        {
            yield return (start, end, false, false);
            yield break;
        }

        int steps = Math.Max(1, (int)Math.Ceiling((end - start) / WaterStepMeters));
        double step = (end - start) / steps;
        double? runStart = null;
        bool cutStart = false;
        for (int k = 0; k < steps; k++)
        {
            double u = start + ((k + 0.5) * step);
            bool wet = water.IsWater(a.X + (dir.X * u), a.Y + (dir.Y * u));
            if (!wet && runStart is null)
            {
                runStart = start + (k * step);
                cutStart = k > 0;
            }
            else if (wet && runStart is not null)
            {
                yield return (runStart.Value, start + (k * step), cutStart, true);
                runStart = null;
            }
        }

        if (runStart is not null)
        {
            yield return (runStart.Value, end, cutStart, false);
        }
    }

    // ---- palisade ------------------------------------------------------------------------------------------------

    // A fence of round logs along the wall line with a gap at every gate, and a wooden watchtower either side of each gate.
    private static PropMesh Palisade(City city, IGroundSampler ground, IWaterSampler? water)
    {
        var b = new PropMeshBuilder();
        IReadOnlyList<Vec2> wall = city.Wall;
        HashSet<int> gateVertices = Enumerable.Range(0, wall.Count).Where(i => city.Gates.Any(g => Vec2.Distance(g, wall[i]) < SameCorner)).ToHashSet();
        for (int i = 0; i < wall.Count; i++)
        {
            int next = (i + 1) % wall.Count;
            int previous = (i + wall.Count - 1) % wall.Count;
            Vec2 a = wall[i];
            Vec2 c = wall[next];
            double length = Vec2.Distance(a, c);
            Vec2 dir = (c - a).Normalized();
            double start = gateVertices.Contains(i) ? GateWidthMeters / 2 : 0;
            double end = length - (gateVertices.Contains(next) ? GateWidthMeters / 2 : 0);
            if (end - start > 0.5)
            {
                foreach ((double u0, double u1, bool cutStart, bool cutEnd) in LandRuns(a, dir, start, end, water))
                {
                    Logs(b, ground, a + (dir * (u0 - (cutStart ? WaterOverlapMeters : 0))), a + (dir * (u1 + (cutEnd ? WaterOverlapMeters : 0))));
                }
            }

            if (gateVertices.Contains(i))
            {
                Vec2 back = (wall[previous] - a).Normalized();
                Watchtower(b, ground, a + (dir * (GateWidthMeters / 2 + WatchtowerSide / 2)));
                Watchtower(b, ground, a + (back * (GateWidthMeters / 2 + WatchtowerSide / 2)));
            }
        }

        return b.Build("city.palisade");
    }

    // Upright round logs standing shoulder to shoulder: two staggered rows so no light shows between them, each log a little
    // different in radius and height, flat-topped, and sunk below the ground so slopes show no gap underneath.
    private static void Logs(PropMeshBuilder b, IGroundSampler ground, Vec2 from, Vec2 to)
    {
        foreach (Log log in LogsAlong(from, to))
        {
            double h = ground.HeightAt(log.At.X, log.At.Y);
            b.Tube(P(log.At, h - LogSinkMeters), P(log.At, h + log.Height), log.Radius, log.Radius * LogTaper, LogSides, SettlementPalette.Log(log.Hash));
        }
    }

    /// <summary>One upright log of a palisade: where it stands, how thick and how tall.</summary>
    internal readonly record struct Log(Vec2 At, float Radius, double Height, uint Hash);

    /// <summary>Lays the logs of one palisade run; neighbours in a row overlap, the second row is staggered behind the first, and the ends reach past the run so corners close.</summary>
    internal static IReadOnlyList<Log> LogsAlong(Vec2 from, Vec2 to)
    {
        var logs = new List<Log>();
        double length = Vec2.Distance(from, to);
        if (length < 1e-6)
        {
            return logs;
        }

        Vec2 dir = (to - from).Normalized();
        Vec2 across = new(-dir.Y, dir.X);
        for (int row = 0; row < LogRows; row++)
        {
            double along = row == 0 ? 0 : -LogRowStagger;
            double offset = row * LogRowOffsetMeters;
            while (along < length + LogEndOverlapMeters)
            {
                Vec2 at = from + (dir * along) + (across * offset);
                uint hash = unchecked((uint)((int)(at.X * 31) ^ (int)(at.Y * 17) ^ (row * 7919)) * 2654435761u);
                float radius = LogMinRadius + ((hash & 0xFF) / 255f * (LogMaxRadius - LogMinRadius));
                double height = PalisadeHeight + ((hash >> 8) % 100 / 100.0 * PalisadeHeightSpread) - (row * 0.15);
                logs.Add(new Log(at, radius, height, hash));
                along += radius * LogSpacingFactor; // closer than a diameter, so the round logs overlap
            }
        }

        return logs;
    }

    // Four posts, a platform and a pointed roof.
    private static void Watchtower(PropMeshBuilder b, IGroundSampler ground, Vec2 at)
    {
        double h = ground.HeightAt(at.X, at.Y);
        double half = WatchtowerSide / 2 - 0.25;
        foreach ((int sx, int sz) in new[] { (-1, -1), (1, -1), (1, 1), (-1, 1) })
        {
            Vec2 post = new(at.X + (sx * half), at.Y + (sz * half));
            b.Tube(P(post, h - WallSink / 2), P(post, h + WatchtowerHeight), 0.26f, 0.22f, LogSides, SettlementPalette.Log(0));
        }

        b.YawBox(P(at, h + WatchtowerPlatformHeight), 0, new Vector3((float)WatchtowerSide, 0.3f, (float)WatchtowerSide), SettlementPalette.Plank);
        b.Cone((float)at.X, (float)at.Y, (float)(h + WatchtowerHeight), (float)(WatchtowerSide * 0.85), 2.2f, 4, SettlementPalette.SlumRoof);
    }

    // A stretch of wall cut into pieces no longer than DrapeMeters, each tilted to follow the ground and sunk into it.
    private static void WallRun(PropMeshBuilder b, IGroundSampler ground, Vec2 from, Vec2 to)
    {
        int pieces = Math.Max(1, (int)Math.Ceiling(Vec2.Distance(from, to) / DrapeMeters));
        for (int k = 0; k < pieces; k++)
        {
            Vec2 a = Vec2.Lerp(from, to, k / (double)pieces);
            Vec2 c = Vec2.Lerp(from, to, (k + 1) / (double)pieces);
            Vector3 pa = P(a, ground.HeightAt(a.X, a.Y));
            Vector3 pc = P(c, ground.HeightAt(c.X, c.Y));
            Vector3 along = Vector3.Normalize(pc - pa);
            Vector3 across = Vector3.Normalize(new Vector3(-along.Z, 0, along.X));
            Vector3 up = Vector3.Normalize(Vector3.Cross(along, across));
            if (up.Y < 0)
            {
                up = -up;
            }

            float thickness = (float)WallThickness;
            float tall = (float)(WallHeight + WallSink);
            // Overlap the pieces a little so a bend in the ground leaves no gap.
            float length = (pc - pa).Length() + 0.3f;
            Vector3 centre = ((pa + pc) / 2) + (up * (tall / 2 - (float)WallSink));
            b.OrientedBox(centre, along, up, across, new Vector3(length, tall, thickness), SettlementPalette.Wall);
        }
    }

    private static void Tower(PropMeshBuilder b, IGroundSampler ground, Vec2 at, double radius, float height)
    {
        double h = ground.HeightAt(at.X, at.Y);
        b.Tube(P(at, h - WallSink), P(at, h + height), (float)radius * 1.1f, (float)radius * 0.93f, TowerSides, SettlementPalette.Wall);
    }

    private static Vector3 P(Vec2 p, double y) => new((float)p.X, (float)y, (float)p.Y);
}
