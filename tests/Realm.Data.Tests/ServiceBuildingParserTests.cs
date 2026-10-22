using System.Linq;
using Realm.Core.Validation;
using Realm.Data.Content;
using Xunit;

namespace Realm.Data.Tests;

public class ServiceBuildingParserTests
{
    private static (ServiceBuildingDefinition? Service, ValidationReport Report) Parse(string yaml)
    {
        var report = new ValidationReport();
        return (ServiceBuildingParser.Parse(yaml, "services/test.yaml", report), report);
    }

    [Fact]
    public void FullRecord_Parses()
    {
        var (s, report) = Parse(
            "id: service.inn\nname: Inn\ntags: [civic]\ncounts: { city: 2, town: 1 }\nwards: [merchant, craftsmen]\nnear: gate\nmin_area_m2: 80\nfoundation: stone\nmain_room: kitchen\n");

        Assert.Empty(report.Issues);
        Assert.Equal("service.inn", s!.Id.Value);
        Assert.Equal(2, s.CountFor("city"));
        Assert.Equal(1, s.CountFor("town"));
        Assert.Equal(new[] { "merchant", "craftsmen" }, s.Wards.ToArray());
        Assert.Equal("gate", s.Near);
        Assert.Equal(80, s.MinAreaMeters2);
        Assert.Equal("stone", s.Foundation);
        Assert.Equal("kitchen", s.MainRoom);
        Assert.False(s.OutsideWall);
    }

    [Fact]
    public void Defaults_AreApplied()
    {
        var (s, report) = Parse("id: service.baker\nname: Baker\ncounts: { town: 1 }\n");

        Assert.Empty(report.Issues);
        Assert.Equal(0, s!.CountFor("city"));
        Assert.Empty(s.Wards);
        Assert.Equal(ServiceVocabulary.DefaultNear, s.Near);
        Assert.Equal(ServiceVocabulary.DefaultMainRoom, s.MainRoom);
        Assert.Equal(ServiceVocabulary.DefaultMinAreaMeters2, s.MinAreaMeters2);
        Assert.Null(s.Foundation);
    }

    [Fact]
    public void MissingCounts_IsRejected()
    {
        var (s, report) = Parse("id: service.x\nname: X\n");

        Assert.Null(s);
        Assert.Contains(report.Issues, i => i.Code == ValidatorCodes.MissingField);
    }

    [Theory]
    [InlineData("counts: { village: 1 }")]
    [InlineData("counts: { city: 0 }")]
    [InlineData("counts: { city: 99 }")]
    [InlineData("counts: { city: 1 }\nwards: [harbour]")]
    [InlineData("counts: { city: 1 }\nnear: moon")]
    [InlineData("counts: { city: 1 }\nfoundation: gold")]
    [InlineData("counts: { city: 1 }\nmain_room: nave")]
    [InlineData("counts: { city: 1 }\nmin_area_m2: -5")]
    [InlineData("counts: { city: 1 }\noutside_wall: true\nwards: [slum]")]
    public void BadValues_AreRejected_WithDATA018(string body)
    {
        var (s, report) = Parse("id: service.x\nname: X\n" + body + "\n");

        Assert.Null(s);
        Assert.Contains(report.Issues, i => i.Code == ValidatorCodes.InvalidServiceField);
    }

    [Fact]
    public void UnknownField_IsRejected()
    {
        var (s, report) = Parse("id: service.x\nname: X\ncounts: { city: 1 }\ncolour: red\n");

        Assert.Null(s);
        Assert.Contains(report.Issues, i => i.Code == ValidatorCodes.YamlInvalid);
    }
}
