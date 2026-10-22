# 0023. A walking graph per settlement
- Status: accepted
- Date: 2026-10-23

## Context
NPCs (P4, milestone M3) have to walk between buildings, dozens at a time in settlements and hundreds in cities, without hurting frame time. CLAUDE.md section 4.6 says the active zone may use navigation meshes but farther zones move along a graph, and promotion and demotion must lose nothing. The generated cities have streets only from the square to the gates; most houses face lanes between rows that exist only as open ground.

## Options considered
1. A Godot navigation mesh per city — tied to the engine, so the simulation, headless tests and the CLI could not use it; slow to bake for 250 houses; useless for far NPCs.
2. A grid of walkable cells searched with A* per walk — simple but a path per NPC per trip costs milliseconds, and positions need per-frame stepping.
3. A sparse graph in the simulation layer: nodes at the square, along streets, at gates, at bridge ends and in front of every front door, joined by straight edges that are checked for open ground; a walk is a short node list and a walker's position is a function of time.

## Decision
Option 3.
- `Realm.Sim.Navigation` (no Godot, no generator): `NavGraph` (nodes, edges, connected groups, nearest node, door lookup), `NavPathFinder` (A*, allocation-free after construction, ties broken by node ID so it is deterministic), `NavRoute` (position after a distance along a walk).
- `Realm.Gen.Settlements.Navigation.NavGraphBuilder` builds the graph from a generated `City` in the city's plan frame: a ring around the square's centrepiece, streets resampled to 8 m, gates and an outside node beyond each gate (where the world's roads will join), bridge ends joined by bridge edges, a door node 1.3 m in front of every door. Each door links to its nearest visible nodes within 40 m; a link is allowed only over open ground (not through buildings with a 0.3 m margin, the centrepiece, market stalls, water, or the wall except at gates). A node that is still cut off is joined by a breadth-first search over a 1 m grid of open ground, whose route is straightened into new street nodes, so doors in pockets between rows get their lane.
- Validators: `NAV001` (a door cannot be reached from the square, warning) and `NAV002` (a gate cannot, error). A gate standing in water gets no node.
- A debug drawing (key `N`, flag `--nav-debug`) shows the graph over the city.

## Consequences
- Every door of all 13 shipped settlements is reachable; a build takes about 0.2 s for a 250-house city, so it can run when a settlement is loaded or when the simulation starts, and it is not baked.
- Walkers will follow straight edges: long diagonal links across yards and some lane zig-zags appear; smoothing or a denser lane graph is a later improvement.
- The graph stops at the settlement's outside nodes; joining it to the world road graph (for travel between settlements) is the next step, as are interiors (NPCs vanish at the door for now) and a door-in-water or enclosed-house report from the generator.
