using System;
using System.Linq;
using Realm.Core.Geometry;
using Realm.Core.Random;
using Realm.Sim.Combat;
using static Realm.Sim.Tests.Combat.CombatFixtures;

namespace Realm.Sim.Tests.Combat;

public class MovementTests
{
    // 5 m/s at 20 ticks per second is 0.25 m a tick, exact in binary, so positions can be compared exactly.
    private const double Speed = 5;
    private static readonly Attack Club = new("attack.club", 20, 2, new Dice(1, 4)) { AttackBonus = 100 };

    private static int FirstAttackTick(CombatEncounter fight, int actorId) =>
        (int)fight.Events.First(e => e.Kind == CombatEventKind.Attacked && e.ActorId == actorId).Tick;

    [Fact]
    public void AMeleeCombatant_WalksIntoRange_ThenAttacks()
    {
        var fight = new CombatEncounter(Seed, 1);
        Combatant walker = At(1, 0, 10, health: 1000, attack: Club, speed: Speed);
        fight.Add(walker, NearestEnemyBrain.Instance);
        fight.Add(At(2, 1, 0, health: 1000), new HeldIntentBrain());

        fight.Run(40);

        // 0.25 m a tick: after 32 steps (ticks 0 to 31) it stands at exactly 2 m, inside the club's 2 m reach, so the
        // first swing comes at tick 32 and it never needs the shortened last step.
        Assert.Equal(32, FirstAttackTick(fight, 1));
        Assert.Equal(2.0, walker.Position.X);
        Assert.Equal(0, walker.Position.Z);
    }

    [Fact]
    public void ACombatantInRange_StandsStill_WhileRecovering()
    {
        var fight = new CombatEncounter(Seed, 1);
        Combatant fighter = At(1, 0, 1.5, health: 1000, attack: Club, speed: Speed);
        fight.Add(fighter, NearestEnemyBrain.Instance);
        fight.Add(At(2, 1, 0, health: 1000), new HeldIntentBrain());

        fight.Run(100);

        Assert.Equal(new Point2D(1.5, 0), fighter.Position);
    }

    [Fact]
    public void WithoutSpeed_NobodyMoves()
    {
        var fight = new CombatEncounter(Seed, 1);
        Combatant turret = At(1, 0, 10, attack: Club);
        fight.Add(turret, NearestEnemyBrain.Instance);
        fight.Add(At(2, 1, 0), new HeldIntentBrain());

        fight.Run(100);

        Assert.Equal(new Point2D(10, 0), turret.Position);
    }

    [Fact]
    public void Walkers_KeepTheirDistance_AndGoAroundEachOther()
    {
        var fight = new CombatEncounter(Seed, 1);
        fight.Add(At(1, 1, 0, health: 100_000), new HeldIntentBrain());
        for (int id = 2; id <= 5; id++)
        {
            // Four attackers in a line behind each other, all heading for the same target.
            fight.Add(At(id, 0, 3 + (id * 0.9), health: 1000, attack: Club, speed: Speed), NearestEnemyBrain.Instance);
        }

        double closest = double.MaxValue;
        for (int t = 0; t < 200; t++)
        {
            fight.Step();
            for (int a = 0; a < fight.Combatants.Count; a++)
            {
                for (int b = a + 1; b < fight.Combatants.Count; b++)
                {
                    closest = Math.Min(closest, fight.Combatants[a].DistanceTo(fight.Combatants[b]));
                }
            }
        }

        Assert.True(closest >= fight.Rules.MinSeparationMeters - 1e-9, $"closest pair was {closest:0.000} m");
        Assert.True(fight.Events.Count(e => e.Kind == CombatEventKind.Attacked) > 0);
        Assert.True(fight.Combatants.Skip(1).Count(c => c.DistanceTo(fight.Combatants[0]) <= Club.RangeMeters) >= 2, "at least two found a way round into reach");
    }

    [Fact]
    public void TheHeldIntentBrain_WalksThePlayer_ToItsMoveTarget()
    {
        var fight = new CombatEncounter(Seed, 1);
        var input = new HeldIntentBrain { MoveTo = new Point2D(0, 5) };
        Combatant player = At(1, 0, 0, attack: Club, speed: Speed);
        fight.Add(player, input);
        fight.Add(At(2, 1, 50), new HeldIntentBrain());

        fight.Run(10);
        Assert.Equal(new Point2D(0, 2.5), player.Position);

        fight.Run(20);
        Assert.Equal(new Point2D(0, 5), player.Position); // arrives and stops
    }

    [Fact]
    public void SameSeed_GivesTheSamePositions()
    {
        static Point2D[] Run()
        {
            var fight = new CombatEncounter(Seed, 3);
            var sword = new Attack("attack.sword", 20, 2, new Dice(1, 8));
            fight.Add(new Combatant(1, "a", 0, new Point2D(0, 0), 40, 2, 2, new[] { sword }, speedMetersPerSecond: 4.3), NearestEnemyBrain.Instance);
            fight.Add(new Combatant(2, "b", 1, new Point2D(9.1, 3.7), 40, 2, 2, new[] { sword }, speedMetersPerSecond: 3.1), NearestEnemyBrain.Instance);
            fight.Add(new Combatant(3, "c", 1, new Point2D(-7.3, 5.2), 40, 2, 2, new[] { sword }, speedMetersPerSecond: 3.9), NearestEnemyBrain.Instance);
            fight.Run(10_000);
            return fight.Combatants.Select(c => c.Position).ToArray();
        }

        Assert.Equal(Run(), Run());
    }

    [Fact]
    public void WhilePausedOrResolving_ThePlayerStands()
    {
        var fight = new CombatEncounter(Seed, 1);
        Combatant player = At(2, 0, 0, attack: SureHit(1, recoveryTicks: 100, apCost: 1), maxAp: 6, speed: Speed);
        fight.Add(At(1, 1, 5, health: 1000), new HeldIntentBrain());
        fight.Add(player, new HeldIntentBrain { MoveTo = new Point2D(0, 50) });

        fight.Pause(2);
        fight.QueueAction(new CombatAction(0, 1));
        fight.QueueAction(new CombatAction(0, 1));
        fight.Step();
        Assert.Equal(new Point2D(0, 0), player.Position); // paused

        fight.Confirm();
        fight.Run(5); // queued actions at ticks 0 and 4; no walking in either, nor between
        Assert.Equal(new Point2D(0, 0), player.Position);

        fight.Run(1); // real time again from tick 5
        Assert.Equal(new Point2D(0, 0.25), player.Position);
    }

    [Fact]
    public void WhileAsleep_NobodyWalks()
    {
        Attack lullaby = SureHit(0, recoveryTicks: 1000) with { Applies = new ConditionApplication(Asleep, 100) };
        var fight = new CombatEncounter(Seed, 1);
        fight.Add(At(1, 1, 5, health: 1000, attack: lullaby), NearestEnemyBrain.Instance);
        Combatant player = At(2, 0, 0, speed: Speed);
        fight.Add(player, new HeldIntentBrain { MoveTo = new Point2D(0, 50) });

        fight.Run(20);

        Assert.True(player.HasCondition(Asleep.Id));
        Assert.Equal(new Point2D(0, 0), player.Position); // put to sleep at tick 0, before its own turn
    }
}
