namespace Realm.Gen.Terrain;

/// <summary>One placed landmark.</summary>
/// <param name="Kind">The kind.</param>
/// <param name="X">World X of its centre in metres.</param>
/// <param name="Z">World Z of its centre in metres.</param>
/// <param name="HeightMeters">Height (hill, mountain) or depth (basin) in metres.</param>
/// <param name="RadiusMeters">Footprint radius in metres.</param>
public readonly record struct Landmark(LandmarkKind Kind, double X, double Z, double HeightMeters, double RadiusMeters);
