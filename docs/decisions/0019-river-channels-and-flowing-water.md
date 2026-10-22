# 0019. River channels and flowing water surfaces
- Status: accepted
- Date: 2026-10-22

## Context
Rivers and lakes were colour painted onto whatever terrain was there (ADR 0018). The water had no level: on a slope it was a stripe
on a hillside, and where the ground dipped it read as a ditch with a texture on the bottom. The developer asked for a level water
surface that flows from the mountains to the valleys, with an animated flow texture that respects altitude and slope.

## Options considered
1. Keep painting, only change the texture — cheap, but the level problem stays.
2. Carve a bed and render a separate water surface mesh with a flow shader — real level, real flow direction; needs baked water geometry and a shader.
3. Simulate water (shallow-water equations) — far too heavy for the retro, 20 km target.

## Decision
Option 2.
- **Profile** (`RiverChannelBuilder.Profile`, in the erosion step): each river's centre line is smoothed; the level at each point starts as the average of the ground at the centre and at both banks, is smoothed, and may only fall along the flow. The end is tied to the sea (0 m), the level of the river it joins, or the lake it enters; no drop is steeper than `MaxDescent` (1.0, so steep mountain rivers become cascades instead of giant trenches).
- **Carve** (`RiverChannelBuilder.Carve`): inside the river's half width the ground becomes a parabolic bed below the level; outside it the bank may rise or fall from 0.35 m above the water at 0.6 slope, so on a hillside the high side is cut and the low side built up. Where two segments overlap, the nearest wins. Sea floor is only cut. Lake cells are cut 1.5 m below the lake level.
- **Mesh** (`WaterMesher`): river ribbons (three vertices across) and flat lake cells, split into 2 km tiles, with per-vertex flow speed, altitude and turbulence in the vertex colour; baked as `water.bin` (`WaterFile`).
- **Game** (`WaterLayer`): one mesh per tile with a shader that scrolls a ripple texture along each river at `RiverFlow.Speed` (from the slope and the flow), adds foam where steep or near the banks, and tints from murky lowland to clear highland.
- Terrain3D still paints the bed with the water texture (ADR 0016 follow-up), so any sliver of bed that shows is water coloured.

## Consequences
- Rivers and lakes have a true level; goldens of the eroded terrain, placement and bake changed (erosion generator v3).
- The carve adds cut and fill along rivers; roads and settlements see the final ground because the channels are cut inside the erosion step.
- All water tiles are built at start (a few tens of tiles); streaming them per region is a later step (known issue 69).
- No swimming, current force, depth or reflections yet.
