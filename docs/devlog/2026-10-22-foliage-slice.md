# 2026-10-22 — foliage slice

## Goal
Grasslands and forests on the map without hurting performance (M1/M6, terrain step 11).

## What was done
- `foliage` content type, five placeholder species.
- `Realm.Gen.Foliage`: generator, exclusion raster, validator, file format, grass scatter, placeholder models.
- `bake-terrain` writes `foliage.bin` last (it needs final terrain, roads and settlements).
- `FoliageLayer` in the game: chunked MultiMeshes with near and far models, grass near the camera, pooled trunk colliders; `--no-foliage` flag.

## Decisions made
[ADR 0020](../decisions/0020-foliage.md). Open choices I took as defaults: low-poly near models with simpler far models (no billboards yet), generated placeholder art.

## Problems encountered and how they were solved
- The 600 m biome blend lets a species leak into the sea half of a 1 km test world; tests use a 64 m blend.
- Grass is capped at 255 tufts per 32 m cell, which saturates in dense meadows; fine for now.
- Tufts looked like dark cones from afar; the colours were brightened.

## Tests added
Parser tests, generator tests (land only, spacing, determinism, species independence, settlement exclusion, slope limit, grass map, file round trip, validator, golden), mesh tests, a shipped-content test.

## Docs added/updated
ADR 0020, foliage reference, how-to, system doc, validators, CLI, glossary, known issues 70 and 71, CHANGELOG, index.

## Open questions / next steps
Billboards or hand-made far models, wind, a frame-time soak test, per-chunk file reads, editor painting and locks (generate, bake, edit). Next big slice: NPCs (clock, street graph first).
