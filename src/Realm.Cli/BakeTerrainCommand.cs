using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using Realm.Core.Validation;
using Realm.Data.Content;
using Realm.Gen.Bake;
using Realm.Gen.Terrain;
using Realm.Gen.Terrain.Erosion;

namespace Realm.Cli;

internal static class BakeTerrainCommand
{
    private const int ExitOk = 0;
    private const int ExitErrors = 1;
    private const int ExitUsage = 2;
    private const ulong DefaultSeed = 1;
    private const int DefaultSpacing = 4; // ADR 0009: 256 vertices x 4 m = 1,024 m regions

    /// <summary>Runs the terrain pipeline (regions, heights, erosion) and bakes it to region tiles.</summary>
    /// <returns>0 when there are no errors, 1 on errors, 2 on bad arguments.</returns>
    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        string contentDirectory = "content";
        string outputDirectory = Path.Combine("baked", "terrain");
        ulong seed = DefaultSeed;
        int spacing = DefaultSpacing;
        int regionSize = TerrainBaker.DefaultRegionSize;
        bool erode = true;

        for (int i = 1; i < args.Length; i++)
        {
            string arg = args[i];
            if (arg == "--out" && i + 1 < args.Length)
            {
                outputDirectory = args[++i];
            }
            else if (arg == "--seed" && i + 1 < args.Length && ulong.TryParse(args[++i], NumberStyles.None, CultureInfo.InvariantCulture, out ulong s))
            {
                seed = s;
            }
            else if (arg == "--spacing" && i + 1 < args.Length && int.TryParse(args[++i], NumberStyles.None, CultureInfo.InvariantCulture, out int sp))
            {
                spacing = sp;
            }
            else if (arg == "--region-size" && i + 1 < args.Length && int.TryParse(args[++i], NumberStyles.None, CultureInfo.InvariantCulture, out int rs))
            {
                regionSize = rs;
            }
            else if (arg == "--no-erode")
            {
                erode = false;
            }
            else if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                contentDirectory = arg;
            }
            else
            {
                error.WriteLine($"Bad argument '{arg}'. Usage: realm bake-terrain [dir] [--out dir] [--seed N] [--spacing M] [--region-size N] [--no-erode]");
                return ExitUsage;
            }
        }

        var report = new ValidationReport();
        ContentDatabase db = ContentLoader.Load(contentDirectory, report);
        var total = Stopwatch.StartNew();

        foreach (MacroMapDefinition map in db.MacroMaps.Values)
        {
            RegionField regions = MacroRasterizer.Rasterize(map, report);
            var step = Stopwatch.StartNew();
            HeightField? field = HeightFieldGenerator.Generate(
                map, regions, db.Biomes, seed, new HeightFieldSettings { SpacingMeters = spacing }, report);
            if (field is null)
            {
                continue;
            }

            output.WriteLine($"  height field: {field.VerticesPerSide} x {field.VerticesPerSide} vertices at {spacing} m ({step.Elapsed.TotalSeconds:F1} s)");

            RiverNetwork? network = null;
            string generators = $"{HeightFieldGenerator.GeneratorId} v{HeightFieldGenerator.GeneratorVersion}";
            if (erode)
            {
                step.Restart();
                var settings = new ErosionSettings { WorkingSpacingMeters = Math.Max(ErosionSettings.DefaultWorkingSpacingMeters, spacing) };
                ErodedTerrain? eroded = ErosionGenerator.Apply(map, regions, db.Biomes, field, settings, report);
                if (eroded is null)
                {
                    continue;
                }

                field = eroded.Field;
                network = eroded.Network;
                generators += $", {ErosionGenerator.GeneratorId} v{ErosionGenerator.GeneratorVersion}";
                output.WriteLine($"  erosion: deepest carve {eroded.MaxCarveDepthMeters:F1} m, {network.Rivers.Count} river(s), {network.Lakes.Count} lake(s) ({step.Elapsed.TotalSeconds:F1} s)");
            }

            step.Restart();
            TerrainManifest? manifest = TerrainBaker.Bake(
                field, network, map.Id.Value, seed, new[] { generators }, outputDirectory, report, regionSize);
            if (manifest is null)
            {
                continue;
            }

            output.WriteLine(
                $"  baked {manifest.RegionsPerAxis * manifest.RegionsPerAxis} region tile(s) of {manifest.RegionSize} x {manifest.RegionSize} vertices " +
                $"({manifest.RegionExtentMeters} m each) to '{outputDirectory}' ({step.Elapsed.TotalSeconds:F1} s)");
            output.WriteLine($"  height range {manifest.MinHeight:F1} m to {manifest.MaxHeight:F1} m; spawn ({manifest.SpawnX:F0}, {manifest.SpawnZ:F0}); checksum {manifest.Checksum}");
        }

        foreach (ValidationIssue issue in report.Issues)
        {
            output.WriteLine(issue);
        }

        output.WriteLine($"{report.Count(Severity.Error)} error(s), {report.Count(Severity.Warning)} warning(s) ({total.Elapsed.TotalSeconds:F1} s total).");
        return report.HasErrors ? ExitErrors : ExitOk;
    }
}
