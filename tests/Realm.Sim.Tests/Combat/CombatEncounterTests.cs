using System.Collections.Generic;
using System.Linq;
using Realm.Core.Geometry;
using Realm.Core.Random;
using Realm.Sim.Combat;
using static Realm.Sim.Tests.Combat.CombatFixtures;

namespace Realm.Sim.Tests.Combat;

public class CombatEncounterTests
{
    private static int CountAttacks(CombatEncounter fight, int actorId) =>
        fight.Events.Count(e => e.Kind == CombatEventKind.Attacked && e.ActorId == actorId);

    private static CombatEncounter Duel(Attack attack, Combatant? target = null)
    {
        var fight = new CombatEncounter(Seed, 1);
        fight.Add(At(1, 0, 0, attack: attack), NearestEnemyBrain.Instance);
        fight.Add(target ?? At(2, 1, 2, health: 100_000), new HeldIntentBrain());
        return fight;
    }

    [Theory]
    [InlineData(20, 5)]
    [InlineData(10, 10)]
    [InlineData(35, 3)]
    public void Recovery_SetsHowOftenACombatantActs(int recoveryTicks, int expectedAttacks)
    {
        CombatEncounter fight = Duel(SureHit(1, recoveryTicks));

        fight.Run(100);

        Assert.Equal(expectedAttacks, CountAttacks(fight, 1));
        Assert.Equal(100, fight.Tick);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(50, 5)]
    [InlineData(100, 0)]
    public void Resistance_ReducesDamageOfItsType(int resistPercent, int expectedDamage)
    {
        Combatant target = At(2, 1, 2, health: 1000, resistances: new Dictionary<string, int> { [DamageTypes.Fire] = resistPercent });
        CombatEncounter fight = Duel(SureHit(10, type: DamageTypes.Fire), target);

        fight.Step();

        Assert.Equal(expectedDamage, fight.Events.Single(e => e.Kind == CombatEventKind.Damaged).Amount);
        Assert.Equal(1000 - expectedDamage, target.Health);
    }

    [Fact]
    public void AreaAttack_HitsEveryoneInside_IncludingTheCaster()
    {
        var fireball = new Attack("attack.fireball", 20, 20, new Dice(0, 1, 10)) { AreaRadiusMeters = 3, DamageType = DamageTypes.Fire };
        Combatant caster = At(1, 0, 0, attack: fireball);
        Combatant target = At(2, 1, 2);
        Combatant bystander = At(3, 2, 4);
        Combatant faraway = At(4, 1, 10);
        var fight = new CombatEncounter(Seed, 1);
        fight.Add(caster, new HeldIntentBrain { Intent = new CombatAction(0, 2) });
        fight.Add(target, new HeldIntentBrain());
        fight.Add(bystander, new HeldIntentBrain());
        fight.Add(faraway, new HeldIntentBrain());

        fight.Step();

        Assert.Equal(90, caster.Health);
        Assert.Equal(90, target.Health);
        Assert.Equal(90, bystander.Health);
        Assert.Equal(100, faraway.Health);
        Assert.DoesNotContain(fight.Events, e => e.Kind is CombatEventKind.Hit or CombatEventKind.Missed);
    }

    [Theory]
    [InlineData(1.5, 0)]
    [InlineData(10, 1)]
    public void NearestEnemyBrain_PicksTheStrongestAttack_ThatWillNotHitItself(double distance, int expectedAttack)
    {
        var club = new Attack("attack.club", 20, 2, new Dice(1, 6));
        var fireball = new Attack("attack.fireball", 20, 20, new Dice(3, 6)) { AreaRadiusMeters = 3 };
        var self = new Combatant(1, "hero", 0, new Point2D(0, 0), 50, 0, 0, new[] { club, fireball });
        var fight = new CombatEncounter(Seed, 1);
        fight.Add(self, NearestEnemyBrain.Instance);
        fight.Add(At(2, 1, distance), new HeldIntentBrain());

        CombatAction? action = NearestEnemyBrain.Instance.Decide(fight, self);

        Assert.Equal(new CombatAction(expectedAttack, 2), action);
    }

    [Fact]
    public void OutOfRange_TheBrainWaits()
    {
        var club = new Attack("attack.club", 20, 2, new Dice(1, 6));
        CombatEncounter fight = Duel(club, At(2, 1, 5));

        fight.Run(50);

        Assert.Equal(0, CountAttacks(fight, 1));
    }

    [Fact]
    public void Poison_PulsesUntilItWearsOff()
    {
        Attack sting = SureHit(0, recoveryTicks: 1000) with { Applies = new ConditionApplication(Poisoned, 100) };
        Combatant target = At(2, 1, 2);
        CombatEncounter fight = Duel(sting, target);

        fight.Run(150);

        // Applied at tick 0 for 100 ticks, 2 damage every 20 ticks: pulses at 20, 40, 60, 80 and 100.
        Assert.Equal(5, fight.Events.Count(e => e.Kind == CombatEventKind.Damaged && e.Detail == Poisoned.Id));
        Assert.Equal(90, target.Health);
        Assert.False(target.HasCondition(Poisoned.Id));
        Assert.Contains(fight.Events, e => e.Kind == CombatEventKind.ConditionEnded && e.Tick == 100 && e.Detail == Poisoned.Id);
    }

    [Fact]
    public void Asleep_StopsActing_UntilDamageWakesIt()
    {
        Attack lullaby = SureHit(0, recoveryTicks: 1000) with { Applies = new ConditionApplication(Asleep, 100) };
        var poke = new HeldIntentBrain();
        var fight = new CombatEncounter(Seed, 1);
        fight.Add(At(1, 0, 0, attack: lullaby), NearestEnemyBrain.Instance);
        fight.Add(At(2, 1, 2), new HeldIntentBrain { Intent = new CombatAction(0, 1) });
        fight.Add(At(3, 0, 4), poke);

        fight.Run(100);
        Assert.Equal(0, CountAttacks(fight, 2));
        Assert.True(fight.Find(2)!.HasCondition(Asleep.Id));

        poke.Intent = new CombatAction(0, 2);
        fight.Run(5);

        Assert.False(fight.Find(2)!.HasCondition(Asleep.Id));
        Assert.True(CountAttacks(fight, 2) > 0);
    }

    [Fact]
    public void LastTeamStanding_EndsTheFight()
    {
        Combatant target = At(2, 1, 2, health: 25);
        CombatEncounter fight = Duel(SureHit(10), target);

        long ran = fight.Run(1000);

        Assert.Equal(CombatPhase.Over, fight.Phase);
        Assert.Equal(0, fight.WinningTeam);
        Assert.False(target.IsAlive);
        Assert.Equal(41, ran); // hits at ticks 0, 20 and 40
        Assert.Contains(fight.Events, e => e.Kind == CombatEventKind.Died && e.TargetId == 2);
        Assert.Equal(CombatEventKind.Ended, fight.Events[^1].Kind);
    }

    [Fact]
    public void SameSeedAndNumber_GiveTheSameFight_AndAnotherNumberDiffers()
    {
        static CombatEncounter Brawl(long number)
        {
            var sword = new Attack("attack.sword", 20, 2, new Dice(1, 8, 1));
            var fight = new CombatEncounter(Seed, number);
            fight.Add(new Combatant(1, "a", 0, new Point2D(0, 0), 40, 2, 3, new[] { sword }), NearestEnemyBrain.Instance);
            fight.Add(new Combatant(2, "b", 1, new Point2D(1, 0), 40, 2, 3, new[] { sword }), NearestEnemyBrain.Instance);
            fight.Run(10_000);
            return fight;
        }

        Assert.Equal(Brawl(1).Events, Brawl(1).Events);
        Assert.NotEqual(Brawl(1).Events, Brawl(2).Events);
    }
}
