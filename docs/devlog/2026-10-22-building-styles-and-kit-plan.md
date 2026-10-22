# 2026-10-22 — building-styles-and-kit-plan

## Goal
The developer asked how buildings could be replaced with real art, then decided: no snap-to-grid or preset sizes, tiling textures, interiors random for now, advice on supplying textures; and asked for thick walls and ceilings, two building types (rich as now, slum small and muddy), and house caps of 250 per city and 50 per town.

## What was done
- Plan: [ADR 0015](../decisions/0015-modular-building-kit.md) (proposed): tagged kit pieces, feature plus tiling fill pieces, region styles as data, texture supply advice. Nothing of the kit is built yet.
- `BuildingStyle` (Rich, Slum) from the ward; slum lots of 34 m2 and dwellings of at most about 5.8 x 5.2 m, one storey, one room, one window, mud colour, dark roof, no timber.
- Thick walls: outer 30 cm (rich) or 15 cm (slum); walls between rooms are solid 15 cm boxes with doorways cut as separate wall runs (jambs and lintels); floors and ceilings are 30 cm slabs per storey boundary with visible edges round the stairwell; collision follows.
- Settings: 250 and 50 houses; the city now takes the smallest ward count that holds them (otherwise the outer slums are trimmed first).
- Road validator: bed check skipped where two roads share a corridor.
- World re-baked.

## Decisions made
ADR 0015 (proposed).

## Problems encountered and how they were solved
- The bake reported `ROAD003` (1.2 m bed mismatch) on a road that shares its last kilometre with another: the later road's bed wins. The validator now leaves out the bed check where roads overlap; cut and fill still apply.
- A first aerial screenshot showed no city: the city builds about 2 s after load and the screenshot was taken too early.
- The fixed ward count would have trimmed the slums away, so the count is now chosen by shrinking until just enough houses remain.

## Tests added
`BuildingStyleTests` (8: style by ward, slum size, wall thickness, mud colours and no timber, 30 cm slabs, solid interior walls, house caps); service tests at 250 and 50; the oracle tests now use the style's wall colour and roof; placement golden recaptured.

## Docs added/updated
ADR 0015, settlements system and reference, CLI example, glossary, known issues 59 to 61, CHANGELOG, this entry, index.

## Open questions / next steps
- Developer: look at the slum dwellings and the thick interiors; say if the mud colour is right and whether cities should be denser (issue 61).
- If ADR 0015 is approved: the five-piece prototype.
