namespace Realm.Gen.Foliage;

/// <summary>One baked tree or bush.</summary>
/// <param name="Species">Index into <see cref="FoliageData.Species"/>.</param>
/// <param name="X">World east in metres.</param>
/// <param name="Z">World south in metres.</param>
/// <param name="Y">Ground height in metres.</param>
/// <param name="YawRadians">Turn about the vertical axis.</param>
/// <param name="Scale">Size multiplier.</param>
public readonly record struct FoliageInstance(int Species, double X, double Z, double Y, double YawRadians, double Scale);
