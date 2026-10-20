# 0006. Macro map format: YAML polygons, explicit oceans, cell-centre ownership
- Status: accepted
- Date: 2026-10-20

## Context
The terrain pipeline (CLAUDE.md section 6.1) starts from a hand-authored macro map of regions, biomes and coastline. The developer chose YAML polygons over a painted image. The world is about 20 km across ([ADR 0005](0005-world-scale-and-seamless-streaming.md)), so the format and its outputs must be in world metres and aligned with the chunk grid from the start.

## Options considered
1. Painted low-res image with a colour legend — easy to paint, harder to diff or review, resolution fixed by the image.
2. YAML polygons in world metres — diff-friendly, exact, resolution independent, validated by geometry tests.
3. Inline per-region height hints versus biome records, and land-only polygons versus explicit ocean regions (the two sub-choices below).

## Decision
Decided by the developer on 2026-10-20: YAML polygons; regions reference **biome records**; **ocean is an explicit region** (a region whose biome is tagged `ocean`).

Details chosen while implementing (implementation, not game design):
- One `world` record per file in `content/world/`, with `id`, `name`, `world_size_m` and `regions`. Each region has `id` (`region.*`), `name`, `biome` (`biome.*`) and `polygons`, a list of vertex rings in metres.
- Coordinates: origin at the world centre, X east, Z south (Godot's horizontal axes).
- A region may have several polygons, because polygons have no holes: a ring of ocean around land needs several pieces.
- Polygons must be simple (no self-intersection or touching edges), non-degenerate and inside the world; checked at load (`DATA015` to `DATA017`).
- Coverage is mandatory: every part of the world belongs to exactly one region. Checked by rasterising onto a grid of macro cells (proposed 64 m) and testing each cell centre (`TERR002` gap, `TERR003` overlap).
- A cell belongs to the region whose polygon contains its centre. Polygon containment uses a half-open rule, so polygons sharing an edge give every point exactly one owner, including points on the shared edge.
- `world_size_m` must be a multiple of the cell size (`TERR001`). The sample world is 20,480 m = 80 chunks of 256 m = 320 cells of 64 m.
- Region IDs share the global ID space; a clash with any record is a duplicate (`DATA005`).
- `biome` records are shaped like materials (tags and free-form numeric properties). No terrain-parameter vocabulary is invented yet; the height-field step will define what it reads.
- The generator is versioned (`terrain.macro_raster`, version 1) and its output has a checksum for golden tests.

## Consequences
- Easier: reviewable diffs, exact coordinates, automatic coverage proof, and a region field that aligns with chunks (4 x 4 cells per 256 m chunk).
- Harder: authors write polygons by hand, so an editor panel will be needed eventually (Planned, CLAUDE.md section 6.1). Slivers narrower than a cell can be missed by the cell-centre test; a region that vanishes entirely is reported as `TERR004` (warning).
- No holes: an island inside a region needs the surrounding region split into pieces.
- Cell size (64 m), chunk size (256 m) and world size (20,480 m) are proposals, recorded as such, until the Terrain3D spike.
