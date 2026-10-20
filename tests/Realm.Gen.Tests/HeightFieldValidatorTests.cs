using Realm.Core.Validation;
using Realm.Gen.Terrain;
using Xunit;
using static Realm.Gen.Tests.TerrainTestHelpers;

namespace Realm.Gen.Tests;

public class HeightFieldValidatorTests
{
    // A 1,024 m world at 64 m spacing: 17 x 17 vertices; land west of x = 0, sea east of it.
    private const int Spacing = 64;
    private const int Side = 17;

    private static HeightField Field(System.Func<int, int, float> height)
    {
        var heights = new float[Side * Side];
        for (int z = 0; z < Side; z++)
        {
            for (int x = 0; x < Side; x++)
            {
                heights[(z * Side) + x] = height(x, z);
            }
        }

        return new HeightField(1024, Spacing, heights);
    }

    private static ValidationReport Validate(HeightField field, double maxAbs = 4000, double maxSlope = 2)
    {
        var report = new ValidationReport();
        HeightFieldValidator.Validate(field, Rasterize(LandAndSea()), report, maxAbs, maxSlope);
        return report;
    }

    [Fact]
    public void FlatTerrain_IsValid()
    {
        Assert.Empty(Validate(Field((_, _) => 10)).Issues);
    }

    [Fact]
    public void NonFiniteHeight_IsReportedWithCountAndPlace()
    {
        HeightField field = Field((x, z) => x == 3 && z == 4 ? float.NaN : 10);

        ValidationIssue issue = Assert.Single(Validate(field).Issues);
        Assert.Equal(TerrainCodes.HeightNonFinite, issue.Code);
        Assert.Contains("1 height(s)", issue.Message);
        Assert.Contains("(-320, -256)", issue.Message); // vertex (3, 4) = -512 + 3 * 64, -512 + 4 * 64
    }

    [Fact]
    public void InfiniteHeight_IsReported()
    {
        HeightField field = Field((x, z) => x == 0 && z == 0 ? float.PositiveInfinity : 10);

        Assert.Contains(Validate(field).Issues, i => i.Code == TerrainCodes.HeightNonFinite);
    }

    [Fact]
    public void ExtremeHeights_AreReported_AgainstTheLimit()
    {
        HeightField field = Field((x, z) => x == 5 && z == 5 ? 4500 : 10);

        ValidationIssue issue = Assert.Single(Validate(field).Issues);
        Assert.Equal(TerrainCodes.HeightOutOfRange, issue.Code);
        Assert.Contains("4500", issue.Message);
        Assert.Empty(Validate(field, maxAbs: 5000).Issues);
    }

    [Fact]
    public void ExtremeDepths_AreReportedToo()
    {
        Assert.Contains(Validate(Field((_, _) => -4001f)).Issues, i => i.Code == TerrainCodes.HeightOutOfRange);
    }

    [Fact]
    public void SteepStepAcrossARegionBorder_IsReported()
    {
        // Land (x < 8) at 0 m, sea at -500 m: a 500 m step over 64 m is a slope of 7.8, far over 2.
        HeightField field = Field((x, _) => x < 8 ? 0 : -500);

        ValidationIssue issue = Assert.Single(Validate(field).Issues);
        Assert.Equal(TerrainCodes.BorderTooSteep, issue.Code);
        Assert.Contains("7.81", issue.Message);
    }

    [Fact]
    public void GentleSlopeAcrossTheBorder_IsAccepted()
    {
        HeightField field = Field((x, _) => (x - 8) * -50f); // 50 m per 64 m = 0.78 everywhere

        Assert.Empty(Validate(field).Issues);
    }

    [Fact]
    public void SteepCliffInsideOneRegion_IsNotASeam()
    {
        // A sheer step far from the border (between x = 2 and 3) belongs to the terrain, not to a seam.
        HeightField field = Field((x, _) => x < 3 ? 0 : 300);

        Assert.Empty(Validate(field).Issues);
    }

    [Fact]
    public void BorderLimit_IsConfigurable()
    {
        HeightField field = Field((x, _) => x < 8 ? 0 : -100); // slope 1.56

        Assert.Empty(Validate(field, maxSlope: 2).Issues);
        Assert.Single(Validate(field, maxSlope: 1).Issues);
    }

    [Fact]
    public void Validate_RejectsNullArguments()
    {
        HeightField field = Field((_, _) => 0);
        RegionField regions = Rasterize(LandAndSea());
        var report = new ValidationReport();

        Assert.Throws<System.ArgumentNullException>(() => HeightFieldValidator.Validate(null!, regions, report));
        Assert.Throws<System.ArgumentNullException>(() => HeightFieldValidator.Validate(field, null!, report));
        Assert.Throws<System.ArgumentNullException>(() => HeightFieldValidator.Validate(field, regions, null!));
    }
}
