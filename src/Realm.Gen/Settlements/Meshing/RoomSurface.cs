using Realm.Gen.Props;

namespace Realm.Gen.Settlements.Meshing;

/// <summary>The inside surfaces of one room on one floor (walls facing into it, floor, ceiling, door leaves, stairs).</summary>
/// <param name="Level">The floor, 0 on the ground.</param>
/// <param name="Room">The room.</param>
/// <param name="SkyFill">Share of the sky's ambient light that reaches the room (<see cref="RoomLighting.SkyFill"/>).</param>
/// <param name="HasLamp">Whether the room keeps a lamp (<see cref="RoomLighting.HasLamp"/>).</param>
/// <param name="BaseY">Height of the room's floor in the mesh's frame, in metres.</param>
/// <param name="Mesh">The surfaces.</param>
public sealed record RoomSurface(int Level, int Room, double SkyFill, bool HasLamp, double BaseY, PropMesh Mesh);
