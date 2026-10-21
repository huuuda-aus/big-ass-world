using System.Collections.Generic;
using Realm.Gen.Settlements.Geometry;

namespace Realm.Gen.Settlements.Model;

/// <summary>
/// A centrepiece in the central square. Radius is how much of the square it may occupy, so
/// a hamlet gets a modest one and a city a grand one. A market is a ring of stalls instead of
/// a single object; the stalls are rectangles, not enterable buildings.
/// </summary>
public sealed class Landmark
{
    /// <summary>What stands in the square.</summary>
    public CentrepieceKind Kind { get; }
    /// <summary>Centre of the landmark.</summary>
    public Vec2 Center { get; }
    /// <summary>How much of the square it may occupy, in metres.</summary>
    public double Radius { get; }
    /// <summary>Stall rectangles for a market; empty otherwise.</summary>
    public IReadOnlyList<ConvexPoly> Stalls { get; }

    /// <summary>Creates a landmark.</summary>
    /// <param name="kind">What stands in the square.</param>
    /// <param name="center">Centre.</param>
    /// <param name="radius">Space it may occupy, in metres.</param>
    /// <param name="stalls">Stall rectangles (market only).</param>
    public Landmark(CentrepieceKind kind, Vec2 center, double radius, IReadOnlyList<ConvexPoly> stalls)
    {
        Kind = kind;
        Center = center;
        Radius = radius;
        Stalls = stalls;
    }
}
