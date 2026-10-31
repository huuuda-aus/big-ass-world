using System;
using System.IO;
using System.Linq;
using Realm.Core.Geometry;
using Realm.Core.Ids;
using Realm.Core.Random;
using Realm.Core.Validation;
using Realm.Data.Content;
using Realm.Sim.Combat;

namespace Realm.Sim.Tests.Combat;

/// <summary>Combat built from the shipped content/ directory.</summary>
public class CombatContentTests
{
    private static readonly Lazy<CombatContent> Shipped = new(() =>
    {
        var report = new ValidationReport();
        ContentDatabase db = ContentLoader.Load(Path.Combine(RepoRoot(), "content"), report);
        Assert.False(report.HasErrors, string.Join(Environment.NewLine, report.Issues));
        return new CombatContent(db);
    });

    private static ContentId Id(string text) => ContentId.Parse(text);

    [Theory]
    [InlineData(1.2, 24)]
    [InlineData(1.3, 26)]
    [InlineData(0.025, 1)] // half a tick rounds away from zero
    [InlineData(0.001, 1)] // never below one tick
    [InlineData(10, 200)]
    public void Seconds_BecomeWholeTicks(double seconds, int ticks)
    {
        Assert.Equal(ticks, Shipped.Value.ToTicks(seconds));
    }

    [Fact]
    public void Attacks_AreConverted_WithTheirConditions()
    {
        Attack bow = Shipped.Value.Attack(Id("attack.goblin_bow"));

        Assert.Equal("attack.goblin_bow", bow.Id);
        Assert.Equal(30, bow.RecoveryTicks);
        Assert.Equal(25, bow.RangeMeters);
        Assert.Equal(new Dice(1, 6), bow.Damage);
        Assert.Equal(1, bow.AttackBonus);
        Assert.Equal(20, bow.Applies!.ChancePercent);

        Condition poisoned = bow.Applies.Condition;
        Assert.Same(Shipped.Value.Condition(Id("condition.poisoned")), poisoned);
        Assert.Equal((200, 20, 1, "poison"), (poisoned.DurationTicks, poisoned.PulseIntervalTicks, poisoned.PulseDamage, poisoned.PulseDamageType));

        Attack fireball = Shipped.Value.Attack(Id("attack.fireball"));
        Assert.Equal(("fire", 3.0, 5), (fireball.DamageType, fireball.AreaRadiusMeters, fireball.ActionPointCost));
    }

    [Fact]
    public void Creatures_AreBuiltPerTier()
    {
        Combatant archer = Shipped.Value.CreateCreature(Id("creature.goblin"), 2, 7, 1, new Point2D(3, 4));

        Assert.Equal((7, "Goblin Archer", 1, 10), (archer.Id, archer.Name, archer.Team, archer.MaxHealth));
        Assert.Equal(new Point2D(3, 4), archer.Position);
        Assert.Equal(new[] { "attack.goblin_bow" }, archer.Attacks.Select(a => a.Id).ToArray());
        Assert.Equal(0, archer.MaxActionPoints);

        Combatant chief = Shipped.Value.CreateCreature(Id("creature.goblin"), 3, 8, 1, new Point2D(0, 0));
        Assert.Equal(50, chief.ResistancePercent(DamageTypes.Poison));
        Assert.IsType<NearestEnemyBrain>(Shipped.Value.BrainFor(Id("creature.goblin")));
    }

    [Fact]
    public void UnknownIds_AndTiers_Throw()
    {
        Assert.Throws<System.Collections.Generic.KeyNotFoundException>(() => Shipped.Value.Attack(Id("attack.nope")));
        Assert.Throws<System.Collections.Generic.KeyNotFoundException>(() => Shipped.Value.CreateCreature(Id("creature.nope"), 1, 1, 1, default));
        Assert.Throws<ArgumentOutOfRangeException>(() => Shipped.Value.CreateCreature(Id("creature.goblin"), 4, 1, 1, default));
    }

    [Fact]
    public void EveryBehaviourWord_HasABrain()
    {
        Assert.All(CombatVocabulary.Behaviours, b => Assert.NotNull(CombatBrains.ForBehaviour(b)));
        Assert.Throws<ArgumentException>(() => CombatBrains.ForBehaviour("berserk"));
    }

    [Fact]
    public void ShippedCreatures_FightEachOther()
    {
        CombatContent content = Shipped.Value;
        var fight = new CombatEncounter(1, 1, content.Rules);
        fight.Add(content.CreateCreature(Id("creature.goblin"), 3, 1, 0, new Point2D(0, 0)), content.BrainFor(Id("creature.goblin")));
        fight.Add(content.CreateCreature(Id("creature.goblin"), 1, 2, 1, new Point2D(1.5, 0)), content.BrainFor(Id("creature.goblin")));

        fight.Run(10_000);

        Assert.Equal(CombatPhase.Over, fight.Phase);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Realm.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Realm.sln not found above the test directory.");
    }
}
