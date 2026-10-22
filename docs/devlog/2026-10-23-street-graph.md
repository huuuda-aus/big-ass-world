# 2026-10-23 — street graph for NPCs

## Goal
The walking graph NPCs will use in settlements (M3, navigation).

## What was done
- `Realm.Sim.Navigation`: `NavGraph`, `NavPathFinder`, `NavRoute`.
- `NavGraphBuilder` for generated cities; `NAV001`/`NAV002`; a debug drawing in the game (key N, `--nav-debug`); `CityBuild` carries the graph.

## Decisions made
[ADR 0023](../decisions/0023-settlement-walking-graph.md).

## Problems encountered and how they were solved
- Door-to-door visibility links left doors in pockets between rows isolated: added a search over open ground whose route becomes new street nodes.
- The landmark's radius is the space it may take, not what it fills: it blocked doors near the square; only 55 percent of it is solid.
- A far-bank street was linked straight to the square across the river: street links are checked for open ground, and bridge ends join the banks.
- A gate standing in the river made the shipped river city fail NAV002: such a gate gets no node.

## Tests added
Pathfinding on a grid, against Floyd-Warshall on random graphs, routes, lookups; graphs of radial and cross settlements, walks never crossing buildings, a river city joined only by bridges, determinism, validator; all 13 shipped settlements.

## Docs added/updated
ADR 0023, navigation system doc, validators, glossary, run-the-game, known issue 75, CHANGELOG, index.

## Open questions / next steps
Join the graph to the road graph; schedules and the NPC generator; character rendering; smoothing of the long diagonal links.
