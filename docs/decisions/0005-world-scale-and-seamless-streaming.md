# 0005. World scale and seamless streaming
- Status: accepted (decisions) / assumptions partly verified: Terrain3D streaming by [ADR 0009](0009-terrain3d-integration-and-streaming.md), the rest still open
- Date: 2026-10-20

## Context
The developer wants to walk in a straight line for 30 minutes without a visible loading screen or stutter, in a world bigger than 10 km. At an assumed 5 to 6 m/s that is 9 to 11 km. The old D2 default (about 10 x 10 km) makes that walk reach the edge; walking 30 minutes from anywhere needs at least 20 km across.

Analysis ([explanation](../explanation/world-scale-and-streaming.md)): loading throughput is not the hard part (about 0.1 to 0.2 chunks per second at 256 m chunks and about 500 m draw distance). The hard part is frame-time spikes: scene-tree work, physics shape creation, GC pauses, shader compilation, large transform shifts and disk seeks on the main thread.

## Options considered
1. Keep about 10 km, accept reaching the edge — smallest content cost; does not meet "30 minutes from anywhere".
2. About 16 km — about 25 minutes from the centre in any direction; about 4 GB baked.
3. About 20 km — 30 minutes in any direction from anywhere; about 6 GB baked; rebasing needed.
4. About 30 km — about 14 GB baked; large content burden for one developer.

## Decision
Decided by the developer on 2026-10-20:
- **World size: about 20 x 20 km** (option 3). Replaces D2's default.
- **Visible transitions are allowed only for fast travel/teleport, entering dungeons/interiors, and the initial game load.** Everything else, including running and overworld mounts, is seamless.
- **Strategy** (CLAUDE.md section 4.9): async read, worker decode/build, time-sliced main-thread commit (initial cap about 1 ms per frame); ring-based streaming with hysteresis, prioritised by heading; per-chunk baked data stored as one contiguous blob per chunk and packed in spatial-curve order in a few large archive files; instanced foliage and props; pooled nodes and actors; no per-frame allocations; shader warm-up; snapshot-based background saving; budgeted promotion and demotion between simulation LOD zones.
- **Origin rebasing** (D6) stays the default and is now required at this size (float32 precision is about 1 to 2 mm at 10 to 15 km from the origin). It must be one root-transform shift scheduled between streaming commits.
- **Proof:** an automated traversal soak test is the M1 gate. It flies the full world at 2 to 3 times walking speed and records frame-time p50/p99/max, streaming milliseconds per frame, GC counts and memory.
- **Macro map and bake output** are in world metres and tiled by chunk from the start. Generation stays monolithic and offline; only the bake step slices into tiles with overlap borders.

## Assumptions to verify (not yet measured)
- Walking speed about 5 to 6 m/s (design value, not decided).
- Chunk size 256 m, draw distance about 500 m, vertex spacing 2 m: proposals, to be fixed by a later ADR after the spike.
- About 1 MB of baked data per chunk and 20 to 50 ms of decode/build per chunk on a worker: estimates.
- Terrain3D: **verified by the spike (ADR 0009).** Runtime region add and remove work; there are at most 32 x 32 region slots; the cost is a shared-texture rebuild of about 7 ms per frame with 256-vertex regions (16 to 34 ms with 512-vertex regions), so no own-mesh fallback is needed for now. The commit budget is frame-based, not 1 ms.
- Storage (open decision D10): SSD recommended, layout designed so a 7200 rpm HDD still works for walking; the developer has not confirmed the minimum spec.

## Consequences
- Easier: the walking stutter problem becomes a measurable budget instead of a hope; the contiguous per-chunk layout keeps HDD viable.
- Harder: content volume (about 400 km2) rests on generators plus hand-finishing; every streaming-related feature needs a main-thread cost budget; bake output size grows quadratically (about 6 GB).
- Fast travel and dungeon entry may fade, so destinations do not need predictive loading (they can start loading while the player chooses).
- New work: Terrain3D spike and traversal soak test (M1); a chunk-pack file format and spatial-curve ordering (later ADR); named constants for chunk size, vertex spacing and ring radii in one place.
