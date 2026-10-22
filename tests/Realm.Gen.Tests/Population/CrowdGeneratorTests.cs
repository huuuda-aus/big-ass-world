using System;
using System.Collections.Generic;
using System.Linq;
using Realm.Gen.Population;
using Realm.Gen.Settlements.Generation;
using Realm.Gen.Settlements.Geometry;
using Realm.Gen.Settlements.Model;
using Realm.Gen.Settlements.Navigation;
using Realm.Gen.Settlements.Placement;
using Realm.Sim.Navigation;
using Realm.Sim.Npcs;
using Xunit;

namespace Realm.Gen.Tests.Population;

public class CrowdGeneratorTests
{
    private static readonly City City = CityGenerator.Generate(new CityParams { Seed = 3, MaxBuildings = 250, Parks = false, Cathedral = true });
    private static readonly NavGraphResult Nav = NavGraphBuilder.Build(City);
    private static readonly OpenGround Ground = new(City, null);

    private static IReadOnlyList<NpcPlan> Make(int count = 220, ulong seed = 1) =>
        CrowdGenerator.Generate(City, Nav, Ground, "settlement.test", count, seed);

    [Fact]
    public void ACityGetsTwoHundredAndMoreCharacters_InThreeRoles()
    {
        IReadOnlyList<NpcPlan> plans = Make();

        Assert.Equal(220, plans.Count);
        Assert.Equal(Enumerable.Range(0, 220), plans.Select(p => p.Id));
        int guards = plans.Count(p => p.Role == NpcRole.Guard);
        int loiterers = plans.Count(p => p.Role == NpcRole.Loiterer);
        int walkers = plans.Count(p => p.Role == NpcRole.Walker);
        Assert.InRange(guards, 1, 22);
        Assert.InRange(loiterers, 90, 130);
        Assert.True(walkers > 60, $"only {walkers} walkers");
        Assert.Equal(220, guards + loiterers + walkers);
    }

    [Fact]
    public void Loiterers_CrowdTheSquare_ThickestNextToTheFountain_AndStayOnOpenGround()
    {
        IReadOnlyList<NpcPlan> plans = Make();
        double solid = Ground.LandmarkSolidRadius;
        var distances = plans.Where(p => p.Role == NpcRole.Loiterer).SelectMany(p => p.Spots).Select(s => Math.Sqrt((s.X * s.X) + (s.Z * s.Z))).ToList();

        Assert.NotEmpty(distances);
        Assert.All(plans.SelectMany(p => p.Spots), s => Assert.False(Ground.Blocked(new Vec2(s.X, s.Z)), $"a spot at ({s.X:F1}, {s.Z:F1}) is not open ground"));
        Assert.All(distances, d => Assert.True(d >= solid, "a spot is inside the fountain"));
        double near = distances.Count(d => d <= solid + 8) / (double)distances.Count;
        Assert.True(near > 0.6, $"only {near:P0} of the spots are within 8 m of the fountain's edge");
        Assert.True(distances.Count(d => d <= solid + 3) > distances.Count(d => d > solid + 12), "the crowd does not thin out with distance");
    }

    [Fact]
    public void Walkers_LiveInRealHouses_AndVisitOthers()
    {
        IReadOnlyList<NpcPlan> plans = Make();
        var doorNodes = Nav.Graph.Nodes.Select((n, i) => (n, i)).Where(t => t.n.Kind == NavNodeKind.Door).Select(t => t.i).ToHashSet();

        foreach (NpcPlan p in plans.Where(p => p.Role == NpcRole.Walker))
        {
            Assert.Contains(p.HomeNode, doorNodes);
            Assert.NotEmpty(p.Destinations);
            Assert.All(p.Destinations, d =>
            {
                Assert.Contains(d, doorNodes);
                Assert.NotEqual(p.HomeNode, d);
                Assert.True(Nav.Graph.AreConnected(p.HomeNode, d));
            });
            Assert.Equal(p.Destinations.Distinct().Count(), p.Destinations.Length);
        }

        Assert.True(plans.Where(p => p.Role == NpcRole.Walker).Select(p => p.HomeNode).Distinct().Count() > 60, "walkers share too few homes");
    }

    [Fact]
    public void Guards_StandNearTheGates()
    {
        IReadOnlyList<NpcPlan> plans = Make();

        foreach (NpcPlan g in plans.Where(p => p.Role == NpcRole.Guard))
        {
            var post = new Vec2(g.Spots[0].X, g.Spots[0].Z);
            Assert.Contains(City.Gates, gate => Vec2.Distance(gate, post) < 8);
        }
    }

    [Fact]
    public void SameInputs_SamePlans_DifferentSeed_Different()
    {
        Assert.Equal(Make().Select(p => (p.HomeNode, p.Seed, p.SleepMinute)), Make().Select(p => (p.HomeNode, p.Seed, p.SleepMinute)));
        Assert.NotEqual(Make().Select(p => p.Seed), Make(seed: 2).Select(p => p.Seed));
        Assert.Empty(Make(0));
    }

    [Fact]
    public void ADayInTheCity_Crowd_StaysOnOpenGround_FillsTheSquareByDay_AndEmptiesItAtNight()
    {
        IReadOnlyList<NpcPlan> plans = Make();
        var crowd = new Crowd(Nav.Graph, plans, 9 * 60, 1.2, (ax, az, bx, bz) => Ground.Clear(new Vec2(ax, az), new Vec2(bx, bz)));
        int outsideNoon = 0;
        int outsideNight = 0;
        int nearFountain = 0;
        double start = 9 * 60;

        for (double t = start; t <= start + (2 * 1440); t += 4)
        {
            crowd.Update(t);
            double minute = t % 1440;
            for (int i = 0; i < crowd.Count; i++)
            {
                if (!crowd.TryGetSnapshot(i, t, out NpcSnapshot s))
                {
                    continue;
                }

                if (t > start + 1440)
                {
                    if (minute is >= 700 and <= 740)
                    {
                        outsideNoon++;
                        nearFountain += Math.Sqrt((s.X * s.X) + (s.Z * s.Z)) < Ground.LandmarkSolidRadius + 10 ? 1 : 0;
                    }

                    if (minute is >= 150 and <= 270)
                    {
                        outsideNight++;
                    }
                }

                // The only places nobody may be: inside the fountain or a stall or a house (walking along graph edges and across the plaza).
                Assert.False(new Vec2(s.X, s.Z).Length < Ground.LandmarkSolidRadius - 0.5, $"character {i} is inside the fountain at minute {t}");
            }
        }

        int samples = 11; // samples of 4 minutes in the 40 minute noon window
        Assert.True(outsideNoon / (double)samples > 100, $"only {outsideNoon / (double)samples:F0} characters outside at noon");
        Assert.True(nearFountain / (double)Math.Max(1, outsideNoon) > 0.3, $"the crowd does not gather round the fountain at noon: {nearFountain} of {outsideNoon}");
        Assert.True(outsideNight / 31.0 < 40, $"{outsideNight / 31.0:F0} characters outside at night, expected only guards and a few stragglers");
    }
}
