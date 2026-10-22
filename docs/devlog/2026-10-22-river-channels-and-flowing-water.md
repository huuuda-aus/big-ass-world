# 2026-10-22 — river channels and flowing water

## Goal
Make the water level actually level (rivers were ditches under a texture) and give it a flow from mountains to valleys, animated, respecting altitude and slope.

## What was done
- Palisade rebuilt from overlapping round logs; rivers and lakes got an opaque water texture (Terrain3D control map).
- `Realm.Gen.Terrain.Water`: `RiverChannelBuilder` (profile and carve), `RiverChannel`, `ChannelPoint`, `RiverFlow`, `WaterMesher`, `WaterTile`, `WaterFile`; `ErosionSettings.CutRiverChannels`; `RiverNetwork.Channels`; erosion generator v3.
- `realm bake-terrain` writes `water.bin`; the game's `WaterLayer` builds the tiles with a flow shader.

## Decisions made
ADR 0019.

## Problems encountered and how they were solved
- Taking the lowest bank as the level made trenches up to 14 m deep along a ridge-hugging river: the level is now the average of centre and banks, with cut and fill.
- Limiting the fall to 0.25 gave an 80 m trench on a coastal escarpment: the limit is 1.0.
- A shelf raised at a river mouth made steep steps over the open sea (TERR014): sea floor is only cut.
- Banks climbing to a deep cut stopped short and left cliffs: the bank run now grows with the height gap.

## Tests added
`RiverChannelTests` (never rises, bed below water, banks hold, sea mouth, speed, mesh and file round trip); texture test for water.

## Docs added/updated
ADR 0019, CHANGELOG, known issues 68 and 69, settlements doc.

## Open questions / next steps
Stream water tiles with the terrain; lake shores are blocky (32 m cells); waterfalls have no special mesh; sea plane does not use the flow shader.
