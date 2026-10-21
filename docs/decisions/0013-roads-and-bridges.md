# 0013. Roads, bridges, names and the world map
- Status: accepted
- Date: 2026-10-21

## Context
After the cities were placed ([ADR 0012](0012-city-generator-import-and-placement.md)) the developer saw only one city when playing (the others stream in only within about 800 m, and nothing showed where they were) and asked for names and roads. CLAUDE.md section 6.1 lists roads (step 7) and bridges (step 8) as terrain pipeline steps.

## Options considered
1. Roads as meshes laid over the terrain — flicker against the terrain, collision issues, hard to blend; rejected.
2. Roads as terrain: graded into the height field and painted into the colour map (chosen); only bridges are meshes.
3. Roads drawn only on a map — no walkable road; rejected.

## Decision
- **Names**: `SettlementNamer` builds placeholder names from a stem and an ending (`Thornford`, `Oakley`); cities and towns use different endings; deterministic from `Rng.ForKey(seed, "settlement", tier, index, "name")`. `settlement.placement` is now v2.
- **Road network** (`road.planner` v1, `Realm.Gen/Settlements/Roads`): the settlements are linked by a minimum spanning tree over straight-line distances plus each settlement's nearest unlinked neighbour (loops). Each link is the cheapest route found by A* on a 32 m grid, from a gate of one city to a gate of the other (outside the wall by 40 m): cost is distance x (1 + 25 x slope squared), plus 400 m for every cell in a river; sea, lakes and ground below 2 m are impassable. The route is smoothed (Chaikin), resampled every 8 m, and given a graded surface: terrain heights (bridged over water), smoothed over 9 samples, limited to a 12 percent grade. Roads are 6 m wide.
- **Bridges**: a run of river samples becomes a bridge from the sample before the water to the sample after it (runs less than 3 samples apart merge); the deck is straight between its ends. `BridgeMesher` builds a timber deck with railings and stone abutments; the game places one per crossing (duplicates at a shared crossing are dropped) with trimesh collision. The river channel under a bridge is not touched.
- **Terrain**: `RoadTerrain.Carve` moves each vertex within the road width to the surface height of its nearest segment and blends over a 10 m shoulder (smoothstep); the baker paints the road colour 0.5 m wider than the surface.
- **Validation** (`ROAD001` to `ROAD006`): settlements connected, surface matches the terrain (only when the grid is no coarser than the road), cut and fill at most 20 m, nothing in water, bridges at most 80 m, manifest valid.
- **Baked**: `roads.json` (centre lines and bridges) and `map.png` (an overview of the final terrain) next to the tiles. The game builds bridges from `roads.json`; the map screen (M) draws `map.png` plus the roads, the names and the player.
- **Visibility**: cities now load when the player is within the larger of 500 m and the terrain view distance plus 300 m, so flying shows them from about 2 km; the HUD names the nearest settlement with its distance and direction.

## Consequences
- Easier: roads are walkable terrain with no extra nodes; the plan is testable on small synthetic worlds (a river gives one bridge, a ridge is crossed at a pass, a sea strip gives "not connected").
- Harder: roads are 6 m on a 4 m vertex grid, so they look rough up close; junctions where roads meet or cross are not designed; roads that share a crossing overlap. The bake takes about 3 s longer.
- Known: some roads cross steep ground with cuts of up to 20 m; the road colour is a flat brown.
