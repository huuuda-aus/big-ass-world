using System.Collections.Generic;
using Realm.Core.Geometry;
using Realm.Core.Ids;

namespace Realm.Data.Content;

/// <summary>
/// A named area of the world made of one or more polygons, with one biome. Defined inside a macro map.
/// </summary>
/// <param name="Id">Stable identifier, for example <c>region.verdant_vale</c>.</param>
/// <param name="Name">Display name.</param>
/// <param name="Biome">The biome of the whole region.</param>
/// <param name="Polygons">The areas the region covers, in world metres; each is simple and inside the world.</param>
public sealed record RegionDefinition(
    ContentId Id,
    string Name,
    ContentId Biome,
    IReadOnlyList<Polygon> Polygons);
