using System.Collections.Generic;
using Realm.Sim.Navigation;

namespace Realm.Gen.Settlements.Navigation;

/// <summary>Where one settlement's nodes sit in the world graph.</summary>
/// <param name="Id">The settlement ID.</param>
/// <param name="FirstNode">Index of the settlement's first node in the world graph; its nodes follow in the order of its own graph.</param>
/// <param name="NodeCount">Number of nodes the settlement contributed.</param>
public sealed record WorldNavSettlement(string Id, int FirstNode, int NodeCount);

/// <summary>The walking graph of the whole world: every settlement's graph in world coordinates, joined by the roads.</summary>
/// <param name="Graph">The graph, in world metres (X east, Z south).</param>
/// <param name="Settlements">Where each settlement's nodes are, in the order given.</param>
/// <param name="NotJoined">IDs of settlements that the road network does not connect to the first settlement.</param>
public sealed record WorldNavResult(NavGraph Graph, IReadOnlyList<WorldNavSettlement> Settlements, IReadOnlyList<string> NotJoined);
