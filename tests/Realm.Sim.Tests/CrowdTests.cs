using System;
using System.Collections.Generic;
using System.Linq;
using Realm.Sim.Navigation;
using Realm.Sim.Npcs;
using Xunit;

namespace Realm.Sim.Tests;

public class CrowdTests
{
    private const double Speed = 1.2; // metres per game minute

    // A small square (nodes 0-3) with three doors off it.
    private static NavGraph Town()
    {
        var nodes = new List<NavNode>
        {
            new(0, 0, NavNodeKind.Street), new(10, 0, NavNodeKind.Street), new(10, 10, NavNodeKind.Street), new(0, 10, NavNodeKind.Street),
            new(0, -5, NavNodeKind.Door, 100), new(10, -5, NavNodeKind.Door, 101), new(20, 10, NavNodeKind.Door, 102),
        };
        var edges = new List<NavEdge>
        {
            new(0, 1, 10), new(1, 2, 10), new(2, 3, 10), new(3, 0, 10),
            new(4, 0, 5), new(5, 1, 5), new(6, 2, 10),
        };
        return new NavGraph(nodes, edges);
    }

    private static readonly (double X, double Z)[] Spots = { (4, 4), (6, 5), (5, 8), (2, 6) };

    private static NpcPlan Loiterer(int id = 0, ulong seed = 1) =>
        new(id, NpcRole.Loiterer, 4, new[] { 5, 6 }, Spots, 1320, 360, seed);

    private static NpcPlan Walker(int id = 1, ulong seed = 2) =>
        new(id, NpcRole.Walker, 4, new[] { 5, 6 }, Array.Empty<(double, double)>(), 1320, 360, seed);

    private static NpcPlan Guard(int id = 2) =>
        new(id, NpcRole.Guard, -1, Array.Empty<int>(), new[] { (3.0, 3.0) }, 1320, 360, 3);

    [Fact]
    public void AGuard_StandsAtItsPostForever()
    {
        var crowd = new Crowd(Town(), new[] { Guard() }, 600, Speed);

        for (double t = 600; t < 600 + (3 * 1440); t += 37)
        {
            crowd.Update(t);
            Assert.True(crowd.TryGetSnapshot(0, t, out NpcSnapshot s));
            Assert.Equal((3.0, 3.0, false), (s.X, s.Z, s.Walking));
        }
    }

    [Fact]
    public void ALoiterer_HangsAboutTheSpots_GoesOnErrands_AndIsNeverTeleported()
    {
        var crowd = new Crowd(Town(), new[] { Loiterer() }, 600, Speed);
        bool sawInside = false, sawWalking = false, sawStanding = false;
        NpcSnapshot? previous = null;
        double previousT = 0;

        for (double t = 600; t < 600 + (4 * 1440); t += 0.5)
        {
            crowd.Update(t);
            bool outside = crowd.TryGetSnapshot(0, t, out NpcSnapshot s);
            sawInside |= !outside;
            if (outside)
            {
                sawWalking |= s.Walking;
                sawStanding |= !s.Walking;
                Assert.InRange(s.X, -0.01, 20.01);
                Assert.InRange(s.Z, -5.01, 10.01);
                if (previous is { } p && previousT == t - 0.5)
                {
                    double moved = Math.Sqrt(Math.Pow(s.X - p.X, 2) + Math.Pow(s.Z - p.Z, 2));
                    Assert.True(moved <= (Speed * 0.5) + 1e-6, $"jumped {moved:F2} m in half a minute at {t}");
                }

                previous = s;
            }
            else
            {
                previous = null;
            }

            previousT = t;
        }

        Assert.True(sawInside && sawWalking && sawStanding);
    }

    [Fact]
    public void AWalker_SleepsAtHomeAtNight_AndIsOutAndAboutByDay()
    {
        var crowd = new Crowd(Town(), new[] { Walker() }, 600, Speed);
        int dayOutside = 0, dayMinutes = 0;

        // Run from the start through three days; judge the last two (the first morning is settling in).
        for (double t = 600; t < 600 + (3 * 1440); t += 1)
        {
            crowd.Update(t);
            double minute = t % 1440;
            bool outside = crowd.TryGetSnapshot(0, t, out _);
            if (t > 1440 + 600)
            {
                if (minute is > 30 and < 300)
                {
                    Assert.False(outside, $"awake in the dead of night at minute {minute}");
                }

                if (minute is > 480 and < 1100)
                {
                    dayMinutes++;
                    dayOutside += outside ? 1 : 0;
                }
            }
        }

        Assert.True(dayOutside > dayMinutes * 0.1, $"a walker is hardly ever outside by day ({dayOutside} of {dayMinutes})");
        Assert.True(dayOutside < dayMinutes, "a walker never goes into a building by day");
    }

    [Fact]
    public void SamePlans_SameDay_AndATimeJump_CatchesUpWithoutTeleporting()
    {
        NpcPlan[] plans = { Loiterer(0, 5), Walker(1, 6), Walker(2, 7) };
        var a = new Crowd(Town(), plans, 600, Speed);
        var b = new Crowd(Town(), plans, 600, Speed);

        for (double t = 600; t < 2000; t += 3)
        {
            a.Update(t);
            b.Update(t);
            for (int i = 0; i < 3; i++)
            {
                Assert.Equal(a.TryGetSnapshot(i, t, out NpcSnapshot sa), b.TryGetSnapshot(i, t, out NpcSnapshot sb));
                Assert.Equal(sa, sb);
            }
        }

        // A jump of ten days: nothing throws and every visible position is in the town.
        a.Update(2000 + (10 * 1440));
        for (int i = 0; i < 3; i++)
        {
            if (a.TryGetSnapshot(i, 2000 + (10 * 1440), out NpcSnapshot s))
            {
                Assert.InRange(s.X, -0.01, 20.01);
                Assert.InRange(s.Z, -5.01, 10.01);
            }
        }
    }

    [Fact]
    public void SleepingHours_WrapAroundMidnight()
    {
        NpcPlan plan = Walker();

        Assert.True(plan.IsAsleepAt(1400));
        Assert.True(plan.IsAsleepAt(10));
        Assert.False(plan.IsAsleepAt(720));
        Assert.False(plan.IsAsleepAt(1319));
        Assert.True(plan with { SleepMinute = 100, WakeMinute = 200 } is { } day && day.IsAsleepAt(150) && !day.IsAsleepAt(250));
    }

    [Fact]
    public void ManyCharacters_AreStaggered_AndCountsAreRight()
    {
        NpcPlan[] plans = Enumerable.Range(0, 200).Select(i => i % 3 == 0 ? Loiterer(i, (ulong)i + 1) : Walker(i, (ulong)i + 1)).ToArray();

        var crowd = new Crowd(Town(), plans, 600, Speed);
        crowd.Update(600);

        Assert.Equal(200, crowd.Count);
        Assert.InRange(crowd.CountOutside(), 40, 200);
        Assert.Equal(NpcRole.Loiterer, crowd.PlanOf(0).Role);
        Assert.Throws<ArgumentOutOfRangeException>(() => new Crowd(Town(), plans, 0, 0));
    }
}
