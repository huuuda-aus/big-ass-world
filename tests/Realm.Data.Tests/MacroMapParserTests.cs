using System.Linq;
using Realm.Core.Validation;
using Realm.Data.Content;
using Xunit;

namespace Realm.Data.Tests;

public class MacroMapParserTests
{
    private const string Valid = @"id: world.test
name: Test
world_size_m: 1000
regions:
  - id: region.a
    name: A
    biome: biome.x
    polygons:
      - [[-500, -500], [0, -500], [0, 500], [-500, 500]]
  - id: region.b
    name: B
    biome: biome.y
    polygons:
      - [[0, -500], [500, -500], [500, 500], [0, 500]]
      - [[-500, 500], [-400, 500], [-400, 400]]
";

    private static (MacroMapDefinition? Map, ValidationReport Report) Parse(string yaml)
    {
        var report = new ValidationReport();
        return (MacroMapParser.Parse(yaml, "world/test.yaml", report), report);
    }

    private static string WithRegion(string regionBody, string size = "1000") =>
        $"id: world.test\nname: Test\nworld_size_m: {size}\nregions:\n  - {regionBody}\n";

    [Fact]
    public void Valid_Map_Parses()
    {
        var (map, report) = Parse(Valid);

        Assert.Empty(report.Issues);
        Assert.Equal("world.test", map!.Id.Value);
        Assert.Equal(1000, map.WorldSizeMeters);
        Assert.Equal(new[] { "region.a", "region.b" }, map.Regions.Select(r => r.Id.Value).ToArray());
        Assert.Equal("biome.y", map.Regions[1].Biome.Value);
        Assert.Single(map.Regions[0].Polygons);
        Assert.Equal(2, map.Regions[1].Polygons.Count);
        Assert.Equal(500_000, map.Regions[0].Polygons[0].Area); // 500 m wide x 1,000 m tall
    }

    [Fact]
    public void UnknownField_IsRejected_WithLine()
    {
        var (map, report) = Parse(Valid + "extra: 1\n");

        Assert.Null(map);
        ValidationIssue issue = Assert.Single(report.Issues);
        Assert.Equal(ValidatorCodes.YamlInvalid, issue.Code);
        Assert.Contains("Unknown field 'extra'", issue.Message);
    }

    [Fact]
    public void MissingWorldSizeNameAndRegions_AreAllReported()
    {
        var (map, report) = Parse("id: world.test\n");

        Assert.Null(map);
        Assert.Equal(3, report.Issues.Count(i => i.Code == ValidatorCodes.MissingField));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    public void NonPositiveWorldSize_IsRejected(string size)
    {
        var (map, report) = Parse(WithRegion("id: region.a\n    name: A\n    biome: biome.x\n    polygons:\n      - [[0,0],[1,0],[0,1]]", size));

        Assert.Null(map);
        Assert.Contains(report.Issues, i => i.Code == ValidatorCodes.InvalidWorldSize);
    }

    [Fact]
    public void RegionMissingParts_AreEachReported()
    {
        var (map, report) = Parse(WithRegion("id: region.a"));

        Assert.Null(map);
        Assert.Equal(3, report.Issues.Count(i => i.Code == ValidatorCodes.MissingField));
        Assert.Contains(report.Issues, i => i.Message.Contains("'name'"));
        Assert.Contains(report.Issues, i => i.Message.Contains("'biome'"));
        Assert.Contains(report.Issues, i => i.Message.Contains("'polygons'"));
    }

    [Fact]
    public void RegionIdOfWrongType_IsRejected()
    {
        var (map, report) = Parse(WithRegion("id: biome.a\n    name: A\n    biome: biome.x\n    polygons:\n      - [[0,0],[1,0],[0,1]]"));

        Assert.Null(map);
        ValidationIssue issue = Assert.Single(report.Issues);
        Assert.Equal(ValidatorCodes.IdTypeMismatch, issue.Code);
        Assert.Contains("region.a", issue.SuggestedFix);
    }

    [Fact]
    public void BiomeReferenceOfWrongType_IsRejected()
    {
        var (map, report) = Parse(WithRegion("id: region.a\n    name: A\n    biome: item.torch\n    polygons:\n      - [[0,0],[1,0],[0,1]]"));

        Assert.Null(map);
        Assert.Equal(ValidatorCodes.ReferenceTypeMismatch, Assert.Single(report.Issues).Code);
    }

    [Fact]
    public void DuplicateRegionInFile_IsRejected()
    {
        string body = "id: region.a\n    name: A\n    biome: biome.x\n    polygons:\n      - [[0,0],[1,0],[0,1]]";
        var (map, report) = Parse($"id: world.test\nname: T\nworld_size_m: 1000\nregions:\n  - {body}\n  - {body}\n");

        Assert.Null(map);
        Assert.Equal(ValidatorCodes.DuplicateId, Assert.Single(report.Issues).Code);
    }

    [Theory]
    [InlineData("[[0,0],[1,0]]", "at least 3")]
    [InlineData("[[0,0],[1,0],[1]]", "[x, z]")]
    [InlineData("[[0,0],[1,0],[1,2,3]]", "[x, z]")]
    [InlineData("[[0,0],[4,4],[4,0],[0,4]]", "cross or touch")]
    [InlineData("[[0,0],[1,1],[2,2]]", "zero area")]
    [InlineData("[[0,0],[1,0],[.nan,1]]", "non-finite")]
    public void InvalidPolygons_AreRejected_WithReason(string ring, string reason)
    {
        var (map, report) = Parse(WithRegion($"id: region.a\n    name: A\n    biome: biome.x\n    polygons:\n      - {ring}"));

        Assert.Null(map);
        ValidationIssue issue = Assert.Single(report.Issues);
        Assert.Equal(ValidatorCodes.InvalidPolygon, issue.Code);
        Assert.Contains(reason, issue.Message);
        Assert.Contains("polygon 0", issue.Message);
    }

    [Fact]
    public void PolygonOutsideWorld_IsRejected()
    {
        var (map, report) = Parse(WithRegion("id: region.a\n    name: A\n    biome: biome.x\n    polygons:\n      - [[0,0],[501,0],[0,100]]"));

        Assert.Null(map);
        ValidationIssue issue = Assert.Single(report.Issues);
        Assert.Equal(ValidatorCodes.PolygonOutsideWorld, issue.Code);
        Assert.Contains("-500 to 500", issue.Message);
    }

    [Fact]
    public void PolygonExactlyOnWorldEdge_IsAllowed()
    {
        var (map, report) = Parse(WithRegion("id: region.a\n    name: A\n    biome: biome.x\n    polygons:\n      - [[-500,-500],[500,-500],[500,500],[-500,500]]"));

        Assert.Empty(report.Issues);
        Assert.NotNull(map);
    }

    [Fact]
    public void SecondRingErrors_NameTheRing()
    {
        var (map, report) = Parse(WithRegion(
            "id: region.a\n    name: A\n    biome: biome.x\n    polygons:\n      - [[0,0],[1,0],[0,1]]\n      - [[0,0],[1,0]]"));

        Assert.Null(map);
        Assert.Contains("polygon 1", Assert.Single(report.Issues).Message);
    }

    [Fact]
    public void EmptyFile_IsRejected()
    {
        var (map, report) = Parse(string.Empty);

        Assert.Null(map);
        Assert.Equal(ValidatorCodes.EmptyFile, Assert.Single(report.Issues).Code);
    }
}
