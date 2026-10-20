using System.IO;
using System.Linq;
using Realm.Core.Ids;
using Realm.Core.Validation;
using Realm.Data.Content;
using Realm.Gen.Terrain;

namespace Realm.Cli;

internal static class MacroMapCommand
{
    private const int ExitOk = 0;
    private const int ExitErrors = 1;
    private const double SquareMetresPerSquareKilometre = 1_000_000.0;
    private const double Percent = 100.0;

    /// <summary>Validates every world in a content directory and prints a per-region coverage table.</summary>
    /// <returns>0 when there are no errors, 1 otherwise.</returns>
    public static int Run(string contentDirectory, TextWriter output)
    {
        var report = new ValidationReport();
        ContentDatabase db = ContentLoader.Load(contentDirectory, report);

        foreach (MacroMapDefinition map in db.MacroMaps.Values)
        {
            RegionField field = MacroRasterizer.Rasterize(map, report);
            PrintMap(map, field, db, output);
        }

        if (db.MacroMaps.Count == 0)
        {
            output.WriteLine("No world maps found.");
        }

        foreach (ValidationIssue issue in report.Issues)
        {
            output.WriteLine(issue);
        }

        output.WriteLine($"{report.Count(Severity.Error)} error(s), {report.Count(Severity.Warning)} warning(s).");
        return report.HasErrors ? ExitErrors : ExitOk;
    }

    private static void PrintMap(MacroMapDefinition map, RegionField field, ContentDatabase db, TextWriter output)
    {
        output.WriteLine(
            $"{map.Id}: {map.WorldSizeMeters} m square, {field.CellsPerSide} x {field.CellsPerSide} cells of {field.CellMeters} m " +
            $"({MacroRasterizer.GeneratorId} v{MacroRasterizer.GeneratorVersion}, checksum {field.Checksum():x16})");

        if (field.CellsPerSide == 0)
        {
            return;
        }

        long total = (long)field.CellsPerSide * field.CellsPerSide;
        double cellKm2 = (double)field.CellMeters * field.CellMeters / SquareMetresPerSquareKilometre;
        output.WriteLine($"  {"region",-28} {"biome",-26} {"cells",8} {"km2",9} {"share",7}");
        foreach (ContentId id in field.Regions)
        {
            RegionDefinition region = db.Regions[id];
            int cells = field.CellCount(id);
            output.WriteLine(
                $"  {id,-28} {region.Biome,-26} {cells,8} {cells * cellKm2,9:F1} {cells * Percent / total,6:F1}%");
        }

        int unassigned = field.UnassignedCount();
        if (unassigned > 0)
        {
            output.WriteLine($"  {"(unassigned)",-28} {string.Empty,-26} {unassigned,8} {unassigned * cellKm2,9:F1} {unassigned * Percent / total,6:F1}%");
        }
    }
}
