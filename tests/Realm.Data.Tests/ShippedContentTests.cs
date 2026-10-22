using System;
using System.IO;
using Realm.Core.Ids;
using Realm.Core.Validation;
using Realm.Data.Content;
using Xunit;

namespace Realm.Data.Tests;

/// <summary>Content lint (CLAUDE.md section 7.4): the shipped content/ directory must always load cleanly.</summary>
public class ShippedContentTests
{
    [Fact]
    public void ShippedContent_LoadsWithoutErrorsOrWarnings()
    {
        var report = new ValidationReport();
        ContentDatabase db = ContentLoader.Load(Path.Combine(RepoRoot(), "content"), report);

        Assert.True(report.Issues.Count == 0, string.Join(Environment.NewLine, report.Issues));
        Assert.NotEmpty(db.Materials);
    }

    [Fact]
    public void ShippedContent_ContainsIronOre()
    {
        var report = new ValidationReport();
        ContentDatabase db = ContentLoader.Load(Path.Combine(RepoRoot(), "content"), report);

        MaterialDefinition iron = db.Materials[ContentId.Parse("material.iron_ore")];
        Assert.True(iron.Tags.Contains("metal"));
    }

    [Fact]
    public void ShippedItems_ReferenceShippedMaterials()
    {
        var report = new ValidationReport();
        ContentDatabase db = ContentLoader.Load(Path.Combine(RepoRoot(), "content"), report);

        ItemDefinition log = db.Items[ContentId.Parse("item.oak_log")];
        Assert.Equal(ContentId.Parse("material.oak_wood"), log.Material);
        Assert.True(db.Materials.ContainsKey(log.Material!.Value));
        Assert.Null(db.Items[ContentId.Parse("item.torch")].Material);
    }

    [Fact]
    public void ShippedServices_CoverTheRequestedBuildings_ForCitiesAndTowns()
    {
        var report = new ValidationReport();
        ContentDatabase db = ContentLoader.Load(Path.Combine(RepoRoot(), "content"), report);

        foreach (string name in new[] { "city_hall", "inn", "blacksmith", "trader", "baker", "jail", "warehouse", "farmer", "woodcutter" })
        {
            Assert.True(db.Services.ContainsKey(ContentId.Parse("service." + name)), name);
        }

        Assert.Equal(1, db.Services[ContentId.Parse("service.city_hall")].CountFor("city"));
        Assert.Equal(0, db.Services[ContentId.Parse("service.city_hall")].CountFor("town"));
        Assert.True(db.Services[ContentId.Parse("service.inn")].CountFor("town") >= 1);
        Assert.True(db.Services[ContentId.Parse("service.farmer")].OutsideWall);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Realm.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Realm.sln not found above the test binary.");
    }
}
