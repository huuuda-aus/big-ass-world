using System;

namespace Realm.Sim.Combat;

/// <summary>
/// A condition such as poisoned or asleep: how long it lasts and what it does while active.
/// Spike: defined in code; becomes a YAML content type later (ADR 0029).
/// </summary>
/// <remarks>Thread-safety: immutable.</remarks>
public sealed record ConditionDefinition
{
    /// <summary>Creates a condition.</summary>
    /// <param name="id">Stable identifier, for example <c>condition.poisoned</c>.</param>
    /// <param name="durationTicks">How long it lasts once applied; must be positive.</param>
    /// <exception cref="ArgumentException"><paramref name="id"/> is empty.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="durationTicks"/> is not positive.</exception>
    public ConditionDefinition(string id, int durationTicks)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(durationTicks);
        Id = id;
        DurationTicks = durationTicks;
    }

    /// <summary>Stable identifier.</summary>
    public string Id { get; }

    /// <summary>How long it lasts once applied, in sim ticks.</summary>
    public int DurationTicks { get; }

    /// <summary>When true the affected combatant cannot act (asleep, paralysed).</summary>
    public bool PreventsActions { get; init; }

    /// <summary>When true any damage above zero ends the condition (being hit wakes a sleeper).</summary>
    public bool BreaksOnDamage { get; init; }

    /// <summary>Ticks between damage pulses; zero for no pulses.</summary>
    public int PulseIntervalTicks { get; init; }

    /// <summary>Damage dealt by each pulse, before resistance.</summary>
    public int PulseDamage { get; init; }

    /// <summary>Damage type of the pulses, for example <c>poison</c>.</summary>
    public string PulseDamageType { get; init; } = DamageTypes.Physical;
}
