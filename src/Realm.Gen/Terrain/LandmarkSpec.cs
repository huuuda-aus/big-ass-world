namespace Realm.Gen.Terrain;

/// <summary>How often a biome scatters one kind of landmark and how big they are.</summary>
/// <param name="Kind">The kind.</param>
/// <param name="DensityPer100Km2">Expected number per 100 square kilometres.</param>
/// <param name="HeightMeters">Height of a hill or mountain, or depth of a basin, in metres.</param>
/// <param name="RadiusMeters">Radius of its footprint in metres.</param>
public readonly record struct LandmarkSpec(LandmarkKind Kind, double DensityPer100Km2, double HeightMeters, double RadiusMeters);
