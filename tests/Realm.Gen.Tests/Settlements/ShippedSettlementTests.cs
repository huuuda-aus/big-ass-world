using System;
using System.IO;
using System.Linq;
using Realm.Core.Ids;
using Realm.Core.Validation;
using Realm.Data.Content;
using Realm.Gen.Settlements.Generation;
using Realm.Gen.Settlements.Meshing;
using Realm.Gen.Settlements.Geometry;
using Realm.Gen.Settlements.Model;
using Realm.Gen.Settlements.Navigation;
using Realm.Gen.Settlements.Placement;
using Realm.Gen.Settlements.Roads;
using Realm.Gen.Terrain;
using Realm.Gen.Terrain.Erosion;
using Xunit;

namespace Realm.Gen.Tests.Settlements;

public class ShippedSettlementTests
{
    private const int Spacing = 128; // field and working grid, so the test runs in a few seconds

    private sealed record Result(HeightField Flattened, ErodedTerrain Eroded, System.Collections.Generic.IReadOnlyList<Settlement> Settlements, System.Collections.Generic.IReadOnlyList<Road> Roads, HeightField Graded, ValidationReport Report);

    // The pipeline takes several seconds, so the tests that only read its result share one run.
    private static readonly Lazy<Result> Shared = new(Run);

    private static Result Run()
    {
        var report = new ValidationReport();
        ContentDatabase db = ContentLoader.Load(Path.Combine(RepoRoot(), "content"), report);
        MacroMapDefinition map = db.MacroMaps[ContentId.Parse("world.main")];
        RegionField regions = MacroRasterizer.Rasterize(map, report);
        HeightField source = HeightFieldGenerator.Generate(map, regions, db.Biomes, 1, new HeightFieldSettings { SpacingMeters = Spacing }, report)!;
        ErodedTerrain eroded = ErosionGenerator.Apply(map, regions, db.Biomes, source, new ErosionSettings { WorkingSpacingMeters = Spacing }, report)!;
        var settings = new SettlementSettings();
        var mountainRegions = map.Regions.Where(x => db.Biomes[x.Biome].Tags.Contains("mountainous")).Select(x => x.Id).ToHashSet();
        var settlements = SettlementPlacer.Place(eroded.Field, eroded.Network, 1, settings, report, (x, z) => mountainRegions.Contains(regions.RegionAt(x, z)));
        HeightField flattened = SettlementTerrain.Flatten(eroded.Field, settlements, settings);
        SettlementValidator.Validate(settlements, flattened, eroded.Network, settings, report);
        var roads = RoadPlanner.Plan(flattened, eroded.Network, settlements, null, report);
        HeightField graded = RoadTerrain.Carve(flattened, roads);
        RoadValidator.Validate(roads, flattened, graded, eroded.Network, null, report, settlements);
        return new Result(flattened, eroded, settlements, roads, graded, report);
    }

    [Fact]
    public void ShippedWorld_GetsFiveCitiesFiveTownsAndThreeHamlets_WithNoIssues()
    {
        Result r = Shared.Value;

        Assert.Empty(r.Report.Issues);
        Assert.Equal(5, r.Settlements.Count(s => s.Tier == SettlementTier.City)); // three plus the river city and the mountain city
        Assert.Equal(5, r.Settlements.Count(s => s.Tier == SettlementTier.Town));
        Assert.Equal(3, r.Settlements.Count(s => s.Tier == SettlementTier.Hamlet));
        Assert.All(r.Settlements.Where(s => s.Tier == SettlementTier.Hamlet), s => Assert.InRange(s.GroundHeightMeters, 15, 200));
    }

    [Fact]
    public void EverySettlement_HasAGeneratedName_AndAllNamesDiffer()
    {
        Result r = Shared.Value;

        Assert.Equal(r.Settlements.Count, r.Settlements.Select(s => s.Name).Distinct().Count());
        Assert.All(r.Settlements, s => Assert.Matches("^[A-Z][a-z]{4,}$", s.Name));
    }

    [Fact]
    public void Roads_JoinEverySettlement_WithShortBridgesAndNoIssues()
    {
        Result r = Shared.Value;

        Assert.Empty(r.Report.Issues);
        Assert.InRange(r.Roads.Count, 7, 40);
        var reached = new System.Collections.Generic.HashSet<string> { r.Settlements[0].Id };
        for (int round = 0; round < r.Settlements.Count; round++)
        {
            foreach (Road road in r.Roads.Where(x => reached.Contains(x.From) || reached.Contains(x.To)))
            {
                reached.Add(road.From);
                reached.Add(road.To);
            }
        }

        Assert.Equal(r.Settlements.Count, reached.Count);
        Assert.All(r.Roads.SelectMany(x => x.Bridges), b => Assert.InRange(b.LengthMeters, 1, 80));
    }

    [Fact]
    public void EverySettlement_IsOnLandAboveTheSea_WithinTheReliefLimit_OnNaturalGround()
    {
        Result r = Shared.Value;
        var settings = new SettlementSettings();

        Assert.Same(r.Eroded.Field, r.Flattened); // ADR 0014: nothing is flattened by default
        foreach (Settlement s in r.Settlements)
        {
            double highest = double.MinValue;
            double lowest = double.MaxValue;
            for (int ring = 0; ring <= 3; ring++)
            {
                for (int i = 0; i < 16; i++)
                {
                    double angle = i * Math.PI / 8;
                    double h = r.Flattened.Sample(s.X + (s.PadRadiusMeters * ring / 3.0 * Math.Cos(angle)), s.Z + (s.PadRadiusMeters * ring / 3.0 * Math.Sin(angle)));
                    highest = Math.Max(highest, h);
                    lowest = Math.Min(lowest, h);
                }
            }

            double limit = (s.Highland ? settings.MountainMaxReliefPerRadius : settings.MaxReliefPerRadius) * s.PadRadiusMeters;
            Assert.InRange(highest - lowest, 0, limit + 0.5);
            Assert.InRange(s.GroundHeightMeters, s.Highland ? settings.MountainMinHeightMeters : settings.MinHeightMeters, s.Highland ? settings.MountainMaxHeightMeters : settings.MaxHeightMeters);
        }
    }

    [Fact]
    public void ShippedWorld_HasOneMountainCity_InTheMountains()
    {
        Result r = Shared.Value;

        Settlement mountain = Assert.Single(r.Settlements, s => s.Highland);
        Assert.Equal(SettlementTier.City, mountain.Tier);
        Assert.True(mountain.GroundHeightMeters >= new SettlementSettings().MountainMinHeightMeters);
        Assert.Equal(5, r.Settlements.Count(s => s.Tier == SettlementTier.City));
    }

    [Fact]
    public void ShippedWorld_HasARiverCity_WithTheRiverRunningThroughItAndBridged()
    {
        Result r = Shared.Value;

        Settlement river = Assert.Single(r.Settlements, s => s.IsRiverCity);
        var raster = new RiverRaster(r.Graded.WorldSizeMeters, r.Eroded.Network);
        GroundGrid grid = GroundGrid.Capture(r.Graded, river, GroundGrid.DefaultSpacingMeters, raster);
        var ground = new SettlementGround(river, grid);
        City city = CityGenerator.Generate(river.ToCityParams(ground));

        // The centre is dry and the river passes within reach of it.
        Assert.False(ground.IsWater(0, 0));
        Assert.True(grid.Water.ToArray().Count(w => w) > 40, "the river does not reach the city");
        // The houses are on both banks, away from the water, and the streets are bridged.
        var houses = city.CityPatches.SelectMany(p => p.Buildings).ToList();
        Assert.True(houses.Count >= new SettlementSettings().CityBuildings * 0.6, $"only {houses.Count} houses");
        Assert.All(houses, b => Assert.False(CityGenerator.TouchesWater(b.Footprint, ground, 8), $"building {b.Id} is by the water"));
        Assert.InRange(city.Bridges.Count, 1, 2);
        Assert.All(city.Bridges, b => Assert.False(ground.IsWater(b.From.X, b.From.Y) || ground.IsWater(b.To.X, b.To.Y)));
    }

    // City plans and walking graphs of every settlement, built once and shared (each takes a second or so).
    private static readonly Lazy<(System.Collections.Generic.List<City> Cities, System.Collections.Generic.List<NavGraphResult> Graphs)> Walking = new(() =>
    {
        Result r = Shared.Value;
        var raster = new RiverRaster(r.Graded.WorldSizeMeters, r.Eroded.Network);
        var cities = new System.Collections.Generic.List<City>();
        var graphs = new System.Collections.Generic.List<NavGraphResult>();
        foreach (Settlement s in r.Settlements)
        {
            GroundGrid grid = GroundGrid.Capture(r.Graded, s, GroundGrid.DefaultSpacingMeters, raster);
            var ground = new SettlementGround(s, grid);
            City city = CityGenerator.Generate(s.ToCityParams(ground));
            cities.Add(city);
            graphs.Add(NavGraphBuilder.Build(city, ground, s.Fortification));
        }

        return (cities, graphs);
    });

    [Fact]
    public void EverySettlement_HasAWalkingGraph_ThatReachesEveryDoorAndGate()
    {
        Result r = Shared.Value;
        (var cities, var graphs) = Walking.Value;

        for (int i = 0; i < r.Settlements.Count; i++)
        {
            var report = new ValidationReport();
            NavGraphBuilder.Validate(graphs[i], r.Settlements[i].Id, report);
            Assert.True(report.Issues.Count == 0, $"{r.Settlements[i].Id}: {string.Join("; ", report.Issues.Select(x => x.Message))}");
            Assert.Equal(cities[i].AllBuildings.Count(), graphs[i].Graph.Nodes.Count(n => n.Kind == Realm.Sim.Navigation.NavNodeKind.Door));
        }
    }

    [Fact]
    public void TheRoads_JoinEverySettlementsGraph_IntoOneWalkableWorld()
    {
        Result r = Shared.Value;
        (var cities, var graphs) = Walking.Value;

        WorldNavResult world = WorldNavBuilder.Build(r.Settlements, graphs, r.Roads);

        var report = new ValidationReport();
        WorldNavBuilder.Validate(world, report);
        Assert.Empty(report.Issues);
        // A walk from a door in the first settlement to a door in the last one exists, and is no shorter than the straight line.
        Realm.Sim.Navigation.NavGraph g = world.Graph;
        WorldNavSettlement from = world.Settlements[0];
        WorldNavSettlement to = world.Settlements[^1];
        int a = from.FirstNode + graphs[0].Graph.DoorOf(cities[0].AllBuildings.First().Id);
        int b = to.FirstNode + graphs[^1].Graph.DoorOf(cities[^1].AllBuildings.First().Id);
        var path = new System.Collections.Generic.List<int>();
        Assert.True(new Realm.Sim.Navigation.NavPathFinder(g).TryFindPath(a, b, path, out double length));
        double straight = Math.Sqrt(Math.Pow(g.Nodes[a].X - g.Nodes[b].X, 2) + Math.Pow(g.Nodes[a].Z - g.Nodes[b].Z, 2));
        Assert.True(length >= straight);
        Assert.Contains(path, n => g.Nodes[n].Kind == Realm.Sim.Navigation.NavNodeKind.Road);
        // The road bridges became bridge edges on top of the cities' own.
        Assert.True(g.Edges.Count(e => e.IsBridge) > graphs.Sum(x => x.Graph.Edges.Count(e => e.IsBridge)));
    }

    [Fact]
    public void OnlySmallSettlements_AreCrossesAndOnlyBiggerTowns_HaveAPalisade()
    {
        Result r = Shared.Value;
        int small = SettlementSettings.SmallSettlementMaxBuildings;

        Assert.All(r.Settlements, s =>
        {
            Assert.Equal(s.MaxBuildings <= small ? SettlementLayout.Cross : SettlementLayout.Radial, s.Layout);
            Assert.Equal(s.Tier == SettlementTier.City ? Fortification.Stone : s.Tier == SettlementTier.Town && s.MaxBuildings > small ? Fortification.Palisade : Fortification.None, s.Fortification);
        });
        Assert.NotEmpty(r.Settlements.Where(s => s.Layout == SettlementLayout.Cross));
        Assert.All(r.Settlements.Where(s => s.Tier == SettlementTier.Hamlet), s => Assert.InRange(s.MaxBuildings, 8, 22));
        Assert.True(r.Settlements.Where(s => s.Tier == SettlementTier.Hamlet).Select(s => s.MaxBuildings).Distinct().Count() > 1, "every hamlet has the same size");
    }

    [Fact]
    public void SameSeed_SamePlacement()
    {
        Result a = Shared.Value;
        Result b = Run();

        Assert.Equal(a.Settlements, b.Settlements);
        Assert.Equal(a.Flattened.Checksum(), b.Flattened.Checksum());
        Assert.Equal(a.Graded.Checksum(), b.Graded.Checksum());
    }

    [Fact]
    public void ShippedWorld_Placement_IsStableGolden()
    {
        Result r = Shared.Value;
        var manifest = new SettlementManifest { World = "world.main", Seed = 1, Settlements = r.Settlements };

        ulong hash = 14695981039346656037UL;
        foreach (char c in manifest.ToJson())
        {
            hash = (hash ^ c) * 1099511628211UL;
        }

        Assert.Equal(GoldenChecksum, hash);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Realm.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Realm.sln not found above the test binary.");
    }

    // Captured from the accepted implementation (settlement.placement v1, 128 m grid, seed 1, six-region sample world).
    private const ulong GoldenChecksum = 680697116126850207UL;
}
