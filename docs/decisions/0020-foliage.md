# 0020. Foliage: baked trees and bushes, scattered grass
- Status: accepted
- Date: 2026-10-22

## Context
The world has terrain, rivers, roads and settlements but no plants. Forests and grassland need hundreds of thousands of plants over 20 x 20 km (CLAUDE.md section 6.1 step 11, P2, P6) without hurting frame time, and species must be content (P7).

## Options considered
1. One scene node per plant — simple, far too slow.
2. Terrain3D's own instancer — fewer pieces to write, but the wrapper rule (section 5.2) and the 1.0.2 API make it awkward, and it cannot see roads, settlements and rivers from our generators.
3. Baked per-plant data from our generator, drawn with one MultiMesh per chunk and species — one draw call per group, deterministic, validated like every other artefact.

## Decision
Option 3, split by kind:
- **Trees and bushes** are baked instances (`Realm.Gen.Foliage.FoliageGenerator`, `foliage.bin`): position, ground height, yaw, scale and species in 9 bytes, grouped by 256 m chunk. Each species is a jittered candidate grid with its own seed (`Rng` per candidate from the world seed, species ID and cell), so adding a species never moves another. A candidate survives with a probability from the biome density (blended across region borders), the species' clumping noise, and the height and slope limits, and is dropped on water, roads and settlements (`FoliageExclusion`) and closer than `min_spacing_m` to its own kind.
- **Grass** is baked only as a density map (32 m cells, dominant species and tuft count). The game scatters the tufts of cells near the camera at run time from a seeded function (`GrassScatter`), so the same cell always grows the same grass.
- **Content:** a new `foliage` type (`FoliageDefinition`, `FoliageParser`, code `DATA019`) with per-biome density per hectare. Models are tied to species by kind and tags (`FoliageMeshes.StyleFor`); until art exists they are procedural low-poly models, near and far versions.
- **Game** (`FoliageLayer`): chunks within 850 m are built two per frame; near (0 to 170 m) and far (150 to 950 m) models are separate MultiMeshes switched by Godot's visibility ranges; grass within 64 m; 48 pooled cylinder colliders make trunks within 16 m solid.
- Validators `FOL001` to `FOL003` run on every bake.

## Consequences
- About 825,000 plants (8 MB) for the shipped world; the whole file and its instance list are held in memory (about 40 MB managed). Per-chunk reads and a compact in-memory form are a later step (known issue 70).
- Trees do not move with wind and have one model per style; hand-made meshes and billboards for the far level are later steps.
- Roads and settlements must exist before foliage is generated (they are inputs to the exclusion raster), so foliage is the last step of `bake-terrain`.
