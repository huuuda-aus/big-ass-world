using System.Linq;
using Realm.Core.Validation;
using Realm.Data.Content;
using Realm.Gen.Terrain;
using Realm.Gen.Terrain.Erosion;
using Xunit;
using static Realm.Gen.Tests.TerrainTestHelpers;

namespace Realm.Gen.Tests.Erosion;

public class BiomeErosionParametersTests
{
    private static BiomeDefinition WetLand(double rainfall = 0.7, double erodibility = 0.5, double slope = 40, double loss = 0.1) =>
        Biome(
            "biome.land",
            false,
            ("rainfall", rainfall),
            ("erodibility", erodibility),
            ("max_slope_deg", slope),
            ("river_loss", loss));

    private static (BiomeErosionParameters? Parameters, ValidationReport Report) Read(BiomeDefinition biome)
    {
        var report = new ValidationReport();
        return (BiomeErosionParameters.TryFrom(biome, report), report);
    }

    [Fact]
    public void LandBiome_ReadsAllFour()
    {
        var (p, report) = Read(WetLand());

        Assert.Empty(report.Issues);
        Assert.Equal(0.7, p!.Rainfall);
        Assert.Equal(0.5, p.Erodibility);
        Assert.Equal(40, p.MaxSlopeDegrees);
        Assert.Equal(0.1, p.RiverLoss);
        Assert.False(p.IsOcean);
    }

    [Fact]
    public void RangeBounds_AreInclusive_WhereStated()
    {
        Assert.Empty(Read(WetLand(rainfall: 0, erodibility: 0, loss: 0)).Report.Issues);
        Assert.Empty(Read(WetLand(rainfall: 1, erodibility: 1, slope: 90, loss: 1)).Report.Issues);
    }

    [Fact]
    public void OceanBiome_NeedsNothing_AndIsInert()
    {
        var (p, report) = Read(Sea("biome.sea", 200, 30, 0.3));

        Assert.Empty(report.Issues);
        Assert.True(p!.IsOcean);
        Assert.Equal(0, p.Rainfall);
        Assert.Equal(0, p.Erodibility);
        Assert.Equal(0, p.RiverLoss);
        Assert.Equal(BiomeErosionParameters.OceanMaxSlopeDegrees, p.MaxSlopeDegrees);
    }

    [Fact]
    public void ErosionPropertiesOnOcean_AreWarnedAndIgnored()
    {
        var sea = Biome("biome.sea", true, ("water_depth_m", 100), ("relief_m", 5), ("roughness", 0.1), ("rainfall", 0.9), ("erodibility", 0.9));

        var (p, report) = Read(sea);

        Assert.Equal(0, p!.Rainfall);
        Assert.Equal(2, report.Issues.Count);
        Assert.All(report.Issues, i => Assert.Equal(TerrainCodes.BiomePropertyIgnored, i.Code));
        Assert.All(report.Issues, i => Assert.Equal(Severity.Warning, i.Severity));
    }

    [Fact]
    public void LandBiomeMissingEverything_ReportsAllFour()
    {
        var (p, report) = Read(Biome("biome.land", false));

        Assert.Null(p);
        Assert.Equal(4, report.Issues.Count(i => i.Code == TerrainCodes.BiomeMissingProperty));
        foreach (string name in new[] { "'rainfall'", "'erodibility'", "'max_slope_deg'", "'river_loss'" })
        {
            Assert.Contains(report.Issues, i => i.Message.Contains(name));
        }
    }

    [Theory]
    [InlineData(-0.1, 0.5, 40, 0.1)]
    [InlineData(1.1, 0.5, 40, 0.1)]
    [InlineData(0.5, -0.1, 40, 0.1)]
    [InlineData(0.5, 1.5, 40, 0.1)]
    [InlineData(0.5, 0.5, 0, 0.1)]
    [InlineData(0.5, 0.5, 91, 0.1)]
    [InlineData(0.5, 0.5, 40, -0.1)]
    [InlineData(0.5, 0.5, 40, 1.1)]
    public void OutOfRangeValues_AreRejected(double rainfall, double erodibility, double slope, double loss)
    {
        var (p, report) = Read(WetLand(rainfall, erodibility, slope, loss));

        Assert.Null(p);
        Assert.Equal(TerrainCodes.BiomePropertyOutOfRange, Assert.Single(report.Issues).Code);
    }

    [Fact]
    public void SeveralProblems_AreAllReported()
    {
        var (p, report) = Read(Biome("biome.land", false, ("rainfall", 2), ("erodibility", -1), ("max_slope_deg", 40)));

        Assert.Null(p);
        Assert.Equal(3, report.Issues.Count); // two out of range, one missing river_loss
    }
}
