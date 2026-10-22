using System.Collections.Generic;
using Realm.Core.Ids;

namespace Realm.Data.Content;

/// <summary>
/// A service building: a building every settlement of the listed tiers must contain (city hall, inn, blacksmith, ...).
/// Schema reference: docs/reference/content/service.md.
/// </summary>
/// <param name="Id">Stable identifier, for example <c>service.inn</c>.</param>
/// <param name="Name">Display name.</param>
/// <param name="Tags">Tags such as <c>trade</c>; enumerates in sorted order.</param>
/// <param name="Counts">How many of it a settlement of each tier gets (<c>city</c>, <c>town</c>); a tier that is absent gets none. Sorted by tier.</param>
/// <param name="Wards">Wards it may stand in, most preferred first (see <see cref="ServiceVocabulary.Wards"/>); empty means any ward.</param>
/// <param name="Near">Where in the ward it prefers to stand (see <see cref="ServiceVocabulary.Near"/>).</param>
/// <param name="OutsideWall">Whether it stands outside the wall, on farmland.</param>
/// <param name="MinAreaMeters2">Smallest footprint area it needs, in square metres.</param>
/// <param name="Foundation"><c>stone</c> or <c>wood</c> to force a foundation style, or null to choose from the ward.</param>
/// <param name="MainRoom">Room type of its main ground-floor room (see <see cref="ServiceVocabulary.Rooms"/>).</param>
public sealed record ServiceBuildingDefinition(
    ContentId Id,
    string Name,
    TagSet Tags,
    IReadOnlyDictionary<string, int> Counts,
    IReadOnlyList<string> Wards,
    string Near,
    bool OutsideWall,
    double MinAreaMeters2,
    string? Foundation,
    string MainRoom)
{
    /// <summary>How many of this building a settlement of a tier gets.</summary>
    /// <param name="tier">The tier word (<c>city</c> or <c>town</c>).</param>
    /// <returns>The count; 0 when the tier is not listed.</returns>
    public int CountFor(string tier) => Counts.TryGetValue(tier, out int count) ? count : 0;
}
