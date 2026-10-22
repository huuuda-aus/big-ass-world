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

    /// <summary>Total walking length in metres.</summary>
    public double LengthMeters => _distance[^1];

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
}
