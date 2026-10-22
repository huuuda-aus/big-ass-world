using System.Collections.Generic;
using Realm.Sim.Navigation;

namespace Realm.Gen.Settlements.Navigation;

/// <summary>A settlement's walking graph plus what could not be connected.</summary>
/// <param name="Graph">The graph, in the city's plan frame (metres, central square at the origin).</param>
/// <param name="UnreachableBuildings">IDs of buildings whose front door cannot be reached from the central square.</param>
/// <param name="UnreachableGates">Indices into <c>City.Gates</c> of gates that cannot be reached from the central square.</param>
public sealed record NavGraphResult(NavGraph Graph, IReadOnlyList<int> UnreachableBuildings, IReadOnlyList<int> UnreachableGates);
