using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace Realm.Architecture.Tests;

/// <summary>Enforces the dependency rules of CLAUDE.md section 5 by reading the .csproj files.</summary>
public class DependencyRuleTests
{
    // Allowed project references per src project. Anything else fails the test.
    private static readonly Dictionary<string, string[]> Allowed = new()
    {
        ["Realm.Core"] = Array.Empty<string>(),
        ["Realm.Data"] = new[] { "Realm.Core" },
        ["Realm.Sim"] = new[] { "Realm.Core", "Realm.Data" },
        ["Realm.Gen"] = new[] { "Realm.Core", "Realm.Data", "Realm.Sim" },
        ["Realm.Scripting"] = new[] { "Realm.Core", "Realm.Data", "Realm.Sim" },
        ["Realm.Cli"] = new[] { "Realm.Core", "Realm.Data", "Realm.Sim", "Realm.Gen", "Realm.Scripting" },

        // The Godot presentation layer may use every Godot-free library but nothing may depend on it.
        ["Realm.Game"] = new[] { "Realm.Core", "Realm.Data", "Realm.Sim", "Realm.Gen", "Realm.Scripting" },
    };

    // The only projects allowed to depend on Godot. Realm.Editor does not exist yet and is not checked until it does.
    private static readonly string[] GodotProjects = { "Realm.Game", "Realm.Editor" };

    public static IEnumerable<object[]> SrcProjects() =>
        Directory.GetFiles(Path.Combine(RepoRoot(), "src"), "*.csproj", SearchOption.AllDirectories)
            .Select(p => new object[] { Path.GetFileNameWithoutExtension(p) });

    [Theory]
    [MemberData(nameof(SrcProjects))]
    public void ProjectReferences_MatchAllowedGraph(string project)
    {
        if (project == "Realm.Editor")
        {
            return; // Not created yet.
        }

        Assert.True(Allowed.ContainsKey(project), $"{project} has no entry in the allowed dependency map. Add it deliberately.");

        string[] actual = Doc(project).Descendants("ProjectReference")
            .Select(e => Path.GetFileNameWithoutExtension(((string)e.Attribute("Include")!).Replace('\\', '/')))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();
        string[] expected = Allowed[project].OrderBy(n => n, StringComparer.Ordinal).ToArray();

        Assert.True(
            actual.All(expected.Contains),
            $"{project} references {string.Join(", ", actual.Except(expected))}, which CLAUDE.md section 5 forbids.");
    }

    [Theory]
    [MemberData(nameof(SrcProjects))]
    public void NonGodotProjects_DoNotReferenceGodot(string project)
    {
        if (GodotProjects.Contains(project))
        {
            return;
        }

        string[] offenders = Doc(project).Descendants()
            .Where(e => e.Name.LocalName is "PackageReference" or "Reference" or "ProjectReference")
            .Select(e => (string?)e.Attribute("Include") ?? string.Empty)
            .Where(i => i.Contains("Godot", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.True(offenders.Length == 0, $"{project} must not reference Godot: {string.Join(", ", offenders)}");
    }

    [Theory]
    [MemberData(nameof(SrcProjects))]
    public void NoProject_ReferencesTheGodotPresentationLayer(string project)
    {
        string[] references = Doc(project).Descendants("ProjectReference")
            .Select(e => Path.GetFileNameWithoutExtension(((string)e.Attribute("Include")!).Replace('\\', '/')))
            .ToArray();

        Assert.DoesNotContain("Realm.Game", references);
        Assert.DoesNotContain("Realm.Editor", references);
    }

    [Fact]
    public void GameProject_UsesTheGodotNetSdk_AndOnlyItDoes()
    {
        string sdk = (string)Doc("Realm.Game").Root!.Attribute("Sdk")!;
        Assert.StartsWith("Godot.NET.Sdk/", sdk);

        foreach (string project in Allowed.Keys.Where(p => p != "Realm.Game"))
        {
            string other = (string?)Doc(project).Root!.Attribute("Sdk") ?? string.Empty;
            Assert.DoesNotContain("Godot", other);
        }
    }

    [Fact]
    public void EveryAllowedProject_Exists()
    {
        string[] found = SrcProjects().Select(o => (string)o[0]).ToArray();
        Assert.All(Allowed.Keys, k => Assert.Contains(k, found));
    }

    private static XDocument Doc(string project) =>
        XDocument.Load(Path.Combine(RepoRoot(), "src", project, project + ".csproj"));

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
