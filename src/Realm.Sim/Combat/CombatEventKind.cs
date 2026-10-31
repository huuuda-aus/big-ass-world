namespace Realm.Sim.Combat;

/// <summary>What a <see cref="CombatEvent"/> reports.</summary>
public enum CombatEventKind
{
    /// <summary>An actor used an attack on a target.</summary>
    Attacked,

    /// <summary>A single-target attack hit.</summary>
    Hit,

    /// <summary>A single-target attack missed.</summary>
    Missed,

    /// <summary>A combatant lost health; <see cref="CombatEvent.Amount"/> is the damage after resistance.</summary>
    Damaged,

    /// <summary>A condition was applied or refreshed.</summary>
    ConditionApplied,

    /// <summary>A condition wore off or was broken.</summary>
    ConditionEnded,

    /// <summary>A combatant died.</summary>
    Died,

    /// <summary>The tactical pause started.</summary>
    Paused,

    /// <summary>An action was queued in the pause; <see cref="CombatEvent.Amount"/> is its action-point cost.</summary>
    ActionQueued,

    /// <summary>The pause was cancelled; <see cref="CombatEvent.Amount"/> is the refunded action points.</summary>
    PauseCancelled,

    /// <summary>The queued actions started resolving.</summary>
    QueueConfirmed,

    /// <summary>The queue stopped early because its actor died or was prevented from acting.</summary>
    QueueInterrupted,

    /// <summary>The queue finished and real time resumed.</summary>
    QueueFinished,

    /// <summary>Only one team (or none) has living members.</summary>
    Ended,
}
