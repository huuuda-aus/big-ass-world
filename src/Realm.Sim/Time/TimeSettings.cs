using System;

namespace Realm.Sim.Time;

/// <summary>
/// How simulation ticks map to game time. Integer-only so the mapping is exact on every machine.
/// </summary>
/// <remarks>Thread-safety: immutable.</remarks>
public sealed record TimeSettings
{
    /// <summary>
    /// Default pacing, accepted by the developer on 2026-10-20: 20 ticks per game minute. At a 20 Hz tick
    /// this is one game minute per real second, so a day lasts 24 real minutes.
    /// </summary>
    public static readonly TimeSettings Default = new(20);

    /// <summary>Creates settings.</summary>
    /// <param name="ticksPerGameMinute">Simulation ticks that make up one game minute; must be positive.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="ticksPerGameMinute"/> is not positive.</exception>
    public TimeSettings(int ticksPerGameMinute)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ticksPerGameMinute);
        TicksPerGameMinute = ticksPerGameMinute;
    }

    /// <summary>Simulation ticks that make up one game minute.</summary>
    public int TicksPerGameMinute { get; }
}
