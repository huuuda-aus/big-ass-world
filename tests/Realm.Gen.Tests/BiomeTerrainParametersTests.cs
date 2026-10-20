using System.Linq;
using Realm.Core.Validation;
using Realm.Data.Content;
using Realm.Gen.Terrain;
using Xunit;
using static Realm.Gen.Tests.TerrainTestHelpers;

namespace Realm.Gen.Tests;

public class BiomeTerrainParametersTests
{
    private static (BiomeTerrainParameters? Parameters, ValidationReport Report) Read(BiomeDefinition biome)
    {
        var report = new ValidationReport();
        return (BiomeTerrainParameters.TryFrom(biome, report), report);
    }

    [Fact]
    public void LandBiome_ReadsAllThree()
    {
        var (p, report) = Read(Land("biome.hills", 120, 80, 0.5));

        Assert.Empty(report.Issues);
        Assert.Equal(120, p!.BaseHeightMeters);
        Assert.Equal(80, p.ReliefMeters);
        Assert.Equal(0.5, p.Roughness);
        Assert.False(p.IsOcean);
    }

    [Fact]
    public void OceanBiome_BaseHeightIsMinusWaterDepth()
    {
        var (p, report) = Read(Sea("biome.sea", 200, 30, 0.3));

        Assert.Empty(report.Issues);
        Assert.Equal(-200, p!.BaseHeightMeters);
        Assert.True(p.IsOcean);
    }

    [Fact]
    public void LandBaseHeight_MayBeNegative_AndRoughnessBoundsAreInclusive()
    {
        Assert.Empty(Read(Land("biome.basin", -20, 5, 0)).Report.Issues);
        Assert.Empty(Read(Land("biome.spiky", 10, 5, 1)).Report.Issues);
        Assert.Empty(Read(Land("biome.flat", 10, 0, 0.5)).Report.Issues);
    }

    [Fact]
    public void MissingProperties_AreEachReported()
    {
        var (p, report) = Read(Biome("biome.empty", false));

        Assert.Null(p);
        Assert.Equal(3, report.Issues.Count(i => i.Code == TerrainCodes.BiomeMissingProperty));
        Assert.Contains(report.Issues, i => i.Message.Contains("'base_height_m'"));
        Assert.Contains(report.Issues, i => i.Message.Contains("'relief_m'"));
        Assert.Contains(report.Issues, i => i.Message.Contains("'roughness'"));
    }

    [Fact]
    public void OceanMissingWaterDepth_IsReported()
    {
        var (p, report) = Read(Biome("biome.sea", true, ("relief_m", 10), ("roughness", 0.2)));

        Assert.Null(p);
        ValidationIssue issue = Assert.Single(report.Issues);
        Assert.Equal(TerrainCodes.BiomeMissingProperty, issue.Code);
        Assert.Contains("'water_depth_m'", issue.Message);
    }

    [Theory]
    [InlineData(1.5)]
    [InlineData(-0.1)]
    public void RoughnessOutsideZeroToOne_IsRejected(double roughness)
    {
        var (p, report) = Read(Land("biome.x", 0, 10, roughness));

        Assert.Null(p);
        Assert.Equal(TerrainCodes.BiomePropertyOutOfRange, Assert.Single(report.Issues).Code);
    }

    [Fact]
    public void NegativeRelief_IsRejected()
    {
        var (p, report) = Read(Land("biome.x", 0, -1, 0.5));

        Assert.Null(p);
        Assert.Equal(TerrainCodes.BiomePropertyOutOfRange, Assert.Single(report.Issues).Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void NonPositiveWaterDepth_IsRejected(double depth)
    {
        var (p, report) = Read(Sea("biome.x", depth, 10, 0.5));

        Assert.Null(p);
        Assert.Equal(TerrainCodes.BiomePropertyOutOfRange, Assert.Single(report.Issues).Code);
    }

    [Fact]
    public void ProblemsInSeveralProperties_AreAllReported()
    {
        var (p, report) = Read(Biome("biome.bad", false, ("relief_m", -1), ("roughness", 2)));

        Assert.Null(p);
        Assert.Equal(3, report.Issues.Count); // negative relief, roughness range, missing base height
    }

    [Fact]
    public void InapplicableProperty_IsAWarning_AndIgnored()
    {
        var land = Biome("biome.land", false, ("base_height_m", 50), ("relief_m", 5), ("roughness", 0.1), ("water_depth_m", 30));
        var sea = Biome("biome.sea", true, ("water_depth_m", 90), ("relief_m", 5), ("roughness", 0.1), ("base_height_m", 70));

        var (landParams, landReport) = Read(land);
        var (seaParams, seaReport) = Read(sea);

        Assert.Equal(50, landParams!.BaseHeightMeters);
        Assert.Equal(Severity.Warning, Assert.Single(landReport.Issues).Severity);
        Assert.Equal(-90, seaParams!.BaseHeightMeters);
        Assert.Equal(TerrainCodes.BiomePropertyIgnored, Assert.Single(seaReport.Issues).Code);
    }

    [Fact]
    public void OptionalProperties_DefaultToNothing()
    {
        BiomeTerrainParameters p = BiomeTerrainParameters.TryFrom(Land("biome.plain", 10, 5, 0.5), new ValidationReport())!;

        Assert.Equal(0, p.Ridge);
        Assert.Equal(0, p.Terrace);
        Assert.Empty(p.Landmarks);
    }

    [Fact]
    public void RidgeTerraceAndLandmarks_AreRead()
    {
        BiomeDefinition biome = Biome("biome.rugged", false,
            ("base_height_m", 100), ("relief_m", 50), ("roughness", 0.5), ("ridge", 0.7), ("terrace", 0.2),
            ("hill_density", 8), ("hill_height_m", 90), ("hill_radius_m", 600),
            ("basin_density", 2), ("basin_height_m", 20), ("basin_radius_m", 400));

        BiomeTerrainParameters p = BiomeTerrainParameters.TryFrom(biome, new ValidationReport())!;

        Assert.Equal(0.7, p.Ridge);
        Assert.Equal(0.2, p.Terrace);
        Assert.Equal(
            new[] { new LandmarkSpec(LandmarkKind.Hill, 8, 90, 600), new LandmarkSpec(LandmarkKind.Basin, 2, 20, 400) },
            p.Landmarks);
    }

    [Theory]
    [InlineData("ridge", 1.5)]
    [InlineData("terrace", -0.1)]
    [InlineData("hill_density", -1)]
    [InlineData("mountain_density", 500)]
    public void OptionalPropertiesOutOfRange_AreRejected(string name, double value)
    {
        var report = new ValidationReport();

        Assert.Null(BiomeTerrainParameters.TryFrom(Biome("biome.bad", false, ("base_height_m", 1), ("relief_m", 1), ("roughness", 0.5), (name, value)), report));
        Assert.Equal(TerrainCodes.BiomePropertyOutOfRange, Assert.Single(report.Issues).Code);
    }

    [Fact]
    public void Landmark_NeedsItsHeightAndRadius_WhenItHasADensity()
    {
        var report = new ValidationReport();

        Assert.Null(BiomeTerrainParameters.TryFrom(Biome("biome.bad", false, ("base_height_m", 1), ("relief_m", 1), ("roughness", 0.5), ("mountain_density", 3)), report));
        Assert.Equal(2, report.Issues.Count(i => i.Code == TerrainCodes.BiomeMissingProperty));
    }

    [Fact]
    public void Landmark_RadiusBelowTheMinimum_IsRejected()
    {
        var report = new ValidationReport();

        Assert.Null(BiomeTerrainParameters.TryFrom(Biome("biome.bad", false, ("base_height_m", 1), ("relief_m", 1), ("roughness", 0.5),
            ("hill_density", 3), ("hill_height_m", 10), ("hill_radius_m", 10)), report));
        Assert.Equal(TerrainCodes.BiomePropertyOutOfRange, Assert.Single(report.Issues).Code);
    }

    [Fact]
    public void OceanBiomes_IgnoreLandmarks()
    {
        BiomeDefinition sea = Biome("biome.sea", true, ("water_depth_m", 50), ("relief_m", 5), ("roughness", 0.3), ("hill_density", 5), ("hill_height_m", 10), ("hill_radius_m", 200));

        Assert.Empty(BiomeTerrainParameters.TryFrom(sea, new ValidationReport())!.Landmarks);
    }
}
