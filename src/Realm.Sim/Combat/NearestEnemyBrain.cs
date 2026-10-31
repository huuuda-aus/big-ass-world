using System;

namespace Realm.Sim.Combat;

/// <summary>
/// The spike's only AI profile: target the nearest living enemy (ties by lower <see cref="Combatant.Id"/>) and use
/// the in-range attack with the highest average damage, skipping area attacks whose blast would reach itself.
/// </summary>
/// <remarks>Stateless and deterministic; one instance can serve every combatant.</remarks>
public sealed class NearestEnemyBrain : ICombatBrain
{
    /// <summary>A shared instance.</summary>
    public static readonly NearestEnemyBrain Instance = new();

    /// <inheritdoc />
    public CombatAction? Decide(CombatEncounter encounter, Combatant self)
    {
        ArgumentNullException.ThrowIfNull(encounter);
        ArgumentNullException.ThrowIfNull(self);

        Combatant? target = null;
        double best = double.MaxValue;
        foreach (Combatant other in encounter.Combatants)
        {
            if (!other.IsAlive || other.Team == self.Team)
            {
                continue;
            }

            double d = self.DistanceTo(other);
            if (d < best)
            {
                best = d;
                target = other;
            }
        }

        if (target is null)
        {
            return null;
        }

        int chosen = -1;
        double bestDamage = double.MinValue;
        for (int i = 0; i < self.Attacks.Count; i++)
        {
            Attack attack = self.Attacks[i];
            if (best > attack.RangeMeters || (attack.IsArea && best <= attack.AreaRadiusMeters))
            {
                continue;
            }

            if (attack.Damage.Average > bestDamage)
            {
                bestDamage = attack.Damage.Average;
                chosen = i;
            }
        }

        return chosen < 0 ? null : new CombatAction(chosen, target.Id);
    }
}
