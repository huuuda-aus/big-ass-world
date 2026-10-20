using System.Collections.Generic;
using Realm.Core.Ids;

namespace Realm.Data.Content;

/// <summary>
/// The coarse description of the world: its size and the regions that tile it. Ocean is an ordinary
/// region whose biome is tagged <c>ocean</c>. Coverage (every part of the world in exactly one region) is
/// checked by the terrain generator, not here. Schema reference: docs/reference/content/world.md.
/// </summary>
/// <param name="Id">Stable identifier, for example <c>world.main</c>.</param>
/// <param name="Name">Display name.</param>
/// <param name="WorldSizeMeters">Side length of the square world in metres; the origin is the world centre.</param>
/// <param name="Regions">The regions in file order.</param>
public sealed record MacroMapDefinition(
    ContentId Id,
    string Name,
    int WorldSizeMeters,
    IReadOnlyList<RegionDefinition> Regions);
