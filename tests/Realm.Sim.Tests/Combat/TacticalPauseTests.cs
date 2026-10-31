using System.Linq;
using Realm.Sim.Combat;
using static Realm.Sim.Tests.Combat.CombatFixtures;

namespace Realm.Sim.Tests.Combat;

public class TacticalPauseTests
{
    private const int PlayerId = 2;
    private const int EnemyId = 1;
    private static readonly CombatRules NoRegen = CombatRules.Default with { ActionPointRegenTicks = 1_000_000 };

    // The player (id 2) only acts through the pause; the enemy (id 1) is a punching bag unless given an attack.
    private static (CombatEncounter Fight, Combatant Player, Combatant Enemy) Setup(int maxAp = 6, Attack? enemyAttack = null, CombatRules? rules = null)
    {
        var fight = new CombatEncounter(Seed, 1, rules ?? NoRegen);
        Combatant enemy = At(EnemyId, 1, 2, health: 1000, attack: enemyAttack);
        Combatant player = At(PlayerId, 0, 0, health: 1000, attack: SureHit(1, recoveryTicks: 100, apCost: 2), maxAp: maxAp);
        fight.Add(enemy, enemyAttack is null ? new HeldIntentBrain() : NearestEnemyBrain.Instance);
        fight.Add(player, new HeldIntentBrain());
        return (fight, player, enemy);
    }

    private static int[] AttackTicks(CombatEncounter fight, int actorId) =>
        fight.Events.Where(e => e.Kind == CombatEventKind.Attacked && e.ActorId == actorId).Select(e => (int)e.Tick).ToArray();

    [Fact]
    public void Pause_StopsTheClock()
    {
        (CombatEncounter fight, _, _) = Setup(enemyAttack: SureHit(1, recoveryTicks: 1));
        fight.Run(10);
        int events = fight.Events.Count;

        Assert.True(fight.Pause(PlayerId));
        for (int i = 0; i < 50; i++)
        {
            fight.Step();
        }

        Assert.Equal(10, fight.Tick);
        Assert.Equal(CombatPhase.Paused, fight.Phase);
        Assert.Equal(events + 1, fight.Events.Count); // only the Paused event
    }

    [Fact]
    public void Queue_SpendsActionPoints_AndCancelRefundsThem()
    {
        (CombatEncounter fight, Combatant player, _) = Setup(maxAp: 5);
        fight.Pause(PlayerId);

        Assert.True(fight.QueueAction(new CombatAction(0, EnemyId)));
        Assert.True(fight.QueueAction(new CombatAction(0, EnemyId)));
        Assert.False(fight.QueueAction(new CombatAction(0, EnemyId))); // 1 AP left, costs 2
        Assert.False(fight.QueueAction(new CombatAction(0, 99))); // no such target
        Assert.Equal(1, player.ActionPoints);

        Assert.True(fight.CancelPause());

        Assert.Equal(5, player.ActionPoints);
        Assert.Empty(fight.QueuedActions);
        Assert.Equal(CombatPhase.Running, fight.Phase);
    }

    [Fact]
    public void Confirm_ResolvesTheQueue_FasterThanRecovery_ThenRecoveryApplies()
    {
        (CombatEncounter fight, Combatant player, _) = Setup();
        fight.Pause(PlayerId);
        for (int i = 0; i < 3; i++)
        {
            fight.QueueAction(new CombatAction(0, EnemyId));
        }

        Assert.True(fight.Confirm());
        fight.Run(20);

        Assert.Equal(new[] { 0, 4, 8 }, AttackTicks(fight, PlayerId)); // recovery is 100, the gap is 4
        Assert.Contains(fight.Events, e => e.Kind == CombatEventKind.QueueFinished && e.Tick == 8);
        Assert.Equal(108, player.ReadyAt);
        Assert.Equal(CombatPhase.Running, fight.Phase);
        Assert.Null(fight.PausedBy);
    }

    [Fact]
    public void WhileTheQueueResolves_EnemiesKeepActing()
    {
        (CombatEncounter fight, _, _) = Setup(enemyAttack: SureHit(1, recoveryTicks: 1));
        fight.Pause(PlayerId);
        for (int i = 0; i < 3; i++)
        {
            fight.QueueAction(new CombatAction(0, EnemyId));
        }

        fight.Confirm();
        fight.Run(9);

        Assert.Equal(Enumerable.Range(0, 9).ToArray(), AttackTicks(fight, EnemyId));
    }

    [Fact]
    public void FallingAsleep_InterruptsTheQueue()
    {
        Attack lullaby = SureHit(0, recoveryTicks: 1000) with { Applies = new ConditionApplication(Asleep, 100) };
        (CombatEncounter fight, Combatant player, _) = Setup(enemyAttack: lullaby);
        fight.Pause(PlayerId);
        for (int i = 0; i < 3; i++)
        {
            fight.QueueAction(new CombatAction(0, EnemyId));
        }

        fight.Confirm();
        fight.Run(10);

        Assert.Equal(new[] { 0 }, AttackTicks(fight, PlayerId)); // the queue runs before the enemy acts at tick 0
        Assert.True(player.HasCondition(Asleep.Id));
        Assert.Contains(fight.Events, e => e.Kind == CombatEventKind.QueueInterrupted && e.Amount == 2);
        Assert.Equal(CombatPhase.Running, fight.Phase);
        Assert.Equal(0, player.ActionPoints); // points spent on dropped actions are lost
    }

    [Fact]
    public void QueuedActionOnADeadTarget_IsSkippedAndRefunded()
    {
        var fight = new CombatEncounter(Seed, 1, NoRegen);
        fight.Add(At(EnemyId, 1, 2, health: 5), new HeldIntentBrain());
        fight.Add(At(3, 1, 8, health: 1000), new HeldIntentBrain());
        Combatant player = At(PlayerId, 0, 0, attack: SureHit(10, recoveryTicks: 100, apCost: 2), maxAp: 6);
        fight.Add(player, new HeldIntentBrain());
        fight.Pause(PlayerId);
        fight.QueueAction(new CombatAction(0, EnemyId));
        fight.QueueAction(new CombatAction(0, EnemyId));

        fight.Confirm();
        fight.Run(10);

        Assert.Single(AttackTicks(fight, PlayerId));
        Assert.Equal(4, player.ActionPoints);
    }

    [Fact]
    public void Pause_IsRefused_WithoutAnActionPointPool_OrTwice_OrOnceOver()
    {
        (CombatEncounter fight, _, _) = Setup(enemyAttack: SureHit(5000));

        Assert.False(fight.Pause(EnemyId)); // no AP pool
        Assert.False(fight.Pause(99));
        Assert.True(fight.Pause(PlayerId));
        Assert.False(fight.Pause(PlayerId));
        Assert.True(fight.Confirm()); // empty queue just resumes
        Assert.Equal(CombatPhase.Running, fight.Phase);

        fight.Step(); // the enemy kills the player
        Assert.Equal(CombatPhase.Over, fight.Phase);
        Assert.False(fight.Pause(PlayerId));
    }

    [Fact]
    public void ActionPoints_RegenerateOverTime()
    {
        (CombatEncounter fight, Combatant player, _) = Setup(maxAp: 6, rules: CombatRules.Default);
        fight.Pause(PlayerId);
        fight.QueueAction(new CombatAction(0, EnemyId));
        fight.QueueAction(new CombatAction(0, EnemyId));
        fight.Confirm();
        Assert.Equal(2, player.ActionPoints);

        fight.Run(40);

        Assert.Equal(4, player.ActionPoints); // one point per 20 ticks
        fight.Run(1000);
        Assert.Equal(6, player.ActionPoints); // never above the pool
    }
}
