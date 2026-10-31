using System;
using System.Collections.Generic;
using Realm.Core.Geometry;
using Realm.Core.Ids;
using Realm.Data.Content;

namespace Realm.Sim.Combat;

/// <summary>
/// Turns loaded combat content (<c>condition</c>, <c>attack</c>, <c>creature</c> records) into the runtime objects
/// <see cref="CombatEncounter"/> uses, converting seconds to sim ticks with <see cref="CombatRules.TicksPerSecond"/>.
/// </summary>
/// <remarks>
/// Every attack and condition is converted once, up front; referenced IDs must exist, which content validation
/// (<c>DATA013</c>) guarantees for a database loaded without errors.
/// Determinism: integer ticks from <see cref="ToTicks"/>; the same content and rules give the same objects.
/// Thread-safety: immutable after construction.
/// </remarks>
public sealed class CombatContent
{
    private readonly ContentDatabase _database;
    private readonly Dictionary<ContentId, Condition> _conditions = new();
    private readonly Dictionary<ContentId, Attack> _attacks = new();

    /// <summary>Converts every condition and attack in the database.</summary>
    /// <param name="database">Loaded content, ideally without errors.</param>
    /// <param name="rules">Tuning values; <see cref="CombatRules.Default"/> when null.</param>
    /// <exception cref="ArgumentNullException"><paramref name="database"/> is null.</exception>
    /// <exception cref="KeyNotFoundException">An attack applies a condition that is not in the database.</exception>
    public CombatContent(ContentDatabase database, CombatRules? rules = null)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
        Rules = rules ?? CombatRules.Default;

        foreach (ConditionDefinition c in database.Conditions.Values)
        {
            _conditions[c.Id] = new Condition(c.Id.Value, ToTicks(c.DurationSeconds))
            {
                PreventsActions = c.PreventsActions,
                BreaksOnDamage = c.BreaksOnDamage,
                PulseIntervalTicks = c.Pulse is { } p ? ToTicks(p.EverySeconds) : 0,
                PulseDamage = c.Pulse?.Damage ?? 0,
                PulseDamageType = c.Pulse?.DamageType ?? DamageTypes.Physical,
            };
        }

        foreach (AttackDefinition a in database.Attacks.Values)
        {
            _attacks[a.Id] = new Attack(a.Id.Value, ToTicks(a.RecoverySeconds), a.RangeMeters, a.Damage)
            {
                DamageType = a.DamageType,
                AttackBonus = a.AttackBonus,
                AreaRadiusMeters = a.AreaRadiusMeters,
                ActionPointCost = a.ActionPointCost,
                Applies = a.Applies is { } applies ? new ConditionApplication(_conditions[applies.Condition], applies.ChancePercent) : null,
            };
        }
    }

    /// <summary>The rules used for the tick conversion; pass the same rules to the encounters.</summary>
    public CombatRules Rules { get; }

    /// <summary>The runtime attack for an <c>attack</c> record.</summary>
    /// <param name="id">The attack ID.</param>
    /// <returns>The attack.</returns>
    /// <exception cref="KeyNotFoundException">No such attack.</exception>
    public Attack Attack(ContentId id) =>
        _attacks.TryGetValue(id, out Attack? attack) ? attack : throw new KeyNotFoundException($"No attack '{id}'.");

    /// <summary>The runtime condition for a <c>condition</c> record.</summary>
    /// <param name="id">The condition ID.</param>
    /// <returns>The condition.</returns>
    /// <exception cref="KeyNotFoundException">No such condition.</exception>
    public Condition Condition(ContentId id) =>
        _conditions.TryGetValue(id, out Condition? condition) ? condition : throw new KeyNotFoundException($"No condition '{id}'.");

    /// <summary>Creates a combatant from one tier of a creature family.</summary>
    /// <param name="creature">The creature family ID.</param>
    /// <param name="tier">Tier number, from 1.</param>
    /// <param name="id">Combatant ID, unique within its encounter.</param>
    /// <param name="team">Side.</param>
    /// <param name="position">Where it stands.</param>
    /// <returns>The combatant, named after the tier; creatures have no action-point pool.</returns>
    /// <exception cref="KeyNotFoundException">No such creature or attack.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The family has no such tier.</exception>
    public Combatant CreateCreature(ContentId creature, int tier, int id, int team, Point2D position)
    {
        CreatureTier t = Creature(creature).Tier(tier);
        var attacks = new List<Attack>(t.Attacks.Count);
        foreach (ContentId attack in t.Attacks)
        {
            attacks.Add(Attack(attack));
        }

        return new Combatant(id, t.Name, team, position, t.Health, t.AttackRating, t.Defence, attacks, 0, t.Resistances);
    }

    /// <summary>The AI brain for a creature family's behaviour.</summary>
    /// <param name="creature">The creature family ID.</param>
    /// <returns>The brain.</returns>
    /// <exception cref="KeyNotFoundException">No such creature.</exception>
    public ICombatBrain BrainFor(ContentId creature) => CombatBrains.ForBehaviour(Creature(creature).Behaviour);

    /// <summary>Converts seconds to whole sim ticks at <see cref="CombatRules.TicksPerSecond"/>, rounding half away from zero, at least 1.</summary>
    /// <param name="seconds">A positive duration.</param>
    /// <returns>The ticks.</returns>
    public int ToTicks(double seconds) =>
        Math.Max(1, (int)Math.Round(seconds * Rules.TicksPerSecond, MidpointRounding.AwayFromZero));

    private CreatureDefinition Creature(ContentId id) =>
        _database.Creatures.TryGetValue(id, out CreatureDefinition? c) ? c : throw new KeyNotFoundException($"No creature '{id}'.");
}
