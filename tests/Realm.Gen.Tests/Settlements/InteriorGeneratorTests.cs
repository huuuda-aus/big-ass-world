using System.Collections.Generic;
using System.Linq;
using Realm.Gen.Settlements.Generation;
using Realm.Gen.Settlements.Geometry;
using Realm.Gen.Settlements.Model;
using Xunit;

namespace Realm.Gen.Tests.Settlements;

public class InteriorGeneratorTests
{
    private static IEnumerable<(City city, Building building)> AllBuildings(params int[] seeds)
    {
        foreach (int seed in seeds)
        {
            City city = CityGenerator.Generate(new CityParams { Seed = seed });
            foreach (Building b in city.CityPatches.SelectMany(p => p.Buildings))
                yield return (city, b);
        }
    }

    [Fact]
    public void SameBuildingGivesSameInterior()
    {
        var (city, b) = AllBuildings(3).First();
        Interior x = InteriorGenerator.Generate(b, city.Seed);
        Interior y = InteriorGenerator.Generate(b, city.Seed);

        Assert.Equal(x.Rooms.Select(r => r.Shape.Centroid), y.Rooms.Select(r => r.Shape.Centroid));
        Assert.Equal(x.Floors.Select(f => f.Doors.Count), y.Floors.Select(f => f.Doors.Count));
    }

    [Fact]
    public void EveryBuildingHasOneFrontDoorAndReachableRooms()
    {
        foreach (var (city, b) in AllBuildings(1, 2, 3, 42))
        {
            Interior i = InteriorGenerator.Generate(b, city.Seed);

            Assert.NotEmpty(i.Rooms);
            Assert.NotEmpty(i.Floors);
            Assert.Single(i.Floors[0].Doors, d => d.RoomB == -1);

            foreach (Floor f in i.Floors)
                Assert.True(Connected(i.Rooms.Count, f.Doors),
                    $"building {b.Id} (seed {city.Seed}) floor {f.Level}: unreachable room");
        }
    }

    [Fact]
    public void RoomsTileTheFootprintInterior()
    {
        foreach (var (city, b) in AllBuildings(5))
        {
            Interior i = InteriorGenerator.Generate(b, city.Seed);
            double roomArea = i.Rooms.Sum(r => r.Shape.Area);

            Assert.True(roomArea <= b.Footprint.Area + 1e-6);
            Assert.True(roomArea > b.Footprint.Area * 0.4, $"building {b.Id}: rooms cover too little");
        }
    }

    [Fact]
    public void StairsFitInsideTheirRoomAndHouseHasMatchingFloors()
    {
        int multiFloor = 0;
        foreach (var (city, b) in AllBuildings(1, 2, 3))
        {
            Interior i = InteriorGenerator.Generate(b, city.Seed);
            if (i.Floors.Count == 1)
            {
                Assert.Null(i.Stairs);
                continue;
            }

            multiFloor++;
            Assert.NotNull(i.Stairs);
            ConvexPoly room = i.Rooms[i.Stairs!.Room].Shape;
            Vec2 side = new Vec2(-(i.Stairs.End - i.Stairs.Start).Y, (i.Stairs.End - i.Stairs.Start).X).Normalized()
                        * (i.Stairs.Width / 2);
            foreach (Vec2 corner in new[] { i.Stairs.Start + side, i.Stairs.Start - side, i.Stairs.End + side, i.Stairs.End - side })
                for (int e = 0; e < room.Count; e++)
                    Assert.True(Vec2.Dot(room.OutwardNormal(e), corner - room.Points[e]) < 0, $"building {b.Id}: stair leaves room");
        }
        Assert.True(multiFloor > 20, "expected a good share of multi-floor houses");
    }

    [Fact]
    public void WindowsSitOnExteriorWalls()
    {
        foreach (var (city, b) in AllBuildings(4))
        {
            Interior i = InteriorGenerator.Generate(b, city.Seed);
            foreach (Window w in i.Floors.SelectMany(f => f.Windows))
            {
                ConvexPoly s = i.Rooms[w.Room].Shape;
                bool onEdge = Enumerable.Range(0, s.Count).Any(e =>
                    s.Tags[e] >= 1000 &&
                    System.Math.Abs(Vec2.Dot(s.OutwardNormal(e), w.Position - s.Points[e])) < 1e-6);
                Assert.True(onEdge, $"building {b.Id}: window floating");
            }
        }
    }

    [Fact]
    public void HousesHaveOneToThreeWindowsInTotal()
    {
        int counted = 0;
        foreach (var (city, b) in AllBuildings(1, 2, 3, 42))
        {
            if (b.Ward == WardType.Cathedral) continue;

            int windows = InteriorGenerator.Generate(b, city.Seed).Floors.Sum(f => f.Windows.Count);
            counted++;
            Assert.InRange(windows, 1, 3);
        }
        Assert.True(counted > 100);
    }

    private static bool Connected(int roomCount, IEnumerable<Door> doors)
    {
        var adj = Enumerable.Range(0, roomCount).ToDictionary(r => r, _ => new List<int>());
        foreach (Door d in doors.Where(d => d.RoomB >= 0))
        {
            adj[d.RoomA].Add(d.RoomB);
            adj[d.RoomB].Add(d.RoomA);
        }
        var seen = new HashSet<int> { 0 };
        var queue = new Queue<int>(new[] { 0 });
        while (queue.Count > 0)
            foreach (int n in adj[queue.Dequeue()])
                if (seen.Add(n)) queue.Enqueue(n);
        return seen.Count == roomCount;
    }
}
