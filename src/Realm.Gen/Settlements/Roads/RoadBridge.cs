namespace Realm.Gen.Settlements.Roads;

/// <summary>A bridge where a road crosses a river.</summary>
/// <param name="X">World X of the bridge centre in metres.</param>
/// <param name="Z">World Z of the bridge centre in metres.</param>
/// <param name="YawRadians">Direction of the bridge along the road, as the plan angle <c>atan2(dz, dx)</c>.</param>
/// <param name="LengthMeters">Length of the deck between the abutments.</param>
/// <param name="WidthMeters">Width of the deck.</param>
/// <param name="StartHeight">Deck height at the first end, in metres above sea level.</param>
/// <param name="EndHeight">Deck height at the second end, in metres above sea level.</param>
/// <param name="FirstPoint">Index of the road point where the bridge starts.</param>
/// <param name="LastPoint">Index of the road point where the bridge ends.</param>
/// <param name="StartFootingHeight">Height of the lowest ground under the first abutment, in metres, so the abutment can reach it; null when unknown (older files).</param>
/// <param name="EndFootingHeight">Height of the lowest ground under the second abutment, in metres; null when unknown.</param>
public readonly record struct RoadBridge(
    double X,
    double Z,
    double YawRadians,
    double LengthMeters,
    double WidthMeters,
    double StartHeight,
    double EndHeight,
    int FirstPoint,
    int LastPoint,
    double? StartFootingHeight = null,
    double? EndFootingHeight = null);
