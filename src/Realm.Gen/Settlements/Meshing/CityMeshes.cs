using Realm.Gen.Props;

namespace Realm.Gen.Settlements.Meshing;

/// <summary>The city-wide meshes: ground, streets, wall and the centrepiece.</summary>
/// <param name="Wall">The wall, towers and gate towers (also the collision shape).</param>
/// <param name="Landmark">The centrepiece, or null. Its mesh has its base at 0; the caller places it at <paramref name="LandmarkBaseY"/>.</param>
/// <param name="LandmarkBaseY">Height of the centrepiece's base above the city origin, in metres.</param>
public sealed record CityMeshes(PropMesh? Wall, LandmarkMeshes? Landmark, double LandmarkBaseY = 0);
