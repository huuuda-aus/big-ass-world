using System;
using System.Collections.Generic;

namespace Realm.Sim.Navigation;

/// <summary>
/// A walking graph: nodes joined by straight edges. Simulated characters walk along it instead of over a navigation mesh, so a
/// walk is a list of nodes and the position of a walker is a function of time (see <see cref="NavRoute"/>).
/// </summary>
/// <remarks>Determinism: pure data and fixed iteration orders. Thread-safety: immutable after construction; a <see cref="NavPathFinder"/> keeps its own scratch space.</remarks>
public sealed class NavGraph
{
    private readonly int[] _firstEdge;
    private readonly int[] _incident;
    private readonly int[] _component;

    /// <summary>Creates a graph.</summary>
    /// <param name="nodes">The nodes; a node's ID is its index.</param>
    /// <param name="edges">The edges.</param>
    /// <exception cref="ArgumentException">An edge refers to a node that does not exist or has a negative length.</exception>
    public NavGraph(IReadOnlyList<NavNode> nodes, IReadOnlyList<NavEdge> edges)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(edges);
        Nodes = nodes;
        Edges = edges;
        int n = nodes.Count;
        var degree = new int[n + 1];
        foreach (NavEdge e in edges)
        {
            if ((uint)e.A >= (uint)n || (uint)e.B >= (uint)n || !(e.LengthMeters >= 0) || !(e.Weight >= 1))
            {
                throw new ArgumentException($"Edge {e.A}-{e.B} refers to a missing node, or has a bad length or a weight below 1.", nameof(edges));
            }

            degree[e.A]++;
            degree[e.B]++;
        }

        _firstEdge = new int[n + 1];
        for (int i = 0; i < n; i++)
        {
            _firstEdge[i + 1] = _firstEdge[i] + degree[i];
        }

        _incident = new int[_firstEdge[n]];
        var fill = new int[n];
        for (int i = 0; i < edges.Count; i++)
        {
            NavEdge e = edges[i];
            _incident[_firstEdge[e.A] + fill[e.A]++] = i;
            _incident[_firstEdge[e.B] + fill[e.B]++] = i;
        }

        _component = new int[n];
        Array.Fill(_component, -1);
        var stack = new Stack<int>();
        for (int start = 0; start < n; start++)
        {
            if (_component[start] >= 0)
            {
                continue;
            }

            _component[start] = ComponentCount;
            stack.Push(start);
            while (stack.Count > 0)
            {
                int node = stack.Pop();
                for (int k = _firstEdge[node]; k < _firstEdge[node + 1]; k++)
                {
                    int next = edges[_incident[k]].Other(node);
                    if (_component[next] < 0)
                    {
                        _component[next] = ComponentCount;
                        stack.Push(next);
                    }
                }
            }

            ComponentCount++;
        }
    }

    /// <summary>The nodes; a node's ID is its index.</summary>
    public IReadOnlyList<NavNode> Nodes { get; }

    /// <summary>The edges.</summary>
    public IReadOnlyList<NavEdge> Edges { get; }

    /// <summary>Number of connected groups of nodes.</summary>
    public int ComponentCount { get; private set; }

    /// <summary>The edges that touch a node.</summary>
    /// <param name="node">The node ID.</param>
    /// <returns>Edge indices, in the order the edges were given.</returns>
    public ReadOnlySpan<int> EdgesOf(int node) => new(_incident, _firstEdge[node], _firstEdge[node + 1] - _firstEdge[node]);

    /// <summary>Which connected group a node is in.</summary>
    /// <param name="node">The node ID.</param>
    /// <returns>A group number from 0; nodes in the same group can reach each other.</returns>
    public int ComponentOf(int node) => _component[node];

    /// <summary>Whether a walk exists between two nodes.</summary>
    /// <param name="a">One node ID.</param>
    /// <param name="b">The other node ID.</param>
    /// <returns><c>true</c> when they are in the same connected group.</returns>
    public bool AreConnected(int a, int b) => _component[a] == _component[b];

    /// <summary>The node nearest to a position.</summary>
    /// <param name="x">Position east.</param>
    /// <param name="z">Position south.</param>
    /// <param name="kind">Only consider nodes of this kind, or any when null.</param>
    /// <returns>The node ID, or -1 when the graph has no such node. Ties go to the lower ID.</returns>
    public int Nearest(double x, double z, NavNodeKind? kind = null)
    {
        int best = -1;
        double bestD = double.MaxValue;
        for (int i = 0; i < Nodes.Count; i++)
        {
            NavNode node = Nodes[i];
            if (kind is not null && node.Kind != kind)
            {
                continue;
            }

            double d = ((node.X - x) * (node.X - x)) + ((node.Z - z) * (node.Z - z));
            if (d < bestD)
            {
                bestD = d;
                best = i;
            }
        }

        return best;
    }

    /// <summary>The door node of a building.</summary>
    /// <param name="buildingId">The building ID.</param>
    /// <returns>The node ID, or -1 when the building has no door node.</returns>
    public int DoorOf(int buildingId)
    {
        for (int i = 0; i < Nodes.Count; i++)
        {
            if (Nodes[i].Kind == NavNodeKind.Door && Nodes[i].Ref == buildingId)
            {
                return i;
            }
        }

        return -1;
    }
}
