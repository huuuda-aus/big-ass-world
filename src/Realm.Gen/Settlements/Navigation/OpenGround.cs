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
    /// <summary>How far from a building (its foundation and steps included) a walk keeps, in metres.</summary>
    public const double BuildingMarginMeters = 0.55;

    private const double SampleMeters = 0.5;
    private const double GateToleranceMeters = 4.5; // a line may cross the wall only this close to a gate (the opening is 4 m either side)
    private const double GateHalfMeters = 4; // half the opening of a gate: the wall stops this far from the gate vertex
    private const double StoneHalfThicknessMeters = 0.9;
    private const double PalisadeHalfThicknessMeters = 0.6;
    private const double WallMarginMeters = 0.3; // the wall and its towers: close, so that the gates (which can be narrow at a sharp corner of the wall) stay passable
    private const double TowerSolidShare = 0.8;
    private const double CornerTowerRadius = 2.8;
    private const double GateTowerRadius = 2.4;
    private const double WatchtowerSide = 3.4;
    private const double SameCorner = 0.01;
    private const double CellMeters = 16;
    private const double LandmarkSolidShare = 0.55;
    private const int ConstrainPasses = 3;

    private readonly City _city;
    private readonly IWaterSampler? _water;
    private readonly Fortification _fortification;
    private readonly List<ConvexPoly> _obstacles = new();
    private readonly List<double> _margins = new(); // how far a walk keeps from each obstacle: wide for houses, narrow for the wall and towers
    private readonly Dictionary<(int, int), List<int>> _grid = new();

    /// <summary>Prepares the obstacle index.</summary>
    /// <param name="city">The city.</param>
    /// <param name="water">Where water lies, or null for a dry site.</param>
    /// <param name="fortification">What was built along the wall line: a stone wall with towers, a palisade with watchtowers at the gates, or nothing.</param>
    public OpenGround(City city, IWaterSampler? water, Fortification fortification = Fortification.Stone)
    {
        ArgumentNullException.ThrowIfNull(city);
        _city = city;
        _water = water;
        _fortification = fortification;
        // The landmark's radius is the space it may take, not what it fills: the fountain and its basin are about half of it.
        LandmarkSolidRadius = (city.Landmark?.Radius ?? 0) * LandmarkSolidShare;
        AddObstacles(city.AllBuildings.Select(b => b.Footprint), BuildingMarginMeters);
        if (city.Landmark is not null)
        {
            AddObstacles(city.Landmark.Stalls, BuildingMarginMeters);
        }

        if (fortification != Fortification.None)
        {
            AddWall();
        }

        for (int i = 0; i < _obstacles.Count; i++)
        {
            IndexObstacle(i);
        }
    }

    /// <summary>Radius of the solid core of the centrepiece around the origin, in metres.</summary>
    public double LandmarkSolidRadius { get; }

    /// <summary>
    /// The direction out of a gate, as a unit vector: the way through the opening, which is the bisector of the wall's corner at the gate
    /// (a gate stands at a vertex of the wall, and the middle of the opening is not always the direction away from the centre).
    /// </summary>
    /// <param name="city">The city.</param>
    /// <param name="gate">A gate position (a vertex of the wall).</param>
    /// <returns>A unit vector pointing outward through the gate.</returns>
    public static Vec2 GateOutward(City city, Vec2 gate)
    {
        ArgumentNullException.ThrowIfNull(city);
        IReadOnlyList<Vec2> wall = city.Wall;
        Vec2 radial = gate.Length < 1e-6 ? new Vec2(1, 0) : gate.Normalized();
        for (int i = 0; i < wall.Count; i++)
        {
            if (Vec2.Distance(wall[i], gate) >= SameCorner)
            {
                continue;
            }

            Vec2 toNext = (wall[(i + 1) % wall.Count] - gate).Normalized();
            Vec2 toPrevious = (wall[(i + wall.Count - 1) % wall.Count] - gate).Normalized();
            Vec2 inward = toNext + toPrevious; // between the two arms of the wall: toward the inside of a convex corner
            if (inward.Length < 1e-3)
            {
                Vec2 normal = new(-toNext.Y, toNext.X); // a straight wall: the normal that points away from the centre
                return Vec2.Dot(normal, radial) >= 0 ? normal : -normal;
            }

            Vec2 outward = -(inward.Normalized());
            return Vec2.Dot(outward, radial) >= 0 || gate.Length < 1e-6 ? outward : -outward; // keep it pointing away from the centre
        }

        return radial;
    }

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
                if (Inside(_obstacles[i], p, _margins[i]))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Whether a straight line crosses the wall anywhere but at a gate.</summary>
    /// <param name="a">Start in the city's plan frame.</param>
    /// <param name="b">End in the plan frame.</param>
    /// <returns><c>true</c> when the line passes through the wall.</returns>
    public bool CrossesWall(Vec2 a, Vec2 b)
    {
        if (_fortification == Fortification.None)
        {
            return false;
        }

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

    private void AddObstacles(IEnumerable<ConvexPoly> polygons, double margin)
    {
        foreach (ConvexPoly poly in polygons)
        {
            _obstacles.Add(poly);
            _margins.Add(margin);
        }
    }

    // The wall as a thing to walk into: its stretches (a gate opening is left free), the corner towers of a stone wall, the towers either
    // side of a gate (round for stone, square watchtowers for a palisade). A body must stay out of them as out of a house.
    private void AddWall()
    {
        IReadOnlyList<Vec2> wall = _city.Wall;
        double half = _fortification == Fortification.Stone ? StoneHalfThicknessMeters : PalisadeHalfThicknessMeters;
        var gateVertices = Enumerable.Range(0, wall.Count).Where(i => _city.Gates.Any(g => Vec2.Distance(g, wall[i]) < SameCorner)).ToHashSet();
        for (int i = 0; i < wall.Count; i++)
        {
            int next = (i + 1) % wall.Count;
            int previous = (i + wall.Count - 1) % wall.Count;
            Vec2 a = wall[i];
            Vec2 b = wall[next];
            double length = Vec2.Distance(a, b);
            if (length < 1e-6)
            {
                continue;
            }

            Vec2 dir = (b - a) * (1 / length);
            double start = gateVertices.Contains(i) ? GateHalfMeters : 0;
            double end = length - (gateVertices.Contains(next) ? GateHalfMeters : 0);
            if (end - start > 0.2)
            {
                AddObstacles(new[] { Rectangle(a + (dir * start), a + (dir * end), half) }, WallMarginMeters);
            }

            if (gateVertices.Contains(i))
            {
                Vec2 back = (wall[previous] - a).Normalized();
                foreach (Vec2 along in new[] { dir, back })
                {
                    if (_fortification == Fortification.Stone)
                    {
                        AddObstacles(new[] { Disc(a + (along * GateHalfMeters), GateTowerRadius * TowerSolidShare) }, WallMarginMeters);
                    }
                    else
                    {
                        Vec2 centre = a + (along * (GateHalfMeters + (WatchtowerSide / 2)));
                        AddObstacles(new[] { Rectangle(centre - (along * (WatchtowerSide / 2)), centre + (along * (WatchtowerSide / 2)), WatchtowerSide / 2) }, WallMarginMeters);
                    }
                }
            }
            else if (_fortification == Fortification.Stone)
            {
                AddObstacles(new[] { Disc(a, CornerTowerRadius * TowerSolidShare) }, WallMarginMeters);
            }
        }
    }

    // A rectangle along a line, a half width to each side.
    private static ConvexPoly Rectangle(Vec2 a, Vec2 b, double halfWidth)
    {
        Vec2 d = (b - a).Normalized();
        var n = new Vec2(-d.Y, d.X);
        var corners = new[] { a - (n * halfWidth), b - (n * halfWidth), b + (n * halfWidth), a + (n * halfWidth) };
        return new ConvexPoly(corners, Enumerable.Repeat(-1, corners.Length));
    }

    // An octagon that covers a circle.
    private static ConvexPoly Disc(Vec2 centre, double radius)
    {
        const int Sides = 8;
        double outer = radius / Math.Cos(Math.PI / Sides);
        var corners = Enumerable.Range(0, Sides)
            .Select(k => centre + new Vec2(outer * Math.Cos((2 * Math.PI * k / Sides) + (Math.PI / Sides)), outer * Math.Sin((2 * Math.PI * k / Sides) + (Math.PI / Sides))))
            .ToArray();
        return new ConvexPoly(corners, Enumerable.Repeat(-1, corners.Length));
    }

    /// <summary>
    /// Keeps a character's body out of buildings, market stalls and the centrepiece: a position that has strayed inside one (a lane offset, a
    /// cut corner, a push from a neighbour) is moved out to the nearest edge, so the body of radius <paramref name="radius"/> never overlaps
    /// them. A position that ends up in water or on the other side of the wall falls back to <paramref name="from"/>.
    /// </summary>
    /// <param name="from">A position known to be fine (the walk's own line), in the city's plan frame.</param>
    /// <param name="wanted">Where the character would like to stand.</param>
    /// <param name="radius">Radius of the character's body in metres.</param>
    /// <returns>The nearest position that is clear of the obstacles.</returns>
    public Vec2 Constrain(Vec2 from, Vec2 wanted, double radius)
    {
        Vec2 p = wanted;
        for (int pass = 0; pass < ConstrainPasses; pass++)
        {
            bool moved = false;
            double reach = LandmarkSolidRadius + radius;
            if (LandmarkSolidRadius > 0 && p.Length < reach)
            {
                p = p.Length < 1e-6 ? new Vec2(reach, 0) : p.Normalized() * reach;
                moved = true;
            }

            int cx = (int)Math.Floor(p.X / CellMeters);
            int cy = (int)Math.Floor(p.Y / CellMeters);
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    if (!_grid.TryGetValue((cx + dx, cy + dy), out List<int>? list))
                    {
                        continue;
                    }

                    foreach (int i in list)
                    {
                        if (PushOut(_obstacles[i], p, radius, out Vec2 q))
                        {
                            p = q;
                            moved = true;
                        }
                    }
                }
            }

            if (!moved)
            {
                break;
            }
        }

        if ((_water is not null && _water.IsWater(p.X, p.Y)) || CrossesWall(from, p))
        {
            return from;
        }

        return p;
    }

    // If a point lies inside a convex polygon grown by a radius, the nearest point on the grown outline (through the nearest edge).
    private static bool PushOut(ConvexPoly poly, Vec2 p, double radius, out Vec2 result)
    {
        IReadOnlyList<Vec2> pts = poly.Points;
        double area = 0;
        for (int i = 0; i < pts.Count; i++)
        {
            area += Vec2.Cross(pts[i], pts[(i + 1) % pts.Count]);
        }

        double sign = area >= 0 ? 1 : -1;
        double shallowest = double.MaxValue;
        Vec2 outward = default;
        for (int i = 0; i < pts.Count; i++)
        {
            Vec2 a = pts[i];
            Vec2 e = pts[(i + 1) % pts.Count] - a;
            double length = e.Length;
            if (length < 1e-9)
            {
                continue;
            }

            double inside = sign * Vec2.Cross(e, p - a) / length; // distance inside the edge (negative outside it)
            double depth = inside + radius;
            if (depth < 0)
            {
                result = p;
                return false; // clear of this edge, so outside the grown polygon
            }

            if (depth < shallowest)
            {
                shallowest = depth;
                outward = new Vec2(e.Y, -e.X) * (sign / length);
            }
        }

        result = p + (outward * shallowest);
        return shallowest < double.MaxValue;
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
