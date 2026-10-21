using Realm.Gen.Props;

namespace Realm.Gen.Settlements.Meshing;

/// <summary>The meshes of a building that has a real interior.</summary>
/// <param name="Visual">Everything the player sees: outer walls with openings, interior, roof, timber.</param>
/// <param name="Collision">Walls, floors and stairs for physics; stairs are a smooth ramp so a walker can climb them.</param>
public sealed record BuildingMeshes(PropMesh Visual, PropMesh Collision);
