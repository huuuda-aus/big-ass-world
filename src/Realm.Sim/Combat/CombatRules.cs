using System;

namespace Realm.Sim.Combat;

/// <summary>Tuning values shared by every fight. Integer-only so results match on every machine.</summary>
/// <remarks>Thread-safety: immutable.</remarks>
public sealed record CombatRules
{
    /// <summary>Percent values are out of this.</summary>
    public const int PercentScale = 100;

    /// <summary>The spike's starting values; tune with <c>realm combat-sim</c>.</summary>
    public static readonly CombatRules Default = new();

    /// <summary>Sim ticks per real second (the host's tick rate, 20 Hz); used only to report times in seconds.</summary>
    public int TicksPerSecond { get; init; } = 20;

    /// <summary>Hit chance when attack rating equals defence.</summary>
    public int HitBasePercent { get; init; } = 75;

    /// <summary>Hit chance gained per point of attack rating above defence (lost per point below).</summary>
    public int HitPercentPerPoint { get; init; } = 5;

    /// <summary>Lowest hit chance: there is always some hope.</summary>
    public int HitMinPercent { get; init; } = 5;

    /// <summary>Highest hit chance: there is always some risk.</summary>
    public int HitMaxPercent { get; init; } = 95;

    /// <summary>Ticks between two actions resolved from a tactical-pause queue (0.2 s at 20 Hz).</summary>
    public int QueuedActionGapTicks { get; init; } = 4;

    /// <summary>Ticks to regain one action point (one per second at 20 Hz).</summary>
    public int ActionPointRegenTicks { get; init; } = 20;

    /// <summary>The chance, in percent, that an attack with rating <paramref name="attackRating"/> hits defence <paramref name="defence"/>.</summary>
    /// <param name="attackRating">Attacker's rating plus the attack's bonus.</param>
    /// <param name="defence">Target's defence.</param>
    /// <returns>A value from <see cref="HitMinPercent"/> to <see cref="HitMaxPercent"/>.</returns>
    public int HitChancePercent(int attackRating, int defence)
    {
        long raw = HitBasePercent + ((long)attackRating - defence) * HitPercentPerPoint;
        return (int)Math.Clamp(raw, HitMinPercent, HitMaxPercent);
    }
}
