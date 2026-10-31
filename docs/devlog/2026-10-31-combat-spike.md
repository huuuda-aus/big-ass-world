# 2026-10-31 — combat spike

## Goal
Prove the combat model from ADR 0029 in Godot-free, tested code before any M5 presentation work: one timeline, target-based rolls, area effects that hurt the caster, conditions, the tactical pause, and a headless balance runner.

## What was done
- `Realm.Sim.Combat`: `CombatEncounter` (the tick loop, rules, pause queue, events), `Combatant`, `AttackDefinition`, `ConditionDefinition`, `ConditionApplication`, `ActiveCondition`, `Dice`, `CombatRules`, `CombatAction`, `CombatEvent`/`CombatEventKind`, `CombatPhase`, `DamageTypes`, `ICombatBrain` with `NearestEnemyBrain` and `HeldIntentBrain`, `CombatSimRunner`/`CombatSimSummary`.
- `Realm.Cli combat-sim` with two code-defined scenarios (`duel`, `goblin-ambush`) and a win-rate band check.
- First numbers (placeholders): the hero wins the duel 100 % and the ambush about 97 % with about half its health left; 10,000 ambush fights take about 1.3 s.

## Decisions made (link ADRs)
No new ADR; these are implementation details inside ADR 0029:
- Time is counted in sim ticks (20 Hz), integers only. Hit chance is linear and clamped (75 % at equal rating, 5 % per point, 5–95 %) instead of the "smooth curve" in the ADR: simplest integer form; revisit with real stats.
- One `Rng` per action, keyed by seed, `"combat"`, encounter number, tick and actor ID.
- Area attacks have no hit roll.
- Queued actions run 4 ticks (0.2 s) apart, ignoring recovery; after the queue the actor's recovery is the last queued action's. Enemies keep acting during the queue.
- A queued action whose target already died is skipped and refunded; actions dropped because the actor fell asleep or died are not refunded.
- The spike encounter is a standalone object, not components on `SimWorld`, so the rules could be tested before deciding how combat joins the simulation.

## Problems encountered and how they were solved
- Ordering inside a tick matters for the pause: the queue resolves before ordinary combatants act, so a queued action at tick 0 lands before an enemy who acts at tick 0. Written down in the system doc and pinned by `FallingAsleep_InterruptsTheQueue`.

## Tests added
`tests/Realm.Sim.Tests/Combat/`: `CombatRulesTests` (hit curve, dice, constructor checks), `CombatEncounterTests` (recovery pacing, resistance, self-damage from area attacks, brain choice, range, poison pulses, sleep and waking, end of fight, determinism), `TacticalPauseTests` (clock stops, AP spending and refund, queue timing, enemies acting during the queue, interruption, dead-target refund, refusals, AP regeneration), `CombatSimRunnerTests`. 33 tests.

## Docs added/updated
New: `docs/systems/combat.md`, `docs/how-to/run-a-combat-balance-sweep.md`. Updated: `docs/reference/cli.md` (combat-sim, current help text), `docs/systems/sim.md`, `docs/explanation/combat.md` (status, hit curve, balance), glossary (balance sweep, brain, combat encounter; AP, ready time and tactical pause no longer marked planned), CHANGELOG, `docs/index.md`.

## Open questions / next steps
- Movement (melee fighters out of reach wait forever) and projectiles in flight.
- Attacks, conditions and creatures as YAML content types with validators; scenarios as data.
- Moving combat into `Simulation` as components plus a system, with a reusable event buffer.
- A `combat-sim` hero that uses the pause, so AP costs and regeneration get balanced too.
- Wire a `combat-sim` band check into CI once the numbers mean something.
