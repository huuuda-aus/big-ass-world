using System.Linq;
using Realm.Core.Validation;
using Realm.Data.Content;
using Xunit;

namespace Realm.Data.Tests;

public class ProfessionParserTests
{
    private static (ProfessionDefinition? Profession, ValidationReport Report) Parse(string yaml)
    {
        var report = new ValidationReport();
        return (ProfessionParser.Parse(yaml, "professions/test.yaml", report), report);
    }

    private const string Baker =
        "id: profession.baker\nname: Baker\ntags: [food]\nworkplace: { service: service.baker }\nshare: { city: 0.04, town: 0.05 }\n" +
        "day:\n  - { from: \"05:30\", to: \"17:00\", do: work }\n  - { from: \"18:30\", to: \"21:00\", do: inn }\n";

    [Fact]
    public void FullRecord_Parses()
    {
        var (p, report) = Parse(Baker);

        Assert.Empty(report.Issues);
        Assert.Equal("profession.baker", p!.Id.Value);
        Assert.Equal("service", p.WorkplaceKind);
        Assert.Equal("service.baker", p.WorkplaceService!.Value.Value);
        Assert.Equal(0.04, p.ShareFor("city"));
        Assert.Equal(0, p.ShareFor("hamlet"));
        Assert.Equal(new DayBlock(330, 1020, "work"), p.Day[0]);
        Assert.Equal(new DayBlock(1110, 1260, "inn"), p.Day[1]);
    }

    [Fact]
    public void AWorkplaceKind_AndAWrappingBlock_AreAccepted()
    {
        var (p, report) = Parse("id: profession.watchman\nname: Watchman\nworkplace: { kind: patrol }\nshare: { city: 0.04 }\nday:\n  - { from: \"18:00\", to: \"06:00\", do: patrol }\n");

        Assert.Empty(report.Issues);
        Assert.Equal("patrol", p!.WorkplaceKind);
        Assert.Null(p.WorkplaceService);
        Assert.Equal(new DayBlock(1080, 360, "patrol"), p.Day[0]);
    }

    [Fact]
    public void MidnightEnd_IsMinuteZero()
    {
        var (p, report) = Parse("id: profession.x\nname: X\nshare: { city: 0.1 }\nday:\n  - { from: \"10:00\", to: \"00:00\", do: work }\n");

        Assert.Empty(report.Issues);
        Assert.Equal(new DayBlock(600, 0, "work"), p!.Day[0]);
    }

    [Theory]
    [InlineData("workplace: { kind: moon }\nshare: { city: 0.1 }\nday:\n  - { from: \"06:00\", to: \"18:00\", do: work }")]
    [InlineData("share: { village: 0.1 }\nday:\n  - { from: \"06:00\", to: \"18:00\", do: work }")]
    [InlineData("share: { city: 1.5 }\nday:\n  - { from: \"06:00\", to: \"18:00\", do: work }")]
    [InlineData("share: { city: 0.1 }\nday:\n  - { from: \"06:00\", to: \"18:00\", do: dance }")]
    [InlineData("share: { city: 0.1 }\nday:\n  - { from: \"6\", to: \"18:00\", do: work }")]
    [InlineData("share: { city: 0.1 }\nday:\n  - { from: \"25:00\", to: \"18:00\", do: work }")]
    [InlineData("share: { city: 0.1 }\nday:\n  - { from: \"06:00\", to: \"06:00\", do: work }")]
    [InlineData("share: { city: 0.1 }\nday:\n  - { from: \"06:00\", to: \"12:00\", do: work }\n  - { from: \"11:00\", to: \"14:00\", do: inn }")]
    [InlineData("workplace: { colour: red }\nshare: { city: 0.1 }\nday:\n  - { from: \"06:00\", to: \"18:00\", do: work }")]
    public void BadRecords_AreRejected(string body)
    {
        var (p, report) = Parse("id: profession.x\nname: X\n" + body + "\n");

        Assert.Null(p);
        Assert.True(report.HasErrors);
    }

    [Fact]
    public void MissingShareOrDay_IsAnError()
    {
        Assert.Contains(Parse("id: profession.x\nname: X\nday:\n  - { from: \"06:00\", to: \"18:00\", do: work }\n").Report.Issues, i => i.Code == ValidatorCodes.MissingField);
        Assert.Contains(Parse("id: profession.x\nname: X\nshare: { city: 0.1 }\n").Report.Issues, i => i.Code == ValidatorCodes.MissingField);
    }

    [Fact]
    public void TheActivityWords_MatchTheSimulationsEnum()
    {
        // The simulation maps the words to its own enum; a new word on one side only would fail at generation time.
        Assert.Equal(
            ProfessionVocabulary.Activities.OrderBy(x => x),
            System.Enum.GetNames<Realm.Sim.Npcs.ScheduleActivity>().Select(x => x.ToLowerInvariant()).OrderBy(x => x));
    }
}
