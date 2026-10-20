# 0008. Erosion and rivers: stream-power carving, basin fill, biome-driven inputs
- Status: accepted (vocabulary and method) / placeholders (tuning values)
- Date: 2026-10-20

## Context
CLAUDE.md section 6.1 steps 3 and 4 ask for hydraulic and thermal erosion, and rivers traced downhill with lakes at basins. The developer decided how biomes feed this step: erosion and river levels come from per-biome properties, blended across borders like the height properties, but rivers themselves are not owned by a biome (a river crosses borders, so a biome can only supply the inputs). The step must be deterministic (section 4.2), run offline on the whole world, and use only data the developer approved.

## Options considered
1. Droplet (particle) hydraulic erosion — familiar look, but droplets update shared terrain in parallel (hard to keep deterministic) and need huge droplet counts at 6.5 million vertices.
2. Flow accumulation plus implicit stream-power erosion on a coarse grid, then thermal slope relaxation on the full grid — deterministic, ordered, linear cost per iteration.
3. Hand-placed rivers only — full control, but no emergent drainage and a lot of authoring; better as a later locking and editing layer.

## Decision
Option 2. The developer approved the vocabulary on 2026-10-20: `rainfall`, `erodibility`, `max_slope_deg`, and `river_loss` so that rivers can fade in dry biomes. Ocean biomes ignore all four.

Method (`terrain.erosion` version 1):
- **Routing** (priority-flood on a coarse working grid, default 32 m): vertices visited from the outlets (world edge and everything at or below sea level) upward by height, depressions filled to their lowest spill point, each vertex draining to its steepest lower neighbour among those visited earlier. Ties break by vertex index.
- **Flow accumulation:** each vertex adds `rainfall x area`, measured in effective square kilometres of catchment, and passes it downstream, losing `river_loss` of it per kilometre travelled (the loss of the vertex water drains from).
- **Hydraulic erosion:** an implicit stream-power step per iteration (Braun and Willett's linear-slope scheme, flow exponent 0.5), erodibility times strength times `flow^0.5 / distance`. Unconditionally stable: a vertex ends between its old height and its receiver's new height. Depressions and outlets are not eroded.
- **Basin fill:** depressions that are not kept lakes are raised to their water level every iteration (small and shallow basins stand for sediment-filled floodplains, so rivers cross them). A lake is kept when its surface is at least `MinLakeAreaKm2` (default 4) and it is at least `LakeMinDepthMeters` (default 2 m, must be positive) deep.
- **Carve transfer:** the coarse carve is bilinearly applied to the full-resolution field.
- **Thermal relaxation** on the full field: where the slope between neighbours exceeds the local stable slope (the biome's `max_slope_deg`, blurred), a tenth of the excess slumps downhill. Mass conserving; each pair's transfer depends only on that pair, so rows run in parallel with identical results.
- **Rivers:** land vertices whose flow reaches `RiverThresholdKm2` (default 2). Rivers start where no river vertex lies upstream, are traced longest flow path first, and end at the sea, the world edge, a confluence with an earlier river, or by drying up (flow fell below the threshold, for example through river loss).
- **Output** (`ErodedTerrain`): the new `HeightField`, a `RiverNetwork` (rivers as polylines with flow, lakes as surface height, area and cells), the deepest carve, and a checksum. Validators: the height-field validators again, plus `TERR016` (river rises downstream) and `TERR017` (river ends somewhere inconsistent).

## What tuning taught us
- With gentle erosion (strength 0.5) the raw noise terrain filled with 194 lakes, because closed basins are never breached.
- Cranking erosion up did drain them but flattened the landscape: strength 500 over 60 iterations carved 574 m deep and cut the highest point from 576 m to 224 m. The implicit law has no uplift, so more erosion only wears everything down.
- Hence the separation: fill small basins, keep only large lakes, and use moderate strength (default 2; deepest carve about 70 m on the sample world) for valley character.
- `LakeMinDepthMeters = 0` would make every vertex a lake, so validation requires it to be positive.
- Tuning knobs of the default run on the sample world: threshold 1 km2 gives 35 rivers, 6 km2 gives 7; strength 10 carves 275 m and drains the remaining lake.

## Tuning values (placeholders for the developer)
Working grid 32 m, 20 hydraulic iterations, strength 2, thermal passes 20, river threshold 2 km2, lake depth 2 m, minimum lake area 4 km2, blend 384 m. The shipped biome values and the six-region sample layout are placeholders.

## Consequences
- Easier: rivers and lakes emerge from rainfall and terrain, driven entirely by biome data; the result is checksummed and tested with hand-checkable cases.
- Heights are no longer bounded by `base +- relief` after erosion: valleys are lower, and filled basins can rise by up to the lake depth. `relief_m` means relief before erosion.
- River polylines use the coarse working-grid water surface; they are not yet burned into the full-resolution terrain, and nothing renders water yet. No locking or hand-editing of rivers (CLAUDE.md section 4.4) exists yet.
- Cost: 11.5 s at 8 m vertex spacing, 6.9 s with a 32 m field (single runs; the 32 m working grid dominates). The thermal pass allocates a double per full-resolution vertex (about 52 MB at 8 m, about 840 MB at 2 m), so baking at 2 m needs a tiled approach.
