# 2026-11-01 — combat movement

## Goal
Let combatants walk, so melee creatures close in instead of having to be placed within reach, and fights no longer depend on hand-placed positions.

## What was done
- `Combatant` has `SpeedMetersPerSecond` and a position the encounter changes.
- `ICombatBrain.Decide` returns a `CombatDecision` (an action, a point to walk to, a stop distance) and is called every tick, ready or not. The encounter acts if ready and the action is valid; otherwise it walks one tick.
- Walking: straight at the goal, else 45° left/right, else 90° left/right, taking the first spot that keeps `CombatRules.MinSeparationMeters` (0.8 m) from every other living combatant.
- `NearestEnemyBrain` walks towards its target until its longest-reaching usable attack is in range (stopping `ApproachMarginMeters`, 0.05 m, inside). `HeldIntentBrain.MoveTo` walks the player.
- Content: `creature` gains a required `speed_m_s` (0 to 50), overridable per tier; the goblins walk at 4, 3.5 and 4.5 m/s. The `combat-sim` hero walks at 5 m/s.
- New scenario `goblin-charge` (hero wins about 77 %). `duel`, `goblin-ambush` and `goblin-chief` give the same numbers as before, which checks that adding movement didn't change fights where nobody needs to move.

## Decisions made (link ADRs)
No new ADR; these are implementation details within ADR 0029:
- **Walking is not an action**: no recovery, and it happens while recovering (M&M monsters close in between swings). A combatant does not walk in a tick it acts.
- **Speed is required in content**, with `0` for creatures that never move, rather than a hidden default speed.
- **No trigonometry** in movement: the side-step directions come from swapping and negating the heading and multiplying by `sqrt(0.5)`, so positions are bit-identical on every machine.
- **Local avoidance only.** Real pathfinding round walls and terrain belongs with the world's navigation (`Realm.Sim.Navigation`), not the combat spike.

## Problems encountered and how they were solved
- With only 45° side-steps, four attackers in a line jammed behind the first. Adding 90° side-steps lets them slide round; three of four now reach the target, the fourth is boxed in (recorded as a limitation).
- The player walked in the tick its last queued action landed, because the queue finished before the movement pass. The combatant resolving a queue now sits out the whole tick. Caught by `WhilePausedOrResolving_ThePlayerStands`.
- One test expectation was wrong: with 0.25 m steps from 10 m the walker lands on exactly 2.0 m, already in reach, so it never takes the shortened step.

## Tests added
`MovementTests` (walks into range then attacks at an exact tick, stands while in range, speed 0 never moves, personal space kept and walkers go round each other, the player's move target, standing while paused or resolving, asleep means no walking, same seed same positions). Data: `speed_m_s` required and bounded, `0` allowed, tier override.

## Docs added/updated
`docs/systems/combat.md` (walking, brain, determinism, performance, limitations), `docs/explanation/combat.md` (a section on moving), `docs/reference/content/creature.md`, `docs/how-to/add-a-creature.md` (wolves now start 12–15 m away and run in; walked through again verbatim), `docs/how-to/run-a-combat-balance-sweep.md`, `docs/reference/cli.md`, `docs/reference/validators.md`, glossary (personal space), CHANGELOG, `docs/index.md`.

## Open questions / next steps
- A tick now costs O(n²) in the number of combatants (`goblin-charge` 10,000 fights: about 4.6 s, was about 1.3 s for `goblin-ambush` without walking). Fine for small fights; revisit before large battles.
- Pathfinding and terrain: use the settlement and world walking graphs for routes, and the terrain sampler for slopes.
- More behaviour profiles now that moving is possible: ranged kiter (keep distance), coward (flee at low health).
