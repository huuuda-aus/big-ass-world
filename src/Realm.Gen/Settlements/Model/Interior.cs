using System.Collections.Generic;
using Realm.Gen.Settlements.Geometry;

namespace Realm.Gen.Settlements.Model;

/// <summary>The rooms, doors, windows and stairs inside one building.</summary>
public sealed class Interior
{
    /// <summary>Identifier of the building this belongs to.</summary>
    public int BuildingId { get; }
    /// <summary>Height of one storey in metres.</summary>
    public double FloorHeight { get; }
    /// <summary>Thickness of the outer walls in metres.</summary>
    public double ExteriorWallThickness { get; }
    /// <summary>Thickness of walls between rooms in metres.</summary>
    public double InteriorWallThickness { get; }
    /// <summary>The rooms; the same partition on every floor.</summary>
    public IReadOnlyList<Room> Rooms { get; }
    /// <summary>The floors, ground floor first.</summary>
    public IReadOnlyList<Floor> Floors { get; }

    /// <summary>Null for single-floor buildings.</summary>
    public Stair? Stairs { get; }

    /// <summary>Creates an interior.</summary>
    /// <param name="buildingId">Identifier of the building.</param>
    /// <param name="floorHeight">Height of one storey in metres.</param>
    /// <param name="exteriorWall">Outer wall thickness in metres.</param>
    /// <param name="interiorWall">Inner wall thickness in metres.</param>
    /// <param name="rooms">The rooms.</param>
    /// <param name="floors">The floors.</param>
    /// <param name="stairs">The staircase, or null for single-floor buildings.</param>
    public Interior(int buildingId, double floorHeight, double exteriorWall, double interiorWall,
        IReadOnlyList<Room> rooms, IReadOnlyList<Floor> floors, Stair? stairs)
    {
        BuildingId = buildingId;
        FloorHeight = floorHeight;
        ExteriorWallThickness = exteriorWall;
        InteriorWallThickness = interiorWall;
        Rooms = rooms;
        Floors = floors;
        Stairs = stairs;
    }
}
