using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using Realm.Core.Ids;
using Realm.Core.Validation;
using Realm.Data.Content;
using Realm.Gen.Bake;
using Realm.Gen.Settlements.Generation;
using Realm.Gen.Settlements.Geometry;
using Realm.Gen.Settlements.Model;
using Realm.Gen.Settlements.Placement;
using Realm.Gen.Settlements.Roads;
using Realm.Gen.Terrain;
using Realm.Gen.Terrain.Erosion;

namespace Realm.Cli;

internal static class BakeTerrainCommand
{
    private const int ExitOk = 0;
    private const int ExitErrors = 1;
    private const int ExitUsage = 2;
    private const ulong DefaultSeed = 1;
    private const string MapFileName = "map.png";
    private const int MapPixels = 1024;
    private const double SpawnClearanceMeters = 1.5; // beyond the edge of the first city's centrepiece
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
        bool withSettlements = true;
        var settlementSettings = new SettlementSettings();
        bool withRoads = true;

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
            else if (arg == "--cities" && i + 1 < args.Length && int.TryParse(args[++i], NumberStyles.None, CultureInfo.InvariantCulture, out int cities))
            {
                settlementSettings = settlementSettings with { Cities = cities };
            }
            else if (arg == "--towns" && i + 1 < args.Length && int.TryParse(args[++i], NumberStyles.None, CultureInfo.InvariantCulture, out int towns))
            {
                settlementSettings = settlementSettings with { Towns = towns };
            }
            else if (arg == "--hamlets" && i + 1 < args.Length && int.TryParse(args[++i], NumberStyles.None, CultureInfo.InvariantCulture, out int hamlets))
            {
                settlementSettings = settlementSettings with { Hamlets = hamlets };
            }
            else if (arg == "--no-roads")
            {
                withRoads = false;
            }
            else if (arg == "--no-settlements")
            {
                withSettlements = false;
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
                error.WriteLine($"Bad argument '{arg}'. Usage: realm bake-terrain [dir] [--out dir] [--seed N] [--spacing M] [--region-size N] [--no-erode] [--cities N] [--towns N] [--hamlets N] [--no-settlements] [--no-roads]");
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

            IReadOnlyList<Settlement> settlements = Array.Empty<Settlement>();
            (double X, double Z)? spawn = null;
            if (withSettlements)
            {
                step.Restart();
                HashSet<ContentId> mountainRegions = map.Regions
                    .Where(r => db.Biomes.TryGetValue(r.Biome, out BiomeDefinition? biome) && biome.Tags.Contains("mountainous"))
                    .Select(r => r.Id)
                    .ToHashSet();
                settlements = ServiceCatalog.Attach(
                    SettlementPlacer.Place(field, network, seed, settlementSettings, report, (x, z) => mountainRegions.Contains(regions.RegionAt(x, z))), db);
                field = SettlementTerrain.Flatten(field, settlements, settlementSettings);
                SettlementValidator.Validate(settlements, field, network, settlementSettings, report);
                generators += $", {SettlementPlacer.GeneratorId} v{SettlementPlacer.GeneratorVersion}";
                output.WriteLine($"  settlements: {settlements.Count} placed on the natural ground ({step.Elapsed.TotalSeconds:F1} s)");
                foreach (Settlement s in settlements)
                {
                    output.WriteLine($"    {s.Id,-22} {s.Name,-14} {s.Tier,-5} at ({s.X:F0}, {s.Z:F0}), ground {s.GroundHeightMeters:F0} m{(s.Highland ? " (mountain)" : string.Empty)}, {(s.Fortification == Fortification.None ? "no walls" : s.Fortification.ToString().ToLowerInvariant() + " walls")}, radius {s.WallRadiusMeters:F0} m, up to {s.MaxBuildings} houses, {s.ServiceList.Sum(x => x.Count)} service building(s)");
                }

                if (settlements.Count > 0)
                {
                    // Start the player in the first city's central square, just outside its centrepiece.
                    Settlement first = settlements[0];
                    City firstCity = CityGenerator.Generate(first.ToCityParams());
                    double clearance = (firstCity.Landmark?.Radius ?? 0) + SpawnClearanceMeters;
                    spawn = first.ToWorld(new Vec2(clearance, 0));
                }
            }

            IReadOnlyList<Road> roads = Array.Empty<Road>();
            if (withSettlements && withRoads && settlements.Count > 1)
            {
                step.Restart();
                HeightField beforeRoads = field;
                roads = RoadPlanner.Plan(field, network, settlements, null, report);
                field = RoadTerrain.Carve(field, roads);
                RoadValidator.Validate(roads, beforeRoads, field, network, null, report, settlements);
                generators += $", {RoadPlanner.GeneratorId} v{RoadPlanner.GeneratorVersion}";
                double kilometres = roads.Sum(r => Enumerable.Range(1, r.Points.Count - 1).Sum(i => Math.Sqrt(Math.Pow(r.Points[i].X - r.Points[i - 1].X, 2) + Math.Pow(r.Points[i].Z - r.Points[i - 1].Z, 2)))) / 1000.0;
                output.WriteLine($"  roads: {roads.Count} road(s), {kilometres:F1} km, {roads.Sum(r => r.Bridges.Count)} bridge(s), terrain graded under them ({step.Elapsed.TotalSeconds:F1} s)");
            }

            step.Restart();
            TerrainManifest? manifest = TerrainBaker.Bake(
                field, network, map.Id.Value, seed, new[] { generators }, outputDirectory, report, regionSize, spawn: spawn, roads: roads);
            if (manifest is null)
            {
                continue;
            }

            if (withSettlements)
            {
                // A flat map of the finished world for the in-game map screen (M).
                byte[] mapPixels = HeightPreview.Render(field, MapPixels, out int mapWidth, out int mapHeight, network, null);
                File.WriteAllBytes(Path.Combine(outputDirectory, MapFileName), PngEncoder.Encode(mapWidth, mapHeight, mapPixels));
                var settlementManifest = new SettlementManifest
                {
                    World = map.Id.Value,
                    Seed = seed,
                    Generators = new[] { $"{SettlementPlacer.GeneratorId} v{SettlementPlacer.GeneratorVersion}" },
                    Settlements = settlements,
                };
                File.WriteAllText(Path.Combine(outputDirectory, SettlementManifest.FileName), settlementManifest.ToJson());
                // The terrain under each city after roads, so the game can build on it without the terrain engine.
                File.WriteAllBytes(
                    Path.Combine(outputDirectory, GroundGridFile.FileName),
                    GroundGridFile.ToBytes(settlements.Select(x => GroundGrid.Capture(field, x)).ToList()));
                var roadManifest = new RoadManifest
                {
                    World = map.Id.Value,
                    Seed = seed,
                    Generators = new[] { $"{RoadPlanner.GeneratorId} v{RoadPlanner.GeneratorVersion}" },
                    Roads = roads,
                };
                File.WriteAllText(Path.Combine(outputDirectory, RoadManifest.FileName), roadManifest.ToJson());
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
