# 2026-10-20 — sim-foundations

## Goal
M0 Foundations, fourth slice: start `Realm.Sim` with a clock, entities with components and a deterministic tick loop (CLAUDE.md sections 4.2 and 5.1).

## What was done
- `TimeSettings`, `GameTime`, `SimClock`: integer ticks to game minutes.
- `EntityId` (increasing, never reused), `SimWorld` with struct components and ordered queries.
- `ISystem` and `Simulation` (systems in registration order, then the clock advances).
- Docs: Sim system doc, determinism explanation, add-a-system how-to, glossary, known issues.

## Decisions made
- No ADR: this implements the model CLAUDE.md already specifies (simple explicit components, no ECS library).
- The simulation property is `WorldState`, not `World`, so it cannot shadow the reserved `Realm.Sim.World` namespace.
- Game-design items kept out of code: pacing is a parameter with a flagged placeholder default; no calendar above days.

## Problems encountered and how they were solved
- `EntityId` had `CompareTo` but no `<` operator; added `<` and `>`.
- A snapshot test failed because a query copy still listed an entity destroyed during iteration, and writing to it correctly threw. The code was right. The contract is now documented on `Query`, in the how-to, and covered by two tests (one that handles it with `IsAlive`, one that asserts the throw).

## Tests added
- `TimeTests`, `SimWorldTests`, `SimulationTests`: 33 tests, including determinism (same seed gives identical worlds, chunked runs equal one long run, different seeds differ). Whole solution: 141 tests, 0 warnings.

## Docs added/updated
Sim system doc, determinism explanation, add-a-system how-to, glossary, known issues (10 to 12), index, CHANGELOG.

## Open questions / next steps
- Developer design needed: day length in real time, calendar structure, material and item design.
- Next candidates: save-state skeleton (versioned binary header and a migration test harness, per CLAUDE.md 4.5), or the first terrain pipeline pieces in `Realm.Gen` (macro map loading plus a height-field step with validators). Terrain is the M1 critical path.
