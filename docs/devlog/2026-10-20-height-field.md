# 2026-10-20 — height-field

## Goal
M1 terrain pipeline, step 2: terrain heights from the macro map's biomes, blended smoothly at region borders, with validators and a way to see the result.

## What was done
- Biome terrain vocabulary (approved by the developer): `base_height_m`, `relief_m`, `roughness`, `water_depth_m`; `BiomeTerrainParameters` reads and validates it.
- `GradientNoise` (seeded through `Rng.ForKey`), `ParameterGrid` (box blur and bilinear sampling), `HeightFieldGenerator`, `HeightField`, `HeightFieldSettings`.
- `HeightFieldValidator`: non-finite and extreme heights; slope across region borders.
- `HeightPreview` and a dependency-free `PngEncoder`; CLI `height-field` with `--seed`, `--spacing`, `--blend`, `--png`.
- Placeholder terrain values in the three sample biomes.
- Docs: ADR 0007, biome reference, validators, CLI reference, Gen system doc, tune-the-terrain how-to, glossary, known issues 23-28.

## Decisions made
- [0007 Height-field generation](../decisions/0007-height-field-generation.md): blurred per-cell parameters plus fractal gradient noise.
- Default blend raised from 256 m to 384 m after measurement (see below). All tuning values stay placeholders for the developer.

## Problems encountered and how they were solved
- **Blend was twice as wide as documented.** Two box-blur passes of the full radius spread a step over twice the requested distance. Found when a test expected exactly 100 m at 400 m from a border and got 93 m. Fixed (each pass uses half the distance, rounded up to whole cells) and pinned by a test that checks where the blend starts.
- **The fix exposed a real seam.** With the corrected 256 m blend the shipped world failed the seam validator at the highlands coast (worst slope 2.33; it had passed only because of the bug). Added `--blend`, measured 256/320/384/448/512 m (320 and up pass) and chose 384 m. The unit test did not catch this because 128 m spacing hides the steepest slopes; the CLI at 8 m did.
- Oracle tests that assume a blend now pin their own blend (256 m) so they do not depend on the tuning default; the shipped golden was recaptured after the default changed.
- Two test-side mistakes: float precision in an averaging assertion, and an overload ambiguity; code was correct.
- A local function could not capture a `Span`; the border check was restructured with a stats struct.
- Hill shading in the preview was too faint to show relief; strengthened and checked visually. The final preview shows ocean, rocky highlands with relief, smooth vale and gentle coasts.

## Tests added
- Gen: 78 new tests (94 total in `Realm.Gen.Tests`): noise properties and golden, biome parameters, parameter grid, generator oracles (exact flat values, midpoint by symmetry, blend extent, determinism, failure paths), validator, PNG (standard CRC-32 vector, zlib round trip), preview, shipped world (interior ranges worked from the biome values, coast, golden).
- Whole solution: 289 tests, 0 warnings.

## Docs added/updated
See "What was done". The tune-the-terrain how-to was written from real runs, including the seam failure and the fix.

## Open questions / next steps
- Developer: tune the look with the preview (noise wavelength, relief, roughness, blend), and decide real biome values.
- Next: erosion and rivers (CLAUDE.md 6.1 steps 3 and 4) are the next generator steps and need design input (how strong, how many rivers); or the chunk-tiling bake step once the chunk size is confirmed by the Terrain3D spike.
