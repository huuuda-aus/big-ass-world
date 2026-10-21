using System.Collections.Generic;
using Realm.Gen.Settlements.Geometry;

namespace Realm.Gen.Settlements.Model;

/// <summary>One storey of a building.</summary>
public sealed class Floor
{
    /// <summary>Floor number, 0 on the ground.</summary>
    public int Level { get; }
    /// <summary>Doors on this floor.</summary>
    public IReadOnlyList<Door> Doors { get; }
    /// <summary>Windows on this floor.</summary>
    public IReadOnlyList<Window> Windows { get; }

    /// <summary>Same room partition on every floor; only the room uses differ.</summary>
    public IReadOnlyList<RoomType> RoomTypes { get; }

    /// <summary>Creates a floor.</summary>
    /// <param name="level">Floor number, 0 on the ground.</param>
    /// <param name="doors">Doors on this floor.</param>
    /// <param name="windows">Windows on this floor.</param>
    /// <param name="roomTypes">Use of each room on this floor.</param>
    public Floor(int level, IReadOnlyList<Door> doors, IReadOnlyList<Window> windows, IReadOnlyList<RoomType> roomTypes)
    {
        Level = level;
        Doors = doors;
        Windows = windows;
        RoomTypes = roomTypes;
    }
}
