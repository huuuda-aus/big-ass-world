using System.IO;
using Realm.Core.Validation;
using Realm.Data.Content;

namespace Realm.Cli;

internal static class ContentLintCommand
{
    private const int ExitOk = 0;
    private const int ExitLintErrors = 1;

    /// <summary>Loads and validates a content directory, printing every issue and a summary.</summary>
    /// <returns>0 when there are no errors, 1 otherwise.</returns>
    public static int Run(string contentDirectory, TextWriter output)
    {
        var report = new ValidationReport();
        ContentDatabase db = ContentLoader.Load(contentDirectory, report);

        foreach (ValidationIssue issue in report.Issues)
        {
            output.WriteLine(issue);
        }

        output.WriteLine(
            $"{db.Materials.Count} material(s), {db.Items.Count} item(s), {db.Biomes.Count} biome(s), " +
            $"{db.Regions.Count} region(s), {db.Services.Count} service(s), {db.Foliage.Count} foliage species, " +
            $"{report.Count(Severity.Error)} error(s), {report.Count(Severity.Warning)} warning(s).");

        return report.HasErrors ? ExitLintErrors : ExitOk;
    }
}
