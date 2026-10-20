using System;
using Realm.Core.Validation;
using Xunit;

namespace Realm.Core.Tests;

public class ValidationReportTests
{
    [Fact]
    public void NewReport_HasNoIssuesOrErrors()
    {
        var report = new ValidationReport();

        Assert.Empty(report.Issues);
        Assert.False(report.HasErrors);
    }

    [Fact]
    public void WarningsAndInfo_DoNotCountAsErrors()
    {
        var report = new ValidationReport();
        report.Warning("X001", "loc", "careful");
        report.Add(new ValidationIssue(Severity.Info, "X002", "loc", "fyi"));

        Assert.False(report.HasErrors);
        Assert.Equal(1, report.Count(Severity.Warning));
        Assert.Equal(1, report.Count(Severity.Info));
    }

    [Fact]
    public void Error_SetsHasErrors_AndKeepsInsertionOrder()
    {
        var report = new ValidationReport();
        report.Warning("X001", "a", "first");
        report.Error("X002", "b", "second", "do this");

        Assert.True(report.HasErrors);
        Assert.Equal(new[] { "X001", "X002" }, new[] { report.Issues[0].Code, report.Issues[1].Code });
        Assert.Equal("do this", report.Issues[1].SuggestedFix);
    }

    [Fact]
    public void Add_RejectsNull()
    {
        Assert.Throws<ArgumentNullException>(() => new ValidationReport().Add(null!));
    }

    [Fact]
    public void Issue_ToString_IncludesAllParts()
    {
        var issue = new ValidationIssue(Severity.Error, "DATA001", "a.yaml:3", "bad", "fix it");

        Assert.Equal("Error DATA001 a.yaml:3: bad (fix: fix it)", issue.ToString());
        Assert.Equal("Warning W1 l: m", new ValidationIssue(Severity.Warning, "W1", "l", "m").ToString());
    }
}
