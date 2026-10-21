using System.Numerics;
using Realm.Gen.Props;

namespace Realm.Gen.Settlements.Meshing;

/// <summary>The meshes of a city's centrepiece.</summary>
/// <param name="Solid">Stone, wood, awnings and goods; also used for collision.</param>
/// <param name="Water">Water surfaces (drawn translucent), or null.</param>
/// <param name="Flames">Flame tufts of a pyre (animated by the game), or null.</param>
/// <param name="FireLight">Where a pyre's light stands, in the city's local frame, or null for centrepieces without fire.</param>
/// <param name="JetOrigin">Where a fountain's water jets start, or null.</param>
public sealed record LandmarkMeshes(PropMesh Solid, PropMesh? Water, PropMesh? Flames, Vector3? FireLight, Vector3? JetOrigin);
