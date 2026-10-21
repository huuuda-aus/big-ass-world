using System.Collections.Generic;
using Realm.Gen.Settlements.Geometry;

namespace Realm.Gen.Settlements.Model;

/// <summary>
/// Straight stair inside one room, rising from Start on floor n to End on floor n+1.
/// The same footprint is used on every floor, so the opening above lines up.
/// </summary>
public sealed class Stair
{
    /// <summary>Index of the room holding the stair.</summary>
    public int Room { get; }
    /// <summary>Where the stair starts on its lower floor.</summary>
    public Vec2 Start { get; }
    /// <summary>Where the stair ends on the floor above.</summary>
    public Vec2 End { get; }
    /// <summary>Width of the stair in metres.</summary>
    public double Width { get; }

    /// <summary>Creates a stair.</summary>
    /// <param name="room">Room holding the stair.</param>
    /// <param name="start">Start point on the lower floor.</param>
    /// <param name="end">End point on the upper floor.</param>
    /// <param name="width">Width in metres.</param>
    public Stair(int room, Vec2 start, Vec2 end, double width)
    {
        Room = room;
        Start = start;
        End = end;
        Width = width;
    }
}
