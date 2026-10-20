using System;
using System.IO;
using System.Linq;
using Realm.Core.Ids;
using Realm.Core.Validation;
using Realm.Data.Content;
using Xunit;

namespace Realm.Data.Tests;

public sealed class ContentLoaderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "realm-tests-" + Guid.NewGuid().ToString("N"));

    public ContentLoaderTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private void Write(string relativePath, string text)
    {
        string path = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private static string Material(string name) => $"id: material.{name}\nname: {name}\n";

    [Fact]
    public void Loads_AllValidFiles_InIdOrder()
    {
        Write("materials/tin.yaml", Material("tin"));
        Write("materials/iron_ore.yaml", Material("iron_ore"));
        Write("materials/copper.yaml", Material("copper"));

        var report = new ValidationReport();
        ContentDatabase db = ContentLoader.Load(_root, report);

        Assert.Empty(report.Issues);
        Assert.Equal(
            new[] { "material.copper", "material.iron_ore", "material.tin" },
            db.Materials.Keys.Select(k => k.Value).ToArray());
    }

    [Fact]
    public void MissingRoot_IsAnError()
    {
        var report = new ValidationReport();
        ContentDatabase db = ContentLoader.Load(Path.Combine(_root, "nope"), report);

        Assert.Equal(ValidatorCodes.RootMissing, Assert.Single(report.Issues).Code);
        Assert.Empty(db.Materials);
    }

    [Fact]
    public void EmptyRoot_LoadsNothing_WithoutIssues()
    {
        var report = new ValidationReport();

        Assert.Empty(ContentLoader.Load(_root, report).Materials);
        Assert.Empty(report.Issues);
    }

    [Fact]
    public void InvalidFile_ReportsLocation_AndOthersStillLoad()
    {
        Write("materials/good.yaml", Material("good"));
        Write("materials/bad.yaml", "id: material.bad\nname: Bad\nfoo: 1\n");

        var report = new ValidationReport();
        ContentDatabase db = ContentLoader.Load(_root, report);

        ValidationIssue issue = Assert.Single(report.Issues);
        Assert.Equal("materials/bad.yaml:3", issue.Location);
        Assert.Equal(new[] { "material.good" }, db.Materials.Keys.Select(k => k.Value).ToArray());
    }

    [Fact]
    public void DuplicateId_IsAnError_AndFirstWins()
    {
        Write("materials/a.yaml", "id: material.same\nname: First\n");
        Write("materials/b.yaml", "id: material.same\nname: Second\n");

        var report = new ValidationReport();
        ContentDatabase db = ContentLoader.Load(_root, report);

        ValidationIssue duplicate = report.Issues.Single(i => i.Code == ValidatorCodes.DuplicateId);
        Assert.Equal("materials/b.yaml", duplicate.Location);
        Assert.Contains("materials/a.yaml", duplicate.Message);
        Assert.Equal("First", db.Materials[ContentId.Parse("material.same")].Name);
    }

    [Fact]
    public void IdTypeDifferentFromDirectory_IsAnError()
    {
        Write("materials/goblin.yaml", "id: creature.goblin\nname: Goblin\n");

        var report = new ValidationReport();
        ContentDatabase db = ContentLoader.Load(_root, report);

        Assert.Equal(ValidatorCodes.IdTypeMismatch, Assert.Single(report.Issues).Code);
        Assert.Empty(db.Materials);
    }

    [Fact]
    public void FileNameNotMatchingId_IsOnlyAWarning_AndStillLoads()
    {
        Write("materials/wrongname.yaml", Material("iron"));

        var report = new ValidationReport();
        ContentDatabase db = ContentLoader.Load(_root, report);

        ValidationIssue issue = Assert.Single(report.Issues);
        Assert.Equal(Severity.Warning, issue.Severity);
        Assert.Equal(ValidatorCodes.FileNameMismatch, issue.Code);
        Assert.False(report.HasErrors);
        Assert.Single(db.Materials);
    }

    [Fact]
    public void UnknownDirectory_IsAWarning_AndIgnored()
    {
        Write("creatures/goblin.yaml", "id: creature.goblin\nname: Goblin\n");

        var report = new ValidationReport();
        ContentDatabase db = ContentLoader.Load(_root, report);

        ValidationIssue issue = Assert.Single(report.Issues);
        Assert.Equal(Severity.Warning, issue.Severity);
        Assert.Equal(ValidatorCodes.UnknownDirectory, issue.Code);
        Assert.Empty(db.Materials);
    }

    [Fact]
    public void Loads_Materials_AndItems_TogetherWithSources()
    {
        Write("materials/oak_wood.yaml", Material("oak_wood"));
        Write("items/oak_log.yaml", "id: item.oak_log\nname: Log\nmaterial: material.oak_wood\n");

        var report = new ValidationReport();
        ContentDatabase db = ContentLoader.Load(_root, report);

        Assert.Empty(report.Issues);
        Assert.Single(db.Materials);
        Assert.Single(db.Items);
        Assert.True(db.Contains(ContentId.Parse("item.oak_log")));
        Assert.True(db.TryGetSource(ContentId.Parse("item.oak_log"), out string source));
        Assert.Equal("items/oak_log.yaml", source);
        Assert.False(db.TryGetSource(ContentId.Parse("item.nope"), out _));
    }

    [Fact]
    public void DanglingMaterialReference_IsAnError_WithItemLocation()
    {
        Write("items/torch.yaml", "id: item.torch\nname: Torch\nmaterial: material.missing\n");

        var report = new ValidationReport();
        ContentDatabase db = ContentLoader.Load(_root, report);

        ValidationIssue issue = Assert.Single(report.Issues);
        Assert.Equal(ValidatorCodes.DanglingReference, issue.Code);
        Assert.Equal("items/torch.yaml", issue.Location);
        Assert.Contains("material.missing", issue.Message);
        Assert.Single(db.Items);
    }

    [Fact]
    public void ReferenceToMaterialInAnotherFile_Resolves()
    {
        Write("items/a_log.yaml", "id: item.a_log\nname: A\nmaterial: material.zeta\n");
        Write("materials/zeta.yaml", Material("zeta"));

        var report = new ValidationReport();
        ContentLoader.Load(_root, report);

        Assert.Empty(report.Issues);
    }

    [Fact]
    public void ItemInMaterialsDirectory_IsATypeMismatch()
    {
        Write("materials/torch.yaml", "id: item.torch\nname: Torch\n");

        var report = new ValidationReport();
        ContentLoader.Load(_root, report);

        Assert.Equal(ValidatorCodes.IdTypeMismatch, Assert.Single(report.Issues).Code);
    }

    private const string TinyWorld = @"id: world.test
name: Test
world_size_m: 1000
regions:
  - id: region.a
    name: A
    biome: biome.land
    polygons:
      - [[-500, -500], [500, -500], [500, 500], [-500, 500]]
";

    [Fact]
    public void Loads_BiomesAndWorld_WithRegionsAndSources()
    {
        Write("biomes/land.yaml", "id: biome.land\nname: Land\ntags: [land]\n");
        Write("world/test.yaml", TinyWorld);

        var report = new ValidationReport();
        ContentDatabase db = ContentLoader.Load(_root, report);

        Assert.Empty(report.Issues);
        Assert.Single(db.Biomes);
        Assert.Single(db.MacroMaps);
        RegionDefinition region = db.Regions[ContentId.Parse("region.a")];
        Assert.Equal("biome.land", region.Biome.Value);
        Assert.True(db.Contains(ContentId.Parse("region.a")));
        Assert.True(db.TryGetSource(ContentId.Parse("region.a"), out string source));
        Assert.Equal("world/test.yaml", source);
    }

    [Fact]
    public void RegionWithMissingBiome_IsADanglingReference()
    {
        Write("world/test.yaml", TinyWorld);

        var report = new ValidationReport();
        ContentLoader.Load(_root, report);

        ValidationIssue issue = Assert.Single(report.Issues);
        Assert.Equal(ValidatorCodes.DanglingReference, issue.Code);
        Assert.Equal("world/test.yaml", issue.Location);
        Assert.Contains("biome.land", issue.Message);
    }

    [Fact]
    public void SameRegionInTwoWorldFiles_IsADuplicate()
    {
        Write("biomes/land.yaml", "id: biome.land\nname: Land\n");
        Write("world/a.yaml", TinyWorld.Replace("world.test", "world.a"));
        Write("world/b.yaml", TinyWorld.Replace("world.test", "world.b"));

        var report = new ValidationReport();
        ContentDatabase db = ContentLoader.Load(_root, report);

        ValidationIssue issue = report.Issues.Single(i => i.Code == ValidatorCodes.DuplicateId);
        Assert.Equal("world/b.yaml", issue.Location);
        Assert.Contains("world/a.yaml", issue.Message);
        Assert.Single(db.Regions);
    }

    [Fact]
    public void WorldFile_WithBadPolygon_IsReportedAndNotLoaded()
    {
        Write("biomes/land.yaml", "id: biome.land\nname: Land\n");
        Write("world/test.yaml", TinyWorld.Replace("[[-500, -500], [500, -500], [500, 500], [-500, 500]]", "[[0,0],[1,0]]"));

        var report = new ValidationReport();
        ContentDatabase db = ContentLoader.Load(_root, report);

        Assert.Equal(ValidatorCodes.InvalidPolygon, Assert.Single(report.Issues).Code);
        Assert.Empty(db.MacroMaps);
        Assert.Empty(db.Regions);
    }

    [Fact]
    public void NonYamlFiles_AreIgnored()
    {
        Write("materials/notes.txt", "not yaml");
        Write("materials/iron.yaml", Material("iron"));

        var report = new ValidationReport();

        Assert.Single(ContentLoader.Load(_root, report).Materials);
        Assert.Empty(report.Issues);
    }
}
