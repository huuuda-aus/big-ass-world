using System;
using System.Collections.Generic;
using System.Linq;
using Realm.Sim.Navigation;
using Xunit;

namespace Realm.Sim.Tests;

public class NavigationTests
{
    // A 4 x 4 grid of nodes 10 m apart, with a wall of missing edges between column 1 and column 2 except in row 3.
    private static NavGraph Grid(bool openRowThree = true)
    {
        var nodes = new List<NavNode>();
        for (int z = 0; z < 4; z++)
        {
            for (int x = 0; x < 4; x++)
            {
                nodes.Add(new NavNode(x * 10, z * 10, NavNodeKind.Street));
            }
        }

        var edges = new List<NavEdge>();
        for (int z = 0; z < 4; z++)
        {
            for (int x = 0; x < 4; x++)
            {
                int i = (z * 4) + x;
                if (x < 3 && (x != 1 || (openRowThree && z == 3)))
                {
                    edges.Add(new NavEdge(i, i + 1, 10));
                }

                if (z < 3)
                {
                    edges.Add(new NavEdge(i, i + 4, 10));
                }
            }
        }

        return new NavGraph(nodes, edges);
    }

    [Fact]
    public void ShortestPath_GoesThroughTheOnlyGap()
    {
        NavGraph g = Grid();
        var finder = new NavPathFinder(g);
        var path = new List<int>();

        bool found = finder.TryFindPath(0, 3, path, out double length);

        Assert.True(found);
        // From (0,0) to (30,0): down to row 3, across the gap, back up: 30 + 30 + 30 = 90 m.
        Assert.Equal(90, length, 6);
        Assert.Equal(0, path[0]);
        Assert.Equal(3, path[^1]);
        Assert.Contains(13, path);
        Assert.Contains(14, path);
    }

    [Fact]
    public void WhenTheGapIsClosed_ThereIsNoPath_AndTheGroupsDiffer()
    {
        NavGraph g = Grid(openRowThree: false);
        var finder = new NavPathFinder(g);

        Assert.False(finder.TryFindPath(0, 3, new List<int>(), out double length));
        Assert.Equal(0, length);
        Assert.Equal(2, g.ComponentCount);
        Assert.False(g.AreConnected(0, 3));
        Assert.True(g.AreConnected(0, 5));
    }

    [Fact]
    public void ThePathFinder_IsReusable_AndDeterministic()
    {
        NavGraph g = Grid();
        var finder = new NavPathFinder(g);
        var a = new List<int>();
        var b = new List<int>();

        finder.TryFindPath(0, 15, a, out double la);
        finder.TryFindPath(15, 0, new List<int>(), out _);
        finder.TryFindPath(0, 15, b, out double lb);

        Assert.Equal(a, b);
        Assert.Equal(la, lb);
        Assert.True(finder.TryFindPath(5, 5, a, out double zero));
        Assert.Equal(new[] { 5 }, a);
        Assert.Equal(0, zero);
    }

    [Fact]
    public void Astar_MatchesABruteForceShortestPath_OnRandomGraphs()
    {
        var rng = new Realm.Core.Random.Rng(7);
        for (int trial = 0; trial < 20; trial++)
        {
            int n = 25;
            var nodes = Enumerable.Range(0, n).Select(_ => new NavNode(rng.NextDouble() * 100, rng.NextDouble() * 100, NavNodeKind.Street)).ToList();
            var edges = new List<NavEdge>();
            for (int i = 0; i < n; i++)
            {
                for (int j = i + 1; j < n; j++)
                {
                    double d = Math.Sqrt(Math.Pow(nodes[i].X - nodes[j].X, 2) + Math.Pow(nodes[i].Z - nodes[j].Z, 2));
                    if (d < 30)
                    {
                        edges.Add(new NavEdge(i, j, d * (1 + rng.NextDouble())));
                    }
                }
            }

            var graph = new NavGraph(nodes, edges);
            var finder = new NavPathFinder(graph);
            double[,] best = FloydWarshall(n, edges);
            for (int k = 0; k < 10; k++)
            {
                int from = rng.NextInt(n);
                int to = rng.NextInt(n);
                bool found = finder.TryFindPath(from, to, new List<int>(), out double length);
                Assert.Equal(!double.IsInfinity(best[from, to]), found);
                if (found)
                {
                    Assert.Equal(best[from, to], length, 6);
                }
            }
        }
    }

    [Fact]
    public void ARoute_GivesPositionsAlongTheWalk_ClampedAtTheEnds()
    {
        NavGraph g = Grid();
        var route = new NavRoute(g, new[] { 0, 1, 2 });

        Assert.Equal(20, route.LengthMeters, 9);
        route.PositionAt(-5, out double x0, out double z0);
        route.PositionAt(5, out double x1, out double z1);
        route.PositionAt(15, out double x2, out double z2);
        route.PositionAt(99, out double x3, out double z3);

        Assert.Equal((0.0, 0.0), (x0, z0));
        Assert.Equal((5.0, 0.0), (x1, z1));
        Assert.Equal((15.0, 0.0), (x2, z2));
        Assert.Equal((20.0, 0.0), (x3, z3));
        Assert.Throws<ArgumentException>(() => new NavRoute(g, Array.Empty<int>()));
    }

    [Fact]
    public void NearestAndDoorLookups_Work()
    {
        var nodes = new List<NavNode>
        {
            new(0, 0, NavNodeKind.Street),
            new(10, 0, NavNodeKind.Door, 42),
            new(11, 0, NavNodeKind.Street),
        };
        var g = new NavGraph(nodes, new[] { new NavEdge(0, 1, 10), new NavEdge(1, 2, 1) });

        Assert.Equal(1, g.Nearest(9, 1));
        Assert.Equal(2, g.Nearest(9, 1, NavNodeKind.Street));
        Assert.Equal(1, g.DoorOf(42));
        Assert.Equal(-1, g.DoorOf(7));
        Assert.Equal(-1, g.Nearest(0, 0, NavNodeKind.Gate));
        Assert.Throws<ArgumentException>(() => new NavGraph(nodes, new[] { new NavEdge(0, 9, 1) }));
    }

    private static double[,] FloydWarshall(int n, List<NavEdge> edges)
    {
        var d = new double[n, n];
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++)
            {
                d[i, j] = i == j ? 0 : double.PositiveInfinity;
            }
        }

        foreach (NavEdge e in edges)
        {
            d[e.A, e.B] = Math.Min(d[e.A, e.B], e.LengthMeters);
            d[e.B, e.A] = Math.Min(d[e.B, e.A], e.LengthMeters);
        }

        for (int k = 0; k < n; k++)
        {
            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < n; j++)
                {
                    d[i, j] = Math.Min(d[i, j], d[i, k] + d[k, j]);
                }
            }
        }

        return d;
    }
}
