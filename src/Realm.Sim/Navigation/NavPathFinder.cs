using System;
using System.Collections.Generic;

namespace Realm.Sim.Navigation;

/// <summary>
/// Finds shortest walks on a <see cref="NavGraph"/> with A*. One finder owns its scratch arrays and reuses them, so repeated searches
/// do not allocate.
/// </summary>
/// <remarks>Determinism: ties are broken by node ID, so the same graph and query always give the same path. Thread-safety: one finder per thread.</remarks>
public sealed class NavPathFinder
{
    private readonly NavGraph _graph;
    private readonly double[] _cost;
    private readonly int[] _parent;
    private readonly int[] _stamp;
    private readonly bool[] _closed;
    private readonly List<(double F, int Node)> _open = new();
    private int _search;

    /// <summary>Creates a finder for a graph.</summary>
    /// <param name="graph">The graph to search.</param>
    public NavPathFinder(NavGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        _graph = graph;
        int n = graph.Nodes.Count;
        _cost = new double[n];
        _parent = new int[n];
        _stamp = new int[n];
        _closed = new bool[n];
    }

    /// <summary>Finds the shortest walk between two nodes.</summary>
    /// <param name="from">The start node ID.</param>
    /// <param name="to">The goal node ID.</param>
    /// <param name="path">Receives the node IDs from start to goal; cleared first.</param>
    /// <param name="lengthMeters">Receives the walking length.</param>
    /// <returns><c>true</c> when a walk exists.</returns>
    public bool TryFindPath(int from, int to, List<int> path, out double lengthMeters)
    {
        ArgumentNullException.ThrowIfNull(path);
        path.Clear();
        lengthMeters = 0;
        if ((uint)from >= (uint)_graph.Nodes.Count || (uint)to >= (uint)_graph.Nodes.Count || !_graph.AreConnected(from, to))
        {
            return false;
        }

        _search++;
        _open.Clear();
        Touch(from, 0, -1);
        Push(Estimate(from, to), from);
        while (_open.Count > 0)
        {
            int node = Pop();
            if (_closed[node])
            {
                continue;
            }

            if (node == to)
            {
                lengthMeters = _cost[to];
                for (int at = to; at >= 0; at = _parent[at])
                {
                    path.Add(at);
                }

                path.Reverse();
                return true;
            }

            _closed[node] = true;
            foreach (int edgeIndex in _graph.EdgesOf(node))
            {
                NavEdge edge = _graph.Edges[edgeIndex];
                int next = edge.Other(node);
                double cost = _cost[node] + edge.LengthMeters;
                if (_stamp[next] != _search)
                {
                    Touch(next, cost, node);
                    Push(cost + Estimate(next, to), next);
                }
                else if (cost < _cost[next] - 1e-12)
                {
                    _cost[next] = cost;
                    _parent[next] = node;
                    _closed[next] = false;
                    Push(cost + Estimate(next, to), next);
                }
            }
        }

        return false;
    }

    private void Touch(int node, double cost, int parent)
    {
        _stamp[node] = _search;
        _cost[node] = cost;
        _parent[node] = parent;
        _closed[node] = false;
    }

    private double Estimate(int node, int goal)
    {
        NavNode a = _graph.Nodes[node];
        NavNode b = _graph.Nodes[goal];
        return Math.Sqrt(((a.X - b.X) * (a.X - b.X)) + ((a.Z - b.Z) * (a.Z - b.Z)));
    }

    // A binary min-heap on (F, node); equal F is ordered by node ID so the search is deterministic.
    private void Push(double f, int node)
    {
        _open.Add((f, node));
        int i = _open.Count - 1;
        while (i > 0)
        {
            int parent = (i - 1) / 2;
            if (Less(_open[i], _open[parent]))
            {
                (_open[i], _open[parent]) = (_open[parent], _open[i]);
                i = parent;
            }
            else
            {
                break;
            }
        }
    }

    private int Pop()
    {
        int node = _open[0].Node;
        int last = _open.Count - 1;
        _open[0] = _open[last];
        _open.RemoveAt(last);
        int i = 0;
        while (true)
        {
            int left = (2 * i) + 1;
            int right = left + 1;
            int smallest = i;
            if (left < _open.Count && Less(_open[left], _open[smallest]))
            {
                smallest = left;
            }

            if (right < _open.Count && Less(_open[right], _open[smallest]))
            {
                smallest = right;
            }

            if (smallest == i)
            {
                break;
            }

            (_open[i], _open[smallest]) = (_open[smallest], _open[i]);
            i = smallest;
        }

        return node;
    }

    private static bool Less((double F, int Node) a, (double F, int Node) b) => a.F < b.F || (a.F == b.F && a.Node < b.Node);
}
