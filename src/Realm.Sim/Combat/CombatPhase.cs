namespace Realm.Sim.Combat;

/// <summary>Where a <see cref="CombatEncounter"/> is in its real-time / tactical-pause cycle (ADR 0029).</summary>
public enum CombatPhase
{
    /// <summary>Real time: the clock runs and everyone acts when ready.</summary>
    Running,

    /// <summary>Tactical pause: the clock is stopped while the player queues actions.</summary>
    Paused,

    /// <summary>The queued actions are playing out on the running clock; enemies keep acting.</summary>
    Resolving,

    /// <summary>The fight is over.</summary>
    Over,
}
