using System;
using System.Collections.Generic;
using System.Linq;
using Realm.Core.Random;
using Realm.Core.Validation;
using Realm.Gen.Settlements.Generation;
using Realm.Gen.Settlements.Geometry;
using Realm.Gen.Settlements.Model;
using Realm.Gen.Settlements.Navigation;
using Realm.Sim.Navigation;
using Xunit;

namespace Realm.Gen.Tests.Settlements;

public class NavGraphTests
{
    private sealed class Strip : IWaterSampler
    {
        private readonly double _centre;
        private readonly double _half;
        public Strip(double centre, double half)
        {
            _centre = centre;
            _half = half;
        }

        public bool IsWater(double x, double y) => Math.Abs(x - _centre) <= _half;
    }

    private static City Make(int seed, int buildings, SettlementLayout layout, IWaterSampler? water = null) =>
        CityGenerator.Generate(new CityParams { Seed = seed, MaxBuildings = buildings, Layout = layout, Parks = false, Water = water, WaterBufferMeters = 10 });

    public static IEnumerable<object[]> Cities()
    {
        foreach (SettlementLayout layout in new[] { SettlementLayout.Radial, SettlementLayout.Cross })
        {
            foreach (int buildings in layout == SettlementLayout.Cross ? new[] { 12, 22 } : new[] { 40, 120, 250 })
            {
                foreach (int seed in new[] { 1, 5 })
                {
                    yield return new object[] { seed, buildings, layout };
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(Cities))]
    public void EveryDoorAndGate_CanBeReachedFromTheSquare(int seed, int buildings, SettlementLayout layout)
    {
        City city = Make(seed, buildings, layout);

        NavGraphResult result = NavGraphBuilder.Build(city);

        Assert.Empty(result.UnreachableBuildings);
        Assert.Empty(result.UnreachableGates);
        Assert.Equal(city.AllBuildings.Count(), result.Graph.Nodes.Count(n => n.Kind == NavNodeKind.Door));
        Assert.Equal(city.Gates.Count, result.Graph.Nodes.Count(n => n.Kind == NavNodeKind.Gate));
        Assert.Equal(city.Gates.Count, result.Graph.Nodes.Count(n => n.Kind == NavNodeKind.Outside));
        var report = new ValidationReport();
        NavGraphBuilder.Validate(result, "settlement.test", report);
        Assert.Empty(report.Issues);
    }

    [Fact]
    public void Paths_BetweenRandomDoors_AreNeverShorterThanTheStraightLine_AndNeverCrossABuilding()
    {
        City city = Make(3, 120, SettlementLayout.Radial);
        NavGraphResult result = NavGraphBuilder.Build(city);
        NavGraph graph = result.Graph;
        var finder = new NavPathFinder(graph);
        var rng = new Rng(11);
        var buildings = city.AllBuildings.ToList();
        var path = new List<int>();

        for (int k = 0; k < 40; k++)
        {
            int a = graph.DoorOf(buildings[rng.NextInt(buildings.Count)].Id);
            int b = graph.DoorOf(buildings[rng.NextInt(buildings.Count)].Id);
            Assert.True(finder.TryFindPath(a, b, path, out double length));

            double straight = Math.Sqrt(Math.Pow(graph.Nodes[a].X - graph.Nodes[b].X, 2) + Math.Pow(graph.Nodes[a].Z - graph.Nodes[b].Z, 2));
            Assert.True(length >= straight - 1e-6);
            for (int i = 0; i + 1 < path.Count; i++)
            {
                Vec2 from = new(graph.Nodes[path[i]].X, graph.Nodes[path[i]].Z);
                Vec2 to = new(graph.Nodes[path[i + 1]].X, graph.Nodes[path[i + 1]].Z);
                for (int s = 0; s <= 20; s++)
                {
                    Vec2 p = Vec2.Lerp(from, to, s / 20.0);
                    Assert.All(buildings, building => Assert.False(Contains(building.Footprint, p), $"the walk enters building {building.Id} at {p}"));
                }
            }
        }
    }

    [Fact]
    public void ARiverSplitsTheCity_AndOnlyTheBridgesJoinTheBanks()
    {
        var river = new Strip(45, 18);
        City city = Make(4, 330, SettlementLayout.Radial, river);
        Assert.NotEmpty(city.Bridges);

        NavGraphResult result = NavGraphBuilder.Build(city, river);

        Assert.Empty(result.UnreachableBuildings);
        NavGraph graph = result.Graph;
        // No ordinary link crosses the river; bridge links do.
        foreach (NavEdge edge in graph.Edges.Where(e => !e.IsBridge))
        {
            for (int s = 0; s <= 20; s++)
            {
                double x = graph.Nodes[edge.A].X + ((graph.Nodes[edge.B].X - graph.Nodes[edge.A].X) * s / 20.0);
                Assert.False(Math.Abs(x - 45) <= 18, $"a link {edge.A}-{edge.B} crosses the river: {graph.Nodes[edge.A]} to {graph.Nodes[edge.B]}");
            }
        }

        Assert.Equal(city.Bridges.Count, graph.Edges.Count(e => e.IsBridge));
        // A walk from a house west of the river to one east of it uses a bridge.
        var west = city.AllBuildings.First(b => b.Footprint.Centroid.X < 45 - 30);
        var east = city.AllBuildings.First(b => b.Footprint.Centroid.X > 45 + 30);
        var path = new List<int>();
        Assert.True(new NavPathFinder(graph).TryFindPath(graph.DoorOf(west.Id), graph.DoorOf(east.Id), path, out _));
        Assert.Contains(Enumerable.Range(0, path.Count - 1), i => graph.Edges.Any(e => e.IsBridge && ((e.A == path[i] && e.B == path[i + 1]) || (e.B == path[i] && e.A == path[i + 1]))));
    }

    [Fact]
    public void SameCity_SameGraph()
    {
        NavGraph a = NavGraphBuilder.Build(Make(2, 80, SettlementLayout.Radial)).Graph;
        NavGraph b = NavGraphBuilder.Build(Make(2, 80, SettlementLayout.Radial)).Graph;

        Assert.Equal(a.Nodes, b.Nodes);
        Assert.Equal(a.Edges, b.Edges);
    }

    [Fact]
    public void TheValidator_ReportsUnreachableDoorsAndGates()
    {
        City city = Make(1, 40, SettlementLayout.Radial);
        NavGraphResult good = NavGraphBuilder.Build(city);
        var bad = new NavGraphResult(good.Graph, new[] { 7 }, new[] { 0 });
        var report = new ValidationReport();

        NavGraphBuilder.Validate(bad, "settlement.test", report);

        Assert.Contains(report.Issues, i => i.Code == NavCodes.DoorUnreachable);
        Assert.Contains(report.Issues, i => i.Code == NavCodes.GateUnreachable);
    }

    private static bool Contains(ConvexPoly poly, Vec2 p)
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
            if (sign * Vec2.Cross(e, p - a) < -1e-9)
            {
                return false;
            }
        }

        return true;
    }
}
