using System.Collections.Generic;
using Realm.Core.Ids;

namespace Realm.Data.Content;

/// <summary>
/// A biome: a named bundle of tags and numeric properties that the terrain generator reads for a region.
/// Which properties exist is not decided yet; they are free-form until the height-field step defines
/// what it needs. Schema reference: docs/reference/content/biome.md.
/// </summary>
/// <param name="Id">Stable identifier, for example <c>biome.temperate_forest</c>.</param>
/// <param name="Name">Display name.</param>
/// <param name="Tags">Tags such as <c>ocean</c>; enumerates in sorted order.</param>
/// <param name="Properties">Numeric properties; enumerates in sorted key order.</param>
public sealed record BiomeDefinition(
    ContentId Id,
    string Name,
    TagSet Tags,
    IReadOnlyDictionary<string, double> Properties);
