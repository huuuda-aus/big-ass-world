using System;
using System.Collections.Generic;
using Realm.Core.Geometry;
using Realm.Core.Ids;
using Realm.Sim.Combat;

namespace Realm.Cli;

/// <summary>
/// The fights <c>combat-sim</c> can run. Attacks and creatures come from content; the hero is built here because the
/// player character is not a creature.
/// </summary>
internal static class CombatScenarios
{
    /// <summary>The player's team in every scenario.</summary>
    public const int HeroTeam = 0;

    private const int MonsterTeam = 1;
    private const int GoblinTier = 1;
    private const int ArcherTier = 2;
    private const int ChiefTier = 3;

    private static readonly ContentId Goblin = ContentId.Parse("creature.goblin");

    /// <summary>Scenario names and one-line descriptions, in the order <c>--list</c> prints them.</summary>
    public static readonly IReadOnlyList<(string Name, string Description)> All = new[]
    {
        ("duel", "hero (longsword, fireball) against one goblin with a club, 1.5 m apart"),
        ("goblin-ambush", "hero against two club goblins at 1.5 m and a goblin archer with poison arrows at 12 m"),
        ("goblin-chief", "hero against the goblin chief at 1.5 m and a goblin archer at 12 m"),
    };

    /// <summary>Builds fight number <paramref name="number"/> of a scenario.</summary>
    /// <param name="name">Scenario name from <see cref="All"/>.</param>
    /// <param name="content">Combat content; must hold the hero's attacks and <c>creature.goblin</c> with three tiers.</param>
    /// <param name="seed">Root seed.</param>
    /// <param name="number">Encounter number.</param>
    /// <returns>The encounter, or null for an unknown name.</returns>
    public static CombatEncounter? Build(string name, CombatContent content, ulong seed, long number)
    {
        var fight = new CombatEncounter(seed, number, content.Rules);
        switch (name)
        {
            case "duel":
                fight.Add(Hero(content), NearestEnemyBrain.Instance);
                AddGoblin(fight, content, GoblinTier, 2, new Point2D(1.5, 0));
                return fight;
            case "goblin-ambush":
                fight.Add(Hero(content), NearestEnemyBrain.Instance);
                AddGoblin(fight, content, GoblinTier, 2, new Point2D(1.5, 0));
                AddGoblin(fight, content, GoblinTier, 3, new Point2D(-1.5, 0));
                AddGoblin(fight, content, ArcherTier, 4, new Point2D(0, 12));
                return fight;
            case "goblin-chief":
                fight.Add(Hero(content), NearestEnemyBrain.Instance);
                AddGoblin(fight, content, ChiefTier, 2, new Point2D(1.5, 0));
                AddGoblin(fight, content, ArcherTier, 3, new Point2D(0, 12));
                return fight;
            default:
                return null;
        }
    }

    private static void AddGoblin(CombatEncounter fight, CombatContent content, int tier, int id, Point2D position) =>
        fight.Add(content.CreateCreature(Goblin, tier, id, MonsterTeam, position), content.BrainFor(Goblin));

    private static Combatant Hero(CombatContent content) => new(
        1,
        "hero",
        HeroTeam,
        new Point2D(0, 0),
        60,
        3,
        4,
        new[] { content.Attack(ContentId.Parse("attack.longsword")), content.Attack(ContentId.Parse("attack.fireball")) },
        10,
        new Dictionary<string, int>(StringComparer.Ordinal) { [DamageTypes.Poison] = 25 });
}
