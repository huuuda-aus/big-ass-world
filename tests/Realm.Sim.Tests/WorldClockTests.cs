using System;
using Realm.Sim.Time;
using Xunit;

namespace Realm.Sim.Tests;

public class WorldClockTests
{
    [Fact]
    public void StartsAtTheStartHour_OnDayZero()
    {
        var clock = new WorldClock(6, 1200);

        Assert.Equal(0, clock.Day);
        Assert.Equal(6, clock.Hour, 9);
        Assert.Equal(0.25, clock.TimeOfDay, 9);
        Assert.Equal(6 * 60, clock.Time.TotalMinutes);
    }

    [Fact]
    public void AGameDay_LastsTheConfiguredRealSeconds()
    {
        var clock = new WorldClock(0, 600);

        clock.Advance(300);

        Assert.Equal(12, clock.Hour, 9);
        clock.Advance(300);
        Assert.Equal(1, clock.Day);
        Assert.Equal(0, clock.Hour, 9);
    }

    [Fact]
    public void TimeScale_SpeedsUpSlowsDownAndPauses()
    {
        var clock = new WorldClock(0, 1440) { TimeScale = 60 };

        clock.Advance(1); // a day lasts 1440 real seconds, so one real second at scale 60 is 60/1440 of a day: one hour

        Assert.Equal(60.0 / 1440 * 24, clock.Hour, 9);
        double before = clock.Days;
        clock.TimeScale = 0;
        clock.Advance(100);
        Assert.Equal(before, clock.Days);
    }

    [Fact]
    public void SetHour_JumpsWithinTheCurrentDay()
    {
        var clock = new WorldClock(3, 1200);
        clock.Advance(1200 * 2.5); // two and a half days on

        clock.SetHour(18);

        Assert.Equal(2, clock.Day);
        Assert.Equal(18, clock.Hour, 9);
        Assert.Equal(new GameTime((2 * 1440) + (18 * 60)), clock.Time);
    }

    [Fact]
    public void BadArguments_AreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new WorldClock(24, 1200));
        Assert.Throws<ArgumentOutOfRangeException>(() => new WorldClock(-1, 1200));
        Assert.Throws<ArgumentOutOfRangeException>(() => new WorldClock(6, 0));
        var clock = new WorldClock();
        Assert.Throws<ArgumentOutOfRangeException>(() => clock.Advance(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => clock.SetHour(24));
    }
}
