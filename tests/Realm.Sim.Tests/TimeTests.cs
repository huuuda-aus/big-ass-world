using System;
using Realm.Sim.Time;
using Xunit;

namespace Realm.Sim.Tests;

public class TimeTests
{
    [Fact]
    public void TimeSettings_RejectsNonPositive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TimeSettings(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TimeSettings(-5));
        Assert.Equal(20, TimeSettings.Default.TicksPerGameMinute);
    }

    [Theory]
    [InlineData(0, 0, 0, 0)]
    [InlineData(59, 0, 0, 59)]
    [InlineData(60, 0, 1, 0)]
    [InlineData(1439, 0, 23, 59)]
    [InlineData(1440, 1, 0, 0)]
    [InlineData(1440 * 3 + 14 * 60 + 5, 3, 14, 5)]
    public void GameTime_BreaksDownIntoDayHourMinute(long total, long day, int hour, int minute)
    {
        var time = new GameTime(total);

        Assert.Equal(day, time.Day);
        Assert.Equal(hour, time.Hour);
        Assert.Equal(minute, time.Minute);
        Assert.Equal(hour * 60 + minute, time.MinuteOfDay);
    }

    [Fact]
    public void GameTime_RejectsNegative()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new GameTime(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GameTime(10).AddMinutes(-1));
    }

    [Fact]
    public void GameTime_AddMinutes_RollsOverDays()
    {
        GameTime later = new GameTime(1439).AddMinutes(2);

        Assert.Equal(1, later.Day);
        Assert.Equal(1, later.Minute);
    }

    [Fact]
    public void GameTime_FormatsAndCompares()
    {
        var a = new GameTime(1440 * 3 + 14 * 60 + 5);
        var b = a.AddMinutes(1);

        Assert.Equal("Day 3 14:05", a.ToString());
        Assert.True(a < b);
        Assert.True(b > a);
        var sameAsA = new GameTime(a.TotalMinutes);
        Assert.True(a <= sameAsA);
        Assert.True(a >= sameAsA);
        Assert.False(a >= b);
        Assert.False(b <= a);
        Assert.True(a != b);
        Assert.Equal(a, new GameTime(a.TotalMinutes));
        Assert.True(a == new GameTime(a.TotalMinutes));
        Assert.Equal(a.GetHashCode(), new GameTime(a.TotalMinutes).GetHashCode());
    }

    [Fact]
    public void SimClock_StartsAtZero_AndAdvancesTicks()
    {
        var clock = new SimClock(new TimeSettings(4));

        Assert.Equal(0, clock.Tick);
        Assert.Equal(0, clock.Time.TotalMinutes);

        for (int i = 0; i < 3; i++)
        {
            clock.Advance();
        }

        Assert.Equal(3, clock.Tick);
        Assert.Equal(0, clock.Time.TotalMinutes);
    }

    [Fact]
    public void SimClock_MinuteAdvancesEveryTicksPerGameMinute()
    {
        var clock = new SimClock(new TimeSettings(4));
        for (int i = 0; i < 4; i++)
        {
            clock.Advance();
        }

        Assert.Equal(1, clock.Time.TotalMinutes);

        for (int i = 0; i < 4 * 59; i++)
        {
            clock.Advance();
        }

        Assert.Equal(1, clock.Time.Hour);
        Assert.Equal(0, clock.Time.Minute);
    }

    [Fact]
    public void SimClock_FullDay_TakesExpectedTicks()
    {
        var clock = new SimClock(TimeSettings.Default);
        long ticksPerDay = (long)GameTime.MinutesPerDay * TimeSettings.Default.TicksPerGameMinute;

        for (long i = 0; i < ticksPerDay; i++)
        {
            clock.Advance();
        }

        Assert.Equal(1, clock.Time.Day);
        Assert.Equal(0, clock.Time.MinuteOfDay);
    }

    [Fact]
    public void SimClock_RejectsNullSettings()
    {
        Assert.Throws<ArgumentNullException>(() => new SimClock(null!));
    }
}
