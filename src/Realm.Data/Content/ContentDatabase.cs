using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Realm.Core.Ids;

namespace Realm.Data.Content;

/// <summary>
/// All loaded content, looked up by stable ID. Immutable once built by <see cref="ContentLoader"/>.
/// </summary>
/// <remarks>Determinism: collections enumerate in ordinal ID order. Thread-safety: immutable.</remarks>
public sealed class ContentDatabase
{
    /// <summary>A database with no content.</summary>
    public static readonly ContentDatabase Empty = new(
        new Dictionary<ContentId, MaterialDefinition>(),
        new Dictionary<ContentId, ItemDefinition>(),
        new Dictionary<ContentId, BiomeDefinition>(),
        new Dictionary<ContentId, MacroMapDefinition>(),
        new Dictionary<ContentId, RegionDefinition>(),
        new Dictionary<ContentId, ServiceBuildingDefinition>(),
        new Dictionary<ContentId, FoliageDefinition>(),
        new Dictionary<ContentId, string>());

    private readonly IReadOnlyDictionary<ContentId, string> _sources;

    /// <summary>Creates a database from loaded records.</summary>
    /// <param name="materials">Materials by ID; copied and sorted.</param>
    /// <param name="items">Items by ID; copied and sorted.</param>
    /// <param name="biomes">Biomes by ID; copied and sorted.</param>
    /// <param name="macroMaps">Macro maps by ID; copied and sorted.</param>
    /// <param name="regions">Regions of all macro maps by ID; copied and sorted.</param>
    /// <param name="services">Service buildings by ID; copied and sorted.</param>
    /// <param name="foliage">Foliage species by ID; copied and sorted.</param>
    /// <param name="sources">File location of each record, by ID.</param>
    internal ContentDatabase(
        IDictionary<ContentId, MaterialDefinition> materials,
        IDictionary<ContentId, ItemDefinition> items,
        IDictionary<ContentId, BiomeDefinition> biomes,
        IDictionary<ContentId, MacroMapDefinition> macroMaps,
        IDictionary<ContentId, RegionDefinition> regions,
        IDictionary<ContentId, ServiceBuildingDefinition> services,
        IDictionary<ContentId, FoliageDefinition> foliage,
        IDictionary<ContentId, string> sources)
    {
        ArgumentNullException.ThrowIfNull(materials);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(biomes);
        ArgumentNullException.ThrowIfNull(macroMaps);
        ArgumentNullException.ThrowIfNull(regions);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(foliage);
        ArgumentNullException.ThrowIfNull(sources);

        Materials = Sorted(materials);
        Items = Sorted(items);
        Biomes = Sorted(biomes);
        MacroMaps = Sorted(macroMaps);
        Regions = Sorted(regions);
        Services = Sorted(services);
        Foliage = Sorted(foliage);
        _sources = new ReadOnlyDictionary<ContentId, string>(new Dictionary<ContentId, string>(sources));
    }

    /// <summary>All materials, enumerated in ordinal ID order.</summary>
    public IReadOnlyDictionary<ContentId, MaterialDefinition> Materials { get; }

    /// <summary>All items, enumerated in ordinal ID order.</summary>
    public IReadOnlyDictionary<ContentId, ItemDefinition> Items { get; }

    /// <summary>All biomes, enumerated in ordinal ID order.</summary>
    public IReadOnlyDictionary<ContentId, BiomeDefinition> Biomes { get; }

    /// <summary>All macro maps (<c>world</c> records), enumerated in ordinal ID order.</summary>
    public IReadOnlyDictionary<ContentId, MacroMapDefinition> MacroMaps { get; }

    /// <summary>Every region of every macro map, enumerated in ordinal ID order.</summary>
    public IReadOnlyDictionary<ContentId, RegionDefinition> Regions { get; }

    /// <summary>All service buildings, enumerated in ordinal ID order.</summary>
    public IReadOnlyDictionary<ContentId, ServiceBuildingDefinition> Services { get; }

    /// <summary>All foliage species, enumerated in ordinal ID order.</summary>
    public IReadOnlyDictionary<ContentId, FoliageDefinition> Foliage { get; }

    /// <summary>Checks whether any loaded record has this ID.</summary>
    /// <param name="id">The ID to look for.</param>
    /// <returns><c>true</c> when a record exists.</returns>
    public bool Contains(ContentId id) =>
        Materials.ContainsKey(id) || Items.ContainsKey(id) || Biomes.ContainsKey(id)
        || MacroMaps.ContainsKey(id) || Regions.ContainsKey(id) || Services.ContainsKey(id) || Foliage.ContainsKey(id);

    /// <summary>Finds the file a record was loaded from.</summary>
    /// <param name="id">The record ID. For a region this is the file of its macro map.</param>
    /// <param name="location">Relative file location, for example <c>items/torch.yaml</c>.</param>
    /// <returns><c>true</c> when the record is known.</returns>
    public bool TryGetSource(ContentId id, out string location)
    {
        if (_sources.TryGetValue(id, out string? found))
        {
            location = found;
            return true;
        }

        location = string.Empty;
        return false;
    }

    private static ReadOnlyDictionary<ContentId, T> Sorted<T>(IDictionary<ContentId, T> source) =>
        new(new SortedDictionary<ContentId, T>(source));
}
