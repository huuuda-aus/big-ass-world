using System.Collections.Generic;
using Realm.Core.Geometry;
using Realm.Core.Random;
using Realm.Sim.Combat;

namespace Realm.Sim.Tests.Combat;

internal static class CombatFixtures
{
    public const ulong Seed = 42;

    public static readonly Condition Poisoned = new("condition.poisoned", 100)
    {
        PulseIntervalTicks = 20,
        PulseDamage = 2,
        PulseDamageType = DamageTypes.Poison,
    };

    public static readonly Condition Asleep = new("condition.asleep", 200)
    {
        PreventsActions = true,
        BreaksOnDamage = true,
    };

    /// <summary>Never misses (an area attack needs no hit roll) and only reaches the target when the user stands more than 0.5 m away.</summary>
    public static Attack SureHit(int damage, int recoveryTicks = 20, int apCost = 2, string type = DamageTypes.Physical) =>
        new("attack.sure", recoveryTicks, 10, new Dice(0, 1, damage)) { AreaRadiusMeters = 0.5, ActionPointCost = apCost, DamageType = type };

    public static Combatant At(
        int id,
        int team,
        double x,
        int health = 100,
        Attack? attack = null,
        int maxAp = 0,
        IReadOnlyDictionary<string, int>? resistances = null,
        double speed = 0) =>
        new(id, $"c{id}", team, new Point2D(x, 0), health, 0, 0, new[] { attack ?? SureHit(1) }, maxAp, resistances, speed);
}
