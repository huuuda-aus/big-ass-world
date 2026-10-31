using System;
using System.Collections.Generic;
using Realm.Core.Geometry;
using Realm.Sim.Combat;

namespace Realm.Cli;

/// <summary>
/// The fights <c>combat-sim</c> can run. Spike: built in code; they move to YAML once attacks, conditions and creatures
/// are content types (ADR 0029).
/// </summary>
internal static class CombatScenarios
{
    /// <summary>The player's team in every scenario.</summary>
    public const int HeroTeam = 0;

    private const int MonsterTeam = 1;

    private static readonly ConditionDefinition Poisoned = new("condition.poisoned", 200)
    {
        PulseIntervalTicks = 20,
        PulseDamage = 1,
        PulseDamageType = DamageTypes.Poison,
    };

    private static readonly AttackDefinition Longsword = new("attack.longsword", 24, 2, new Dice(1, 8, 2));

    private static readonly AttackDefinition Fireball = new("attack.fireball", 40, 25, new Dice(2, 6, 2))
    {
        DamageType = DamageTypes.Fire,
        AreaRadiusMeters = 3,
        ActionPointCost = 5,
    };

    private static readonly AttackDefinition Club = new("attack.goblin_club", 26, 2, new Dice(1, 6));

    private static readonly AttackDefinition PoisonBow = new("attack.goblin_bow", 30, 25, new Dice(1, 6))
    {
        AttackBonus = 1,
        Applies = new ConditionApplication(Poisoned, 20),
    };

    /// <summary>Scenario names and one-line descriptions, in the order <c>--list</c> prints them.</summary>
    public static readonly IReadOnlyList<(string Name, string Description)> All = new[]
    {
        ("duel", "hero (longsword, fireball) against one goblin with a club, 1.5 m apart"),
        ("goblin-ambush", "hero against two club goblins at 1.5 m and a goblin archer with poison arrows at 12 m"),
    };

    /// <summary>Builds fight number <paramref name="number"/> of a scenario.</summary>
    /// <param name="name">Scenario name from <see cref="All"/>.</param>
    /// <param name="seed">Root seed.</param>
    /// <param name="number">Encounter number.</param>
    /// <returns>The encounter, or null for an unknown name.</returns>
    public static CombatEncounter? Build(string name, ulong seed, long number)
    {
        var fight = new CombatEncounter(seed, number);
        switch (name)
        {
            case "duel":
                fight.Add(Hero(), NearestEnemyBrain.Instance);
                fight.Add(Goblin(2, 1.5), NearestEnemyBrain.Instance);
                return fight;
            case "goblin-ambush":
                fight.Add(Hero(), NearestEnemyBrain.Instance);
                fight.Add(Goblin(2, 1.5), NearestEnemyBrain.Instance);
                fight.Add(Goblin(3, -1.5), NearestEnemyBrain.Instance);
                fight.Add(
                    new Combatant(4, "goblin archer", MonsterTeam, new Point2D(0, 12), 10, 2, 1, new[] { PoisonBow }),
                    NearestEnemyBrain.Instance);
                return fight;
            default:
                return null;
        }
    }

    private static Combatant Hero() => new(
        1,
        "hero",
        HeroTeam,
        new Point2D(0, 0),
        60,
        3,
        4,
        new[] { Longsword, Fireball },
        10,
        new Dictionary<string, int>(StringComparer.Ordinal) { [DamageTypes.Poison] = 25 });

    private static Combatant Goblin(int id, double x) =>
        new(id, "goblin", MonsterTeam, new Point2D(x, 0), 14, 1, 2, new[] { Club });
}
