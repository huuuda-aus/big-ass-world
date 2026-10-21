using System.Collections.Generic;
using Realm.Gen.Settlements.Geometry;

namespace Realm.Gen.Settlements.Model;

/// <summary>
/// A room outline. Edges are the inner wall faces; two rooms sharing a wall touch along it,
/// so the engine should thicken interior walls around the shared line.
/// </summary>
public sealed class Room
{
    /// <summary>Index of the room in the building.</summary>
    public int Index { get; }
    /// <summary>Outline of the room.</summary>
    public ConvexPoly Shape { get; }

    /// <summary>Creates a room.</summary>
    /// <param name="index">Index of the room.</param>
    /// <param name="shape">Outline of the inner wall faces.</param>
    public Room(int index, ConvexPoly shape)
    {
        Index = index;
        Shape = shape;
    }
}
