namespace Realm.Gen.Props;

/// <summary>One prop in a scale-reference strip, positioned relative to the strip.</summary>
/// <param name="Kind">What to place.</param>
/// <param name="AlongMeters">Distance from the start of its 100 m segment, along the strip.</param>
/// <param name="SideMeters">Distance from the strip centre line; positive is to the left of the walking direction.</param>
/// <param name="HeightMeters">Tree height in metres (0 for houses).</param>
public readonly record struct StripPlacement(StripPropKind Kind, float AlongMeters, float SideMeters, float HeightMeters);
