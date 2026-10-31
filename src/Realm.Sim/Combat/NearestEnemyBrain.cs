using System;

namespace Realm.Sim.Combat;

/// <summary>
/// The <c>nearest_enemy</c> AI profile: target the nearest living enemy (ties by lower <see cref="Combatant.Id"/>) and use
/// the in-range attack with the highest average damage, skipping area attacks whose blast would reach itself. When no
/// attack can be used from where it stands, walk towards the target until the longest-reaching attack that would work
/// is in range.
/// </summary>
/// <remarks>Stateless and deterministic; one instance can serve every combatant.</remarks>
public sealed class NearestEnemyBrain : ICombatBrain
{
    /// <summary>A shared instance.</summary>
    public static readonly NearestEnemyBrain Instance = new();

    /// <inheritdoc />
    public CombatDecision Decide(CombatEncounter encounter, Combatant self)
    {
        ArgumentNullException.ThrowIfNull(encounter);
        ArgumentNullException.ThrowIfNull(self);

        Combatant? target = null;
        double distance = double.MaxValue;
        foreach (Combatant other in encounter.Combatants)
        {
            if (!other.IsAlive || other.Team == self.Team)
            {
                continue;
            }

            double d = self.DistanceTo(other);
            if (d < distance)
            {
                distance = d;
                target = other;
            }
        }

        if (target is null)
        {
            return CombatDecision.None;
        }

        int chosen = -1;
        double bestDamage = double.MinValue;
        double approachTo = -1;
        for (int i = 0; i < self.Attacks.Count; i++)
        {
            Attack attack = self.Attacks[i];
            if (UsableAt(attack, distance))
            {
                if (attack.Damage.Average > bestDamage)
                {
                    bestDamage = attack.Damage.Average;
                    chosen = i;
                }
            }
            else if (attack.RangeMeters < distance && UsableAt(attack, attack.RangeMeters))
            {
                // Out of reach but would work from its full range: a candidate distance to walk to.
                approachTo = Math.Max(approachTo, attack.RangeMeters);
            }
        }

        if (chosen >= 0)
        {
            return CombatDecision.Act(new CombatAction(chosen, target.Id));
        }

        return approachTo > 0
            ? CombatDecision.Move(target.Position, Math.Max(0, approachTo - encounter.Rules.ApproachMarginMeters))
            : CombatDecision.None;
    }

    // In range, and an area attack must not reach the user.
    private static bool UsableAt(Attack attack, double distance) =>
        distance <= attack.RangeMeters && (!attack.IsArea || distance > attack.AreaRadiusMeters);
}
