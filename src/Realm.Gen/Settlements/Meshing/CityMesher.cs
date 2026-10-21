using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Realm.Gen.Props;
using Realm.Gen.Settlements.Geometry;
using Realm.Gen.Settlements.Model;

namespace Realm.Gen.Settlements.Meshing;

/// <summary>
/// Turns the city plan into meshes: ward ground, streets, the wall with its towers and gates, and the centrepiece. Ported from
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

    /// <summary>Height above the terrain of the ward ground colours, in metres (avoids flicker against the terrain).</summary>
    public const float GroundLift = 0.1f;

    /// <summary>Height above the terrain of the street surface, in metres.</summary>
    public const float StreetLift = 0.16f;

    private const double TowerRadius = 2.8;
    private const double GateTowerRadius = 2.4;
    private const double TowerRise = 3;
    private const double GateTowerRise = 4;
    private const int TowerSides = 10;
    private const double SameCorner = 0.01;

    /// <summary>Builds the meshes of a city.</summary>
    /// <param name="city">The generated city.</param>
    /// <param name="citySeed">The seed the city was generated from.</param>
    /// <returns>The meshes.</returns>
    public static CityMeshes Build(City city, int citySeed)
    {
        ArgumentNullException.ThrowIfNull(city);
        return new CityMeshes(Ground(city), Wall(city), city.Landmark is null ? null : LandmarkMesher.Build(city.Landmark, citySeed));
    }

    /// <summary>Builds the ground: one flat polygon per ward and a strip per street segment.</summary>
    /// <param name="city">The city.</param>
    /// <returns>The mesh (never empty: a city has wards).</returns>
    public static PropMesh Ground(City city)
    {
        ArgumentNullException.ThrowIfNull(city);
        var b = new PropMeshBuilder();
        foreach (Patch patch in city.CityPatches)
        {
            b.FacingPolygon(patch.Shape.Points.Select(p => new Vector3((float)p.X, GroundLift, (float)p.Y)).ToList(), Vector3.UnitY, SettlementPalette.WardGround(patch.Ward));
        }

        foreach (Street street in city.Streets)
        {
            for (int i = 0; i + 1 < street.Points.Count; i++)
            {
                Vec2 a = street.Points[i];
                Vec2 c = street.Points[i + 1];
                Vec2 d = (c - a).Normalized();
                Vec2 n = new Vec2(-d.Y, d.X) * (StreetWidthMeters / 2);
                b.FacingQuad(P(a - n, StreetLift), P(c - n, StreetLift), P(c + n, StreetLift), P(a + n, StreetLift), Vector3.UnitY, SettlementPalette.Street);
            }
        }

        return b.Build("city.ground");
    }

    /// <summary>Builds the wall, its towers and the gate towers.</summary>
    /// <param name="city">The city.</param>
    /// <returns>The mesh, or null when the city has no wall.</returns>
    public static PropMesh? Wall(City city)
    {
        ArgumentNullException.ThrowIfNull(city);
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
                Vec2 from = a + (dir * start);
                Vec2 to = a + (dir * end);
                Vec2 mid = (from + to) * 0.5;
                b.YawBox(P(mid, 0), (float)Math.Atan2(dir.Y, dir.X), new Vector3((float)(end - start), height, (float)WallThickness), SettlementPalette.Wall);
            }

            if (gateVertices.Contains(i))
            {
                Vec2 back = (wall[previous] - a).Normalized();
                Tower(b, a + (dir * (GateWidthMeters / 2)), GateTowerRadius, height + (float)GateTowerRise);
                Tower(b, a + (back * (GateWidthMeters / 2)), GateTowerRadius, height + (float)GateTowerRise);
            }
            else
            {
                Tower(b, a, TowerRadius, height + (float)TowerRise);
            }
        }

        return b.Build("city.wall");
    }

    private static void Tower(PropMeshBuilder b, Vec2 at, double radius, float height) =>
        b.Tube(P(at, 0), P(at, height), (float)radius * 1.1f, (float)radius * 0.93f, TowerSides, SettlementPalette.Wall);

    private static Vector3 P(Vec2 p, double y) => new((float)p.X, (float)y, (float)p.Y);
}
