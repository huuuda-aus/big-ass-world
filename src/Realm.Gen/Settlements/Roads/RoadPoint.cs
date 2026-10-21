namespace Realm.Gen.Settlements.Roads;

/// <summary>A point along a road centre line.</summary>
/// <param name="X">World X in metres.</param>
/// <param name="Z">World Z in metres.</param>
/// <param name="Height">Height of the road surface in metres above sea level (the graded profile, not the natural terrain).</param>
public readonly record struct RoadPoint(double X, double Z, double Height);
