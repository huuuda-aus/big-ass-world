namespace Realm.Sim.Navigation;

/// <summary>A straight, walkable link between two nodes.</summary>
/// <param name="A">Index of the first node.</param>
/// <param name="B">Index of the second node.</param>
/// <param name="LengthMeters">Walking length.</param>
/// <param name="IsBridge">Whether the link crosses water on a bridge.</param>
/// <param name="Weight">How unattractive the walk is: a path search counts the length times the weight (1 for a street, more for a short cut across a yard); at least 1.</param>
public readonly record struct NavEdge(int A, int B, double LengthMeters, bool IsBridge = false, double Weight = 1)
{
    /// <summary>The node at the other end.</summary>
    /// <param name="node">One end of the edge.</param>
    /// <returns>The other end.</returns>
    public int Other(int node) => node == A ? B : A;
}
