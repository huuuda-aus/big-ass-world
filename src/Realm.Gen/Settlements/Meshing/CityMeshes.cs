using System.Collections.Generic;
using Realm.Gen.Props;
using Realm.Gen.Settlements.Model;

namespace Realm.Gen.Settlements.Meshing;

/// <summary>The city-wide meshes: ground, streets, wall and the centrepiece.</summary>
/// <param name="Wall">The wall, towers and gate towers (also the collision shape).</param>
/// <param name="Landmark">The centrepiece, or null. Its mesh has its base at 0; the caller places it at <paramref name="LandmarkBaseY"/>.</param>
/// <param name="LandmarkBaseY">Height of the centrepiece's base above the city origin, in metres.</param>
/// <param name="Ladders">Watchtower ladders (a palisade only; a stone wall's towers have none yet).</param>
public sealed record CityMeshes(PropMesh? Wall, LandmarkMeshes? Landmark, double LandmarkBaseY = 0, IReadOnlyList<Ladder>? Ladders = null)
{
    /// <summary>Watchtower ladders, never null (empty when there are none).</summary>
    public IReadOnlyList<Ladder> Ladders { get; } = Ladders ?? System.Array.Empty<Ladder>();
}
