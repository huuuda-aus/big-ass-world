using Realm.Gen.Settlements.Geometry;

namespace Realm.Gen.Settlements.Meshing;

/// <summary>A house whose walls a lit window nearby can light.</summary>
/// <param name="Id">The building ID.</param>
/// <param name="Footprint">Its outline.</param>
/// <param name="BaseY">Height of its floor in the city's frame, in metres.</param>
/// <param name="Height">Height of its walls above the floor, in metres.</param>
public sealed record AuraNeighbour(int Id, ConvexPoly Footprint, double BaseY, double Height);
