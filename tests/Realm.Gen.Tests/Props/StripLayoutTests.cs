using System;
using System.Linq;
using Realm.Gen.Props;
using Xunit;

namespace Realm.Gen.Tests.Props;

public class StripLayoutTests
{
    private const ulong Seed = 1;

    [Fact]
    public void EverySegment_HasOneHouseAndThreeTrees()
    {
        for (int s = 0; s < 300; s++)
        {
            var layout = StripLayout.ForSegment(Seed, s);
            Assert.Equal(1, layout.Count(IsHouse));
            Assert.Equal(3, layout.Count(p => !IsHouse(p)));
        }
    }

    [Fact]
    public void Trees_AreInHeightRange_InSteps_AndStayInsideTheirSegment()
    {
        for (int s = 0; s < 300; s++)
        {
            foreach (StripPlacement p in StripLayout.ForSegment(Seed, s))
            {
                Assert.InRange(p.AlongMeters, 0f, 100f);
                if (IsHouse(p))
                {
                    Assert.Equal(0f, p.HeightMeters);
                    continue;
                }

                Assert.InRange(p.HeightMeters, 4f, 10f);
                Assert.Equal(0f, (p.HeightMeters * 2) % 1f); // multiples of 0.5 m
                Assert.InRange(Math.Abs(p.SideMeters), 5f, 9f);
            }
        }
    }

    [Fact]
    public void NothingOverlaps_AndHousesKeepTreesAway()
    {
        for (int s = 0; s < 300; s++)
        {
            var layout = StripLayout.ForSegment(Seed, s);
            var house = layout.Single(IsHouse);
            foreach (StripPlacement tree in layout.Where(p => !IsHouse(p)))
            {
                bool sameSide = Math.Sign(tree.SideMeters) == Math.Sign(house.SideMeters);
                Assert.False(sameSide && Math.Abs(tree.AlongMeters - house.AlongMeters) < 9f, $"segment {s}: tree too close to the house");
            }

            var trees = layout.Where(p => !IsHouse(p)).ToArray();
            for (int i = 0; i < trees.Length; i++)
            {
                for (int j = i + 1; j < trees.Length; j++)
                {
                    Assert.True(Math.Abs(trees[i].AlongMeters - trees[j].AlongMeters) >= 6f, $"segment {s}: two trees too close");
                }
            }
        }
    }

    [Fact]
    public void SameSeedAndSegment_SameLayout_AndSegmentsAreIndependent()
    {
        Assert.Equal(StripLayout.ForSegment(Seed, 42), StripLayout.ForSegment(Seed, 42));
        Assert.NotEqual(StripLayout.ForSegment(Seed, 42), StripLayout.ForSegment(Seed, 43));
        Assert.NotEqual(StripLayout.ForSegment(Seed, 42), StripLayout.ForSegment(Seed + 1, 42));
    }

    [Fact]
    public void BothHouseTypes_AndAllTreeKinds_AppearAcrossTheStrip()
    {
        var kinds = Enumerable.Range(0, 300).SelectMany(s => StripLayout.ForSegment(Seed, s)).Select(p => p.Kind).Distinct().ToHashSet();

        Assert.Equal(Enum.GetValues<StripPropKind>().Length, kinds.Count);
    }

    private static bool IsHouse(StripPlacement p) => p.Kind is StripPropKind.Cottage or StripPropKind.TownHouse;
}
