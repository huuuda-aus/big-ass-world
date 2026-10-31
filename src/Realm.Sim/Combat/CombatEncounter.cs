using System;
using System.Collections.Generic;
using Realm.Core.Geometry;
using Realm.Core.Random;

namespace Realm.Sim.Combat;

/// <summary>
/// One fight on one timeline (ADR 0029): every combatant acts when its ready time comes up and walks in between,
/// attacks resolve with one hit roll and a damage roll, conditions pulse and expire, and one combatant may stop the clock
/// for a tactical pause and queue actions paid with action points.
/// </summary>
/// <remarks>
/// Each <see cref="Step"/> is one sim tick, in this order: conditions (pulses, then expiry), action-point regeneration,
/// the pause queue (when resolving), then every living, unprevented combatant in <see cref="Combatant.Id"/> order: its
/// brain decides, it acts if ready and the action is valid, and otherwise walks one tick towards where the brain wants
/// (keeping <see cref="CombatRules.MinSeparationMeters"/> from others). Then the end check.
/// Determinism: every roll comes from <c>Rng.ForKey(seed, "combat", encounterNumber, tick, actorId)</c>, one generator
/// per action, and combatants are processed in ID order; the same seed, number, combatants and brain decisions give the
/// same <see cref="Events"/> and positions. Integer arithmetic for rolls and time; positions use only +, -, *, / and square
/// roots, which IEEE 754 makes identical on every machine.
/// Thread-safety: not thread-safe; owned by the simulation thread.
/// </remarks>
public sealed class CombatEncounter
{
    private const string RngScope = "combat";

    private readonly List<Combatant> _combatants = new();
    private readonly Dictionary<int, ICombatBrain> _brains = new();
    private readonly List<CombatEvent> _events = new();
    private readonly List<CombatAction> _queue = new();
    private readonly List<Combatant> _victims = new();
    private readonly List<(Condition Condition, int Amount)> _pulses = new();
    private Combatant? _pausedBy;
    private long _nextQueuedAt;
    private int _lastQueuedRecovery;

    /// <summary>Creates an empty encounter at tick 0.</summary>
    /// <param name="seed">Root seed (the world seed in the game).</param>
    /// <param name="encounterNumber">Distinguishes encounters under one seed; part of every roll's key.</param>
    /// <param name="rules">Tuning values; <see cref="CombatRules.Default"/> when null.</param>
    public CombatEncounter(ulong seed, long encounterNumber, CombatRules? rules = null)
    {
        Seed = seed;
        EncounterNumber = encounterNumber;
        Rules = rules ?? CombatRules.Default;
    }

    /// <summary>Root seed.</summary>
    public ulong Seed { get; }

    /// <summary>Encounter number under <see cref="Seed"/>.</summary>
    public long EncounterNumber { get; }

    /// <summary>Tuning values.</summary>
    public CombatRules Rules { get; }

    /// <summary>Ticks run so far; the clock does not move while <see cref="Phase"/> is <see cref="CombatPhase.Paused"/>.</summary>
    public long Tick { get; private set; }

    /// <summary>Real time, paused, resolving a queue, or over.</summary>
    public CombatPhase Phase { get; private set; }

    /// <summary>The last team standing once the fight is over; null while it runs or when nobody survived.</summary>
    public int? WinningTeam { get; private set; }

    /// <summary>Everyone in the fight, ordered by <see cref="Combatant.Id"/>, dead ones included.</summary>
    public IReadOnlyList<Combatant> Combatants => _combatants;

    /// <summary>Everything that happened, in order.</summary>
    public IReadOnlyList<CombatEvent> Events => _events;

    /// <summary>Actions waiting in the tactical-pause queue.</summary>
    public IReadOnlyList<CombatAction> QueuedActions => _queue;

    /// <summary>Who paused the fight, while paused or resolving; otherwise null.</summary>
    public Combatant? PausedBy => _pausedBy;

    /// <summary>Adds a combatant and the brain that decides for it.</summary>
    /// <param name="combatant">The combatant; its ID must be new to this encounter.</param>
    /// <param name="brain">Decides its actions.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentException">The ID is already taken.</exception>
    /// <exception cref="InvalidOperationException">The fight is over.</exception>
    public void Add(Combatant combatant, ICombatBrain brain)
    {
        ArgumentNullException.ThrowIfNull(combatant);
        ArgumentNullException.ThrowIfNull(brain);
        if (Phase == CombatPhase.Over)
        {
            throw new InvalidOperationException("The fight is over.");
        }

        if (!_brains.TryAdd(combatant.Id, brain))
        {
            throw new ArgumentException($"Combatant ID {combatant.Id} is already in the encounter.", nameof(combatant));
        }

        int index = 0;
        while (index < _combatants.Count && _combatants[index].Id < combatant.Id)
        {
            index++;
        }

        _combatants.Insert(index, combatant);
    }

    /// <summary>Finds a combatant by ID.</summary>
    /// <param name="id">The ID.</param>
    /// <returns>The combatant, or null.</returns>
    public Combatant? Find(int id)
    {
        foreach (Combatant c in _combatants)
        {
            if (c.Id == id)
            {
                return c;
            }
        }

        return null;
    }

    /// <summary>Runs one tick. Does nothing while paused or over.</summary>
    public void Step()
    {
        if (Phase is CombatPhase.Paused or CombatPhase.Over)
        {
            return;
        }

        UpdateConditions();
        RegenerateActionPoints();
        CheckEnd();

        // Whoever is resolving a pause queue sits out the rest of the tick, including the tick its last queued action
        // lands in: a combatant that acts in a tick does not also walk.
        Combatant? queueActor = Phase == CombatPhase.Resolving ? _pausedBy : null;
        if (queueActor is not null)
        {
            ResolveQueue();
        }

        foreach (Combatant c in _combatants)
        {
            if (Phase == CombatPhase.Over)
            {
                break;
            }

            if (!c.IsAlive || c.IsPrevented || c == queueActor || c == _pausedBy)
            {
                continue;
            }

            CombatDecision decision = _brains[c.Id].Decide(this, c);
            if (c.ReadyAt <= Tick && decision.Action is { } a && TryExecute(c, a))
            {
                c.ReadyAt = Tick + c.Attacks[a.AttackIndex].RecoveryTicks;
            }
            else if (decision.MoveTo is { } to)
            {
                Walk(c, to, decision.StopWithinMeters);
            }
        }

        Tick++;
    }

    // One tick of walking: straight at the goal; when another combatant is in the way, 45 degrees to the left or right;
    // failing that, sideways (90 degrees), which slides the walker round whoever blocks it.
    // Only exact arithmetic and square roots (no trigonometry), so positions match on every machine.
    private void Walk(Combatant c, Point2D to, double stopWithin)
    {
        double dx = to.X - c.Position.X;
        double dz = to.Z - c.Position.Z;
        double distance = Math.Sqrt((dx * dx) + (dz * dz));
        double step = Math.Min(c.SpeedMetersPerSecond / Rules.TicksPerSecond, distance - stopWithin);
        if (step <= 0)
        {
            return;
        }

        double fx = dx / distance, fz = dz / distance;
        double diagonal = Math.Sqrt(0.5);
        Span<(double X, double Z)> directions = stackalloc (double, double)[]
        {
            (fx, fz),
            ((fx - fz) * diagonal, (fz + fx) * diagonal),
            ((fx + fz) * diagonal, (fz - fx) * diagonal),
            (-fz, fx),
            (fz, -fx),
        };

        foreach ((double x, double z) in directions)
        {
            var next = new Point2D(c.Position.X + (x * step), c.Position.Z + (z * step));
            if (IsFree(c, next))
            {
                c.Position = next;
                return;
            }
        }
    }

    // A spot is free when it keeps the minimum separation from every other living combatant, or at least does not
    // bring the walker closer to one it already overlaps (so two that start too close can walk apart).
    private bool IsFree(Combatant walker, Point2D next)
    {
        double min = Rules.MinSeparationMeters;
        foreach (Combatant other in _combatants)
        {
            if (other == walker || !other.IsAlive)
            {
                continue;
            }

            double nx = other.Position.X - next.X, nz = other.Position.Z - next.Z;
            double after = (nx * nx) + (nz * nz);
            if (after >= min * min)
            {
                continue;
            }

            double cx = other.Position.X - walker.Position.X, cz = other.Position.Z - walker.Position.Z;
            if (after < (cx * cx) + (cz * cz))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Runs ticks until the fight is over, it pauses, or <paramref name="maxTicks"/> ticks have passed.</summary>
    /// <param name="maxTicks">Upper bound on ticks to run; must not be negative.</param>
    /// <returns>Ticks actually run.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxTicks"/> is negative.</exception>
    public long Run(long maxTicks)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxTicks);
        long start = Tick;
        while (Tick - start < maxTicks && Phase is CombatPhase.Running or CombatPhase.Resolving)
        {
            Step();
        }

        return Tick - start;
    }

    /// <summary>Starts the tactical pause: the clock stops until <see cref="Confirm"/> or <see cref="CancelPause"/>.</summary>
    /// <param name="combatantId">Who pauses (the player).</param>
    /// <returns>False when not in real time, or the combatant is unknown, dead, prevented or has no action-point pool.</returns>
    public bool Pause(int combatantId)
    {
        Combatant? c = Find(combatantId);
        if (Phase != CombatPhase.Running || c is null || !c.IsAlive || c.IsPrevented || c.MaxActionPoints == 0)
        {
            return false;
        }

        Phase = CombatPhase.Paused;
        _pausedBy = c;
        Emit(CombatEventKind.Paused, c.Id, -1, c.ActionPoints, string.Empty);
        return true;
    }

    /// <summary>Queues an action for whoever paused, paying its action-point cost now.</summary>
    /// <param name="action">The action.</param>
    /// <returns>False when not paused, the action is invalid (attack index, dead or out-of-range target), or there are not enough action points.</returns>
    public bool QueueAction(CombatAction action)
    {
        if (Phase != CombatPhase.Paused || _pausedBy is null || !IsValid(_pausedBy, action, out Attack? attack))
        {
            return false;
        }

        if (attack.ActionPointCost > _pausedBy.ActionPoints)
        {
            return false;
        }

        _pausedBy.ActionPoints -= attack.ActionPointCost;
        _queue.Add(action);
        Emit(CombatEventKind.ActionQueued, _pausedBy.Id, action.TargetId, attack.ActionPointCost, attack.Id);
        return true;
    }

    /// <summary>Leaves the pause without acting; every queued action's points are refunded.</summary>
    /// <returns>False when not paused.</returns>
    public bool CancelPause()
    {
        if (Phase != CombatPhase.Paused || _pausedBy is null)
        {
            return false;
        }

        int refund = 0;
        foreach (CombatAction a in _queue)
        {
            refund += _pausedBy.Attacks[a.AttackIndex].ActionPointCost;
        }

        _pausedBy.ActionPoints += refund;
        Emit(CombatEventKind.PauseCancelled, _pausedBy.Id, -1, refund, string.Empty);
        _queue.Clear();
        _pausedBy = null;
        Phase = CombatPhase.Running;
        return true;
    }

    /// <summary>
    /// Leaves the pause and plays the queue on the running clock: one queued action every
    /// <see cref="CombatRules.QueuedActionGapTicks"/> ticks, ignoring the actor's recovery, while everyone else keeps acting.
    /// With an empty queue this simply resumes real time.
    /// </summary>
    /// <returns>False when not paused.</returns>
    public bool Confirm()
    {
        if (Phase != CombatPhase.Paused || _pausedBy is null)
        {
            return false;
        }

        if (_queue.Count == 0)
        {
            _pausedBy = null;
            Phase = CombatPhase.Running;
            return true;
        }

        Phase = CombatPhase.Resolving;
        _nextQueuedAt = Tick;
        _lastQueuedRecovery = 0;
        Emit(CombatEventKind.QueueConfirmed, _pausedBy.Id, -1, _queue.Count, string.Empty);
        return true;
    }

    private void ResolveQueue()
    {
        Combatant actor = _pausedBy!; // Resolving always has a pauser: set by Confirm, cleared together with the phase.
        if (!actor.IsAlive || actor.IsPrevented)
        {
            Emit(CombatEventKind.QueueInterrupted, actor.Id, -1, _queue.Count, string.Empty);
            FinishQueue();
            return;
        }

        if (Tick < _nextQueuedAt)
        {
            return;
        }

        CombatAction next = _queue[0];
        _queue.RemoveAt(0);
        if (TryExecute(actor, next))
        {
            _lastQueuedRecovery = actor.Attacks[next.AttackIndex].RecoveryTicks;
        }
        else
        {
            // The target died or the action became invalid while the queue ran: refund rather than waste the points.
            actor.ActionPoints += actor.Attacks[next.AttackIndex].ActionPointCost;
        }

        _nextQueuedAt = Tick + Rules.QueuedActionGapTicks;
        if (Phase == CombatPhase.Over)
        {
            return;
        }

        if (_queue.Count == 0)
        {
            actor.ReadyAt = Math.Max(actor.ReadyAt, Tick + _lastQueuedRecovery);
            Emit(CombatEventKind.QueueFinished, actor.Id, -1, 0, string.Empty);
            FinishQueue();
        }
    }

    private void FinishQueue()
    {
        _queue.Clear();
        _pausedBy = null;
        if (Phase != CombatPhase.Over)
        {
            Phase = CombatPhase.Running;
        }
    }

    private bool IsValid(Combatant actor, CombatAction action, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Attack? attack)
    {
        attack = null;
        if (action.AttackIndex < 0 || action.AttackIndex >= actor.Attacks.Count)
        {
            return false;
        }

        Combatant? target = Find(action.TargetId);
        Attack candidate = actor.Attacks[action.AttackIndex];
        if (target is null || !target.IsAlive || actor.DistanceTo(target) > candidate.RangeMeters)
        {
            return false;
        }

        attack = candidate;
        return true;
    }

    private bool TryExecute(Combatant actor, CombatAction action)
    {
        if (!IsValid(actor, action, out Attack? attack))
        {
            return false;
        }

        Combatant target = Find(action.TargetId)!; // IsValid has just found it.
        var rng = Rng.ForKey(Seed, RngScope, EncounterNumber, Tick, actor.Id);
        Emit(CombatEventKind.Attacked, actor.Id, target.Id, 0, attack.Id);

        if (attack.IsArea)
        {
            _victims.Clear();
            foreach (Combatant c in _combatants)
            {
                if (c.IsAlive && c.DistanceTo(target) <= attack.AreaRadiusMeters)
                {
                    _victims.Add(c);
                }
            }

            foreach (Combatant victim in _victims)
            {
                Strike(actor, victim, attack, rng);
            }
        }
        else
        {
            int chance = Rules.HitChancePercent(actor.AttackRating + attack.AttackBonus, target.Defence);
            bool hit = rng.NextInt(CombatRules.PercentScale) < chance;
            Emit(hit ? CombatEventKind.Hit : CombatEventKind.Missed, actor.Id, target.Id, chance, attack.Id);
            if (hit)
            {
                Strike(actor, target, attack, rng);
            }
        }

        CheckEnd();
        return true;
    }

    private void Strike(Combatant actor, Combatant victim, Attack attack, Rng rng)
    {
        Damage(victim, actor.Id, attack.Damage.Roll(rng), attack.DamageType, attack.Id);
        if (attack.Applies is { } application && victim.IsAlive && rng.NextInt(CombatRules.PercentScale) < application.ChancePercent)
        {
            ApplyCondition(victim, actor.Id, application.Condition);
        }
    }

    private void Damage(Combatant victim, int actorId, int raw, string damageType, string detail)
    {
        int amount = raw * (CombatRules.PercentScale - victim.ResistancePercent(damageType)) / CombatRules.PercentScale;
        victim.Health = Math.Max(0, victim.Health - amount);
        Emit(CombatEventKind.Damaged, actorId, victim.Id, amount, detail);

        List<ActiveCondition> conditions = victim.MutableConditions;
        if (!victim.IsAlive)
        {
            conditions.Clear();
            Emit(CombatEventKind.Died, actorId, victim.Id, 0, detail);
            return;
        }

        if (amount > 0)
        {
            for (int i = conditions.Count - 1; i >= 0; i--)
            {
                if (conditions[i].Definition.BreaksOnDamage)
                {
                    Emit(CombatEventKind.ConditionEnded, actorId, victim.Id, 0, conditions[i].Definition.Id);
                    conditions.RemoveAt(i);
                }
            }
        }
    }

    private void ApplyCondition(Combatant victim, int actorId, Condition condition)
    {
        var active = new ActiveCondition(condition, Tick + condition.DurationTicks, Tick + condition.PulseIntervalTicks);
        List<ActiveCondition> conditions = victim.MutableConditions;
        int existing = conditions.FindIndex(c => c.Definition.Id == condition.Id);
        if (existing >= 0)
        {
            conditions[existing] = active;
        }
        else
        {
            conditions.Add(active);
        }

        Emit(CombatEventKind.ConditionApplied, actorId, victim.Id, condition.DurationTicks, condition.Id);
    }

    private void UpdateConditions()
    {
        foreach (Combatant c in _combatants)
        {
            List<ActiveCondition> conditions = c.MutableConditions;
            if (!c.IsAlive || conditions.Count == 0)
            {
                continue;
            }

            _pulses.Clear();
            for (int i = 0; i < conditions.Count; i++)
            {
                ActiveCondition active = conditions[i];
                Condition def = active.Definition;
                if (def.PulseIntervalTicks > 0 && active.NextPulseAt <= Tick)
                {
                    _pulses.Add((def, def.PulseDamage));
                    conditions[i] = active with { NextPulseAt = active.NextPulseAt + def.PulseIntervalTicks };
                }
            }

            for (int i = conditions.Count - 1; i >= 0; i--)
            {
                if (conditions[i].EndsAt <= Tick)
                {
                    Emit(CombatEventKind.ConditionEnded, -1, c.Id, 0, conditions[i].Definition.Id);
                    conditions.RemoveAt(i);
                }
            }

            foreach ((Condition condition, int amount) in _pulses)
            {
                if (c.IsAlive)
                {
                    Damage(c, -1, amount, condition.PulseDamageType, condition.Id);
                }
            }
        }
    }

    private void RegenerateActionPoints()
    {
        foreach (Combatant c in _combatants)
        {
            if (!c.IsAlive || c.ActionPoints >= c.MaxActionPoints)
            {
                c.ActionPointProgress = 0;
                continue;
            }

            c.ActionPointProgress++;
            if (c.ActionPointProgress >= Rules.ActionPointRegenTicks)
            {
                c.ActionPoints++;
                c.ActionPointProgress = 0;
            }
        }
    }

    private void CheckEnd()
    {
        if (Phase == CombatPhase.Over)
        {
            return;
        }

        int? team = null;
        foreach (Combatant c in _combatants)
        {
            if (!c.IsAlive)
            {
                continue;
            }

            if (team is null)
            {
                team = c.Team;
            }
            else if (team != c.Team)
            {
                return;
            }
        }

        WinningTeam = team;
        Phase = CombatPhase.Over;
        _queue.Clear();
        _pausedBy = null;
        Emit(CombatEventKind.Ended, -1, -1, team ?? -1, string.Empty);
    }

    private void Emit(CombatEventKind kind, int actorId, int targetId, int amount, string detail) =>
        _events.Add(new CombatEvent(Tick, kind, actorId, targetId, amount, detail));
}
