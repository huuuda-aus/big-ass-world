using System;

namespace Realm.Sim.Combat;

/// <summary>
/// One thing a combatant can do to a target: a weapon swing, a bow shot or a spell.
/// Spike: defined in code; becomes a YAML content type later (ADR 0029).
/// </summary>
/// <remarks>Thread-safety: immutable.</remarks>
public sealed record AttackDefinition
{
    /// <summary>Creates an attack.</summary>
    /// <param name="id">Stable identifier, for example <c>attack.longsword</c>.</param>
    /// <param name="recoveryTicks">Ticks before the user may act again; must be positive.</param>
    /// <param name="rangeMeters">Furthest target distance; must be positive.</param>
    /// <param name="damage">Damage dice.</param>
    /// <exception cref="ArgumentException"><paramref name="id"/> is empty.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="recoveryTicks"/> or <paramref name="rangeMeters"/> is not positive.</exception>
    public AttackDefinition(string id, int recoveryTicks, double rangeMeters, Dice damage)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(recoveryTicks);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rangeMeters);
        Id = id;
        RecoveryTicks = recoveryTicks;
        RangeMeters = rangeMeters;
        Damage = damage;
    }

    /// <summary>Stable identifier.</summary>
    public string Id { get; }

    /// <summary>Ticks before the user may act again (the M&amp;M recovery time).</summary>
    public int RecoveryTicks { get; }

    /// <summary>Furthest target distance in metres.</summary>
    public double RangeMeters { get; }

    /// <summary>Damage dice.</summary>
    public Dice Damage { get; }

    /// <summary>Damage type, matched against resistances.</summary>
    public string DamageType { get; init; } = DamageTypes.Physical;

    /// <summary>Added to the user's attack rating for the hit roll.</summary>
    public int AttackBonus { get; init; }

    /// <summary>
    /// Radius of the blast around the target in metres; zero for a single-target attack. An area attack needs no hit
    /// roll and damages every living combatant inside the radius, the user included (ADR 0029).
    /// </summary>
    public double AreaRadiusMeters { get; init; }

    /// <summary>Action points this costs when queued in the tactical pause.</summary>
    public int ActionPointCost { get; init; } = 1;

    /// <summary>A condition the attack may apply on a hit, or null.</summary>
    public ConditionApplication? Applies { get; init; }

    /// <summary>True when this is an area attack.</summary>
    public bool IsArea => AreaRadiusMeters > 0;
}
