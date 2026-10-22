# 2026-10-23 — crowd of capsules and the world graph

## Goal
Join the settlement graphs to the road graph, and put at least 200 capsule characters in the first city, concentrated around the fountain, walking between buildings with a day of their own.

## What was done
- `WorldNavBuilder` (settlement graphs plus roads, `NAV003`), `OpenGround` extracted from the graph builder.
- `Realm.Sim.Npcs` (`NpcPlan`, `Crowd`), `Realm.Gen.Population` (`CrowdGenerator`, `CrowdSettings`), `CrowdView` and the plumbing in `CityBuild`, `CityInstance`, `SettlementStreamer` and `WorldScene`; `--crowd-scale`.

## Decisions made
[ADR 0024](../decisions/0024-crowd-of-capsules.md).

## Problems encountered and how they were solved
- Walks dominated the day (almost every loiterer was walking): errands went across the whole city. Errands and visits now go to buildings near the square or the home, and stays are longer.
- Characters were still walking at 3 am, because a long stay could start after bedtime and the graph's detours made the way home long: characters now start for home 90 minutes before bedtime, and a stay is cut short at that moment.
- Strolling round the fountain by the graph made loiterers hug the ring: a straight walk across the open ground is used when the open-ground test allows it.
- The graph's detours (median 1.5, p90 2.4 times the straight distance) remain; more neighbour links barely helped.

## Tests added
Crowd (guard, loiterer, walker, sleep, determinism, time jump), generator (counts, spots, homes, guards, a simulated day), world graph (all settlements joined, a walk between two cities uses roads and bridges).

## Docs added/updated
ADR 0024, characters and crowds system doc, navigation doc, validators (NAV003), run-the-game, glossary, known issue 76, CHANGELOG, index.

## Open questions / next steps
Characters entering buildings visibly, collision with the player, travel along the world graph between settlements, real figures, needs and tasks.
