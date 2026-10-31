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
    public void Dice_RollStaysInRange_AndIsDeterministic()
    {
        var dice = new Dice(2, 6, 1);
        var a = new Rng(7);
        var b = new Rng(7);
        for (int i = 0; i < 1000; i++)
        {
            int roll = dice.Roll(a);
            Assert.InRange(roll, 3, 13);
            Assert.Equal(roll, dice.Roll(b));
        }

        Assert.Equal(8.0, dice.Average);
        Assert.Equal("2d6+1", dice.ToString());
        Assert.Equal("1d4-2", new Dice(1, 4, -2).ToString());
    }

    [Fact]
    public void Dice_NeverRollsBelowZero_AndRejectsMalformed()
    {
        Assert.Equal(0, new Dice(1, 4, -10).Roll(new Rng(1)));
        Assert.Throws<InvalidOperationException>(() => new Dice(1, 0).Roll(new Rng(1)));
        Assert.Throws<InvalidOperationException>(() => new Dice(-1, 6).Roll(new Rng(1)));
    }

    [Fact]
    public void Definitions_RejectBadValues()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AttackDefinition("attack.x", 0, 1, new Dice(1, 6)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AttackDefinition("attack.x", 1, 0, new Dice(1, 6)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ConditionDefinition("condition.x", 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ConditionApplication(CombatFixtures.Asleep, 101));
        Assert.Throws<ArgumentException>(() => CombatFixtures.At(1, 0, 0, resistances: new System.Collections.Generic.Dictionary<string, int> { ["fire"] = 150 }));
    }
}
