# 2026-10-21 — names-and-roads

## Goal
The developer saw only one city in the world and asked for names and roads.

## What was done
- Why only one city: cities build on a worker thread only within about 800 m of the player (about 2.7 km apart, nothing showed where they were). Fixes: cities load from the terrain view distance plus 300 m (about 2 km when flying), the HUD names the nearest settlement with distance and direction, and a full-screen map (M) shows every place, the roads and the player.
- Names: `SettlementNamer` (stem plus ending, different endings for cities and towns), placement v2.
- Roads (terrain steps 7 and 8): `RoadPlanner` (links, A*, smoothing, grading, bridge spans), `RoadTerrain`, `RoadValidator`, `RoadManifest`, `RiverRaster`, `BridgeMesher`; the baker paints roads; the CLI plans, carves, validates and writes `roads.json` and `map.png`; the game builds bridges and the map.
- Result on the shipped world: 15 roads (53.5 km) joining all 8 settlements, 10 bridges of about 32 m, no validator issues. The bake takes about 3 s longer.
- Docs: ADR 0013, settlements reference (roads, map), system doc, how-to, CLI and validators references, known issues 51 and 53.

## Decisions made
[ADR 0013](../decisions/0013-roads-and-bridges.md): roads are graded terrain plus colour (only bridges are meshes); gates are the road ends; a river cell costs 400 m of detour.

## Problems encountered and how they were solved
- The first bake reported a 21.5 m cut on one road; raising the slope penalty from 12 to 25 made the planner choose gentler routes and the validator passed.
- The surface-match check failed on the coarse 128 m grid used by tests: a 6 m road cannot be held by a 128 m grid. The check now applies only when the vertex spacing is no coarser than the road; the cut-and-fill limit is always checked.
- The map did not appear: `WorldMap` laid itself out in its constructor, before it was in the scene tree, and the null window threw. It now lays out in `_Ready`.
- Roads sharing a river crossing stacked identical bridges; the game keeps one per 6 m.
- Test runtime: the shipped-world tests share one pipeline run.

## Tests added
`SettlementNamerTests` (5), `RoadTests` (14: one road between gates, grade limit, one bridge over a river, detour through a ridge pass, sea cut gives ROAD002, tree of three towns, carve puts terrain on the surface, bridge keeps the channel, validator, determinism, weight, settings, manifest, bridge mesh) and 2 shipped-world tests. The whole solution: 614 tests, 0 warnings.

## Docs added/updated
See above; also README, CHANGELOG, glossary, index, run-the-game.

## Open questions / next steps
- Developer: walk or fly the roads and say what is wrong (width, colour, routes, bridges).
- Candidates: road junction design and signposts, hamlets along roads, real names, textures and night lighting, doors that open.
