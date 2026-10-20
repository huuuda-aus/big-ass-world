using System;

namespace Realm.Sim.Time;

/// <summary>
/// A point in game time, stored as whole minutes since the world began (day 0, 00:00).
/// Assumes a 24-hour day. Calendar units above days (weeks, months, seasons) do not exist yet.
/// </summary>
/// <remarks>Thread-safety: immutable.</remarks>
public readonly struct GameTime : IEquatable<GameTime>, IComparable<GameTime>
{
    /// <summary>Minutes in an hour.</summary>
    public const int MinutesPerHour = 60;

    /// <summary>Hours in a day.</summary>
    public const int HoursPerDay = 24;

    /// <summary>Minutes in a day.</summary>
    public const int MinutesPerDay = MinutesPerHour * HoursPerDay;

    /// <summary>Creates a time from total minutes since the world began.</summary>
    /// <param name="totalMinutes">Minutes since day 0, 00:00; must not be negative.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="totalMinutes"/> is negative.</exception>
    public GameTime(long totalMinutes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(totalMinutes);
        TotalMinutes = totalMinutes;
    }

    /// <summary>Minutes since the world began.</summary>
    public long TotalMinutes { get; }

    /// <summary>Whole days since the world began (day 0 is the first).</summary>
    public long Day => TotalMinutes / MinutesPerDay;

    /// <summary>Minutes elapsed since midnight, 0 to 1439.</summary>
    public int MinuteOfDay => (int)(TotalMinutes % MinutesPerDay);

    /// <summary>Hour of the day, 0 to 23.</summary>
    public int Hour => MinuteOfDay / MinutesPerHour;

    /// <summary>Minute of the hour, 0 to 59.</summary>
    public int Minute => MinuteOfDay % MinutesPerHour;

    /// <summary>Returns this time moved forward by some minutes.</summary>
    /// <param name="minutes">Minutes to add; must not be negative.</param>
    /// <returns>The later time.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="minutes"/> is negative.</exception>
    public GameTime AddMinutes(long minutes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(minutes);
        return new GameTime(checked(TotalMinutes + minutes));
    }

    /// <inheritdoc />
    public bool Equals(GameTime other) => TotalMinutes == other.TotalMinutes;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is GameTime other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => TotalMinutes.GetHashCode();

    /// <inheritdoc />
    public int CompareTo(GameTime other) => TotalMinutes.CompareTo(other.TotalMinutes);

    /// <summary>Formats as <c>Day 3 14:05</c>.</summary>
    /// <returns>A short description.</returns>
    public override string ToString() => $"Day {Day} {Hour:00}:{Minute:00}";

    /// <summary>Equality operator.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns><c>true</c> when equal.</returns>
    public static bool operator ==(GameTime left, GameTime right) => left.Equals(right);

    /// <summary>Inequality operator.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns><c>true</c> when not equal.</returns>
    public static bool operator !=(GameTime left, GameTime right) => !left.Equals(right);

    /// <summary>Less-than operator.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns><c>true</c> when <paramref name="left"/> is earlier.</returns>
    public static bool operator <(GameTime left, GameTime right) => left.CompareTo(right) < 0;

    /// <summary>Greater-than operator.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns><c>true</c> when <paramref name="left"/> is later.</returns>
    public static bool operator >(GameTime left, GameTime right) => left.CompareTo(right) > 0;

    /// <summary>Less-or-equal operator.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns><c>true</c> when <paramref name="left"/> is not later.</returns>
    public static bool operator <=(GameTime left, GameTime right) => left.CompareTo(right) <= 0;

    /// <summary>Greater-or-equal operator.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns><c>true</c> when <paramref name="left"/> is not earlier.</returns>
    public static bool operator >=(GameTime left, GameTime right) => left.CompareTo(right) >= 0;
}
