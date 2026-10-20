using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using Realm.Core.Ids;
using Realm.Core.Validation;
using Realm.Data.Content;
using Realm.Gen.Terrain;
using Realm.Gen.Terrain.Erosion;

namespace Realm.Cli;

internal static class HeightFieldCommand
{
    private const int ExitOk = 0;
    private const int ExitErrors = 1;
    private const int ExitUsage = 2;
    private const ulong DefaultSeed = 1;
    private const int DefaultPreviewPixels = 1024;

    /// <summary>Generates the height field of each world and prints statistics; optionally writes a PNG preview.</summary>
    /// <returns>0 when there are no errors, 1 on errors, 2 on bad arguments.</returns>
    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        string contentDirectory = "content";
        ulong seed = DefaultSeed;
        int spacing = HeightFieldSettings.DefaultSpacingMeters;
        int blend = HeightFieldSettings.DefaultBlendRadiusMeters;
        string? pngPath = null;
        bool erode = false;
        int iterations = ErosionSettings.DefaultIterations;
        double strength = ErosionSettings.DefaultStrength;
        double threshold = ErosionSettings.DefaultRiverThresholdKm2;
        double minLakeArea = ErosionSettings.DefaultMinLakeAreaKm2;

        for (int i = 1; i < args.Length; i++)
        {
            string arg = args[i];
            if (arg == "--seed" && i + 1 < args.Length && ulong.TryParse(args[++i], NumberStyles.None, CultureInfo.InvariantCulture, out ulong s))
            {
                seed = s;
            }
            else if (arg == "--spacing" && i + 1 < args.Length && int.TryParse(args[++i], NumberStyles.None, CultureInfo.InvariantCulture, out int sp))
            {
                spacing = sp;
            }
            else if (arg == "--blend" && i + 1 < args.Length && int.TryParse(args[++i], NumberStyles.None, CultureInfo.InvariantCulture, out int bl))
            {
                blend = bl;
            }
            else if (arg == "--erode")
            {
                erode = true;
            }
            else if (arg == "--iterations" && i + 1 < args.Length && int.TryParse(args[++i], NumberStyles.None, CultureInfo.InvariantCulture, out int it))
            {
                iterations = it;
            }
            else if (arg == "--strength" && i + 1 < args.Length && double.TryParse(args[++i], NumberStyles.Float, CultureInfo.InvariantCulture, out double st))
            {
                strength = st;
            }
            else if (arg == "--min-lake-area" && i + 1 < args.Length && double.TryParse(args[++i], NumberStyles.Float, CultureInfo.InvariantCulture, out double ml))
            {
                minLakeArea = ml;
            }
            else if (arg == "--threshold" && i + 1 < args.Length && double.TryParse(args[++i], NumberStyles.Float, CultureInfo.InvariantCulture, out double th))
            {
                threshold = th;
            }
            else if (arg == "--png" && i + 1 < args.Length)
            {
                pngPath = args[++i];
            }
            else if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                contentDirectory = arg;
            }
            else
            {
                error.WriteLine($"Bad argument '{arg}'. Usage: realm height-field [dir] [--seed N] [--spacing M] [--blend M] [--erode] [--iterations N] [--strength S] [--threshold KM2] [--min-lake-area KM2] [--png file]");
                return ExitUsage;
            }
        }

        var report = new ValidationReport();
        ContentDatabase db = ContentLoader.Load(contentDirectory, report);
        var settings = new HeightFieldSettings { SpacingMeters = spacing, BlendRadiusMeters = blend };

        foreach (MacroMapDefinition map in db.MacroMaps.Values)
        {
            RegionField regions = MacroRasterizer.Rasterize(map, report);
            var watch = Stopwatch.StartNew();
            HeightField? field = HeightFieldGenerator.Generate(map, regions, db.Biomes, seed, settings, report);
            watch.Stop();
            if (field is null)
            {
                continue;
            }

            Print(map, field, regions, seed, watch.Elapsed.TotalSeconds, output);

            RiverNetwork? network = null;
            if (erode)
            {
                // The working grid cannot be finer than the field, so a coarse field (64 m, 128 m, ...) raises it to match.
                var erosion = new ErosionSettings { WorkingSpacingMeters = Math.Max(ErosionSettings.DefaultWorkingSpacingMeters, spacing), Iterations = iterations, Strength = strength, RiverThresholdKm2 = threshold, MinLakeAreaKm2 = minLakeArea, BlendRadiusMeters = blend };
                var erosionWatch = Stopwatch.StartNew();
                ErodedTerrain? eroded = ErosionGenerator.Apply(map, regions, db.Biomes, field, erosion, report);
                erosionWatch.Stop();
                if (eroded is null)
                {
                    continue;
                }

                field = eroded.Field;
                network = eroded.Network;
                PrintErosion(eroded, erosionWatch.Elapsed.TotalSeconds, output);
            }

            if (pngPath is not null)
            {
                byte[] pixels = HeightPreview.Render(field, DefaultPreviewPixels, out int w, out int h, network, regions);
                File.WriteAllBytes(pngPath, PngEncoder.Encode(w, h, pixels));
                output.WriteLine($"  preview: {pngPath} ({w} x {h})");
            }
        }

        foreach (ValidationIssue issue in report.Issues)
        {
            output.WriteLine(issue);
        }

        output.WriteLine($"{report.Count(Severity.Error)} error(s), {report.Count(Severity.Warning)} warning(s).");
        return report.HasErrors ? ExitErrors : ExitOk;
    }

    private static void Print(MacroMapDefinition map, HeightField field, RegionField regions, ulong seed, double seconds, TextWriter output)
    {
        output.WriteLine(
            $"{map.Id}: {field.VerticesPerSide} x {field.VerticesPerSide} vertices at {field.SpacingMeters} m, seed {seed} " +
            $"({HeightFieldGenerator.GeneratorId} v{HeightFieldGenerator.GeneratorVersion}, checksum {field.Checksum():x16}, {seconds:F2} s)");
        output.WriteLine($"  height range: {field.MinHeight:F1} m to {field.MaxHeight:F1} m");
        output.WriteLine($"  {"region",-28} {"min m",9} {"max m",9} {"mean m",9}");
        foreach (ContentId id in regions.Regions)
        {
            (double min, double max, double mean) = RegionStats(field, regions, id);
            output.WriteLine($"  {id,-28} {min,9:F1} {max,9:F1} {mean,9:F1}");
        }
    }

    private static void PrintErosion(ErodedTerrain eroded, double seconds, TextWriter output)
    {
        RiverNetwork net = eroded.Network;
        double totalKm = 0;
        foreach (River river in net.Rivers)
        {
            totalKm += river.LengthMeters / 1000.0;
        }

        output.WriteLine(
            $"  erosion ({ErosionGenerator.GeneratorId} v{ErosionGenerator.GeneratorVersion}, checksum {eroded.Checksum():x16}, {seconds:F2} s): " +
            $"height range {eroded.Field.MinHeight:F1} m to {eroded.Field.MaxHeight:F1} m, deepest carve {eroded.MaxCarveDepthMeters:F1} m");
        output.WriteLine($"  rivers: {net.Rivers.Count} ({totalKm:F1} km total); lakes: {net.Lakes.Count}");
        int shown = 0;
        foreach (River river in net.Rivers)
        {
            if (shown++ == 5)
            {
                output.WriteLine("  ...");
                break;
            }

            RiverPoint source = river.Points[0];
            output.WriteLine(
                $"    river {river.Index}: {river.LengthMeters / 1000.0:F1} km from ({source.X:F0}, {source.Z:F0}) to {river.Mouth}" +
                (river.JoinsRiver is int j ? $" of river {j}" : string.Empty) + $", max flow {river.MaxFlowKm2:F1} km2");
        }

        foreach (Lake lake in net.Lakes)
        {
            output.WriteLine($"    lake {lake.Index}: {lake.AreaSquareKilometres:F2} km2 at {lake.SurfaceHeight:F0} m near ({lake.CentreX:F0}, {lake.CentreZ:F0})");
        }
    }

    private static (double Min, double Max, double Mean) RegionStats(HeightField field, RegionField regions, ContentId id)
    {
        double half = field.WorldSizeMeters / 2.0;
        double min = double.MaxValue;
        double max = double.MinValue;
        double sum = 0;
        long count = 0;
        for (int vz = 0; vz < field.VerticesPerSide; vz++)
        {
            for (int vx = 0; vx < field.VerticesPerSide; vx++)
            {
                double x = Math.Min(-half + (vx * (double)field.SpacingMeters), half - 1);
                double z = Math.Min(-half + (vz * (double)field.SpacingMeters), half - 1);
                if (regions.RegionAt(x, z) != id)
                {
                    continue;
                }

                double h = field.At(vx, vz);
                min = Math.Min(min, h);
                max = Math.Max(max, h);
                sum += h;
                count++;
            }
        }

        return count == 0 ? (0, 0, 0) : (min, max, sum / count);
    }
}
