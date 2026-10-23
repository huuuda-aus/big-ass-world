using System;
using System.Collections.Generic;
using System.Linq;
using Realm.Sim.Navigation;
using Realm.Sim.Npcs;
using Realm.Sim.Weather;
using Xunit;

namespace Realm.Sim.Tests;

public class ScheduleCrowdTests
{
    private const double Speed = 1.2;

    // The little town of CrowdTests, plus an outside node (7) joined to the square: the way to the fields.
    private static NavGraph Town()
    {
        var nodes = new List<NavNode>
        {
            new(0, 0, NavNodeKind.Street), new(10, 0, NavNodeKind.Street), new(10, 10, NavNodeKind.Street), new(0, 10, NavNodeKind.Street),
            new(0, -5, NavNodeKind.Door, 100), new(10, -5, NavNodeKind.Door, 101), new(20, 10, NavNodeKind.Door, 102),
            new(30, 20, NavNodeKind.Outside),
        };
        var edges = new List<NavEdge>
        {
            new(0, 1, 10), new(1, 2, 10), new(2, 3, 10), new(3, 0, 10),
            new(4, 0, 5), new(5, 1, 5), new(6, 2, 10), new(7, 2, 22.4),
        };
        return new NavGraph(nodes, edges);
    }

    private static readonly (double X, double Z)[] Spots = { (4, 4), (6, 5), (5, 8), (2, 6) };
    private static readonly ScheduleBlock[] SquareDay = { new(570, 1170, ScheduleActivity.About) };

    private static NpcPlan Plan(int id, NpcRole role, ScheduleBlock[] day, Func<NpcPlan, NpcPlan>? tweak = null)
    {
        var plan = new NpcPlan(id, role, 4, new[] { 5, 6 }, Spots, 1320, 360, (ulong)id + 1) { Schedule = day, InnNode = 6 };
        return tweak is null ? plan : tweak(plan);
    }

    private static Crowd Run(NpcPlan[] plans, double from, double to, Func<double, CrowdContext>? context = null, double step = 1)
    {
        var crowd = new Crowd(Town(), plans, from, Speed);
        for (double t = from; t <= to; t += step)
        {
            crowd.Update(t, context?.Invoke(t) ?? default);
        }

        return crowd;
    }

    [Fact]
    public void CountInside_CountsThePeopleBehindEachDoor()
    {
        // At 03:00 everyone is at home: behind door 4 or 5 (their home nodes are 5 and 6 on the plan's list, here node indices).
        NpcPlan[] plans = Enumerable.Range(0, 6).Select(i => Plan(i, NpcRole.Walker, SquareDay)).ToArray();
        Crowd crowd = Run(plans, 180, 183);
        var inside = new int[Town().Nodes.Count];
        crowd.CountInside(inside);

        Assert.Equal(crowd.Count - crowd.CountOutside(), inside.Sum());
        Assert.All(inside.Where((_, node) => Town().Nodes[node].Kind != NavNodeKind.Door), n => Assert.Equal(0, n));
        Assert.True(inside.Sum() > 0, "somebody is indoors at night");
    }

    [Fact]
    public void ScheduledAt_FindsTheBlock_TheGap_AndWrapsPastMidnight()
    {
        NpcPlan plan = Plan(0, NpcRole.Walker, new ScheduleBlock[] { new(1080, 360, ScheduleActivity.Patrol), new(480, 600, ScheduleActivity.Work) });

        Assert.Equal(ScheduleActivity.Patrol, plan.ScheduledAt(180, out double untilDawn));
        Assert.Equal(180, untilDawn, 6);
        Assert.Equal(ScheduleActivity.Home, plan.ScheduledAt(400, out double untilWork)); // a gap: at home until the next block
        Assert.Equal(80, untilWork, 6);
        Assert.Equal(ScheduleActivity.Work, plan.ScheduledAt(500, out double untilNoon));
        Assert.Equal(100, untilNoon, 6);
        // A day an hour late: the patrol that starts at 18:00 starts at 19:00.
        Assert.Equal(ScheduleActivity.Home, (plan with { ScheduleShiftMinutes = 60 }).ScheduledAt(1110, out _));
        Assert.Equal(ScheduleActivity.Patrol, (plan with { ScheduleShiftMinutes = 60 }).ScheduledAt(1150, out _));
    }

    [Fact]
    public void ABaker_WorksInsideTheBakery_ByDay_AndSleepsAtHomeAtNight()
    {
        var day = new ScheduleBlock[] { new(330, 1020, ScheduleActivity.Work), new(1110, 1260, ScheduleActivity.Inn) };
        NpcPlan baker = Plan(0, NpcRole.Walker, day, p => p with { WorkNode = 5 });
        var crowd = new Crowd(Town(), new[] { baker }, 300, Speed);
        int working = 0, inside = 0, samples = 0, asleepAtHome = 0, nightSamples = 0;

        for (double t = 300; t < 300 + (2 * 1440); t += 1)
        {
            crowd.Update(t);
            double minute = t % 1440;
            NpcStatus s = crowd.Describe(0);
            if (t > 1440 && minute is >= 540 and <= 960)
            {
                samples++;
                working += s.Purpose == NpcPurpose.Work ? 1 : 0;
                inside += s.State == NpcState.Inside && s.TargetNode == 5 ? 1 : 0;
            }

            if (t > 1440 && minute is >= 60 and <= 240)
            {
                nightSamples++;
                asleepAtHome += s.State == NpcState.Inside && s.TargetNode == 4 ? 1 : 0;
            }
        }

        Assert.True(working > samples * 0.95, $"working {working} of {samples}");
        Assert.True(inside > samples * 0.6, $"inside the bakery {inside} of {samples}");
        Assert.True(asleepAtHome > nightSamples * 0.95, $"asleep at home {asleepAtHome} of {nightSamples}");
    }

    [Fact]
    public void ATrader_StandsAtTheStallByDay()
    {
        var day = new ScheduleBlock[] { new(480, 1080, ScheduleActivity.Work) };
        NpcPlan trader = Plan(0, NpcRole.Walker, day, p => p with { WorkSpot = (5.0, 5.0) });
        var crowd = new Crowd(Town(), new[] { trader }, 420, Speed);
        int at = 0, samples = 0;

        for (double t = 420; t < 420 + 1440; t += 1)
        {
            crowd.Update(t);
            if (t % 1440 is >= 600 and <= 1000)
            {
                samples++;
                at += crowd.TryGetSnapshot(0, t, out NpcSnapshot s) && !s.Walking && Math.Abs(s.X - 5) < 0.3 && Math.Abs(s.Z - 5) < 0.3 ? 1 : 0;
            }
        }

        Assert.True(at > samples * 0.95, $"at the stall {at} of {samples}");
    }

    [Fact]
    public void AFarmer_IsOutInTheFieldsByDay_AndComesHome()
    {
        var day = new ScheduleBlock[] { new(360, 1050, ScheduleActivity.Fields), new(1080, 1170, ScheduleActivity.About) };
        NpcPlan farmer = Plan(0, NpcRole.Walker, day, p => p with { FieldsNode = 7 });
        var crowd = new Crowd(Town(), new[] { farmer }, 330, Speed);
        int away = 0, samples = 0;
        bool sawHome = false;

        for (double t = 330; t < 330 + (2 * 1440); t += 1)
        {
            crowd.Update(t);
            double minute = t % 1440;
            NpcStatus s = crowd.Describe(0);
            if (t > 1440 && minute is >= 480 and <= 1000)
            {
                samples++;
                away += s.State == NpcState.Inside && s.TargetNode == 7 && !crowd.TryGetSnapshot(0, t, out _) ? 1 : 0;
            }

            sawHome |= t > 1440 && minute is >= 1260 and <= 1400 && s.State == NpcState.Inside && s.TargetNode == 4;
        }

        Assert.True(away > samples * 0.95, $"away in the fields {away} of {samples}");
        Assert.True(sawHome, "the farmer never went home in the evening");
    }

    [Fact]
    public void AWatchman_PatrolsAtNight_StoppingAtEachPost()
    {
        var night = new ScheduleBlock[] { new(1080, 360, ScheduleActivity.Patrol) };
        NpcPlan watch = Plan(0, NpcRole.Guard, night, p => p with { PatrolNodes = new[] { 0, 2 }, ShelterFromRain = false });
        var crowd = new Crowd(Town(), new[] { watch }, 1020, Speed);
        bool nearFirst = false, nearSecond = false, walking = false;
        int outside = 0, samples = 0;

        for (double t = 1020; t < 1020 + 720; t += 1)
        {
            crowd.Update(t);
            if (t % 1440 is >= 1200 or <= 300)
            {
                samples++;
                if (crowd.TryGetSnapshot(0, t, out NpcSnapshot s))
                {
                    outside++;
                    nearFirst |= Math.Abs(s.X - 0) < 1.5 && Math.Abs(s.Z - 0) < 1.5;
                    nearSecond |= Math.Abs(s.X - 10) < 1.5 && Math.Abs(s.Z - 10) < 1.5;
                    walking |= s.Walking;
                }
            }
        }

        Assert.True(outside > samples * 0.9, $"outside {outside} of {samples}");
        Assert.True(nearFirst && nearSecond && walking, "the patrol does not visit both posts");
    }

    [Fact]
    public void WhenItRains_TheIdlersTakeShelter_AndComeBackWhenItStops()
    {
        NpcPlan[] idlers = Enumerable.Range(0, 40).Select(i => Plan(i, NpcRole.Loiterer, SquareDay)).ToArray();
        var crowd = new Crowd(Town(), idlers, 700, Speed);
        for (double t = 700; t <= 800; t += 1)
        {
            crowd.Update(t);
        }

        int dry = crowd.CountOutside();

        for (double t = 801; t <= 860; t += 1)
        {
            crowd.Update(t, new CrowdContext(0.8, false, 0, 0));
        }

        int wet = crowd.CountOutside();
        for (double t = 861; t <= 1060; t += 1)
        {
            crowd.Update(t, default);
        }

        int after = crowd.CountOutside();
        Assert.True(dry >= 14, $"only {dry} outside before the rain");
        Assert.True(wet <= 6, $"{wet} still outside in the rain");
        Assert.True(after >= 14, $"only {after} came back after the rain");
    }

    [Fact]
    public void TheWatch_StaysOutInTheRain()
    {
        var day = new ScheduleBlock[] { new(0, 1439, ScheduleActivity.Work) };
        NpcPlan guard = Plan(0, NpcRole.Guard, day, p => p with { WorkSpot = (3.0, 3.0), ShelterFromRain = false });
        var crowd = new Crowd(Town(), new[] { guard }, 700, Speed);

        for (double t = 700; t <= 900; t += 1)
        {
            crowd.Update(t, new CrowdContext(1, false, 0, 0));
        }

        Assert.True(crowd.TryGetSnapshot(0, 900, out NpcSnapshot s));
        Assert.InRange(s.X, 2.7, 3.3);
    }

    [Fact]
    public void Characters_LookAtThePlayer_AndAGuardChallenges()
    {
        NpcPlan guard = new(0, NpcRole.Guard, -1, Array.Empty<int>(), new[] { (3.0, 3.0) }, 1320, 360, 3) { Challenges = true };
        var crowd = new Crowd(Town(), new[] { guard }, 600, Speed);

        crowd.Update(600, new CrowdContext(0, true, 3, 7)); // 4 m south of the guard
        Assert.True(crowd.TryGetSnapshot(0, 600, out NpcSnapshot near));
        crowd.Update(601, new CrowdContext(0, true, 3, 13)); // 10 m away
        Assert.True(crowd.TryGetSnapshot(0, 601, out NpcSnapshot far));
        crowd.Update(602, new CrowdContext(0, true, 3, 5.5)); // 2.5 m away: noticed
        Assert.True(crowd.TryGetSnapshot(0, 602, out NpcSnapshot close));

        Assert.True(near.Alert && near.Noticing);
        Assert.False(far.Alert || far.Noticing);
        Assert.True(close.Alert);
        // Facing the player, who stands to the south: the yaw that turns minus Z to face +Z is 180 degrees.
        Assert.Equal(Math.PI, Math.Abs(near.Heading), 6);
    }

    [Fact]
    public void AWalker_KeepsToItsLane_AndFacesWhereItWalks()
    {
        var nodes = new List<NavNode> { new(0, 0, NavNodeKind.Door, 1), new(40, 0, NavNodeKind.Door, 2) };
        var graph = new NavGraph(nodes, new[] { new NavEdge(0, 1, 40) });
        NpcPlan walker = new(0, NpcRole.Walker, 0, new[] { 1 }, Array.Empty<(double, double)>(), 1320, 360, 9) { LaneOffsetMeters = 1.0 };
        var crowd = new Crowd(graph, new[] { walker }, 600, Speed);
        double maxZ = double.MinValue;
        bool facedEast = false;

        for (double t = 600; t < 600 + 400; t += 0.25)
        {
            crowd.Update(t);
            if (crowd.TryGetSnapshot(0, t, out NpcSnapshot s) && s.Walking && s.X > 10 && s.X < 30)
            {
                maxZ = Math.Max(maxZ, s.Z);
                facedEast |= Math.Abs(s.Heading - (-Math.PI / 2)) < 0.05;
            }
        }

        // Walking east (+X), the right-hand side is south (+Z); the offset is a full metre mid-walk and fades near the ends.
        Assert.InRange(maxZ, 0.95, 1.05);
        Assert.True(facedEast, "never faced east while walking east");
    }

    [Fact]
    public void RainModel_FallsOnWetCloudyPlaces_NotOnDryOnes_AndComesInShowers()
    {
        Assert.Equal(0, RainModel.Intensity(0.2, 0.1, 0, 0), 9);
        Assert.Equal(0, RainModel.Intensity(0.5, 0.5, 0, 1.3), 9);
        Assert.True(RainModel.Intensity(0.95, 0.9, 0, 0.1) > 0.8);
        Assert.True(RainModel.Intensity(0.8, 0.6, 0.15, 0.1) > RainModel.Intensity(0.8, 0.6, -0.15, 0.1));
        double min = double.MaxValue, max = double.MinValue;
        for (double d = 0; d < 2; d += 0.01)
        {
            double r = RainModel.Intensity(0.85, 0.8, 0, d);
            min = Math.Min(min, r);
            max = Math.Max(max, r);
        }

        Assert.True(max > min + 0.2, "no showers: the rain does not change within a day");
        Assert.InRange(RainModel.Intensity(1, 1, 1, 0.3), 0, 1);
    }

    private static double MinDistance(IReadOnlyList<NpcSnapshot> people)
    {
        double min = double.MaxValue;
        for (int a = 0; a < people.Count; a++)
        {
            for (int b = a + 1; b < people.Count; b++)
            {
                min = Math.Min(min, Math.Sqrt(Math.Pow(people[a].X - people[b].X, 2) + Math.Pow(people[a].Z - people[b].Z, 2)));
            }
        }

        return min;
    }

    [Fact]
    public void PeopleStandingOnTheSameSpot_AreEasedApart_AndNobodyStaysInsideAnother()
    {
        // Idlers (half of them start indoors) whose only spot is the same point.
        NpcPlan[] plans = Enumerable.Range(0, 24).Select(i => new NpcPlan(i, NpcRole.Loiterer, 4, Array.Empty<int>(), new[] { (5.0, 5.0) }, 1320, 360, (ulong)i + 1) { Schedule = SquareDay }).ToArray();
        var crowd = new Crowd(Town(), plans, 700, Speed);
        var people = new List<NpcSnapshot>();
        for (double t = 700; t <= 900; t += 1)
        {
            crowd.Update(t);
        }

        for (int frame = 0; frame < 120; frame++)
        {
            crowd.CollectVisible(900, 1.0 / 60, people);
        }

        Assert.True(people.Count >= 8, $"only {people.Count} visible");
        Assert.True(MinDistance(people) >= 0.65, $"two people are {MinDistance(people):F2} m apart");
        Assert.All(people, p => Assert.True(Math.Sqrt(Math.Pow(p.X - 5, 2) + Math.Pow(p.Z - 5, 2)) <= 2.5, "somebody was pushed a long way off"));
    }

    [Fact]
    public void TwoWalkersPassingHeadOn_DoNotWalkThroughEachOther()
    {
        var nodes = new List<NavNode> { new(0, 0, NavNodeKind.Door, 1), new(40, 0, NavNodeKind.Door, 2) };
        var graph = new NavGraph(nodes, new[] { new NavEdge(0, 1, 40) });
        var day = new ScheduleBlock[] { new(700, 1439, ScheduleActivity.Work) }; // at home until 11:40, then each walks to the other end
        NpcPlan a = new(0, NpcRole.Walker, 0, Array.Empty<int>(), Array.Empty<(double, double)>(), 1320, 360, 11) { Schedule = day, WorkNode = 1 }; // lives at one end, works at the other
        NpcPlan b = new(1, NpcRole.Walker, 1, Array.Empty<int>(), Array.Empty<(double, double)>(), 1320, 360, 12) { Schedule = day, WorkNode = 0 };
        var crowd = new Crowd(graph, new[] { a, b }, 600, Speed);
        var people = new List<NpcSnapshot>();
        double min = double.MaxValue;
        int together = 0;

        for (double t = 600; t < 600 + 300; t += 0.02) // a frame at 60 fps is 1/60 s: 0.02 of a game minute at the normal day length
        {
            crowd.Update(t);
            crowd.CollectVisible(t, 0.016, people);
            if (people.Count == 2 && people.All(p => p.Walking))
            {
                together++;
                min = Math.Min(min, MinDistance(people));
            }
        }

        Assert.True(together > 100, $"they were walking together for only {together} frames: the test proves nothing");
        Assert.True(min >= 0.55, $"they came within {min:F2} m of each other");
    }

    [Fact]
    public void NobodyIsPushedOntoGroundThatIsNotOpen()
    {
        NpcPlan[] plans = Enumerable.Range(0, 6).Select(i => new NpcPlan(i, NpcRole.Loiterer, 4, Array.Empty<int>(), new[] { (5.0, 5.0) }, 1320, 360, (ulong)i + 1) { Schedule = SquareDay }).ToArray();
        var crowd = new Crowd(Town(), plans, 700, Speed, null, (rx, rz, _, _) => (rx, rz)); // nowhere to go but where they stand
        var people = new List<NpcSnapshot>();
        for (double t = 700; t <= 900; t += 1)
        {
            crowd.Update(t);
        }

        for (int frame = 0; frame < 120; frame++)
        {
            crowd.CollectVisible(900, 1.0 / 60, people);
        }

        Assert.All(people, p => Assert.True(Math.Sqrt(Math.Pow(p.X - 5, 2) + Math.Pow(p.Z - 5, 2)) < 0.5, "pushed although the way was not open"));
    }

    [Fact]
    public void HalfThePeople_StartIndoors_AndHalfOut()
    {
        NpcPlan[] plans = Enumerable.Range(0, 200).Select(i => Plan(i, NpcRole.Loiterer, SquareDay)).ToArray();

        var crowd = new Crowd(Town(), plans, 700, Speed);

        Assert.InRange(crowd.CountOutside(), 80, 120);
    }

    [Fact]
    public void PeopleStayInsideAGoodWhile_NotJustInAndOut()
    {
        NpcPlan[] plans = Enumerable.Range(0, 30).Select(i => Plan(i, NpcRole.Loiterer, SquareDay)).ToArray();
        var crowd = new Crowd(Town(), plans, 700, Speed);
        var enteredAt = new double?[30];
        var stays = new List<double>();

        for (double t = 700; t <= 1100; t += 1)
        {
            crowd.Update(t);
            for (int i = 0; i < plans.Length; i++)
            {
                bool inside = crowd.Describe(i).State == NpcState.Inside;
                if (inside && enteredAt[i] is null)
                {
                    enteredAt[i] = t;
                }
                else if (!inside && enteredAt[i] is double since)
                {
                    stays.Add(t - since);
                    enteredAt[i] = null;
                }
            }
        }

        Assert.True(stays.Count > 5, $"only {stays.Count} visits to judge");
        Assert.True(stays.Average() >= 30, $"people stay inside only {stays.Average():F0} minutes on average");
    }

    [Fact]
    public void WhenTheRainStops_ThoseUnderARoofWaitABitBeforeComingOut()
    {
        NpcPlan[] idlers = Enumerable.Range(0, 40).Select(i => Plan(i, NpcRole.Loiterer, SquareDay)).ToArray();
        var crowd = new Crowd(Town(), idlers, 700, Speed);
        for (double t = 700; t <= 800; t += 1)
        {
            crowd.Update(t);
        }

        for (double t = 801; t <= 900; t += 1)
        {
            crowd.Update(t, new CrowdContext(0.8, false, 0, 0));
        }

        int outsideWhenItStopped = crowd.CountOutside();
        for (double t = 901; t <= 908; t += 1)
        {
            crowd.Update(t, default);
        }

        Assert.True(crowd.CountOutside() <= outsideWhenItStopped + 2, "people came out the moment the rain stopped");
    }
}
