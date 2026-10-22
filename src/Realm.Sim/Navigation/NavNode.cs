namespace Realm.Sim.Navigation;

/// <summary>A place on a navigation graph. Its ID is its index in <see cref="NavGraph.Nodes"/>.</summary>
/// <param name="X">Position east, in the graph's own frame (metres).</param>
/// <param name="Z">Position south, in the graph's own frame (metres).</param>
/// <param name="Kind">What the node stands for.</param>
/// <param name="Ref">The building ID for a <see cref="NavNodeKind.Door"/>; -1 for other nodes.</param>
public readonly record struct NavNode(double X, double Z, NavNodeKind Kind, int Ref = -1);
