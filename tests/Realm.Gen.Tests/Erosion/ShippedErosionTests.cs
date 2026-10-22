using System;
using System.IO;
using System.Linq;
using Realm.Core.Ids;
using Realm.Core.Validation;
using Realm.Data.Content;
using Realm.Gen.Terrain;
using Realm.Gen.Terrain.Erosion;
using Xunit;

namespace Realm.Gen.Tests.Erosion;

public class ShippedErosionTests
{
    private const int Spacing = 128; // field and working grid, so the test runs in about a second

    private sealed record Result(HeightField Source, ErodedTerrain Eroded, RegionField Regions, ContentDatabase Db);

    private static Result Run(ValidationReport report, ulong seed = 1)
    {
        ContentDatabase db = ContentLoader.Load(Path.Combine(RepoRoot(), "content"), report);
        MacroMapDefinition map = db.MacroMaps[ContentId.Parse("world.main")];
        RegionField regions = MacroRasterizer.Rasterize(map, report);
        HeightField? source = HeightFieldGenerator.Generate(
            map, regions, db.Biomes, seed, new HeightFieldSettings { SpacingMeters = Spacing }, report);
        Assert.NotNull(source);
        ErodedTerrain? eroded = ErosionGenerator.Apply(
            map, regions, db.Biomes, source!, new ErosionSettings { WorkingSpacingMeters = Spacing }, report);
        Assert.NotNull(eroded);
        return new Result(source!, eroded!, regions, db);
    }

    [Fact]
    public void ShippedWorld_ErodesWithoutIssues()
    {
        var report = new ValidationReport();
        Run(report);

        Assert.Empty(report.Issues);
    }

    [Fact]
    public void SeaFloor_FarFromTheCoast_IsUntouched()
    {
        Result r = Run(new ValidationReport());
        double half = r.Source.WorldSizeMeters / 2.0;

        // Everything beyond 1,500 m of the land square (|x| or |z| above 9,500 m) is open sea.
        int checkedVertices = 0;
        for (int vz = 0; vz < r.Source.VerticesPerSide; vz++)
        {
            for (int vx = 0; vx < r.Source.VerticesPerSide; vx++)
            {
                double x = -half + (vx * (double)Spacing);
                double z = -half + (vz * (double)Spacing);
                if (Math.Abs(x) > 9500 || Math.Abs(z) > 9500)
                {
                    Assert.Equal(r.Source.At(vx, vz), r.Eroded.Field.At(vx, vz));
                    checkedVertices++;
                }
            }
        }

        Assert.True(checkedVertices > 100);
    }

    [Fact]
    public void RiversFormANetwork_ThatReachesTheSea_AndStaysOnLand()
    {
        Result r = Run(new ValidationReport());
        RiverNetwork network = r.Eroded.Network;

        Assert.NotEmpty(network.Rivers);
        Assert.Contains(network.Rivers, river => river.Mouth == RiverMouth.Sea);
        Assert.Contains(network.Rivers, river => river.Mouth == RiverMouth.Confluence);

        foreach (River river in network.Rivers)
        {
            Assert.True(river.Points.Count >= 2);
            Assert.True(river.LengthMeters > 0);
            // A river's source is on land.
            Assert.True(r.Eroded.Field.Sample(river.Points[0].X, river.Points[0].Z) > 0, $"river {river.Index} starts at sea");
        }
    }

    [Fact]
    public void Lakes_SitAboveSeaLevel_AndAreLargeEnough()
    {
        Result r = Run(new ValidationReport());

        Assert.All(r.Eroded.Network.Lakes, lake =>
        {
            Assert.True(lake.SurfaceHeight > 0);
            Assert.True(lake.AreaSquareKilometres >= ErosionSettings.DefaultMinLakeAreaKm2);
        });
    }

    [Fact]
    public void DryRegions_GetFewerRiversThanWetOnes()
    {
        Result r = Run(new ValidationReport());
        double half = r.Source.WorldSizeMeters / 2.0;

        // River length per region, counted by the region each river vertex lies in.
        double LengthIn(string region)
        {
            double total = 0;
            foreach (River river in r.Eroded.Network.Rivers)
            {
                for (int i = 1; i < river.Points.Count; i++)
                {
                    RiverPoint a = river.Points[i - 1];
                    RiverPoint b = river.Points[i];
                    double x = Math.Clamp(b.X, -half, half - 1);
                    double z = Math.Clamp(b.Z, -half, half - 1);
                    if (r.Regions.RegionAt(x, z).Value == region)
                    {
                        total += Math.Sqrt(((b.X - a.X) * (b.X - a.X)) + ((b.Z - a.Z) * (b.Z - a.Z)));
                    }
                }
            }

            return total;
        }

        double RegionAreaKm2(string region) =>
            r.Regions.CellCount(ContentId.Parse(region)) * (double)r.Regions.CellMeters * r.Regions.CellMeters / 1_000_000.0;

        // Compare river length per km2 between a wet biome (marsh, rainfall 0.8) and a dry one (badlands, 0.1).
        double wet = LengthIn("region.saltmarsh") / RegionAreaKm2("region.saltmarsh");
        double dry = LengthIn("region.redrock_badlands") / RegionAreaKm2("region.redrock_badlands");
        Assert.True(wet > dry, $"marsh {wet:F1} m/km2 should exceed badlands {dry:F1} m/km2");
    }

    [Fact]
    public void Gorges_OnlyEverLowerTheGround_AndDoLowerSome()
    {
        ContentDatabase db = ContentLoader.Load(Path.Combine(RepoRoot(), "content"), new ValidationReport());
        MacroMapDefinition map = db.MacroMaps[ContentId.Parse("world.main")];
        RegionField regions = MacroRasterizer.Rasterize(map, new ValidationReport());
        HeightField source = HeightFieldGenerator.Generate(
            map, regions, db.Biomes, 1, new HeightFieldSettings { SpacingMeters = Spacing }, new ValidationReport())!;

        ErodedTerrain With(double fullFlowKm2) => ErosionGenerator.Apply(
            map, regions, db.Biomes, source, new ErosionSettings { WorkingSpacingMeters = Spacing, GorgeFullFlowKm2 = fullFlowKm2 }, new ValidationReport())!;

        // A full-depth flow of a million km2 is out of reach, so that run has (practically) no gorges.
        HeightField none = With(1_000_000).Field;
        HeightField gorges = With(ErosionSettings.DefaultGorgeFullFlowKm2).Field;

        int lower = 0;
        for (int i = 0; i < gorges.Heights.Length; i++)
        {
            Assert.True(gorges.Heights[i] <= none.Heights[i] + 0.01f, $"vertex {i} is higher with gorges");
            lower += gorges.Heights[i] < none.Heights[i] - 1 ? 1 : 0;
        }

        Assert.True(lower > 0, "the shipped world's big rivers in rocky biomes should have cut gorges");
    }

    [Fact]
    public void SameSeed_GivesTheSameResult_DifferentSeedDoesNot()
    {
        ulong a = Run(new ValidationReport(), 1).Eroded.Checksum();

        Assert.Equal(a, Run(new ValidationReport(), 1).Eroded.Checksum());
        Assert.NotEqual(a, Run(new ValidationReport(), 2).Eroded.Checksum());
    }

    [Fact]
    public void ShippedWorld_Erosion_Checksum_IsStableGolden()
    {
        // Golden: a change means the erosion output changed. If intended, bump ErosionGenerator.GeneratorVersion,
        // update this value and note it in the CHANGELOG.
        Assert.Equal(GoldenChecksum, Run(new ValidationReport()).Eroded.Checksum());
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

    // Captured from the accepted implementation (erosion generator version 2, 128 m grid, seed 1, six-region sample world).
    private const ulong GoldenChecksum = 12357116102927642745UL;
}
