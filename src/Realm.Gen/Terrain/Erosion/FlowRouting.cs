namespace Realm.Gen.Terrain.Erosion;

/// <summary>
/// Where water goes on a height grid: depressions filled, a downhill receiver for every vertex, and a
/// downstream-first processing order. Built by <see cref="FlowRouter"/>.
/// </summary>
/// <remarks>Determinism: all ties are broken by vertex index. Thread-safety: immutable after construction.</remarks>
internal sealed class FlowRouting
{
    public FlowRouting(int side, double spacing, float[] filled, int[] receiver, int[] order, bool[] isOutlet, bool[] isLake)
    {
        Side = side;
        Spacing = spacing;
        Filled = filled;
        Receiver = receiver;
        Order = order;
        IsOutlet = isOutlet;
        IsLake = isLake;
    }

    /// <summary>Vertices per side of the grid.</summary>
    public int Side { get; }

    /// <summary>Distance between neighbouring vertices in metres.</summary>
    public double Spacing { get; }

    /// <summary>Heights with every depression filled up to its lowest outlet.</summary>
    public float[] Filled { get; }

    /// <summary>Index of the vertex each vertex drains to; -1 for outlets.</summary>
    public int[] Receiver { get; }

    /// <summary>
    /// Every vertex once, receivers before the vertices that drain into them (outlets first). Iterate it in
    /// reverse to go from the hilltops down.
    /// </summary>
    public int[] Order { get; }

    /// <summary>True for vertices where water leaves the map: the world edge and everything at or below sea level.</summary>
    public bool[] IsOutlet { get; }

    /// <summary>True for vertices filled by at least the minimum lake depth.</summary>
    public bool[] IsLake { get; }

    /// <summary>Distance in metres from a vertex to its receiver (neighbours are 8-connected).</summary>
    public double DistanceToReceiver(int index)
    {
        int receiver = Receiver[index];
        int dx = (index % Side) - (receiver % Side);
        int dz = (index / Side) - (receiver / Side);
        return dx != 0 && dz != 0 ? Spacing * DiagonalFactor : Spacing;
    }

    private const double DiagonalFactor = 1.4142135623730951;
}
