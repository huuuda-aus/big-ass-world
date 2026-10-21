namespace Realm.Gen.Settlements.Meshing;

/// <summary>A door or window cut into an outer wall.</summary>
/// <param name="Edge">Index of the footprint edge the opening is on.</param>
/// <param name="U">Distance of the opening's centre from the start of the edge, in metres.</param>
/// <param name="Width">Opening width in metres.</param>
/// <param name="Bottom">Height of the lower edge above the ground, in metres.</param>
/// <param name="Top">Height of the upper edge above the ground, in metres.</param>
/// <param name="IsDoor">True for the front door.</param>
public readonly record struct WallOpening(int Edge, double U, double Width, double Bottom, double Top, bool IsDoor);
