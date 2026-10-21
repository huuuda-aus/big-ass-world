using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Realm.Core.Random;
using Realm.Gen.Settlements.Placement;
using Xunit;

namespace Realm.Gen.Tests.Settlements;

public class SettlementNamerTests
{
    private static readonly string[] CityEndings = { "haven", "burg", "minster", "gate", "helm", "ford", "ton", "mere", "crest", "hold" };
    private static readonly string[] TownEndings = { "by", "wick", "stead", "ham", "dale", "field", "worth", "ley", "combe", "marsh" };

    [Fact]
    public void Names_AreCapitalisedLetters_AtLeastFiveLong_AndUnique()
    {
        var rng = Rng.ForKey(1, "names");
        var used = new HashSet<string>();

        var names = Enumerable.Range(0, 40).Select(_ => SettlementNamer.Generate(rng, SettlementTier.City, used)).ToList();

        Assert.Equal(40, names.Distinct().Count());
        Assert.All(names, n => Assert.Matches(new Regex("^[A-Z][a-z]{4,}$"), n));
        Assert.Equal(names.Count, used.Count);
    }

    [Theory]
    [InlineData(SettlementTier.City)]
    [InlineData(SettlementTier.Town)]
    public void Endings_FollowTheTier(SettlementTier tier)
    {
        var rng = Rng.ForKey(2, "names");
        string[] endings = tier == SettlementTier.City ? CityEndings : TownEndings;
        string[] other = tier == SettlementTier.City ? TownEndings : CityEndings;

        foreach (string name in Enumerable.Range(0, 30).Select(_ => SettlementNamer.Generate(rng, tier, new HashSet<string>())))
        {
            Assert.True(endings.Any(e => name.EndsWith(e, StringComparison.Ordinal)), $"{name} should end like a {tier}");
            Assert.False(other.Any(e => name.EndsWith(e, StringComparison.Ordinal) && !endings.Any(m => name.EndsWith(m, StringComparison.Ordinal))), $"{name} ends like the other tier");
        }
    }

    [Fact]
    public void SameSeed_SameNames_DifferentSeed_Different()
    {
        List<string> Make(ulong seed)
        {
            var rng = Rng.ForKey(seed, "names");
            var used = new HashSet<string>();
            return Enumerable.Range(0, 8).Select(_ => SettlementNamer.Generate(rng, SettlementTier.Town, used)).ToList();
        }

        Assert.Equal(Make(5), Make(5));
        Assert.NotEqual(Make(5), Make(6));
    }

    [Fact]
    public void WhenEveryNameIsTaken_ANumberIsAppended()
    {
        var used = new HashSet<string>();
        var stems = new[] { "Ald", "Ash", "Black", "Bright", "Cold", "Crow", "Dun", "East", "Elm", "Fair", "Frost", "Gold", "Grey", "Hal", "High", "Iron", "King", "Lang", "Mar", "Mist", "Nor", "Oak", "Raven", "Red", "Salt", "Silver", "Stone", "Storm", "Thorn", "West", "White", "Wolf", "Brook", "Mill", "Clay", "Reed", "Amber", "Cinder", "Hollow", "Marble", "Briar" };
        foreach (string stem in stems)
        {
            foreach (string ending in CityEndings)
            {
                used.Add(stem[^1] == ending[0] ? stem + ending[1..] : stem + ending);
            }
        }

        string name = SettlementNamer.Generate(Rng.ForKey(3, "names"), SettlementTier.City, used);

        Assert.Matches(new Regex("^[A-Z][a-z]+ [0-9]+$"), name);
    }
}
