using System.Linq;
using Realm.Core.Validation;
using Realm.Data.Content;
using Xunit;

namespace Realm.Data.Tests;

public class BiomeParserTests
{
    private static (BiomeDefinition? Biome, ValidationReport Report) Parse(string yaml)
    {
        var report = new ValidationReport();
        return (BiomeParser.Parse(yaml, "biomes/test.yaml", report), report);
    }

    [Fact]
    public void Valid_Record_Parses()
    {
        var (biome, report) = Parse("id: biome.highlands\nname: Highlands\ntags: [mountainous, land]\nproperties:\n  b: 2\n  a: 1\n");

        Assert.Empty(report.Issues);
        Assert.Equal("biome.highlands", biome!.Id.Value);
        Assert.Equal(new[] { "land", "mountainous" }, biome.Tags.ToArray());
        Assert.Equal(new[] { "a", "b" }, biome.Properties.Keys.ToArray());
    }

    [Fact]
    public void TagsAndProperties_AreOptional()
    {
        var (biome, report) = Parse("id: biome.empty\nname: Empty\n");

        Assert.Empty(report.Issues);
        Assert.Empty(biome!.Tags);
        Assert.Empty(biome.Properties);
    }

    [Fact]
    public void UnknownField_IsRejected()
    {
        var (biome, report) = Parse("id: biome.x\nname: X\nheight: 3\n");

        Assert.Null(biome);
        Assert.Equal("Unknown field 'height'.", Assert.Single(report.Issues).Message);
    }

    [Fact]
    public void BadProperty_IsRejected()
    {
        var (biome, report) = Parse("id: biome.x\nname: X\nproperties:\n  Bad: 1\n");

        Assert.Null(biome);
        Assert.Equal(ValidatorCodes.InvalidProperty, Assert.Single(report.Issues).Code);
    }

    [Fact]
    public void MissingFields_AreReported()
    {
        var (biome, report) = Parse("tags: [a]\n");

        Assert.Null(biome);
        Assert.Equal(2, report.Issues.Count(i => i.Code == ValidatorCodes.MissingField));
    }
}
