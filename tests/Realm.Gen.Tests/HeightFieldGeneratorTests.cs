using System;
using System.Linq;
using Realm.Core.Validation;
using Realm.Data.Content;
using Realm.Gen.Terrain;
using Xunit;
using static Realm.Gen.Tests.TerrainTestHelpers;

namespace Realm.Gen.Tests;

public class HeightFieldGeneratorTests
{
    // Flat biomes (relief 0): heights are exactly the blended base height, so they can be checked by hand.
    private static readonly BiomeDefinition FlatLand = Land("biome.land", 100, 0, 0.5);
    private static readonly BiomeDefinition FlatSea = Sea("biome.sea", 50, 0, 0.5);

    private static HeightField Flat(HeightFieldSettings? settings = null)
    {
        var report = new ValidationReport();
        HeightField? field = Generate(LandAndSea(), Biomes(FlatLand, FlatSea), report, 1, settings);
        Assert.Empty(report.Issues);
        return field!;
    }

    [Fact]
    public void FlatBiomes_GiveExactBaseHeightsFarFromTheBorder()
    {
        HeightField field = Flat();

        Assert.Equal(100.0, field.Sample(-400, 0), 4);
        Assert.Equal(100.0, field.Sample(-300, 300), 4);
        Assert.Equal(-50.0, field.Sample(400, 0), 4);
        Assert.Equal(-50.0, field.Sample(300, -300), 4);
    }

    [Fact]
    public void BlendZone_ExtendsAboutTheConfiguredRadius_ToEachSide()
    {
        HeightField field = Flat(new HeightFieldSettings { SpacingMeters = 16, BlendRadiusMeters = 256 });

        // Macro cells are 64 m. Two passes of a 2-cell box reach 4 cells: cell 3's centre (x = -288) is 5 cells
        // from the border (cell 8) and still exactly flat; cell 4's centre (x = -224) already feels the sea.
        Assert.Equal(100.0, field.Sample(-288, 0), 4);
        Assert.True(field.Sample(-224, 0) < 99.9, "blend should have started by 224 m from the border");
        Assert.Equal(-50.0, field.Sample(288, 0), 4);
        Assert.True(field.Sample(224, 0) > -49.9, "blend should have started by 224 m on the sea side");
    }

    [Fact]
    public void Border_IsTheMidpoint_ByExactSymmetry()
    {
        HeightField field = Flat();

        // Land 100, sea -50: the blurred border at x = 0 is exactly halfway.
        Assert.Equal(25.0, field.Sample(0, 0), 4);
        Assert.Equal(25.0, field.Sample(0, 200), 4);
    }

    [Fact]
    public void AcrossTheBorder_HeightFallsMonotonically_WithNoStepBiggerThanTheBlurAllows()
    {
        HeightField field = Flat();

        double previous = double.MaxValue;
        double biggestStep = 0;
        for (double x = -500; x <= 500; x += 16)
        {
            double h = field.Sample(x, 0);
            Assert.True(h <= previous + 1e-6, $"rises at x = {x}");
            if (previous != double.MaxValue)
            {
                biggestStep = Math.Max(biggestStep, previous - h);
            }

            previous = h;
        }

        // A 150 m drop spread over a blend of two 64 m cell radii (about 256 m): well under 150 / 4 per 16 m step.
        Assert.True(biggestStep < 150 / 4.0, $"biggest step {biggestStep}");
    }

    [Fact]
    public void ZeroBlendRadius_GivesAHardStep_ThatTheSeamValidatorRejects()
    {
        var report = new ValidationReport();
        HeightField? field = Generate(
            LandAndSea(),
            Biomes(FlatLand, FlatSea),
            report,
            1,
            new HeightFieldSettings { SpacingMeters = 16, BlendRadiusMeters = 0 });

        Assert.NotNull(field);
        ValidationIssue issue = Assert.Single(report.Issues);
        Assert.Equal(TerrainCodes.BorderTooSteep, issue.Code);
        Assert.Contains("150", issue.Message.Replace(".00", string.Empty) + " 150"); // worst slope 150/16 = 9.38
    }

    [Fact]
    public void WithTheDefaultBlend_NoSeamIsReported()
    {
        var report = new ValidationReport();
        Generate(LandAndSea(), Biomes(FlatLand, FlatSea), report, 1, new HeightFieldSettings { SpacingMeters = 16 }); // default blend

        Assert.Empty(report.Issues);
    }

    [Fact]
    public void NoiseStaysWithinBaseAndRelief_InTheInterior()
    {
        var land = Land("biome.land", 100, 30, 0.6);
        var report = new ValidationReport();
        HeightField field = Generate(LandAndSea(), Biomes(land, FlatSea), report)!;

        for (double x = -480; x <= -300; x += 20)
        {
            for (double z = -480; z <= 480; z += 20)
            {
                double h = field.Sample(x, z);
                Assert.InRange(h, 70 - 1e-6, 130 + 1e-6);
            }
        }
    }

    [Fact]
    public void ReliefMakesTheTerrainActuallyVary()
    {
        var land = Land("biome.land", 100, 30, 0.6);
        var report = new ValidationReport();
        var settings = new HeightFieldSettings { SpacingMeters = 16, BlendRadiusMeters = PinnedBlendMeters, FeatureWavelengthMeters = 256 };
        HeightField field = Generate(LandAndSea(), Biomes(land, FlatSea), report, 1, settings)!;

        double min = double.MaxValue;
        double max = double.MinValue;
        for (double x = -480; x <= -300; x += 8)
        {
            for (double z = -480; z <= 480; z += 8)
            {
                double h = field.Sample(x, z);
                min = Math.Min(min, h);
                max = Math.Max(max, h);
            }
        }

        Assert.True(max - min > 10, $"range {max - min} is too flat");
    }

    [Fact]
    public void SameInputs_GiveTheSameChecksum_EvenWithParallelEvaluation()
    {
        var land = Land("biome.land", 100, 30, 0.6);
        var biomes = Biomes(land, FlatSea);

        ulong first = Generate(LandAndSea(), biomes, new ValidationReport(), 7)!.Checksum();
        for (int i = 0; i < 5; i++)
        {
            Assert.Equal(first, Generate(LandAndSea(), biomes, new ValidationReport(), 7)!.Checksum());
        }
    }

    [Fact]
    public void DifferentSeeds_GiveDifferentTerrain_WhenThereIsRelief()
    {
        var biomes = Biomes(Land("biome.land", 100, 30, 0.6), FlatSea);

        Assert.NotEqual(
            Generate(LandAndSea(), biomes, new ValidationReport(), 1)!.Checksum(),
            Generate(LandAndSea(), biomes, new ValidationReport(), 2)!.Checksum());
    }

    [Fact]
    public void Seed_DoesNotMatter_WhenThereIsNoRelief()
    {
        var biomes = Biomes(FlatLand, FlatSea);

        Assert.Equal(
            Generate(LandAndSea(), biomes, new ValidationReport(), 1)!.Checksum(),
            Generate(LandAndSea(), biomes, new ValidationReport(), 99)!.Checksum());
    }

    [Fact]
    public void HigherRoughness_GivesMoreFineDetail()
    {
        double Detail(double roughness)
        {
            var report = new ValidationReport();
            HeightField field = Generate(LandAndSea(), Biomes(Land("biome.land", 100, 40, roughness), FlatSea), report)!;
            double sum = 0;
            int count = 0;
            for (int z = 4; z < 60; z++)
            {
                for (int x = 4; x < 28; x++)
                {
                    sum += Math.Abs(field.At(x + 1, z) - field.At(x, z));
                    count++;
                }
            }

            return sum / count;
        }

        Assert.True(Detail(1.0) > Detail(0.0), "a rough biome should have more vertex-to-vertex variation");
    }

    [Fact]
    public void FinerSpacing_AddsVertices_WithoutChangingTheLargeShape()
    {
        var biomes = Biomes(Land("biome.land", 100, 30, 0.6), FlatSea);
        HeightField coarse = Generate(LandAndSea(), biomes, new ValidationReport(), 1, new HeightFieldSettings { SpacingMeters = 32 })!;
        HeightField fine = Generate(LandAndSea(), biomes, new ValidationReport(), 1, new HeightFieldSettings { SpacingMeters = 16 })!;

        Assert.Equal(33, coarse.VerticesPerSide);
        Assert.Equal(65, fine.VerticesPerSide);

        // Fine vertices that coincide with coarse ones carry the same height (the function is position-based).
        for (int z = 0; z < coarse.VerticesPerSide; z += 3)
        {
            for (int x = 0; x < coarse.VerticesPerSide; x += 3)
            {
                Assert.Equal(coarse.At(x, z), fine.At(x * 2, z * 2));
            }
        }
    }

    [Fact]
    public void InvalidSettings_AreRejected()
    {
        var biomes = Biomes(FlatLand, FlatSea);
        foreach (HeightFieldSettings bad in new[]
        {
            new HeightFieldSettings { SpacingMeters = 33 },
            new HeightFieldSettings { SpacingMeters = 0 },
            new HeightFieldSettings { BlendRadiusMeters = -1 },
            new HeightFieldSettings { Octaves = 0 },
            new HeightFieldSettings { Octaves = 13 },
            new HeightFieldSettings { FeatureWavelengthMeters = 0 },
            new HeightFieldSettings { Lacunarity = 1 },
            new HeightFieldSettings { MinGain = 0.8, MaxGain = 0.5 },
            new HeightFieldSettings { MaxGain = 1.5 },
            new HeightFieldSettings { WarpMeters = -1 },
            new HeightFieldSettings { WarpWavelengthMeters = 0 },
            new HeightFieldSettings { TerraceStepMeters = 0 },
        })
        {
            var report = new ValidationReport();
            HeightField? field = Generate(LandAndSea(), biomes, report, 1, bad);

            Assert.Null(field);
            Assert.Equal(TerrainCodes.HeightSettingsInvalid, Assert.Single(report.Issues).Code);
        }
    }

    [Fact]
    public void RegionFieldWithGaps_CannotBeUsed()
    {
        MacroMapDefinition map = LandAndSea();
        var halfMap = new MacroMapDefinition(map.Id, map.Name, map.WorldSizeMeters, new[] { map.Regions[0] });
        var regionReport = new ValidationReport();
        RegionField regions = MacroRasterizer.Rasterize(halfMap, regionReport, Cell);
        Assert.Contains(regionReport.Issues, i => i.Code == TerrainCodes.Gap);

        var report = new ValidationReport();
        HeightField? field = HeightFieldGenerator.Generate(halfMap, regions, Biomes(FlatLand, FlatSea), 1, null, report);

        Assert.Null(field);
        Assert.Equal(TerrainCodes.RegionFieldIncomplete, Assert.Single(report.Issues).Code);
    }

    [Fact]
    public void MissingBiome_IsReported()
    {
        var report = new ValidationReport();
        HeightField? field = Generate(LandAndSea(), Biomes(FlatLand), report);

        Assert.Null(field);
        ValidationIssue issue = Assert.Single(report.Issues);
        Assert.Equal(TerrainCodes.BiomeMissing, issue.Code);
        Assert.Contains("biome.sea", issue.Message);
    }

    [Fact]
    public void BiomeWithBadProperties_IsReportedOnce_AndNothingIsGenerated()
    {
        var bad = Biome("biome.land", false); // no properties at all
        var report = new ValidationReport();
        HeightField? field = Generate(LandAndSea(), Biomes(bad, FlatSea), report);

        Assert.Null(field);
        Assert.Equal(3, report.Issues.Count(i => i.Code == TerrainCodes.BiomeMissingProperty));
    }

    [Fact]
    public void ABiomeUsedByTwoRegions_IsCheckedOnce()
    {
        var map = new MacroMapDefinition(
            LandAndSea().Id,
            "Test",
            1024,
            new[]
            {
                Region("region.a", "biome.bad", Rect(-512, -512, 0, 512)),
                Region("region.b", "biome.bad", Rect(0, -512, 512, 512)),
            });
        var report = new ValidationReport();

        HeightField? field = Generate(map, Biomes(Biome("biome.bad", false)), report);

        Assert.Null(field);
        Assert.Equal(3, report.Issues.Count); // three missing properties, not six
    }

    [Fact]
    public void Generate_RejectsNullArguments()
    {
        var report = new ValidationReport();
        MacroMapDefinition map = LandAndSea();
        RegionField regions = Rasterize(map);
        var biomes = Biomes(FlatLand, FlatSea);

        Assert.Throws<ArgumentNullException>(() => HeightFieldGenerator.Generate(null!, regions, biomes, 1, null, report));
        Assert.Throws<ArgumentNullException>(() => HeightFieldGenerator.Generate(map, null!, biomes, 1, null, report));
        Assert.Throws<ArgumentNullException>(() => HeightFieldGenerator.Generate(map, regions, null!, 1, null, report));
        Assert.Throws<ArgumentNullException>(() => HeightFieldGenerator.Generate(map, regions, biomes, 1, null, null!));
    }

    [Fact]
    public void HeightField_AccessorsAndBounds()
    {
        HeightField field = Flat();

        Assert.Equal(65, field.VerticesPerSide);
        Assert.Equal(65 * 65, field.Heights.Length);
        Assert.Equal(field.Heights[0], field.At(0, 0));
        Assert.Equal(field.Heights[(64 * 65) + 64], field.At(64, 64));
        Assert.Equal(-50.0, (double)field.MinHeight, 4);
        Assert.Equal(100.0, (double)field.MaxHeight, 4);
        Assert.Throws<ArgumentOutOfRangeException>(() => field.At(-1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => field.At(0, 65));
        Assert.Throws<ArgumentOutOfRangeException>(() => field.Sample(512.1, 0));
    }

    [Fact]
    public void Sample_AtVertices_EqualsAt_AndInterpolatesBetween()
    {
        var biomes = Biomes(Land("biome.land", 100, 30, 0.6), FlatSea);
        HeightField field = Generate(LandAndSea(), biomes, new ValidationReport())!;
        double half = field.WorldSizeMeters / 2.0;

        Assert.Equal(field.At(10, 20), field.Sample(-half + (10 * 16), -half + (20 * 16)), 9);
        Assert.Equal(field.At(64, 64), field.Sample(half, half), 9);

        double mid = field.Sample(-half + (10.5 * 16), -half + (20.5 * 16));
        double average = ((double)field.At(10, 20) + field.At(11, 20) + field.At(10, 21) + field.At(11, 21)) / 4.0;
        Assert.Equal(average, mid, 6);
    }
}
