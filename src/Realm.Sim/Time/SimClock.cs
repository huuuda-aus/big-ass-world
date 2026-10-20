using System;

namespace Realm.Sim.Time;

/// <summary>Counts simulation ticks and converts them to <see cref="GameTime"/>.</summary>
/// <remarks>
/// Determinism: integer arithmetic only. Thread-safety: not thread-safe; owned by one
/// <see cref="Simulation"/>.
/// </remarks>
public sealed class SimClock
{
    /// <summary>Creates a clock at tick 0.</summary>
    /// <param name="settings">Tick to game-time mapping.</param>
    public SimClock(TimeSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Settings = settings;
    }

    /// <summary>The tick to game-time mapping.</summary>
    public TimeSettings Settings { get; }

    /// <summary>Ticks elapsed since the world began.</summary>
    public long Tick { get; private set; }

    /// <summary>Current game time; moves forward one minute every <see cref="TimeSettings.TicksPerGameMinute"/> ticks.</summary>
    public GameTime Time => new(Tick / Settings.TicksPerGameMinute);

    /// <summary>Advances by one tick.</summary>
    public void Advance() => Tick = checked(Tick + 1);
}
