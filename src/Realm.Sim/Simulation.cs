using System;
using System.Collections.Generic;
using Realm.Sim.Entities;
using Realm.Sim.Time;

namespace Realm.Sim;

/// <summary>Owns a world, a clock and an ordered list of systems, and runs them tick by tick.</summary>
/// <remarks>
/// Determinism: systems always run in registration order, then the clock advances. The same seed,
/// systems and inputs give the same world after the same number of ticks.
/// Thread-safety: not thread-safe; call from one thread.
/// </remarks>
public sealed class Simulation
{
    private readonly List<ISystem> _systems = new();

    /// <summary>Creates a simulation at tick 0.</summary>
    /// <param name="seed">Root seed of the world.</param>
    /// <param name="timeSettings">Tick to game-time mapping; <see cref="TimeSettings.Default"/> when null.</param>
    public Simulation(ulong seed, TimeSettings? timeSettings = null)
    {
        WorldState = new SimWorld(seed);
        Clock = new SimClock(timeSettings ?? TimeSettings.Default);
    }

    /// <summary>The entities and components. Not named <c>World</c>: that name is reserved for the <c>Realm.Sim.World</c> namespace (CLAUDE.md section 5).</summary>
    public SimWorld WorldState { get; }

    /// <summary>The tick and game clock.</summary>
    public SimClock Clock { get; }

    /// <summary>Appends a system. Systems run in the order they were added.</summary>
    /// <param name="system">The system to add.</param>
    /// <exception cref="ArgumentNullException"><paramref name="system"/> is null.</exception>
    public void AddSystem(ISystem system)
    {
        ArgumentNullException.ThrowIfNull(system);
        _systems.Add(system);
    }

    /// <summary>Runs every system once, then advances the clock by one tick.</summary>
    public void Tick()
    {
        foreach (ISystem system in _systems)
        {
            system.Update(WorldState, Clock);
        }

        Clock.Advance();
    }

    /// <summary>Runs several ticks.</summary>
    /// <param name="count">Number of ticks; must not be negative.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is negative.</exception>
    public void Run(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        for (int i = 0; i < count; i++)
        {
            Tick();
        }
    }
}
