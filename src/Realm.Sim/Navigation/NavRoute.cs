using System;
using System.Collections.Generic;

namespace Realm.Sim.Navigation;

/// <summary>
/// A walk along a graph as geometry: where a walker stands after a given distance. A character's position is a function of time
/// (start time, speed, route), so nothing has to be stepped every frame, and a character that is far away costs nothing.
/// </summary>
/// <remarks>Determinism: pure. Thread-safety: immutable.</remarks>
public sealed class NavRoute
{
    private const int CurveSegments = 4;

    private readonly double[] _x;
    private readonly double[] _z;
    private readonly double[] _distance;

    /// <summary>Creates a route from a node path.</summary>
    /// <param name="graph">The graph the path is on.</param>
    /// <param name="path">Node IDs from start to goal; at least one.</param>
    /// <exception cref="ArgumentException">The path is empty.</exception>
    public NavRoute(NavGraph graph, IReadOnlyList<int> path)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(path);
        if (path.Count == 0)
        {
            throw new ArgumentException("A route needs at least one node.", nameof(path));
        }

        int n = path.Count;
        _x = new double[n];
        _z = new double[n];
        _distance = new double[n];
        for (int i = 0; i < n; i++)
        {
            NavNode node = graph.Nodes[path[i]];
            _x[i] = node.X;
            _z[i] = node.Z;
            if (i > 0)
            {
                _distance[i] = _distance[i - 1] + Math.Sqrt(((_x[i] - _x[i - 1]) * (_x[i] - _x[i - 1])) + ((_z[i] - _z[i - 1]) * (_z[i] - _z[i - 1])));
            }
        }
    }

    /// <summary>Creates a route from positions (a start point off the graph, the nodes of a path, an end point).</summary>
    /// <param name="points">Positions east and south in metres; at least one.</param>
    /// <exception cref="ArgumentException">There are no points.</exception>
    public NavRoute(IReadOnlyList<(double X, double Z)> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (points.Count == 0)
        {
            throw new ArgumentException("A route needs at least one point.", nameof(points));
        }

        int n = points.Count;
        _x = new double[n];
        _z = new double[n];
        _distance = new double[n];
        for (int i = 0; i < n; i++)
        {
            _x[i] = points[i].X;
            _z[i] = points[i].Z;
            if (i > 0)
            {
                _distance[i] = _distance[i - 1] + Math.Sqrt(((_x[i] - _x[i - 1]) * (_x[i] - _x[i - 1])) + ((_z[i] - _z[i - 1]) * (_z[i] - _z[i - 1])));
            }
        }
    }

    /// <summary>Creates a route with its corners rounded: each corner is replaced by a short curve, so walkers turn instead of pivoting.</summary>
    /// <param name="points">Positions east and south in metres; at least one.</param>
    /// <param name="cornerMeters">How far before and after a corner the curve starts, at most half of the adjoining leg.</param>
    /// <returns>The rounded route; the first and last points are unchanged.</returns>
    public static NavRoute Smooth(IReadOnlyList<(double X, double Z)> points, double cornerMeters)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (points.Count < 3 || cornerMeters <= 0)
        {
            return new NavRoute(points);
        }

        var result = new List<(double X, double Z)> { points[0] };
        for (int i = 1; i < points.Count - 1; i++)
        {
            (double X, double Z) a = points[i - 1];
            (double X, double Z) b = points[i];
            (double X, double Z) c = points[i + 1];
            double before = Distance(a, b);
            double after = Distance(b, c);
            double r = Math.Min(cornerMeters, Math.Min(before, after) / 2);
            if (r < 1e-6 || before < 1e-6 || after < 1e-6)
            {
                result.Add(b);
                continue;
            }

            (double X, double Z) start = (b.X + ((a.X - b.X) / before * r), b.Z + ((a.Z - b.Z) / before * r));
            (double X, double Z) end = (b.X + ((c.X - b.X) / after * r), b.Z + ((c.Z - b.Z) / after * r));
            for (int k = 0; k <= CurveSegments; k++)
            {
                // A quadratic curve from the start to the end, pulled toward the corner.
                double t = k / (double)CurveSegments;
                double u = 1 - t;
                result.Add(((u * u * start.X) + (2 * u * t * b.X) + (t * t * end.X), (u * u * start.Z) + (2 * u * t * b.Z) + (t * t * end.Z)));
            }
        }

        result.Add(points[^1]);
        return new NavRoute(result);
    }

    /// <summary>Total walking length in metres.</summary>
    public double LengthMeters => _distance[^1];

    /// <summary>The direction a walker faces after walking a distance along the route, as the yaw that turns the game's forward direction (minus Z) to face along the walk: <c>atan2(-dx, -dz)</c>.</summary>
    /// <param name="distanceMeters">Distance from the start; clamped to the route.</param>
    /// <returns>The yaw in radians; 0 for a route of one point.</returns>
    public double HeadingAt(double distanceMeters)
    {
        double d = Math.Clamp(distanceMeters, 0, LengthMeters);
        int i = Array.BinarySearch(_distance, d);
        if (i < 0)
        {
            i = ~i;
        }

        i = Math.Clamp(i, 1, _x.Length - 1);
        if (_x.Length < 2)
        {
            return 0;
        }

        // Skip legs of no length (a repeated point).
        while (i < _x.Length - 1 && _distance[i] - _distance[i - 1] < 1e-9)
        {
            i++;
        }

        return Math.Atan2(-(_x[i] - _x[i - 1]), -(_z[i] - _z[i - 1]));
    }

    /// <summary>Where a walker is after walking a distance along the route.</summary>
    /// <param name="distanceMeters">Distance from the start; clamped to the route.</param>
    /// <param name="x">Receives the position east.</param>
    /// <param name="z">Receives the position south.</param>
    public void PositionAt(double distanceMeters, out double x, out double z)
    {
        double d = Math.Clamp(distanceMeters, 0, LengthMeters);
        int i = Array.BinarySearch(_distance, d);
        if (i < 0)
        {
            i = ~i;
        }

        if (i <= 0)
        {
            x = _x[0];
            z = _z[0];
            return;
        }

        double span = _distance[i] - _distance[i - 1];
        double t = span <= 0 ? 1 : (d - _distance[i - 1]) / span;
        x = _x[i - 1] + ((_x[i] - _x[i - 1]) * t);
        z = _z[i - 1] + ((_z[i] - _z[i - 1]) * t);
    }

    private static double Distance((double X, double Z) a, (double X, double Z) b) => Math.Sqrt(((a.X - b.X) * (a.X - b.X)) + ((a.Z - b.Z) * (a.Z - b.Z)));
}
