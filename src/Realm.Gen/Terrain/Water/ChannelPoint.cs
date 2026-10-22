namespace Realm.Gen.Terrain.Water;

/// <summary>One point of a river's water profile: where the centre line is and how high the water stands there.</summary>
/// <param name="X">World east in metres.</param>
/// <param name="Z">World south in metres.</param>
/// <param name="Surface">Height of the water surface in metres above sea level; never rises along the flow.</param>
/// <param name="HalfWidth">Half the water width in metres.</param>
/// <param name="FlowKm2">Catchment flow, a measure of how much water the river carries here.</param>
/// <param name="DistanceMeters">Distance along the river from its source.</param>
/// <param name="InLake">Whether the point lies inside a lake (the lake surface is used and nothing is carved).</param>
public readonly record struct ChannelPoint(double X, double Z, double Surface, double HalfWidth, double FlowKm2, double DistanceMeters, bool InLake);
