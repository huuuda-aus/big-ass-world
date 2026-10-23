using System;
using System.Collections.Generic;
using System.Linq;
using Realm.Gen.Settlements.Geometry;
using Realm.Gen.Settlements.Model;

namespace Realm.Gen.Settlements.Meshing;

/// <summary>
/// How bright each room of a house is by day and which rooms keep a lamp at night. Ported from the CityGen viewer
/// (<c>roomLighting</c>, <c>roomLit</c>).
/// </summary>
/// <remarks>Determinism: pure functions of the interior and the building ID. Thread-safety: pure.</remarks>
public static class RoomLighting
{
    /// <summary>Sky fill left in a room with no openings at all.</summary>
    public const double MinFill = 0.1;

    /// <summary>How fast openings brighten a room (fill per share of floor area that is opening).</summary>
    public const double Gain = 3.2;

    /// <summary>The most sky fill any room gets.</summary>
    public const double MaxFill = 0.8;

    /// <summary>Share of a window's area that lets light in.</summary>
    public const double WindowOpen = 0.9;

    /// <summary>How much of a lit neighbour's light comes through an open interior door.</summary>
    public const double DoorSpill = 0.35;

    /// <summary>Share of rooms that keep a lamp when someone is home.</summary>
    public const double LitShare = 0.45;

    private const double NeighbourBoost = 3;
    private const uint HashMultiplier = 2654435761;
    private const int HashBuckets = 1000;

    /// <summary>The share of the sky's light that reaches each room, by floor and room: <c>[level][room]</c>.</summary>
    /// <param name="interior">The house.</param>
    /// <returns>Values between <see cref="MinFill"/> and <see cref="MaxFill"/>. A room is lit by its windows and any door to the outside, relative to its floor area, plus some spill through open doors from lit neighbours.</returns>
    public static double[][] SkyFill(Interior interior)
    {
        ArgumentNullException.ThrowIfNull(interior);
        double[] area = interior.Rooms.Select(r => Area(r.Shape)).ToArray();
        var result = new double[interior.Floors.Count][];
        for (int level = 0; level < interior.Floors.Count; level++)
        {
            Floor floor = interior.Floors[level];
            var own = new double[area.Length];
            foreach (Window w in floor.Windows)
            {
                own[w.Room] += w.Width * w.Height * WindowOpen;
            }

            foreach (Door d in floor.Doors.Where(d => d.RoomB == -1))
            {
                own[d.RoomA] += d.Width * d.Height;
            }

            double[] direct = own.Select((v, r) => v / area[r]).ToArray();
            double[] lit = (double[])direct.Clone();
            foreach (Door d in floor.Doors.Where(d => d.RoomB != -1))
            {
                double opening = d.Width * d.Height;
                lit[d.RoomA] += DoorSpill * (opening / area[d.RoomA]) * Math.Min(1, direct[d.RoomB] * NeighbourBoost);
                lit[d.RoomB] += DoorSpill * (opening / area[d.RoomB]) * Math.Min(1, direct[d.RoomA] * NeighbourBoost);
            }

            result[level] = lit.Select(v => Math.Min(MaxFill, MinFill + (Gain * v))).ToArray();
        }

        return result;
    }

    /// <summary>Whether a room keeps a lamp.</summary>
    /// <param name="interior">The house.</param>
    /// <param name="level">The floor.</param>
    /// <param name="room">The room.</param>
    /// <returns><c>true</c> for the room behind the front door and for about <see cref="LitShare"/> of the others, the same ones on every load.</returns>
    public static bool HasLamp(Interior interior, int level, int room)
    {
        ArgumentNullException.ThrowIfNull(interior);
        if (level == 0 && interior.Floors.Count > 0 && interior.Floors[0].Doors.Any(d => d.RoomB == -1 && d.RoomA == room))
        {
            return true;
        }

        uint h = unchecked((uint)((interior.BuildingId * 31) + (level * 7) + room + 1) * HashMultiplier);
        return (h % HashBuckets) / (double)HashBuckets < LitShare;
    }

    private static double Area(ConvexPoly shape)
    {
        double a = 0;
        IReadOnlyList<Vec2> p = shape.Points;
        for (int i = 0; i < p.Count; i++)
        {
            Vec2 q = p[(i + 1) % p.Count];
            a += (p[i].X * q.Y) - (q.X * p[i].Y);
        }

        return Math.Abs(a) / 2;
    }
}
