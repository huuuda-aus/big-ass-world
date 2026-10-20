# World scale and seamless streaming

Decision record: [ADR 0005](../decisions/0005-world-scale-and-seamless-streaming.md). Rules: CLAUDE.md section 4.9.

## The goal in numbers

Walking speed is an assumption (about 5 to 6 m/s), not a design decision yet.

| Quantity | Value |
|---|---|
| 30 minutes of walking | 9 to 11 km |
| Target world | about 20 x 20 km, so 30 minutes is possible in any direction from anywhere |
| Draw distance (retro fog) | about 500 m, about 85 s of lead time at 6 m/s |
| Chunk size (proposal) | 256 m; one chunk row crossed every 43 s |
| New chunks needed while walking | about 5 to 9 per border, about 0.1 to 0.2 per second |

Throughput is easy. What breaks smoothness is any single frame doing too much: scene-tree work, physics shapes, GC pauses, shader compiles, a big transform shift, or a disk seek on the main thread.

## Storage

Typical figures, not measured on the developer's machine. Per-chunk size (about 1 MB) and decode time (20 to 50 ms) are estimates.

| Device | Sequential | Latency per random read |
|---|---|---|
| 7200 rpm HDD | 100 to 150 MB/s | 8 to 12 ms |
| SATA SSD | about 500 MB/s | about 0.1 ms |
| NVMe SSD | 2 to 7 GB/s | 0.02 to 0.1 ms |

Disk time to load one chunk:

| Layout | HDD | SATA SSD | NVMe |
|---|---|---|---|
| One contiguous read | about 18 ms | about 2 ms | about 0.5 ms |
| About 10 small files | about 110 ms | about 3 ms | about 1 ms |

Disk time per situation:

| Situation | Data | HDD, contiguous | HDD, scattered | SATA SSD | NVMe |
|---|---|---|---|---|---|
| Walking to a new chunk border (about 5 chunks, 43 s of lead) | about 5 MB | 0.1 s | 0.6 s | 10 ms | 3 ms |
| Fast travel or initial load (about 25 near chunks) | about 25 MB | 0.45 s | 2.8 s | 53 ms | 12 ms |

Add 0.15 to 0.3 s of worker decode/build for 25 chunks with 4 workers.

Takeaways: walking does not depend on the drive as long as reads are asynchronous; fast travel does, but a fade covers it; layout matters more than the drive, so each chunk is one contiguous blob and chunks are packed in spatial-curve order.

## Precision

Float32 precision grows with distance from the origin.

| Distance from origin | Precision |
|---|---|
| 4 to 8 km | about 0.5 mm |
| 8 to 16 km | about 1 mm |
| 16 to 32 km | about 2 mm |

A 20 km world centred on the origin reaches 10 km out (about 1 mm). Millimetre jitter in physics and shaders is not acceptable, so origin rebasing stays the default. It must be a single root-transform shift between streaming commits.

## Size of the baked world

| World | Chunks (256 m) | Baked (about 1 MB each) |
|---|---|---|
| 10 x 10 km | about 1,500 | about 1.5 GB |
| 16 x 16 km | about 3,900 | about 4 GB |
| 20 x 20 km | about 6,100 | about 6 GB |
| 30 x 30 km | about 13,700 | about 14 GB |

## What is not verified

- Terrain3D's runtime region streaming: unclear from current documentation. A Godot spike must test adding and removing regions during a fly-through before anything depends on it.
- The per-chunk size and cost estimates above.
- Godot's behaviour for creating physics shapes and collision data off the main thread. Verify against the Godot version chosen at M1.

## Related
- [Determinism](determinism.md), [Sim system](../systems/sim.md)
