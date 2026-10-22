using System;
using System.Collections.Generic;
using System.Linq;
using Realm.Core.Random;
using Realm.Gen.Settlements.Geometry;
using Realm.Gen.Settlements.Model;

namespace Realm.Gen.Settlements.Generation;

/// <summary>
/// Builds a building's interior on demand. Deterministic in (citySeed, building.Id), so a
/// house can be generated when the player walks up to it and discarded afterwards.
///
/// The footprint is split once into rooms by BSP; every floor reuses that partition so walls
/// stack. Interior cuts are tagged with a cut id shared by both sides, which is how room
/// adjacency and door positions are found. Exterior edges are tagged ExteriorTagBase + the
/// footprint edge index.
/// </summary>
public static class InteriorGenerator
{
    /// <summary>Thickness of the outer walls of a <see cref="BuildingStyle.Rich"/> building in metres (stone and timber).</summary>
    public const double ExteriorWallThickness = 0.3;

    /// <summary>Thickness of the outer walls of a <see cref="BuildingStyle.Slum"/> dwelling in metres (mud and wattle).</summary>
    public const double SlumWallThickness = 0.15;

    /// <summary>Thickness of every floor and ceiling slab in metres.</summary>
    public const double SlabThickness = 0.3;
    /// <summary>Thickness of walls between rooms in metres.</summary>
    public const double InteriorWallThickness = 0.15;

    internal const int ExteriorTagBase = 1000;
    private const double MinRoomArea = 7;
    private const double MinRoomWidth = 2.0;
    private const double MinDoorOverlap = 1.3;
    private const double StairLength = 3.2;
    private const double StairWidth = 1.0;

    /// <summary>A house gets this many windows in total, across all its floors.</summary>
    private const int MinWindows = 1;
    private const int MaxWindows = 3;
    private const int SlumMaxWindows = 1;

    /// <summary>Builds the interior of one building.</summary>
    /// <param name="building">The building to furnish with rooms, doors, windows and stairs.</param>
    /// <param name="citySeed">Seed of the city the building belongs to.</param>
    /// <returns>The interior.</returns>
    /// <remarks>Determinism: depends only on the building and the city seed, so it can be regenerated on demand and thrown away.</remarks>
    public static Interior Generate(Building building, int citySeed)
    {
        int seed = unchecked(citySeed * 73856093 ^ building.Id * 19349663);
        var rng = Rng.ForKey((ulong)(uint)seed, "interior");

        ConvexPoly footprint = building.Footprint;
        var tagged = new ConvexPoly(footprint.Points,
            Enumerable.Range(0, footprint.Count).Select(i => ExteriorTagBase + i));
        double wallThickness = WallThicknessOf(building);
        ConvexPoly shell = tagged.Inset(Enumerable.Repeat(wallThickness, footprint.Count).ToArray())
                           ?? tagged;

        bool cathedral = building.Ward == WardType.Cathedral;
        var shapes = new List<ConvexPoly>();
        if (cathedral) shapes.Add(shell);
        else Partition(shell, MaxRoomArea(building.Ward), 0, rng, shapes, new int[1]);

        var rooms = shapes.Select((s, i) => new Room(i, s)).ToList();
        int entrance = FindFrontDoor(rooms, building.DoorEdge, out Vec2 doorPos, out Vec2 doorDir);

        int floorCount = cathedral ? 1 : ChooseFloorCount(building, shell.Area, rng);
        Stair? stair = null;
        if (floorCount > 1)
        {
            stair = PlaceStairs(rooms, entrance);
            if (stair == null) floorCount = 1;
        }

        var links = FindLinks(rooms);
        var candidates = Enumerable.Range(0, floorCount)
            .Select(level => MakeWindows(rooms, level == 0 ? (Vec2?)doorPos : null))
            .ToList();
        // a cathedral keeps all of its windows; houses are limited to a few
        var windowsByFloor = cathedral ? candidates : LimitWindows(candidates, rng, building.Style == BuildingStyle.Slum ? SlumMaxWindows : MaxWindows);

        var floors = new List<Floor>();
        for (int level = 0; level < floorCount; level++)
        {
            var doors = ChooseDoors(rooms, links, rng);
            if (level == 0)
                doors.Insert(0, new Door(doorPos, doorDir, entrance, -1, 1.1, 2.2));

            var windows = windowsByFloor[level];
            floors.Add(new Floor(level, doors, windows,
                AssignRoomTypes(rooms, level, floorCount, entrance, building.Ward, cathedral, building.Service?.MainRoom, rng)));
        }

        return new Interior(building.Id, cathedral ? 8.0 : 3.0, wallThickness, InteriorWallThickness,
            rooms, floors, stair);
    }

    /// <summary>Thickness of a building's outer walls: slum dwellings are thin-walled, everything else thick.</summary>
    /// <param name="building">The building.</param>
    /// <returns>Thickness in metres.</returns>
    public static double WallThicknessOf(Building building) =>
        building.Style == BuildingStyle.Slum ? SlumWallThickness : ExteriorWallThickness;

    // ---- partition -----------------------------------------------------------------

    private static double MaxRoomArea(WardType ward)
    {
        switch (ward)
        {
            case WardType.Slum: return 45; // a slum dwelling is one room
            case WardType.Outskirts: return 45;
            case WardType.Merchant: return 30;
            case WardType.Craftsmen: return 26;
            default: return 22;
        }
    }

    private static int ChooseFloorCount(Building b, double area, Rng rng)
    {
        if (b.Style == BuildingStyle.Slum) return 1;
        switch (b.Ward)
        {
            case WardType.Merchant: return area < 60 ? 2 : 2 + (rng.NextDouble() < 0.3 ? 1 : 0);
            case WardType.Craftsmen: return area < 50 ? 1 : 2;
            default: return area < 45 ? 1 : 1 + (rng.NextDouble() < 0.4 ? 1 : 0);
        }
    }

    private static void Partition(ConvexPoly poly, double maxArea, int depth, Rng rng,
        List<ConvexPoly> output, int[] nextCutId)
    {
        if (poly.Area <= maxArea * (0.8 + 0.4 * rng.NextDouble()) || depth > 6)
        {
            output.Add(poly);
            return;
        }

        // Cut perpendicular to the longest edge so rooms stay roughly square.
        int e = poly.LongestEdge();
        double t = 0.4 + 0.2 * rng.NextDouble();
        Vec2 at = Vec2.Lerp(poly.Points[e], poly.EdgeEnd(e), t);
        Vec2 normal = poly.Edge(e).Normalized();
        int id = nextCutId[0]++;

        ConvexPoly? a = poly.Clip(at, normal, id);
        ConvexPoly? b = poly.Clip(at, -normal, id);
        if (!Acceptable(a) || !Acceptable(b))
        {
            output.Add(poly);
            return;
        }
        Partition(a!, maxArea, depth + 1, rng, output, nextCutId);
        Partition(b!, maxArea, depth + 1, rng, output, nextCutId);
    }

    private static bool Acceptable(ConvexPoly? room) =>
        room != null && room.Area >= MinRoomArea && room.Area / room.EdgeLength(room.LongestEdge()) >= MinRoomWidth;

    // ---- front door ----------------------------------------------------------------

    private static int FindFrontDoor(List<Room> rooms, int doorEdge, out Vec2 position, out Vec2 direction)
    {
        int bestRoom = -1, bestEdge = -1;
        double bestLen = -1;
        for (int pass = 0; pass < 2 && bestRoom < 0; pass++)
        {
            foreach (Room r in rooms)
            {
                for (int i = 0; i < r.Shape.Count; i++)
                {
                    int tag = r.Shape.Tags[i];
                    bool match = pass == 0 ? tag == ExteriorTagBase + doorEdge : tag >= ExteriorTagBase;
                    if (!match) continue;
                    double len = r.Shape.EdgeLength(i);
                    if (len > bestLen) { bestLen = len; bestRoom = r.Index; bestEdge = i; }
                }
            }
        }

        ConvexPoly s = rooms[bestRoom].Shape;
        position = Vec2.Lerp(s.Points[bestEdge], s.EdgeEnd(bestEdge), 0.5);
        direction = s.Edge(bestEdge).Normalized();
        return bestRoom;
    }

    // ---- stairs --------------------------------------------------------------------

    private static Stair? PlaceStairs(List<Room> rooms, int entrance)
    {
        // Keep clear of walls so doors in them stay usable; relax if the house is small.
        foreach (double margin in new[] { 0.9, 0.5 })
        {
            foreach (Room r in rooms.OrderBy(x => x.Index == entrance).ThenByDescending(x => x.Shape.Area))
            {
                Stair? s = FitStair(r, margin);
                if (s != null) return s;
            }
        }
        return null;
    }

    private static Stair? FitStair(Room room, double margin)
    {
        ConvexPoly shape = room.Shape;
        Vec2 along = shape.Edge(shape.LongestEdge()).Normalized();
        Vec2[] dirs = { along, new Vec2(-along.Y, along.X) };

        Vec2 c = shape.Centroid;
        double minX = shape.Points.Min(p => p.X), maxX = shape.Points.Max(p => p.X);
        double minY = shape.Points.Min(p => p.Y), maxY = shape.Points.Max(p => p.Y);

        Stair? best = null;
        double bestDist = double.MaxValue;
        foreach (Vec2 dir in dirs)
        {
            Vec2 side = new Vec2(-dir.Y, dir.X);
            for (double x = minX; x <= maxX; x += 0.5)
            for (double y = minY; y <= maxY; y += 0.5)
            {
                Vec2 start = new Vec2(x, y);
                Vec2 end = start + dir * StairLength;
                double d = Vec2.Distance((start + end) * 0.5, c);
                if (d >= bestDist) continue;
                if (!Inside(shape, start + side * (StairWidth / 2), margin) ||
                    !Inside(shape, start - side * (StairWidth / 2), margin) ||
                    !Inside(shape, end + side * (StairWidth / 2), margin) ||
                    !Inside(shape, end - side * (StairWidth / 2), margin)) continue;
                bestDist = d;
                best = new Stair(room.Index, start, end, StairWidth);
            }
        }
        return best;
    }

    private static bool Inside(ConvexPoly poly, Vec2 p, double margin)
    {
        for (int i = 0; i < poly.Count; i++)
        {
            double d = -Vec2.Dot(poly.OutwardNormal(i), p - poly.Points[i]);
            if (d < margin) return false;
        }
        return true;
    }

    // ---- doors ---------------------------------------------------------------------

    private sealed class Link
    {
        public int A, B;
        public Vec2 Position, Direction;
    }

    /// <summary>Every shared wall segment long enough to hold a door.</summary>
    private static List<Link> FindLinks(List<Room> rooms)
    {
        var links = new List<Link>();
        for (int a = 0; a < rooms.Count; a++)
        for (int b = a + 1; b < rooms.Count; b++)
        {
            ConvexPoly pa = rooms[a].Shape, pb = rooms[b].Shape;
            for (int i = 0; i < pa.Count; i++)
            {
                int tag = pa.Tags[i];
                if (tag >= ExteriorTagBase) continue;
                for (int j = 0; j < pb.Count; j++)
                {
                    if (pb.Tags[j] != tag) continue;

                    Vec2 p = pa.Points[i], q = pa.EdgeEnd(i);
                    Vec2 d = (q - p).Normalized();
                    double b0 = Vec2.Dot(pb.Points[j] - p, d), b1 = Vec2.Dot(pb.EdgeEnd(j) - p, d);
                    double lo = Math.Max(0, Math.Min(b0, b1));
                    double hi = Math.Min((q - p).Length, Math.Max(b0, b1));
                    if (hi - lo < MinDoorOverlap) continue;

                    links.Add(new Link { A = a, B = b, Position = p + d * ((lo + hi) / 2), Direction = d });
                }
            }
        }
        return links;
    }

    /// <summary>Rng spanning tree over the room graph (all rooms reachable) plus a few loops.</summary>
    private static List<Door> ChooseDoors(List<Room> rooms, List<Link> links, Rng rng)
    {
        var shuffled = links.ToList();
        for (int i = shuffled.Count - 1; i > 0; i--)
        {
            int j = rng.NextInt(i + 1);
            (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]);
        }

        int[] parent = Enumerable.Range(0, rooms.Count).ToArray();
        int Find(int x) { while (parent[x] != x) x = parent[x] = parent[parent[x]]; return x; }

        var doors = new List<Door>();
        foreach (Link l in shuffled)
        {
            int ra = Find(l.A), rb = Find(l.B);
            bool joins = ra != rb;
            if (!joins && rng.NextDouble() >= 0.15) continue;
            if (joins) parent[ra] = rb;
            doors.Add(new Door(l.Position, l.Direction, l.A, l.B));
        }
        return doors;
    }

    // ---- windows -------------------------------------------------------------------

    /// <summary>
    /// Keeps a random MinWindows..MaxWindows of the candidate windows in total, taking them from
    /// the floors in turn so upper floors get some too, instead of one floor getting them all.
    /// </summary>
    private static List<List<Window>> LimitWindows(List<List<Window>> perFloor, Rng rng, int maxWindows)
    {
        var kept = perFloor.Select(_ => new List<Window>()).ToList();
        var pool = perFloor.Select(f => f.ToList()).ToList();
        int target = rng.NextInt(MinWindows, maxWindows + 1);
        int floor = rng.NextInt(pool.Count);

        // each full cycle over the floors adds at least one window while any remain
        int attempts = target * pool.Count + pool.Count;
        for (int i = 0; i < attempts && kept.Sum(k => k.Count) < target; i++)
        {
            List<Window> candidates = pool[floor];
            if (candidates.Count > 0)
            {
                int pick = rng.NextInt(candidates.Count);
                kept[floor].Add(candidates[pick]);
                candidates.RemoveAt(pick);
            }
            floor = (floor + 1) % pool.Count;
        }
        return kept;
    }

    private static List<Window> MakeWindows(List<Room> rooms, Vec2? frontDoor)
    {
        var windows = new List<Window>();
        foreach (Room r in rooms)
        {
            ConvexPoly s = r.Shape;
            for (int i = 0; i < s.Count; i++)
            {
                if (s.Tags[i] < ExteriorTagBase) continue;
                double len = s.EdgeLength(i);
                int n = (int)Math.Floor((len - 0.6) / 2.5);
                Vec2 dir = s.Edge(i).Normalized();
                for (int k = 0; k < n; k++)
                {
                    Vec2 pos = Vec2.Lerp(s.Points[i], s.EdgeEnd(i), (k + 0.5) / n);
                    if (frontDoor.HasValue && Vec2.Distance(pos, frontDoor.Value) < 1.5) continue;
                    windows.Add(new Window(pos, dir, r.Index));
                }
            }
        }
        return windows.Count > 0 ? windows : FallbackWindow(rooms, frontDoor);
    }

    /// <summary>
    /// For small houses whose walls are all too short for the usual spacing: one window on the
    /// longest exterior wall that has room for it, clear of the front door.
    /// </summary>
    private static List<Window> FallbackWindow(List<Room> rooms, Vec2? frontDoor)
    {
        const double MinWall = 1.6, DoorClearance = 1.15;
        var candidates = new List<(double len, Window window)>();
        foreach (Room r in rooms)
        {
            ConvexPoly s = r.Shape;
            for (int i = 0; i < s.Count; i++)
            {
                double len = s.EdgeLength(i);
                if (s.Tags[i] < ExteriorTagBase || len < MinWall) continue;

                foreach (double t in new[] { 0.5, 0.25, 0.75 })
                {
                    Vec2 pos = Vec2.Lerp(s.Points[i], s.EdgeEnd(i), t);
                    if (frontDoor.HasValue && Vec2.Distance(pos, frontDoor.Value) < DoorClearance) continue;
                    candidates.Add((len, new Window(pos, s.Edge(i).Normalized(), r.Index)));
                    break;
                }
            }
        }
        var best = candidates.OrderByDescending(c => c.len).Take(1).Select(c => c.window).ToList();
        return best;
    }

    // ---- room uses -----------------------------------------------------------------

    private static List<RoomType> AssignRoomTypes(List<Room> rooms, int level, int floorCount,
        int entrance, WardType ward, bool cathedral, RoomType? serviceRoom, Rng rng)
    {
        var types = new RoomType[rooms.Count];
        if (cathedral)
        {
            types[0] = RoomType.Nave;
            return types.ToList();
        }

        var bySize = rooms.OrderByDescending(r => r.Shape.Area).Select(r => r.Index).ToList();
        if (level == 0)
        {
            bool single = rooms.Count == 1;
            types[entrance] = single ? RoomType.LivingRoom
                : ward == WardType.Merchant ? RoomType.Shop
                : RoomType.Hall;

            bool first = true;
            foreach (int i in bySize.Where(i => i != entrance))
            {
                if (first)
                    types[i] = ward == WardType.Craftsmen ? RoomType.Workshop : RoomType.Kitchen;
                else
                    types[i] = rng.NextDouble() < 0.5 ? RoomType.LivingRoom : RoomType.Storage;
                first = false;
            }

            // A service building's main room is what the service is for: a shop or hall at the door, anything else in the biggest room.
            if (serviceRoom is RoomType main)
            {
                if (main is RoomType.Shop or RoomType.Hall)
                {
                    types[entrance] = main;
                }
                else
                {
                    types[bySize.FirstOrDefault(i => i != entrance, entrance)] = main;
                }
            }
        }
        else
        {
            foreach (int i in bySize)
                types[i] = rng.NextDouble() < 0.2 ? RoomType.Storage : RoomType.Bedroom;
            types[bySize[0]] = RoomType.Bedroom;
        }
        return types.ToList();
    }
}
