using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Realm.Gen.Props;
using Realm.Gen.Settlements.Geometry;
using Realm.Gen.Settlements.Model;

namespace Realm.Gen.Settlements.Meshing;

/// <summary>
/// Turns the city plan into meshes: the wall with its towers and gates, and the centrepiece. There is no ground or street mesh: houses stand on the natural terrain (roads and paths come later). Ported from
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
    /// <returns>The meshes.</returns>
    public static CityMeshes Build(City city, int citySeed, IGroundSampler? ground = null)
    {
        ArgumentNullException.ThrowIfNull(city);
        ground ??= FlatGround.Instance;
        double landmarkBase = city.Landmark is null ? 0 : ground.HeightAt(city.Landmark.Center.X, city.Landmark.Center.Y);
        return new CityMeshes(
            Wall(city, ground),
            city.Landmark is null ? null : LandmarkMesher.Build(city.Landmark, citySeed, ground, landmarkBase),
            landmarkBase);
    }

    /// <summary>Builds the wall, its towers and the gate towers, following the ground.</summary>
    /// <param name="city">The city.</param>
    /// <param name="ground">The ground under the city; level ground when null.</param>
    /// <returns>The mesh, or null when the city has no wall.</returns>
    public static PropMesh? Wall(City city, IGroundSampler? ground = null)
    {
        ArgumentNullException.ThrowIfNull(city);
        ground ??= FlatGround.Instance;
        IReadOnlyList<Vec2> wall = city.Wall;
        if (wall.Count < 3)
        {
            return null;
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
                WallRun(b, ground, a + (dir * start), a + (dir * end));
            }

            if (gateVertices.Contains(i))
            {
                Vec2 back = (wall[previous] - a).Normalized();
                Tower(b, ground, a + (dir * (GateWidthMeters / 2)), GateTowerRadius, height + (float)GateTowerRise);
                Tower(b, ground, a + (back * (GateWidthMeters / 2)), GateTowerRadius, height + (float)GateTowerRise);
            }
            else
            {
                Tower(b, ground, a, TowerRadius, height + (float)TowerRise);
            }
        }

        return b.Build("city.wall");
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
