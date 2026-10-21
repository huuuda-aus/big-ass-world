using Realm.Gen.Props;

namespace Realm.Gen.Settlements.Meshing;

/// <summary>The city-wide meshes: ground, streets, wall and the centrepiece.</summary>
/// <param name="Ground">Ward ground colours and streets, lying just above the terrain.</param>
/// <param name="Wall">The wall, towers and gate towers (also the collision shape).</param>
/// <param name="Landmark">The centrepiece, or null.</param>
public sealed record CityMeshes(PropMesh Ground, PropMesh? Wall, LandmarkMeshes? Landmark);
