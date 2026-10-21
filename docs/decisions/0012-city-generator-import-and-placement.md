# 0012. City generator import, placement and 3D
- Status: accepted
- Date: 2026-10-21

## Context
The developer has a separate project, CityGen (`~/Develop/citygen`, their own code): a deterministic walled-city generator (Voronoi wards, wall, gates, streets, lots, buildings, building interiors with rooms, doors, windows and stairs) plus a three.js web viewer that builds the 3D geometry. They asked to import everything necessary, including the 3D, and to place cities around the map. Decisions taken: import and place on the map and build the 3D in the game; "few and big": 3 cities and 5 towns, no hamlets.

CityGen differs from our rules: it uses `System.Random` (CLAUDE.md section 4.2 forbids it), has no XML docs, several public types per file, its own namespaces, a flat 2D plane in its own coordinates, and the 3D code is JavaScript.

## Options considered
1. Reference it as an external project — no. Our determinism, documentation and layering rules would not apply, and the game cannot run JavaScript.
2. Copy the C# library in, adapt it to our rules, and port the viewer's geometry code to C# (chosen).
3. Keep the web viewer and embed it — no; Godot is the presentation layer.

## Decision
- **Vendored copy** in `Realm.Gen/Settlements/` (`Geometry`, `Model`, `Generation`), namespace `Realm.Gen.Settlements.*`. Changes: `System.Random` replaced by `Rng.ForKey` (so layouts differ from CityGen's, but are deterministic across machines), one public type per file, file-scoped namespaces, XML docs. The 46 CityGen tests were ported as they were and pass. There is no live link to the CityGen repository; later fixes there must be ported by hand.
- **Placement** (`Settlements/Placement`, `settlement.placement` v1): cities first, then towns; each city is generated once to learn its wall radius; a candidate grid (128 m) is scored on ring samples across the pad (wall radius + 30 m): above 15 m and below 320 m, relief at most 0.1 x radius, no river or lake within 40 m (coarse water mask), at least 2,500 m (and the pads plus blends) from other settlements, 400 m from the world edge; a bonus for a river within 1,500 m. The ground under the pad is flattened to the mean height with a 100 m smoothstep blend (`SettlementTerrain`).
- **Baked data**: `settlements.json` beside the terrain tiles holds only placements and city seeds. A city is regenerated from its seed at runtime (about 0.3 to 0.6 s per city on a worker thread), so no city geometry is baked. The player spawns in the first city's central square.
- **3D** (`Settlements/Meshing`, Godot-free, tested): ports of the viewer's `facadeMesh`, `timberMesh`, `buildNear`, `roofMesh`, the wall/tower/street/ground code and `landmarks.js`, producing triangle meshes (`PropMesh`) with vertex colours and counter-clockwise winding. Each house has a cheap closed **far** mesh (walls, roof, painted door and windows, half-timbering) and an on-demand detailed **near** mesh (real openings, rooms, floors, ceilings, stairs, door leaves) with a collision mesh whose stairs are a smooth ramp.
- **Game** (`Realm.Game/World/Settlements`): `SettlementStreamer` builds a city on a worker thread when the player comes within the pad radius + 500 m and frees it beyond + 800 m; `CityInstance` adds house nodes 40 per frame and swaps in the detailed version of houses within 35 m (dropped beyond 60 m), at most two builds at a time, each with a lamp per storey.
- **Gates**: each gate is a gap of 8 m between two gate towers (the viewer left a whole wall edge open).

## Not ported (known)
Procedural stucco and wood textures with bump maps, the time-of-day and lamp system, lit-window glow and street aura, per-vertex indoor shading, animated flames, embers and water ripples (a fountain has simple particle jets), per-house lamp switching, door opening, the viewer's 2D collision (replaced by trimesh collision), and the browser controls.

## Consequences
- Easier: cities exist, are walkable and enterable; all geometry logic is Godot-free and tested (walls have exactly the area left by their openings, floors cover the rooms minus stairwells, normals point the right way, determinism).
- Harder: 8 cities of up to 1,200 houses are a lot of nodes; only one is built at a time near the player. Roads between cities, NPCs, shops, per-door interactions and sim LOD are future work. Houses and interiors are plain vertex-coloured geometry; texture and lighting art comes later.
- The terrain under a city is flattened, so the terrain bake changed (new checksum) and must be regenerated (`bake-terrain`).
