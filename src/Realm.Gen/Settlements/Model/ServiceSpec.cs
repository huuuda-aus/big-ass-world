using System.Collections.Generic;

namespace Realm.Gen.Settlements.Model;

/// <summary>
/// One kind of service building a settlement must contain, with how many and where. Built from a <c>service</c> content record
/// by <see cref="Placement.ServiceCatalog"/>; stored in <c>settlements.json</c> so the game needs no content loader.
/// </summary>
/// <param name="Id">Content ID of the service, for example <c>service.inn</c>.</param>
/// <param name="Name">Display name.</param>
/// <param name="Count">How many this settlement gets.</param>
/// <param name="Wards">Wards it may stand in, most preferred first; empty means any ward.</param>
/// <param name="Near">Where in the ward it prefers to stand.</param>
/// <param name="OutsideWall">Whether it stands outside the wall, on farmland.</param>
/// <param name="MinAreaMeters2">Smallest footprint it needs, in square metres.</param>
/// <param name="Foundation">A forced foundation style, or null to choose from the ward and the ground.</param>
/// <param name="MainRoom">Room type of its main ground-floor room.</param>
public sealed record ServiceSpec(
    string Id,
    string Name,
    int Count,
    IReadOnlyList<WardType> Wards,
    ServiceNear Near,
    bool OutsideWall,
    double MinAreaMeters2,
    FoundationStyle? Foundation,
    RoomType MainRoom);
