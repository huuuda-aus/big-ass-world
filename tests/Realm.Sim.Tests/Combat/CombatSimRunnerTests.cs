using System;
using Realm.Core.Geometry;
using Realm.Sim.Combat;

namespace Realm.Sim.Tests.Combat;

public class CombatSimRunnerTests
{
    private static CombatEncounter Duel(long number, int heroHealth)
    {
        var sword = new AttackDefinition("attack.sword", 20, 2, new Dice(1, 8));
        var fight = new CombatEncounter(CombatFixtures.Seed, number);
        fight.Add(new Combatant(1, "hero", 0, new Point2D(0, 0), heroHealth, 2, 2, new[] { sword }), NearestEnemyBrain.Instance);
        fight.Add(new Combatant(2, "rival", 1, new Point2D(1, 0), 30, 2, 2, new[] { sword }), NearestEnemyBrain.Instance);
        return fight;
    }

    [Fact]
    public void Summary_IsDeterministic_AndAddsUp()
    {
        CombatSimSummary a = CombatSimRunner.Run(i => Duel(i, 30), 200, 10_000, 0);
        CombatSimSummary b = CombatSimRunner.Run(i => Duel(i, 30), 200, 10_000, 0);

        Assert.Equal(a, b);
        Assert.Equal(200, a.Wins + a.Losses + a.Timeouts);
        Assert.InRange(a.WinRate, 0.3, 0.8); // an even fight; the hero acts first, so it leans its way
        Assert.True(a.MedianTicks <= a.P90Ticks);
    }

    [Fact]
    public void LopsidedFight_IsAlwaysWon()
    {
        CombatSimSummary s = CombatSimRunner.Run(i => Duel(i, 1000), 50, 10_000, 0);

        Assert.Equal(1.0, s.WinRate);
        Assert.InRange(s.MeanHealthLeftPercent, 80, 100);
    }

    [Fact]
    public void TickLimit_CountsAsTimeout()
    {
        CombatSimSummary s = CombatSimRunner.Run(i => Duel(i, 1000), 10, 5, 0);

        Assert.Equal(10, s.Timeouts);
        Assert.Equal(5, s.MedianTicks);
        Assert.Throws<ArgumentOutOfRangeException>(() => CombatSimRunner.Run(i => Duel(i, 1), 0, 5, 0));
    }
}
