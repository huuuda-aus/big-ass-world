using Realm.Sim.Entities;
using Realm.Sim.Time;

namespace Realm.Sim;

/// <summary>
/// Logic that runs once per simulation tick over the world's components (CLAUDE.md 5.1).
/// </summary>
/// <remarks>
/// Determinism contract for implementers: take randomness only from <c>Rng.ForKey(world.Seed, "system",
/// yourName, clock.Tick)</c>; never use <c>System.Random</c>, wall-clock time or hash-order enumeration.
/// Systems run synchronously on the simulation thread (CLAUDE.md 8.2).
/// </remarks>
public interface ISystem
{
    /// <summary>Runs one tick of this system.</summary>
    /// <param name="world">The entities and components to read and change.</param>
    /// <param name="clock">The clock; <see cref="SimClock.Tick"/> is the tick being processed.</param>
    void Update(SimWorld world, SimClock clock);
}
