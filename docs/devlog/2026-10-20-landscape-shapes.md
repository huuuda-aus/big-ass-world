# 2026-10-20 — landscape-shapes

## Goal
Make the landscape less like smooth hills: lonely hills, a mountain in the middle of a forest, rocky mountains (not big hills), rivers with cliffs, lagoons. Developer choices: landmarks scattered by biome, mountain style mixed by region.

## What was done
- `GradientNoise.Ridged`, domain warping and terracing in the height field; new optional biome properties `ridge`, `terrace`, `hill_*`, `mountain_*`, `basin_*`.
- `LandmarkPlacer` and `LandmarkShaper` (jittered 1 km cells, containment inside one region, three kinds).
- `GorgeCarver` and the `gorge_depth_m` biome property; steep ground is baked and previewed as rock colour.
- Content: placeholder values for all six land biomes; baked world regenerated; screenshots taken of a lone mountain in the forest, a river gorge and rocky mountain faces.
- Fixed a placer bug found by testing: 20,480 / 1,000 left a strip of the world without landmark cells.
- Docs: [ADR 0011](../decisions/0011-terrain-shapes-landmarks-and-gorges.md), biome reference, tune-the-terrain how-to, gen system doc, CHANGELOG, known issues 44-46.

## Decisions made
[ADR 0011](../decisions/0011-terrain-shapes-landmarks-and-gorges.md).

## Problems encountered and how they were solved
- The seam validator (TERR014) rejected the ridged mountains next to the sea: slopes of 2.3 to 3.2 across the border. Fixes that kept the validator: ridged noise limited to 4 octaves at 0.8 of the gain, alpine ridge 0.7 instead of 0.9, landmarks kept inside their region, and the default blend raised 384 -> 512 m.
- Landmarks near region borders made their own steps; the containment ring check removed them.
- An existing test (`ReliefMakesTheTerrainActuallyVary`) depended on where the noise happened to be; it now sets a short feature wavelength. The shipped-world range test now allows for what landmarks and terraces can add.
- The terrace test first compared a hand-picked "fraction on a step" that the noise distribution did not satisfy; it now checks the step formula against the unterraced heights vertex by vertex.
- The edge of the world crashed the game earlier the same day (separate fix, see CHANGELOG).

## Tests added
Landmark placement, shapes, terracing and ridging (`LandmarkTests`), ridged noise, biome property parsing, `GorgeCarverTests` (profile, order independence, never raises), a shipped-world gorge test, colouring tests. Whole solution: 505 tests, 0 warnings.

## Docs added/updated
See above.

## Open questions / next steps
- Developer: look at the world and say what to change (more or fewer landmarks, taller mountains, big alpine lakes).
- Possible follow-ups: hand-pinned landmarks in the macro map; per-biome ground textures (rock and sand); cliff mesh kit for the sheer walls.
