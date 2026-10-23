using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Realm.Gen.Props;
using Realm.Gen.Settlements.Generation;
using Realm.Gen.Settlements.Geometry;
using Realm.Gen.Settlements.Meshing;
using Realm.Gen.Settlements.Model;
using Xunit;

namespace Realm.Gen.Tests.Settlements;

public class NightLightingTests
{
    private static readonly City TestCity = CityGenerator.Generate(new CityParams { Seed = 7, MaxBuildings = 60 });

    private static IReadOnlyList<(Building Building, Interior Interior)> Houses() =>
        TestCity.CityPatches.SelectMany(p => p.Buildings).Select(b => (b, InteriorGenerator.Generate(b, TestCity.Seed))).ToList();

    [Fact]
    public void SkyFill_IsBetweenTheLimits_AndWindowsBrightenARoom()
    {
        foreach ((Building _, Interior it) in Houses())
        {
            double[][] fill = RoomLighting.SkyFill(it);
            Assert.Equal(it.Floors.Count, fill.Length);
            for (int level = 0; level < fill.Length; level++)
            {
                Assert.Equal(it.Rooms.Count, fill[level].Length);
                Assert.All(fill[level], v => Assert.InRange(v, RoomLighting.MinFill, RoomLighting.MaxFill));
                foreach (Window w in it.Floors[level].Windows)
                {
                    Assert.True(fill[level][w.Room] > RoomLighting.MinFill, "a room with a window gets more than the minimum");
                }
            }
        }
    }

    [Fact]
    public void Lamps_AreTheSameOnEveryCall_AboutTheLitShare_AndTheFrontRoomAlwaysHasOne()
    {
        int rooms = 0;
        int lamps = 0;
        foreach ((Building _, Interior it) in Houses())
        {
            for (int level = 0; level < it.Floors.Count; level++)
            {
                for (int r = 0; r < it.Rooms.Count; r++)
                {
                    bool lamp = RoomLighting.HasLamp(it, level, r);
                    Assert.Equal(lamp, RoomLighting.HasLamp(it, level, r));
                    if (level == 0 && it.Floors[0].Doors.Any(d => d.RoomB == -1 && d.RoomA == r))
                    {
                        continue; // the front room always has one
                    }

                    rooms++;
                    lamps += lamp ? 1 : 0;
                }
            }

            Door front = it.Floors[0].Doors.First(d => d.RoomB == -1);
            Assert.True(RoomLighting.HasLamp(it, 0, front.RoomA));
        }

        Assert.InRange(lamps / (double)rooms, 0.35, 0.55);
    }

    [Fact]
    public void Near_HasGlassInEveryWindow_AndTheRoomSurfacesCoverTheInside()
    {
        foreach ((Building b, Interior it) in Houses().Take(25))
        {
            BuildingMeshes near = BuildingMesher.Near(b, it);
            int windows = BuildingMesher.Openings(b, it).Count(o => !o.IsDoor);

            Assert.Equal(windows * 2 * 2, near.Glass?.Triangles.Count ?? 0); // a double-sided quad is two triangles a side
            Assert.All(near.Glass?.Triangles ?? Array.Empty<PropTriangle>(), t => Assert.Equal(SettlementPalette.Glass, t.Colour));
            Assert.DoesNotContain(near.Visual.Triangles, t => t.Colour == SettlementPalette.Inner);
            Assert.DoesNotContain(near.Visual.Triangles, t => t.Colour == SettlementPalette.Ceiling);

            // One surface group per floor and room, in order, with the sky fill and lamp of that room.
            Assert.Equal(it.Floors.Count * it.Rooms.Count, near.Rooms.Count);
            double[][] fill = RoomLighting.SkyFill(it);
            foreach (RoomSurface room in near.Rooms)
            {
                Assert.Equal(fill[room.Level][room.Room], room.SkyFill);
                Assert.Equal(RoomLighting.HasLamp(it, room.Level, room.Room), room.HasLamp);
                Assert.Equal(room.Level * it.FloorHeight, room.BaseY);
                Assert.NotEmpty(room.Mesh.Triangles);
            }

            Assert.Equal(near.Rooms.OrderBy(r => r.Level).ThenBy(r => r.Room).Select(r => (r.Level, r.Room)), near.Rooms.Select(r => (r.Level, r.Room)));
        }
    }

    [Fact]
    public void Glow_HasOneQuadPerLitWindow_InFrontOfTheFacade()
    {
        foreach ((Building b, Interior it) in Houses().Take(25))
        {
            PropMesh? glow = NightLights.Glow(b, it);
            IReadOnlyList<WallOpening> lit = NightLights.LitWindows(b, it);

            Assert.Equal(lit.Count * 2, glow?.Triangles.Count ?? 0);
            Assert.Equal(lit.Count == 0, glow is null);
            if (glow is null)
            {
                continue;
            }

            Assert.All(lit, o => Assert.False(o.IsDoor));
            Assert.All(glow.Triangles, t => Assert.Equal(NightLights.WindowGlow, t.Colour));
            Assert.True(lit.Count <= it.Floors.Sum(f => f.Windows.Count));
        }
    }

    [Fact]
    public void Aura_StaysWithinRangeOfItsWindows_AndLightsNothingForAHouseWithoutLitWindows()
    {
        var neighbours = Houses().Select(h => new AuraNeighbour(h.Building.Id, h.Building.Footprint, 0, h.Interior.Floors.Count * h.Interior.FloorHeight)).ToList();
        int lit = 0;
        foreach ((Building b, Interior it) in Houses().Take(25))
        {
            IReadOnlyList<AuraVertex> aura = NightLights.Aura(b, it, 0, FlatGround.Instance, neighbours);
            IReadOnlyList<WallOpening> windows = NightLights.LitWindows(b, it);
            Assert.Equal(0, aura.Count % 3);
            if (windows.Count == 0)
            {
                Assert.Empty(aura);
                continue;
            }

            lit++;
            Assert.NotEmpty(aura);
            Vec2 centre = b.Footprint.Centroid;
            double reach = b.Footprint.Points.Max(p => Vec2.Distance(p, centre)) + NightLights.AuraRadiusMeters + 1;
            foreach (AuraVertex v in aura)
            {
                Assert.InRange(Vec2.Distance(new Vec2(v.Position.X, v.Position.Z), centre), 0, reach);
                Assert.InRange(v.Uv.X, -0.01f, 1.01f);
                Assert.InRange(v.Uv.Y, -0.01f, 1.01f);
                Assert.True(v.Colour.X > 0 && v.Colour.X <= NightLights.AuraStrength + 1e-4);
            }
        }

        Assert.True(lit > 0);
    }

    [Fact]
    public void Aura_ThrowsAPoolOnTheStreetAtGroundLevel_AndAHigherWindowLightsLess()
    {
        City big = CityGenerator.Generate(new CityParams { Seed = 3, MaxBuildings = 250 });
        (Building b, Interior it) = big.CityPatches.SelectMany(p => p.Buildings).Select(x => (x, InteriorGenerator.Generate(x, big.Seed)))
            .First(h => NightLights.LitWindows(h.x, h.Item2).Any(o => o.Bottom > h.Item2.FloorHeight) && NightLights.LitWindows(h.x, h.Item2).Any(o => o.Bottom < h.Item2.FloorHeight));
        IReadOnlyList<AuraVertex> aura = NightLights.Aura(b, it, 0, FlatGround.Instance, Array.Empty<AuraNeighbour>());

        Assert.Contains(aura, v => Math.Abs(v.Position.Y - 0.085f) < 1e-4f);
        float strongest = aura.Max(v => v.Colour.X);
        float weakest = aura.Min(v => v.Colour.X);
        Assert.True(weakest < strongest, "the upper-floor window glows less than the ground-floor one");
    }
}
