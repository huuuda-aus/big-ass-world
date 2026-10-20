using System;
using System.Collections.Generic;

namespace Realm.Core.Geometry;

/// <summary>
/// A simple polygon (no holes, no self-intersection) on the ground plane. Used by the macro map to
/// describe regions.
/// </summary>
/// <remarks>
/// Determinism: pure double arithmetic on the given vertices; no randomness or platform-dependent
/// calls. Thread-safety: immutable.
/// <see cref="Contains"/> uses a half-open rule so that, for polygons that tile the plane and share
/// edges, every point belongs to exactly one polygon, including points on a shared edge or vertex.
/// </remarks>
public sealed class Polygon
{
    private readonly Point2D[] _points;

    private Polygon(Point2D[] points)
    {
        _points = points;
        double minX = double.MaxValue;
        double minZ = double.MaxValue;
        double maxX = double.MinValue;
        double maxZ = double.MinValue;
        foreach (Point2D p in points)
        {
            minX = Math.Min(minX, p.X);
            minZ = Math.Min(minZ, p.Z);
            maxX = Math.Max(maxX, p.X);
            maxZ = Math.Max(maxZ, p.Z);
        }

        MinX = minX;
        MinZ = minZ;
        MaxX = maxX;
        MaxZ = maxZ;
        Area = Math.Abs(SignedArea(points));
    }

    /// <summary>The vertices in the order given.</summary>
    public IReadOnlyList<Point2D> Points => _points;

    /// <summary>Smallest X of any vertex.</summary>
    public double MinX { get; }

    /// <summary>Smallest Z of any vertex.</summary>
    public double MinZ { get; }

    /// <summary>Largest X of any vertex.</summary>
    public double MaxX { get; }

    /// <summary>Largest Z of any vertex.</summary>
    public double MaxZ { get; }

    /// <summary>Enclosed area in square metres; always positive.</summary>
    public double Area { get; }

    /// <summary>Creates a polygon if the vertices form a valid simple polygon.</summary>
    /// <param name="points">Vertices in order (either winding); the last connects back to the first.</param>
    /// <param name="polygon">The polygon when valid; otherwise null.</param>
    /// <param name="problem">Why the vertices are invalid; otherwise null.</param>
    /// <returns><c>true</c> when valid.</returns>
    /// <remarks>
    /// Invalid means: fewer than 3 vertices, a non-finite coordinate, a repeated consecutive vertex, zero
    /// area, an edge that doubles back on its neighbour, or any two edges that cross or touch (other
    /// than neighbours meeting at their shared vertex).
    /// </remarks>
    public static bool TryCreate(IReadOnlyList<Point2D> points, out Polygon? polygon, out string? problem)
    {
        ArgumentNullException.ThrowIfNull(points);
        polygon = null;

        if (points.Count < 3)
        {
            problem = $"A polygon needs at least 3 vertices but has {points.Count}.";
            return false;
        }

        var copy = new Point2D[points.Count];
        for (int i = 0; i < copy.Length; i++)
        {
            Point2D p = points[i];
            if (!double.IsFinite(p.X) || !double.IsFinite(p.Z))
            {
                problem = $"Vertex {i} has a non-finite coordinate.";
                return false;
            }

            copy[i] = p;
        }

        for (int i = 0; i < copy.Length; i++)
        {
            if (copy[i] == copy[(i + 1) % copy.Length])
            {
                problem = $"Vertices {i} and {(i + 1) % copy.Length} are identical.";
                return false;
            }
        }

        if (AllCollinear(copy))
        {
            problem = "The polygon has zero area (all vertices are on one line).";
            return false;
        }

        // Self-intersection before the area test: lobes of a bow-tie can cancel to a signed area of exactly 0.
        if (!IsSimple(copy, out problem))
        {
            return false;
        }

        if (SignedArea(copy) == 0)
        {
            problem = "The polygon has zero area (all vertices are on one line).";
            return false;
        }

        polygon = new Polygon(copy);
        problem = null;
        return true;
    }

    /// <summary>Tests whether a point is inside, using the even-odd rule with a half-open convention.</summary>
    /// <param name="point">The point to test.</param>
    /// <returns><c>true</c> when inside.</returns>
    public bool Contains(Point2D point)
    {
        if (point.X < MinX || point.X > MaxX || point.Z < MinZ || point.Z > MaxZ)
        {
            return false;
        }

        bool inside = false;
        for (int i = 0, j = _points.Length - 1; i < _points.Length; j = i++)
        {
            Point2D a = _points[i];
            Point2D b = _points[j];
            if ((a.Z > point.Z) != (b.Z > point.Z))
            {
                double crossX = ((b.X - a.X) * (point.Z - a.Z) / (b.Z - a.Z)) + a.X;
                if (point.X < crossX)
                {
                    inside = !inside;
                }
            }
        }

        return inside;
    }

    private static bool AllCollinear(Point2D[] p)
    {
        for (int i = 2; i < p.Length; i++)
        {
            if (Cross(p[0], p[1], p[i]) != 0)
            {
                return false;
            }
        }

        return true;
    }

    private static double SignedArea(Point2D[] p)
    {
        double sum = 0;
        for (int i = 0, j = p.Length - 1; i < p.Length; j = i++)
        {
            sum += (p[j].X * p[i].Z) - (p[i].X * p[j].Z);
        }

        return sum / 2.0;
    }

    private static bool IsSimple(Point2D[] p, out string? problem)
    {
        int n = p.Length;
        for (int i = 0; i < n; i++)
        {
            Point2D a = p[i];
            Point2D b = p[(i + 1) % n];
            Point2D c = p[(i + 2) % n];

            // A neighbour that doubles straight back over this edge is a spike.
            if (Cross(a, b, c) == 0 && Dot(b.X - a.X, b.Z - a.Z, c.X - b.X, c.Z - b.Z) < 0)
            {
                problem = $"Edge {(i + 1) % n} doubles back over edge {i}.";
                return false;
            }

            for (int j = i + 1; j < n; j++)
            {
                bool neighbours = j == i + 1 || (i == 0 && j == n - 1);
                if (neighbours)
                {
                    continue;
                }

                if (SegmentsTouch(a, b, p[j], p[(j + 1) % n]))
                {
                    problem = $"Edges {i} and {j} cross or touch.";
                    return false;
                }
            }
        }

        problem = null;
        return true;
    }

    private static double Cross(Point2D o, Point2D a, Point2D b) =>
        ((a.X - o.X) * (b.Z - o.Z)) - ((a.Z - o.Z) * (b.X - o.X));

    private static double Dot(double ax, double az, double bx, double bz) => (ax * bx) + (az * bz);

    private static bool SegmentsTouch(Point2D a, Point2D b, Point2D c, Point2D d)
    {
        double d1 = Cross(a, b, c);
        double d2 = Cross(a, b, d);
        double d3 = Cross(c, d, a);
        double d4 = Cross(c, d, b);

        if (((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0)))
        {
            return true;
        }

        return (d1 == 0 && OnSegment(a, b, c))
            || (d2 == 0 && OnSegment(a, b, d))
            || (d3 == 0 && OnSegment(c, d, a))
            || (d4 == 0 && OnSegment(c, d, b));
    }

    private static bool OnSegment(Point2D a, Point2D b, Point2D p) =>
        p.X >= Math.Min(a.X, b.X) && p.X <= Math.Max(a.X, b.X)
        && p.Z >= Math.Min(a.Z, b.Z) && p.Z <= Math.Max(a.Z, b.Z);
}
