using System;
using Realm.Core.Random;
using Xunit;

namespace Realm.Core.Tests;

public class RngTests
{
    [Fact]
    public void Pcg32_MatchesReferenceVector()
    {
        // Reference output of the canonical pcg32 demo: seed 42, stream 54.
        var rng = new Rng(42, 54);
        uint[] expected = { 0xa15c02b7, 0x7b47f409, 0xba1d3330, 0x83d2f293, 0xbfa4784b, 0xcbed606e };

        foreach (uint e in expected)
        {
            Assert.Equal(e, rng.NextUInt32());
        }
    }

    [Fact]
    public void SameSeed_GivesSameSequence()
    {
        var a = new Rng(123);
        var b = new Rng(123);

        for (int i = 0; i < 100; i++)
        {
            Assert.Equal(a.NextUInt64(), b.NextUInt64());
        }
    }

    [Fact]
    public void DifferentStreams_GiveDifferentSequences()
    {
        Assert.NotEqual(new Rng(1, 1).NextUInt64(), new Rng(1, 2).NextUInt64());
    }

    [Fact]
    public void ForKey_IsStableGolden()
    {
        // Golden: changing ForKey reshuffles every generator. If this fails on purpose,
        // bump generator versions and regenerate goldens (CLAUDE.md 7.4).
        Rng rng = Rng.ForKey(42, "dungeon", 7, "sector", 3);
        Assert.Equal(GoldenForKeyFirstValue, rng.NextUInt64());
    }

    [Fact]
    public void ForKey_SameInputs_SameSequence()
    {
        Assert.Equal(
            Rng.ForKey(9, "a", 1).NextUInt64(),
            Rng.ForKey(9, "a", 1).NextUInt64());
    }

    [Fact]
    public void ForKey_OrderMatters()
    {
        Assert.NotEqual(
            Rng.ForKey(9, "a", "b").NextUInt64(),
            Rng.ForKey(9, "b", "a").NextUInt64());
    }

    [Fact]
    public void ForKey_StringAndIntegerDoNotCollide()
    {
        Assert.NotEqual(
            Rng.ForKey(9, "3").NextUInt64(),
            Rng.ForKey(9, 3).NextUInt64());
    }

    [Fact]
    public void ForKey_IntegerTypesAgree()
    {
        ulong asInt = Rng.ForKey(9, 5).NextUInt64();
        Assert.Equal(asInt, Rng.ForKey(9, 5L).NextUInt64());
        Assert.Equal(asInt, Rng.ForKey(9, 5U).NextUInt64());
        Assert.Equal(asInt, Rng.ForKey(9, 5UL).NextUInt64());
    }

    [Fact]
    public void ForKey_RejectsUnsupportedAndNullKeys()
    {
        Assert.Throws<ArgumentException>(() => Rng.ForKey(1, 1.5));
        Assert.Throws<ArgumentNullException>(() => Rng.ForKey(1, new object[] { null! }));
    }

    [Fact]
    public void NextInt_StaysInRange()
    {
        var rng = new Rng(5);
        for (int i = 0; i < 10_000; i++)
        {
            int v = rng.NextInt(-3, 4);
            Assert.InRange(v, -3, 3);
        }
    }

    [Fact]
    public void NextInt_CoversWholeRange()
    {
        var rng = new Rng(6);
        var seen = new bool[6];
        for (int i = 0; i < 1_000; i++)
        {
            seen[rng.NextInt(6)] = true;
        }

        Assert.All(seen, Assert.True);
    }

    [Fact]
    public void NextInt_FullIntRangeDoesNotOverflow()
    {
        var rng = new Rng(7);
        for (int i = 0; i < 1_000; i++)
        {
            int v = rng.NextInt(int.MinValue, int.MaxValue);
            Assert.InRange(v, int.MinValue, int.MaxValue - 1);
        }
    }

    [Fact]
    public void NextInt_RejectsBadBounds()
    {
        var rng = new Rng(1);
        Assert.Throws<ArgumentOutOfRangeException>(() => rng.NextInt(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => rng.NextInt(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => rng.NextInt(5, 5));
    }

    [Fact]
    public void NextDouble_IsInUnitInterval()
    {
        var rng = new Rng(8);
        for (int i = 0; i < 10_000; i++)
        {
            double d = rng.NextDouble();
            Assert.True(d >= 0.0 && d < 1.0);
        }
    }

    [Fact]
    public void NextBool_ProducesBothValues()
    {
        var rng = new Rng(10);
        int trues = 0;
        for (int i = 0; i < 1_000; i++)
        {
            if (rng.NextBool())
            {
                trues++;
            }
        }

        Assert.InRange(trues, 400, 600);
    }

    // Captured from the first accepted implementation; see ForKey_IsStableGolden.
    private const ulong GoldenForKeyFirstValue = 10202556452584807035UL;
}
