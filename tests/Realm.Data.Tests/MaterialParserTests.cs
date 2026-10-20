using System.Linq;
using Realm.Core.Validation;
using Realm.Data.Content;
using Xunit;

namespace Realm.Data.Tests;

public class MaterialParserTests
{
    private const string Valid = @"id: material.iron_ore
name: Iron ore
tags: [metal, ore]
properties:
  weight: 7.8
  hardness: 4
";

    private static (MaterialDefinition? Material, ValidationReport Report) Parse(string yaml)
    {
        var report = new ValidationReport();
        return (MaterialParser.Parse(yaml, "materials/test.yaml", report), report);
    }

    [Fact]
    public void Valid_Record_Parses()
    {
        var (m, report) = Parse(Valid);

        Assert.Empty(report.Issues);
        Assert.NotNull(m);
        Assert.Equal("material.iron_ore", m!.Id.Value);
        Assert.Equal("Iron ore", m.Name);
        Assert.Equal(new[] { "metal", "ore" }, m.Tags.ToArray());
        Assert.Equal(4.0, m.Properties["hardness"]);
        Assert.Equal(7.8, m.Properties["weight"]);
    }

    [Fact]
    public void Properties_EnumerateInSortedKeyOrder()
    {
        var (m, _) = Parse(Valid);

        Assert.Equal(new[] { "hardness", "weight" }, m!.Properties.Keys.ToArray());
    }

    [Fact]
    public void TagsAndProperties_AreOptional()
    {
        var (m, report) = Parse("id: material.air\nname: Air\n");

        Assert.Empty(report.Issues);
        Assert.Empty(m!.Tags);
        Assert.Empty(m.Properties);
    }

    [Fact]
    public void UnknownField_IsRejected_WithLineNumber()
    {
        var (m, report) = Parse("id: material.iron\nname: Iron\nhardnes: 4\n");

        Assert.Null(m);
        ValidationIssue issue = Assert.Single(report.Issues);
        Assert.Equal(ValidatorCodes.YamlInvalid, issue.Code);
        Assert.Equal("materials/test.yaml:3", issue.Location);
        Assert.Equal("Unknown field 'hardnes'.", issue.Message);
    }

    [Fact]
    public void WrongValueType_IsRejected()
    {
        var (m, report) = Parse("id: material.iron\nname: Iron\nproperties:\n  weight: heavy\n");

        Assert.Null(m);
        Assert.Equal(ValidatorCodes.YamlInvalid, Assert.Single(report.Issues).Code);
    }

    [Fact]
    public void BrokenYaml_IsRejected()
    {
        var (m, report) = Parse("id: [unclosed\n");

        Assert.Null(m);
        Assert.Equal(ValidatorCodes.YamlInvalid, Assert.Single(report.Issues).Code);
    }

    [Fact]
    public void EmptyFile_IsRejected()
    {
        var (m, report) = Parse(string.Empty);

        Assert.Null(m);
        Assert.Equal(ValidatorCodes.EmptyFile, Assert.Single(report.Issues).Code);
    }

    [Fact]
    public void MissingIdAndName_ReportsBoth()
    {
        var (m, report) = Parse("tags: [metal]\n");

        Assert.Null(m);
        Assert.Equal(2, report.Issues.Count(i => i.Code == ValidatorCodes.MissingField));
    }

    [Theory]
    [InlineData("Material.Iron")]
    [InlineData("iron")]
    [InlineData("material.iron-ore")]
    public void InvalidId_IsRejected(string id)
    {
        var (m, report) = Parse($"id: {id}\nname: Iron\n");

        Assert.Null(m);
        Assert.Equal(ValidatorCodes.InvalidId, Assert.Single(report.Issues).Code);
    }

    [Fact]
    public void InvalidTags_AreEachReported()
    {
        var (m, report) = Parse("id: material.iron\nname: Iron\ntags: [Metal, ok, two words]\n");

        Assert.Null(m);
        Assert.Equal(2, report.Issues.Count(i => i.Code == ValidatorCodes.InvalidTag));
    }

    [Fact]
    public void NullTagEntry_IsRejected()
    {
        var (m, report) = Parse("id: material.iron\nname: Iron\ntags:\n  - metal\n  -\n");

        Assert.Null(m);
        Assert.Equal(ValidatorCodes.InvalidTag, Assert.Single(report.Issues).Code);
    }

    [Fact]
    public void InvalidPropertyName_IsRejected()
    {
        var (m, report) = Parse("id: material.iron\nname: Iron\nproperties:\n  Hardness: 4\n");

        Assert.Null(m);
        Assert.Equal(ValidatorCodes.InvalidProperty, Assert.Single(report.Issues).Code);
    }

    [Theory]
    [InlineData(".nan")]
    [InlineData(".inf")]
    [InlineData("-.inf")]
    public void NonFinitePropertyValue_IsRejected(string value)
    {
        var (m, report) = Parse($"id: material.iron\nname: Iron\nproperties:\n  weight: {value}\n");

        Assert.Null(m);
        Assert.Equal(ValidatorCodes.InvalidProperty, Assert.Single(report.Issues).Code);
    }
}
