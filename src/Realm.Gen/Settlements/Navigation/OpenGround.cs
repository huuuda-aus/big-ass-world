using System;
using System.Collections.Generic;
using System.Linq;
using Realm.Gen.Settlements.Geometry;
using Realm.Gen.Settlements.Model;

namespace Realm.Gen.Settlements.Navigation;

/// <summary>
/// What a character on foot can stand on in a generated city: open ground, not inside a building (with a margin), the solid part of the
/// centrepiece, a market stall or the water, and not through the wall except at a gate.
/// </summary>
/// <remarks>Determinism: pure. Thread-safety: immutable after construction.</remarks>
public sealed class OpenGround
{
    private const double BuildingMarginMeters = 0.3;
    private const double SampleMeters = 0.5;
    private const double GateToleranceMeters = 6;
    private const double CellMeters = 16;
    private const double LandmarkSolidShare = 0.55;

    private readonly City _city;
    private readonly IWaterSampler? _water;
    private readonly List<ConvexPoly> _obstacles = new();
    private readonly Dictionary<(int, int), List<int>> _grid = new();

    /// <summary>Prepares the obstacle index.</summary>
    /// <param name="city">The city.</param>
    /// <param name="water">Where water lies, or null for a dry site.</param>
    public OpenGround(City city, IWaterSampler? water)
    {
        ArgumentNullException.ThrowIfNull(city);
        _city = city;
        _water = water;
        // The landmark's radius is the space it may take, not what it fills: the fountain and its basin are about half of it.
        LandmarkSolidRadius = (city.Landmark?.Radius ?? 0) * LandmarkSolidShare;
        _obstacles.AddRange(city.AllBuildings.Select(b => b.Footprint));
        if (city.Landmark is not null)
        {
            _obstacles.AddRange(city.Landmark.Stalls);
        }

        for (int i = 0; i < _obstacles.Count; i++)
        {
            IndexObstacle(i);
        }
    }

    /// <summary>Radius of the solid core of the centrepiece around the origin, in metres.</summary>
    public double LandmarkSolidRadius { get; }

    /// <summary>Whether a straight walk between two points stays on open ground.</summary>
    /// <param name="a">Start in the city's plan frame.</param>
    /// <param name="b">End in the plan frame.</param>
    /// <returns><c>true</c> when the whole line is open ground.</returns>
    public bool Clear(Vec2 a, Vec2 b)
    {
        double length = Vec2.Distance(a, b);
        int steps = Math.Max(1, (int)Math.Ceiling(length / SampleMeters));
        for (int s = 0; s <= steps; s++)
        {
            if (Blocked(Vec2.Lerp(a, b, s / (double)steps)))
            {
                return false;
            }
        }

        return !CrossesWall(a, b);
    }

    /// <summary>Whether a point cannot be stood on.</summary>
    /// <param name="p">The point in the city's plan frame.</param>
    /// <returns><c>true</c> inside an obstacle or water.</returns>
    public bool Blocked(Vec2 p)
    {
        if (LandmarkSolidRadius > 0 && p.Length < LandmarkSolidRadius)
        {
            return true;
        }

        if (_water is not null && _water.IsWater(p.X, p.Y))
        {
            return true;
        }

        var cell = ((int)Math.Floor(p.X / CellMeters), (int)Math.Floor(p.Y / CellMeters));
        if (_grid.TryGetValue(cell, out List<int>? list))
        {
            foreach (int i in list)
            {
                if (Inside(_obstacles[i], p, BuildingMarginMeters))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private bool CrossesWall(Vec2 a, Vec2 b)
    {
        IReadOnlyList<Vec2> wall = _city.Wall;
        for (int i = 0; i < wall.Count; i++)
        {
            Vec2 c = wall[i];
            Vec2 d = wall[(i + 1) % wall.Count];
            if (Intersect(a, b, c, d, out Vec2 at) && !_city.Gates.Any(g => Vec2.Distance(g, at) <= GateToleranceMeters))
            {
                return true;
            }
        }

        return false;
    }

    private void IndexObstacle(int index)
    {
        IReadOnlyList<Vec2> pts = _obstacles[index].Points;
        double minX = pts.Min(p => p.X) - 1, maxX = pts.Max(p => p.X) + 1, minY = pts.Min(p => p.Y) - 1, maxY = pts.Max(p => p.Y) + 1;
        for (int cx = (int)Math.Floor(minX / CellMeters); cx <= (int)Math.Floor(maxX / CellMeters); cx++)
        {
            for (int cy = (int)Math.Floor(minY / CellMeters); cy <= (int)Math.Floor(maxY / CellMeters); cy++)
            {
                if (!_grid.TryGetValue((cx, cy), out List<int>? list))
                {
                    _grid[(cx, cy)] = list = new List<int>();
                }

                list.Add(index);
            }
        }
    }

    // Whether a point is inside a convex polygon grown by a margin (either winding).
    private static bool Inside(ConvexPoly poly, Vec2 p, double margin)
    {
        IReadOnlyList<Vec2> pts = poly.Points;
        double area = 0;
        for (int i = 0; i < pts.Count; i++)
        {
            area += Vec2.Cross(pts[i], pts[(i + 1) % pts.Count]);
        }

        double sign = area >= 0 ? 1 : -1;
        for (int i = 0; i < pts.Count; i++)
        {
            Vec2 a = pts[i];
            Vec2 e = pts[(i + 1) % pts.Count] - a;
            double length = e.Length;
            if (length < 1e-9)
            {
                continue;
            }

            if (sign * Vec2.Cross(e, p - a) / length < -margin)
            {
                return false;
            }
        }

        return true;
    }

    private static bool Intersect(Vec2 a, Vec2 b, Vec2 c, Vec2 d, out Vec2 at)
    {
        Vec2 r = b - a;
        Vec2 s = d - c;
        double denominator = Vec2.Cross(r, s);
        at = default;
        if (Math.Abs(denominator) < 1e-12)
        {
            return false;
        }

        double t = Vec2.Cross(c - a, s) / denominator;
        double u = Vec2.Cross(c - a, r) / denominator;
        if (t < 0 || t > 1 || u < 0 || u > 1)
        {
            return false;
        }

        at = a + (r * t);
        return true;
    }
}
