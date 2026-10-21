using System.Collections.Generic;
using Realm.Gen.Settlements.Geometry;

namespace Realm.Gen.Settlements.Model;

/// <summary>A house, shop or church standing on one lot.</summary>
public sealed class Building
{
    /// <summary>Stable within a city; seeds the interior.</summary>
    public int Id { get; }

    /// <summary>Outline of the building on the ground.</summary>
    public ConvexPoly Footprint { get; }

    /// <summary>Index of the footprint edge the front door sits on.</summary>
    public int DoorEdge { get; }

    /// <summary>The ward the building belongs to.</summary>
    public WardType Ward { get; }

    /// <summary>Creates a building.</summary>
    /// <param name="id">Identifier, stable within a city.</param>
    /// <param name="footprint">Outline on the ground.</param>
    /// <param name="doorEdge">Index of the footprint edge with the front door.</param>
    /// <param name="ward">The ward it stands in.</param>
    public Building(int id, ConvexPoly footprint, int doorEdge, WardType ward)
    {
        Id = id;
        Footprint = footprint;
        DoorEdge = doorEdge;
        Ward = ward;
    }
}
