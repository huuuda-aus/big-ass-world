using System.Linq;
using Realm.Core.Ids;
using Realm.Core.Validation;
using Realm.Data.Content;
using Xunit;

namespace Realm.Data.Tests;

public class FoliageParserTests
{
    private static (FoliageDefinition? Species, ValidationReport Report) Parse(string yaml)
    {
        var report = new ValidationReport();
        return (FoliageParser.Parse(yaml, "foliage/test.yaml", report), report);
    }

    [Fact]
    public void FullRecord_Parses()
    {
        var (f, report) = Parse(
            "id: foliage.oak\nname: Oak\nkind: tree\ntags: [deciduous]\nper_hectare: { biome.temperate_forest: 70, biome.marsh: 4 }\nmin_spacing_m: 4.5\n" +
            "height_range_m: [0, 650]\nmax_slope_deg: 32\nscale_range: [0.8, 1.35]\ncluster_m: 220\ncluster_strength: 0.8\ntrunk_radius_m: 0.45\n");

        Assert.Empty(report.Issues);
        Assert.Equal("foliage.oak", f!.Id.Value);
        Assert.Equal("tree", f.Kind);
        Assert.Equal(70, f.DensityIn(ContentId.Parse("biome.temperate_forest")));
        Assert.Equal(0, f.DensityIn(ContentId.Parse("biome.badlands")));
        Assert.Equal(70, f.MaxDensity);
        Assert.Equal(4.5, f.MinSpacingMeters);
        Assert.Equal((0, 650), (f.MinHeightMeters, f.MaxHeightMeters));
        Assert.Equal((0.8, 1.35), (f.MinScale, f.MaxScale));
        Assert.Equal(0.45, f.TrunkRadiusMeters);
    }

    [Fact]
    public void Defaults_AreApplied()
    {
        var (f, report) = Parse("id: foliage.shrub\nname: Shrub\nkind: bush\nper_hectare: { biome.marsh: 10 }\n");

        Assert.Empty(report.Issues);
        Assert.Equal(FoliageVocabulary.DefaultMinSpacingMeters, f!.MinSpacingMeters);
        Assert.Equal(FoliageVocabulary.DefaultMaxSlopeDegrees, f.MaxSlopeDegrees);
        Assert.Equal(0, f.TrunkRadiusMeters);
    }

    [Theory]
    [InlineData("kind: shrubbery\nper_hectare: { biome.marsh: 10 }")]
    [InlineData("kind: tree\nper_hectare: { biome.marsh: 100000 }")]
    [InlineData("kind: tree\nper_hectare: { biome.marsh: 0 }")]
    [InlineData("kind: tree\nper_hectare: { biome.marsh: 10 }\nheight_range_m: [500, 100]")]
    [InlineData("kind: tree\nper_hectare: { biome.marsh: 10 }\nscale_range: [1]")]
    [InlineData("kind: tree\nper_hectare: { biome.marsh: 10 }\nmin_spacing_m: 0.1")]
    [InlineData("kind: tree\nper_hectare: { biome.marsh: 10 }\ncluster_strength: 2")]
    [InlineData("kind: tree\nper_hectare: { biome.marsh: 10 }\nmax_slope_deg: 95")]
    public void BadFields_AreRejected(string body)
    {
        var (f, report) = Parse("id: foliage.x\nname: X\n" + body + "\n");

        Assert.Null(f);
        Assert.True(report.HasErrors);
    }

    [Fact]
    public void MissingDensity_IsAnError()
    {
        var (f, report) = Parse("id: foliage.x\nname: X\nkind: tree\n");

        Assert.Null(f);
        Assert.Contains(report.Issues, i => i.Code == ValidatorCodes.MissingField);
    }

    [Fact]
    public void GrassMayBeVeryDense_ButTreesMayNot()
    {
        Assert.NotNull(Parse("id: foliage.g\nname: G\nkind: grass\nper_hectare: { biome.marsh: 15000 }\n").Species);
        Assert.Null(Parse("id: foliage.t\nname: T\nkind: tree\nper_hectare: { biome.marsh: 15000 }\n").Species);
    }

    [Fact]
    public void UnknownField_IsReported()
    {
        var (f, report) = Parse("id: foliage.x\nname: X\nkind: tree\nper_hectare: { biome.marsh: 10 }\ncolour: green\n");

        Assert.Null(f);
        Assert.True(report.Issues.Any());
    }
}
