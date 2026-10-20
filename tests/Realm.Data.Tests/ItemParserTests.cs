using System.Linq;
using Realm.Core.Validation;
using Realm.Data.Content;
using Xunit;

namespace Realm.Data.Tests;

public class ItemParserTests
{
    private static (ItemDefinition? Item, ValidationReport Report) Parse(string yaml)
    {
        var report = new ValidationReport();
        return (ItemParser.Parse(yaml, "items/test.yaml", report), report);
    }

    [Fact]
    public void Valid_Record_Parses()
    {
        var (item, report) = Parse("id: item.oak_log\nname: Oak log\ntags: [raw_material]\nmaterial: material.oak_wood\n");

        Assert.Empty(report.Issues);
        Assert.Equal("item.oak_log", item!.Id.Value);
        Assert.Equal("Oak log", item.Name);
        Assert.Equal(new[] { "raw_material" }, item.Tags.ToArray());
        Assert.Equal("material.oak_wood", item.Material!.Value.Value);
    }

    [Fact]
    public void Material_IsOptional()
    {
        var (item, report) = Parse("id: item.torch\nname: Torch\n");

        Assert.Empty(report.Issues);
        Assert.Null(item!.Material);
        Assert.Empty(item.Tags);
    }

    [Fact]
    public void UnknownField_IsRejected_WithLineNumber()
    {
        var (item, report) = Parse("id: item.torch\nname: Torch\ndamage: 5\n");

        Assert.Null(item);
        ValidationIssue issue = Assert.Single(report.Issues);
        Assert.Equal(ValidatorCodes.YamlInvalid, issue.Code);
        Assert.Equal("items/test.yaml:3", issue.Location);
        Assert.Equal("Unknown field 'damage'.", issue.Message);
    }

    [Fact]
    public void MissingIdAndName_ReportsBoth()
    {
        var (item, report) = Parse("tags: [x]\n");

        Assert.Null(item);
        Assert.Equal(2, report.Issues.Count(i => i.Code == ValidatorCodes.MissingField));
    }

    [Fact]
    public void MaterialReference_WithWrongType_IsRejected()
    {
        var (item, report) = Parse("id: item.torch\nname: Torch\nmaterial: creature.goblin\n");

        Assert.Null(item);
        ValidationIssue issue = Assert.Single(report.Issues);
        Assert.Equal(ValidatorCodes.ReferenceTypeMismatch, issue.Code);
        Assert.Contains("creature.goblin", issue.Message);
    }

    [Fact]
    public void MaterialReference_WithBadSyntax_IsRejected()
    {
        var (item, report) = Parse("id: item.torch\nname: Torch\nmaterial: Not An Id\n");

        Assert.Null(item);
        Assert.Equal(ValidatorCodes.InvalidId, Assert.Single(report.Issues).Code);
    }

    [Fact]
    public void InvalidTag_IsRejected()
    {
        var (item, report) = Parse("id: item.torch\nname: Torch\ntags: [Bad Tag]\n");

        Assert.Null(item);
        Assert.Equal(ValidatorCodes.InvalidTag, Assert.Single(report.Issues).Code);
    }
}
