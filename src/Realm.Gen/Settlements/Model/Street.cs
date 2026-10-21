using System.Collections.Generic;
using Realm.Gen.Settlements.Geometry;

namespace Realm.Gen.Settlements.Model;

/// <summary>A street as a polyline from the central square to a gate.</summary>
public sealed class Street
{
    /// <summary>Points along the street.</summary>
    public IReadOnlyList<Vec2> Points { get; }
    /// <summary>Creates a street.</summary>
    /// <param name="points">Points along the street.</param>
    public Street(IReadOnlyList<Vec2> points) => Points = points;
}
