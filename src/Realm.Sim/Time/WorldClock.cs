using System;

namespace Realm.Sim.Time;

/// <summary>
/// The world's time of day, advanced smoothly by real time. One game day lasts <see cref="RealSecondsPerGameDay"/> seconds at normal
/// speed; <see cref="TimeScale"/> speeds it up or slows it down. The presentation reads it for the sun and moon, and NPC schedules will read
/// <see cref="Time"/> (whole game minutes, the same <see cref="GameTime"/> the tick clock uses).
/// </summary>
/// <remarks>Determinism: state changes only through <see cref="Advance"/>, <see cref="SetHour"/> and the settings. Thread-safety: not thread-safe.</remarks>
public sealed class WorldClock
{
    /// <summary>Default length of a game day in real seconds (20 minutes).</summary>
    public const double DefaultRealSecondsPerGameDay = 1200;

    /// <summary>Default start of the first day, in hours after midnight.</summary>
    public const double DefaultStartHour = 9;

    private const double HoursPerDay = 24;

    /// <summary>Creates a clock.</summary>
    /// <param name="startHour">Hour of day 0 at the start, 0 up to 24.</param>
    /// <param name="realSecondsPerGameDay">Real seconds per game day at normal speed; positive.</param>
    /// <exception cref="ArgumentOutOfRangeException">An argument is out of range.</exception>
    public WorldClock(double startHour = DefaultStartHour, double realSecondsPerGameDay = DefaultRealSecondsPerGameDay)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(startHour);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(startHour, HoursPerDay);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(realSecondsPerGameDay);
        RealSecondsPerGameDay = realSecondsPerGameDay;
        Days = startHour / HoursPerDay;
    }

    /// <summary>Real seconds per game day at normal speed.</summary>
    public double RealSecondsPerGameDay { get; }

    /// <summary>Speed multiplier; 0 pauses the clock. Not negative.</summary>
    public double TimeScale { get; set; } = 1;

    /// <summary>Game days elapsed since the start of day 0, as a continuous number (day 2 at 06:00 is 2.25).</summary>
    public double Days { get; private set; }

    /// <summary>The whole day number, from 0.</summary>
    public long Day => (long)Math.Floor(Days);

    /// <summary>The fraction of the day since midnight, 0 up to 1.</summary>
    public double TimeOfDay => Days - Math.Floor(Days);

    /// <summary>The hour of the day with its fraction, 0 up to 24.</summary>
    public double Hour => TimeOfDay * HoursPerDay;

    /// <summary>The time in whole game minutes.</summary>
    public GameTime Time => new((long)Math.Floor(Days * GameTime.MinutesPerDay));

    /// <summary>Advances the clock.</summary>
    /// <param name="realSeconds">Real seconds that passed; not negative.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="realSeconds"/> is negative.</exception>
    public void Advance(double realSeconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(realSeconds);
        Days += realSeconds * Math.Max(0, TimeScale) / RealSecondsPerGameDay;
    }

    /// <summary>Jumps to an hour of the current day.</summary>
    /// <param name="hour">0 up to 24.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="hour"/> is outside 0 up to 24.</exception>
    public void SetHour(double hour)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(hour);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(hour, HoursPerDay);
        Days = Math.Floor(Days) + (hour / HoursPerDay);
    }
}
