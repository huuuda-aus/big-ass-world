using System;
using Realm.Core.Random;

namespace Realm.Core.Tests;

public class DiceTests
{
    [Fact]
    public void Roll_StaysInRange_AndIsDeterministic()
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
    }

    [Fact]
    public void Roll_NeverGoesBelowZero_AndRejectsMalformed()
    {
        Assert.Equal(0, new Dice(1, 4, -10).Roll(new Rng(1)));
        Assert.Equal(5, new Dice(0, 0, 5).Roll(new Rng(1)));
        Assert.Throws<InvalidOperationException>(() => new Dice(1, 0).Roll(new Rng(1)));
        Assert.Throws<InvalidOperationException>(() => new Dice(-1, 6).Roll(new Rng(1)));
    }

    [Theory]
    [InlineData("1d6", 1, 6, 0)]
    [InlineData("2d6+1", 2, 6, 1)]
    [InlineData("1d4-2", 1, 4, -2)]
    [InlineData("3d8+0", 3, 8, 0)]
    [InlineData("5", 0, 0, 5)]
    [InlineData("0", 0, 0, 0)]
    [InlineData("100d1000+10000", 100, 1000, 10000)]
    public void TryParse_AcceptsValidExpressions(string text, int count, int sides, int bonus)
    {
        Assert.True(Dice.TryParse(text, out Dice dice));
        Assert.Equal(new Dice(count, sides, bonus), dice);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("d6")]
    [InlineData("0d6")]
    [InlineData("1d0")]
    [InlineData("1d")]
    [InlineData("2d6+")]
    [InlineData("2d6+1+1")]
    [InlineData("2 d6")]
    [InlineData("2D6")]
    [InlineData("-1d6")]
    [InlineData("101d6")]
    [InlineData("1d1001")]
    [InlineData("1d6+10001")]
    [InlineData("abc")]
    [InlineData("1.5")]
    public void TryParse_RejectsInvalidExpressions(string? text)
    {
        Assert.False(Dice.TryParse(text, out _));
    }

    [Theory]
    [InlineData("2d6+1")]
    [InlineData("1d4-2")]
    [InlineData("1d8")]
    [InlineData("7")]
    [InlineData("-3")]
    public void ToString_RoundTripsThroughTryParse(string text)
    {
        Assert.True(Dice.TryParse(text, out Dice dice));
        Assert.Equal(text, dice.ToString());
    }
}
