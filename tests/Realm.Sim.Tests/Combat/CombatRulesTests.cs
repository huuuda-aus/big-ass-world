using System;
using Realm.Core.Random;
using Realm.Sim.Combat;

namespace Realm.Sim.Tests.Combat;

public class CombatRulesTests
{
    [Theory]
    [InlineData(5, 5, 75)]
    [InlineData(7, 5, 85)]
    [InlineData(3, 5, 65)]
    [InlineData(100, 0, 95)]
    [InlineData(0, 100, 5)]
    public void HitChance_IsLinearAndClamped(int attack, int defence, int expected)
    {
        Assert.Equal(expected, CombatRules.Default.HitChancePercent(attack, defence));
    }

    [Fact]
    public void Definitions_RejectBadValues()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Attack("attack.x", 0, 1, new Dice(1, 6)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Attack("attack.x", 1, 0, new Dice(1, 6)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Condition("condition.x", 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ConditionApplication(CombatFixtures.Asleep, 101));
        Assert.Throws<ArgumentException>(() => CombatFixtures.At(1, 0, 0, resistances: new System.Collections.Generic.Dictionary<string, int> { ["fire"] = 150 }));
    }
}
