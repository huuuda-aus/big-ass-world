using System.Collections.Generic;
using Realm.Gen.Settlements.Geometry;

namespace Realm.Gen.Settlements.Model;

/// <summary>A door in a wall. RoomB is -1 for the front door leading outside.</summary>
public sealed class Door
{
    /// <summary>Centre of the opening on the wall line.</summary>
    public Vec2 Position { get; }

    /// <summary>Unit vector along the wall the door is cut into.</summary>
    public Vec2 WallDirection { get; }

    /// <summary>Index of the first room the door joins.</summary>
    public int RoomA { get; }
    /// <summary>Index of the second room, or -1 for a front door to the outside.</summary>
    public int RoomB { get; }
    /// <summary>Opening width in metres.</summary>
    public double Width { get; }
    /// <summary>Opening height in metres.</summary>
    public double Height { get; }

    /// <summary>Creates a door.</summary>
    /// <param name="position">Centre of the opening on the wall line.</param>
    /// <param name="wallDirection">Unit vector along the wall.</param>
    /// <param name="roomA">First room index.</param>
    /// <param name="roomB">Second room index, or -1 for outside.</param>
    /// <param name="width">Opening width in metres.</param>
    /// <param name="height">Opening height in metres.</param>
    public Door(Vec2 position, Vec2 wallDirection, int roomA, int roomB, double width = 1.0, double height = 2.1)
    {
        Position = position;
        WallDirection = wallDirection;
        RoomA = roomA;
        RoomB = roomB;
        Width = width;
        Height = height;
    }
}
