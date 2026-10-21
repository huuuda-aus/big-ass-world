using System.Collections.Generic;
using Realm.Gen.Settlements.Geometry;

namespace Realm.Gen.Settlements.Model;

/// <summary>A window opening in an outer wall.</summary>
public sealed class Window
{
    /// <summary>Centre of the opening on the wall line.</summary>
    public Vec2 Position { get; }
    /// <summary>Unit vector along the wall the window is cut into.</summary>
    public Vec2 WallDirection { get; }
    /// <summary>Index of the room the window belongs to.</summary>
    public int Room { get; }
    /// <summary>Width in metres.</summary>
    public double Width { get; }
    /// <summary>Height of the sill above the floor in metres.</summary>
    public double Sill { get; }
    /// <summary>Height of the opening in metres.</summary>
    public double Height { get; }

    /// <summary>Creates a window.</summary>
    /// <param name="position">Centre on the wall line.</param>
    /// <param name="wallDirection">Unit vector along the wall.</param>
    /// <param name="room">Room index.</param>
    /// <param name="width">Width in metres.</param>
    /// <param name="sill">Sill height in metres.</param>
    /// <param name="height">Opening height in metres.</param>
    public Window(Vec2 position, Vec2 wallDirection, int room, double width = 1.0, double sill = 1.0, double height = 1.2)
    {
        Position = position;
        WallDirection = wallDirection;
        Room = room;
        Width = width;
        Sill = sill;
        Height = height;
    }
}
