using System.Collections.Generic;
using Realm.Gen.Props;

namespace Realm.Gen.Settlements.Meshing;

/// <summary>The meshes of a building that has a real interior.</summary>
/// <param name="Visual">The outside the player sees in daylight: outer walls with openings, roof, timber, frames, foundation and front door.</param>
/// <param name="Collision">Walls, floors and stairs for physics; stairs are a smooth ramp so a walker can climb them.</param>
/// <param name="Glass">The window panes (translucent), or null when the house has no windows.</param>
/// <param name="Rooms">The inside surfaces, one entry per floor and room that has any, ordered by floor then room.</param>
public sealed record BuildingMeshes(PropMesh Visual, PropMesh Collision, PropMesh? Glass, IReadOnlyList<RoomSurface> Rooms);
