using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Realm.Core.Ids;
using Realm.Core.Logging;
using Realm.Core.Validation;

namespace Realm.Data.Content;

/// <summary>
/// Loads a content directory (<c>content/&lt;type directory&gt;/*.yaml</c>, one record per file)
/// into a <see cref="ContentDatabase"/>.
/// </summary>
/// <remarks>
/// Determinism: directories and files are visited in ordinal sorted order, so results and issue
/// order do not depend on the file system. Thread-safety: stateless.
/// </remarks>
public static class ContentLoader
{
    private const string YamlExtension = ".yaml";

    private static readonly Logger Logger = Log.For("Data.Loader");

    private static readonly TypeSpec<MaterialDefinition> MaterialSpec =
        new("materials", "material", MaterialParser.Parse, m => m.Id);

    private static readonly TypeSpec<ItemDefinition> ItemSpec =
        new("items", "item", ItemParser.Parse, i => i.Id);

    private static readonly TypeSpec<BiomeDefinition> BiomeSpec =
        new("biomes", "biome", BiomeParser.Parse, b => b.Id);

    private static readonly TypeSpec<MacroMapDefinition> WorldSpec =
        new("world", "world", MacroMapParser.Parse, m => m.Id);

    private static readonly TypeSpec<ServiceBuildingDefinition> ServiceSpec =
        new("services", "service", ServiceBuildingParser.Parse, s => s.Id);

    private static readonly string[] KnownDirectories =
    {
        MaterialSpec.Directory, ItemSpec.Directory, BiomeSpec.Directory, WorldSpec.Directory, ServiceSpec.Directory,
    };

    /// <summary>Loads and validates all content under a root directory, including cross-record references.</summary>
    /// <param name="rootDirectory">The content root, for example <c>content</c>.</param>
    /// <param name="report">Receives every issue. Check <see cref="ValidationReport.HasErrors"/>.</param>
    /// <returns>A database holding only the records that were valid.</returns>
    public static ContentDatabase Load(string rootDirectory, ValidationReport report)
    {
        ArgumentNullException.ThrowIfNull(rootDirectory);
        ArgumentNullException.ThrowIfNull(report);

        if (!Directory.Exists(rootDirectory))
        {
            report.Error(
                ValidatorCodes.RootMissing,
                rootDirectory,
                "Content directory does not exist.",
                "Pass the path of the content directory, for example 'content'.");
            return ContentDatabase.Empty;
        }

        foreach (string dir in SortedEntries(Directory.GetDirectories(rootDirectory)))
        {
            string dirName = Path.GetFileName(dir);
            if (!KnownDirectories.Contains(dirName))
            {
                report.Warning(
                    ValidatorCodes.UnknownDirectory,
                    dirName,
                    $"'{dirName}' is not a known content directory; its files are ignored.");
            }
        }

        var sources = new Dictionary<ContentId, string>();
        Dictionary<ContentId, MaterialDefinition> materials = LoadType(rootDirectory, MaterialSpec, report, sources);
        Dictionary<ContentId, ItemDefinition> items = LoadType(rootDirectory, ItemSpec, report, sources);
        Dictionary<ContentId, BiomeDefinition> biomes = LoadType(rootDirectory, BiomeSpec, report, sources);
        Dictionary<ContentId, MacroMapDefinition> maps = LoadType(rootDirectory, WorldSpec, report, sources);
        Dictionary<ContentId, RegionDefinition> regions = CollectRegions(maps, report, sources);
        Dictionary<ContentId, ServiceBuildingDefinition> services = LoadType(rootDirectory, ServiceSpec, report, sources);

        var database = new ContentDatabase(materials, items, biomes, maps, regions, services, sources);
        ContentValidator.ValidateReferences(database, report);

        Logger.Info(
            $"Loaded {materials.Count} material(s), {items.Count} item(s), {biomes.Count} biome(s), " +
            $"{maps.Count} world map(s) with {regions.Count} region(s), {services.Count} service(s) from '{rootDirectory}'.");
        return database;
    }

    // Regions live inside macro maps but share the global ID space, so a clash with any other record is a duplicate.
    private static Dictionary<ContentId, RegionDefinition> CollectRegions(
        Dictionary<ContentId, MacroMapDefinition> maps,
        ValidationReport report,
        Dictionary<ContentId, string> sources)
    {
        var regions = new Dictionary<ContentId, RegionDefinition>();
        foreach (MacroMapDefinition map in maps.Values.OrderBy(m => m.Id))
        {
            string location = sources[map.Id];
            foreach (RegionDefinition region in map.Regions)
            {
                if (sources.TryGetValue(region.Id, out string? first))
                {
                    report.Error(
                        ValidatorCodes.DuplicateId,
                        location,
                        $"ID '{region.Id}' is already defined in {first}.",
                        "Give each record a unique ID.");
                    continue;
                }

                sources[region.Id] = location;
                regions[region.Id] = region;
            }
        }

        return regions;
    }

    private static Dictionary<ContentId, T> LoadType<T>(
        string rootDirectory,
        TypeSpec<T> spec,
        ValidationReport report,
        Dictionary<ContentId, string> sources)
        where T : class
    {
        var records = new Dictionary<ContentId, T>();
        string directory = Path.Combine(rootDirectory, spec.Directory);
        if (!Directory.Exists(directory))
        {
            return records;
        }

        foreach (string file in SortedEntries(Directory.GetFiles(directory, "*" + YamlExtension)))
        {
            string location = $"{spec.Directory}/{Path.GetFileName(file)}";
            T? record = LoadRecord(file, location, spec, report);
            if (record is null)
            {
                continue;
            }

            ContentId id = spec.GetId(record);
            if (sources.TryGetValue(id, out string? first))
            {
                report.Error(
                    ValidatorCodes.DuplicateId,
                    location,
                    $"ID '{id}' is already defined in {first}.",
                    "Give each record a unique ID.");
                continue;
            }

            sources[id] = location;
            records[id] = record;
        }

        return records;
    }

    private static T? LoadRecord<T>(string file, string location, TypeSpec<T> spec, ValidationReport report)
        where T : class
    {
        string yaml;
        try
        {
            yaml = File.ReadAllText(file);
        }
        catch (IOException e)
        {
            report.Error(ValidatorCodes.FileUnreadable, location, e.Message);
            return null;
        }
        catch (UnauthorizedAccessException e)
        {
            report.Error(ValidatorCodes.FileUnreadable, location, e.Message);
            return null;
        }

        T? record = spec.Parse(yaml, location, report);
        if (record is null)
        {
            return null;
        }

        ContentId id = spec.GetId(record);
        if (id.Type != spec.TypeName)
        {
            report.Error(
                ValidatorCodes.IdTypeMismatch,
                location,
                $"ID '{id}' has type '{id.Type}' but the file is in '{spec.Directory}/'.",
                $"Start the ID with '{spec.TypeName}.' or move the file.");
            return null;
        }

        string idName = id.Value[(id.Type.Length + 1)..];
        if (Path.GetFileNameWithoutExtension(file) != idName)
        {
            report.Warning(
                ValidatorCodes.FileNameMismatch,
                location,
                $"File name does not match ID '{id}'.",
                $"Rename the file to '{idName}{YamlExtension}'.");
        }

        return record;
    }

    private static IEnumerable<string> SortedEntries(IEnumerable<string> paths) =>
        paths.OrderBy(p => p, StringComparer.Ordinal);

    private sealed record TypeSpec<T>(
        string Directory,
        string TypeName,
        Func<string, string, ValidationReport, T?> Parse,
        Func<T, ContentId> GetId)
        where T : class;
}
