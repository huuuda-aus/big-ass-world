# 0009. Terrain3D integration and region streaming parameters
- Status: accepted (method, findings) / proposed (region size, to be confirmed by the developer)
- Date: 2026-10-20

## Context
[ADR 0005](0005-world-scale-and-seamless-streaming.md) listed Terrain3D's runtime region streaming as the biggest unverified assumption and asked for a spike before anything depended on it. The spike ran on Godot 4.5.1 (.NET edition) with Terrain3D 1.0.2 on the developer's machine (Intel Lunar Lake GPU, Vulkan, 8 cores): a camera flies at constant speed over procedurally generated regions that are streamed in and out, every frame time is recorded, and long frames are matched to the streaming steps that happened in them. The harness is `src/Realm.Game/Spike/StreamingSpike.cs`; see the [how-to](../how-to/run-the-streaming-spike.md).

## What the extension's own class listing showed
Taken from `ClassDB` at runtime, not from memory:
- A fixed grid of **32 x 32 region slots** (`Terrain3DData.REGION_MAP_SIZE = 32`, coordinates -16 to 15). The world can be at most 32 regions wide, so a 20,480 m world needs regions at least 640 m wide.
- A region is `region_size` vertices (64, 128, 256, 512, 1024 or 2048) at `vertex_spacing` metres (0.25 to 10). Its width is the product.
- Runtime control: `add_region`, `add_region_blank`, `remove_region` (and `remove_regionl`), `load_region` and `save_region`, each with an `update` flag, plus `update_maps` to rebuild shared textures.
- Defaults: region size 256, spacing 1 m, collision mode "dynamic game", version 1.0.2.

## Measurements
Flight of 3,000 m at 120 m/s (about 25 s, about 6,900 frames), load radius 1 region and unload radius 2, single runs, so indicative. Frame time median was 3.6 to 3.9 ms (about 270 fps) in every configuration. "Add" and "remove" are main-thread milliseconds per region; "rebuild" is the shared-texture update.

| Region (vertices x spacing = width) | Streaming mode | Add | Remove | Rebuild | Worst frame |
|---|---|---|---|---|---|
| 512 x 2 m = 1,024 m | rebuild on every add, build on main thread | 28.9 ms | 15.7 ms | in add | 44 ms |
| 512 x 2 m | batched rebuild, build on main thread | 11.0 ms | 0.3 ms | 17.2 ms | 42 ms |
| 256 x 4 m = 1,024 m | rebuild on every add, build on main thread | 9.1 ms | 7.3 ms | in add | 18.5 ms |
| 256 x 2.5 m = 640 m | rebuild on every add, build on main thread | 9.3 ms | 6.9 ms | in add | 17.0 ms |
| **128 x 8 m = 1,024 m** | rebuild on every add | **2,363 ms** | **2,292 ms** | in add | **4.7 s** |
| 512 x 2 m | batched, **build on worker thread** | 0.11 ms | 0.28 ms | 16.2 ms | 33 ms |
| 512 x 2 m, 25 regions loaded | batched, build on worker | 0.08 ms | 0.17 ms | **34.4 ms** | **57 ms** |
| **256 x 4 m = 1,024 m** | batched, build on worker | 0.09 ms | 0.20 ms | **6.8 ms** | **14.0 ms** |
| **256 x 2.5 m = 640 m** | batched, build on worker | 0.09 ms | 0.47 ms | 6.4 ms | 15.3 ms |
| 256 x 2.5 m with dynamic collision | batched, build on worker | 0.10 ms | 0.17 ms | 7.1 ms | 18.3 ms |
| 256 x 4 m, whole crossing in one frame | batched, build on worker | 0.10 ms | 0.20 ms | 7.0 ms | 15.0 ms |

Where the cost goes (512-vertex regions): about 11.6 ms to build the image and region object (main thread unless moved), about 17 ms for the rebuild. Every long frame in the final runs is one rebuild (5.6 to 9 ms) on top of a normal 4 ms frame. Building regions on a worker thread produced no crashes and no log warnings in any run.

## Follow-up measurements: smaller load set and walking speed
The developer pointed out that the game never needs a 3 km square of terrain loaded, and that walking is far slower than the spike's 120 m/s flight. Both were measured (256 vertices x 4 m = 1,024 m regions, batched, built on worker threads, one commit per frame). The load set is now defined by a **view distance in metres** (`RegionGrid.RegionsWithin`, tested in Core) rather than a ring of regions.

| Run | Regions loaded at end | Rebuilds | Rebuild cost | Worst frame | Preload |
|---|---|---|---|---|---|
| 120 m/s, view 1,100 m (9 to 12 regions) | 9 | 13 | 6.7 ms | 12.7 ms | 511 ms |
| 120 m/s, **view 480 m** | **2** | 4 | **3.7 ms** | **10.9 ms** | 101 ms |
| Walking 6 m/s for 90 s across a region boundary, view 480 m | 2 | **0** | none | 12.4 ms (no streaming step) | 100 ms |
| Walking 6 m/s, view 1,100 m | 12 | 3 | 8.1 ms | 32.9 ms (no streaming step; one gen-2 GC in the run) | 381 ms |

- **With 1,024 m regions and a view under 512 m the player never needs more than 4 regions** (a point can be within 500 m of at most one neighbour per axis); typically 2 to 4. With a view over 512 m it can reach 9. Keep the fog and load distance below half a region width.
- **Walking cuts crossing frequency about 20 times** (a boundary about every 170 s at 6 m/s, against every 8.5 s at 120 m/s) and gives workers minutes, not seconds, to prepare regions. It does not make one rebuild cheaper; faster travel (mounts, flying) restores the rate.
- **A few frames of 7 to 12 ms occur with no streaming step at all** (5 of 23,238 frames in the quiet walk). Likely sources are the desktop compositor, the driver, Terrain3D internals or the .NET garbage collector; not established. The soak test must record GC and OS counters to tell.

## Decision
- **Build the Terrain3D region objects (image, region, maps) on the worker thread**, commit with `update=false`, and run **one `update_maps` per frame** for all adds and removes of that frame.
- **Use 256-vertex regions.** 512-vertex regions are too slow (rebuild 16 to 34 ms); 128-vertex regions are pathologically slow (about 2.3 s per add; worth reporting upstream).
- **Proposed: 256 vertices at 4 m spacing = 1,024 m regions** (developer to confirm the 4 m terrain resolution). A 1,024 m region is 4 x 4 streaming chunks of 256 m and 16 x 16 macro cells of 64 m, so Terrain3D regions, chunks and the generator grid align; 20 of the 32 slots per axis are used, leaving room to grow the world. The alternative, 256 x 2.5 m (640 m regions), fills all 32 slots exactly (20,480 m) but does not align with 256 m chunks.
- **Replace the fixed 1 ms commit budget** in CLAUDE.md section 4.9 by a frame-based budget. One rebuild costs about 7 ms with 9 to 12 loaded regions, so the rest of the frame must stay under about 9 ms for 60 fps at a region crossing. The soak test measures this directly.
- Keep the loaded set small: **load by view distance, kept below half a region width (480 m for 1,024 m regions)**, with a hysteresis margin for unloading. Rebuild cost scales with loaded pixels (25 regions of 512 x 512 cost 34 ms; 2 regions of 256 x 256 cost 3.7 ms).
- All Terrain3D calls live in `Realm.Game/Bridge/TerrainBridge.cs` (CLAUDE.md section 5.2) with a version check against 1.0.2.

## Consequences
- At walking speed a 1,024 m region boundary is crossed about every 3 minutes (about 170 s at 6 m/s), so a rebuild frame of 11 to 15 ms is rare; at 120 m/s (the spike) it is every 8.5 s.
- Terrain resolution is 4 m (was proposed as 2 m): the baked height data for the 20 km world is 5,121 x 5,121 vertices (26 million) and about 315 MB as 256 x 256 float regions with control and colour maps.
- Not measured yet: real textures and materials, foliage instancing, navigation, actors, disk reads (regions were generated procedurally), a full 30 minute walk, collision tied to a player body, and origin rebasing. They remain part of the M1 soak test.
- Only Linux was measured. Terrain3D's Windows binary is vendored but untested here.
