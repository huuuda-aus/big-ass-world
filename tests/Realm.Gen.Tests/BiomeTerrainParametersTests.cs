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
}
