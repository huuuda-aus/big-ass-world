using System;
using System.IO;
using System.Linq;
using Realm.Core.Ids;
using Realm.Core.Validation;
using Realm.Data.Content;
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
        var settlements = SettlementPlacer.Place(eroded.Field, eroded.Network, 1, settings, report);
        HeightField flattened = SettlementTerrain.Flatten(eroded.Field, settlements, settings);
        SettlementValidator.Validate(settlements, flattened, eroded.Network, settings, report);
        var roads = RoadPlanner.Plan(flattened, eroded.Network, settlements, null, report);
        HeightField graded = RoadTerrain.Carve(flattened, roads);
        RoadValidator.Validate(roads, flattened, graded, eroded.Network, null, report);
        return new Result(flattened, eroded, settlements, roads, graded, report);
    }

    [Fact]
    public void ShippedWorld_GetsThreeCitiesAndFiveTowns_WithNoIssues()
    {
        Result r = Shared.Value;

        Assert.Empty(r.Report.Issues);
        Assert.Equal(3, r.Settlements.Count(s => s.Tier == SettlementTier.City));
        Assert.Equal(5, r.Settlements.Count(s => s.Tier == SettlementTier.Town));
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
        Assert.InRange(r.Roads.Count, 7, 20);
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
    public void EverySettlement_IsOnLandAboveTheSeaAndFlat()
    {
        Result r = Shared.Value;

        foreach (Settlement s in r.Settlements)
        {
            Assert.InRange(s.GroundHeightMeters, 15, 320);
            for (int i = 0; i < 16; i++)
            {
                double angle = i * Math.PI / 8;
                Assert.Equal(s.GroundHeightMeters, r.Flattened.Sample(s.X + ((s.PadRadiusMeters - Spacing) * Math.Cos(angle)), s.Z + ((s.PadRadiusMeters - Spacing) * Math.Sin(angle))), 0.5);
            }
        }
    }

    [Fact]
    public void SameSeed_SamePlacement_AndFlattenOnlyChangesGroundNearSettlements()
    {
        Result a = Shared.Value;
        Result b = Run();

        Assert.Equal(a.Settlements, b.Settlements);
        Assert.Equal(a.Flattened.Checksum(), b.Flattened.Checksum());
        // Far from every settlement the terrain is exactly the eroded terrain.
        int side = a.Flattened.VerticesPerSide;
        double half = a.Flattened.WorldSizeMeters / 2.0;
        for (int row = 0; row < side; row += 7)
        {
            for (int col = 0; col < side; col += 7)
            {
                double x = -half + (col * (double)Spacing);
                double z = -half + (row * (double)Spacing);
                if (a.Settlements.All(s => s.DistanceTo(x, z) > s.PadRadiusMeters + 100 + 1))
                {
                    Assert.Equal(a.Eroded.Field.At(col, row), a.Flattened.At(col, row));
                }
            }
        }
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
    private const ulong GoldenChecksum = 1233424748731408796UL;
}
