using System;

namespace Realm.Sim.Combat;

/// <summary>A condition an attack may put on whoever it hits.</summary>
/// <remarks>Thread-safety: immutable.</remarks>
public sealed record ConditionApplication
{
    /// <summary>Creates an application.</summary>
    /// <param name="condition">The condition to apply.</param>
    /// <param name="chancePercent">Chance per hit, 0 to 100.</param>
    /// <exception cref="ArgumentNullException"><paramref name="condition"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="chancePercent"/> is outside 0 to 100.</exception>
    public ConditionApplication(Condition condition, int chancePercent)
    {
        ArgumentNullException.ThrowIfNull(condition);
        ArgumentOutOfRangeException.ThrowIfNegative(chancePercent);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(chancePercent, CombatRules.PercentScale);
        Condition = condition;
        ChancePercent = chancePercent;
    }

    /// <summary>The condition to apply.</summary>
    public Condition Condition { get; }

    /// <summary>Chance per hit, 0 to 100.</summary>
    public int ChancePercent { get; }
}
