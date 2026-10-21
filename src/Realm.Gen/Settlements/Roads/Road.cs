using System.Collections.Generic;

namespace Realm.Gen.Settlements.Roads;

/// <summary>A road between two settlements.</summary>
/// <param name="Id">Stable identifier such as <c>road.1</c>.</param>
/// <param name="From">ID of the settlement it starts at (at a gate).</param>
/// <param name="To">ID of the settlement it ends at (at a gate).</param>
/// <param name="WidthMeters">Width of the road surface in metres.</param>
/// <param name="Points">Centre line, spaced about 8 m apart, with the graded surface height.</param>
/// <param name="Bridges">Bridges over rivers, in order along the road.</param>
public sealed record Road(
    string Id,
    string From,
    string To,
    double WidthMeters,
    IReadOnlyList<RoadPoint> Points,
    IReadOnlyList<RoadBridge> Bridges);
