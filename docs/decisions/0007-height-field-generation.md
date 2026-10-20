# 0007. Height-field generation: biome parameters, blurred blend, gradient noise
- Status: accepted (vocabulary) / placeholders (tuning values)
- Date: 2026-10-20

## Context
CLAUDE.md section 6.1 step 2 asks for per-biome noise profiles blended at borders, with validators for NaN or extreme heights and seamless region borders (section 7.1). The generator must be deterministic (section 4.2), run on worker threads (4.8), work in world metres and tile later by chunk (ADR 0005, ADR 0006). The developer had to decide what a biome tells the generator.

## Options considered
1. Per-vertex nearest-region lookup with a hard switch of noise profile — simple, produces cliffs at region borders.
2. Per-vertex distance-to-border weighting across regions — smooth, but needs a distance transform or neighbour search per vertex and gets complicated at region corners and multi-polygon regions.
3. Biome parameters per macro cell, blurred over the blend distance, then sampled bilinearly per vertex, combined with fractal noise — smooth borders, linear in cell and vertex counts, no special cases at corners.

## Decision
Option 3. The developer approved the biome vocabulary on 2026-10-20:
- `base_height_m`: typical height above sea level (land biomes).
- `relief_m`: how far terrain rises or falls from the base (half the peak-to-peak range). All biomes.
- `roughness`: 0 to 1; how slowly fine noise detail fades. All biomes.
- `water_depth_m`: depth below sea level (ocean biomes, tag `ocean`); the base height is minus this value.
- Sea level is 0 m.

Algorithm: `height = base + relief * fractal_noise(x, z)`. `base`, `relief` and `roughness` come from the region's biome per macro cell, are blurred with two box-blur passes, and are bilinearly interpolated at each vertex. Roughness maps to the per-octave gain (`MinGain` to `MaxGain`). Noise is 2D gradient noise whose lattice table is shuffled by `Rng.ForKey(worldSeed, "terrain", "height")`. Vertices are evaluated in parallel; each depends only on its own position, so results do not depend on thread scheduling.

Details:
- The blend distance (`BlendRadiusMeters`) means how far parameters blend to each side of a border. Two box-blur passes add up, so each pass uses half the distance rounded up to whole macro cells. (A first version used the full distance per pass and blended twice as far as documented; found by a test and fixed.)
- The grid has `world / spacing + 1` vertices per side so neighbouring chunks share edge vertices. Heights are 32-bit floats; sea level is a constant.
- Generator `terrain.height_field` version 1; `HeightField.Checksum()` for golden tests.
- Validators (new codes `TERR006` to `TERR014`): biome property presence and ranges, non-finite and extreme heights, and slopes across region borders above a limit (placeholders: 4000 m and rise over run 2).

## Tuning values (placeholders for the developer)
Noise wavelength 2048 m, 6 octaves, lacunarity 2, gain 0.35 to 0.60, blend 384 m (default), default spacing 8 m. The shipped biome values (ocean 200 m deep, forest 60 +- 40 m, highlands 400 +- 350 m) are placeholders. With a 256 m blend the shipped world failed the seam validator (worst slope 2.33 at the highlands coast); 320 m and above passed, so 384 m was chosen.

## Consequences
- Easier: smooth region borders with no corner special cases; all knobs are data or one settings record; a CLI preview image makes tuning visible.
- Harder: the blend is quantised to macro cells (64 m); a very thin region can be blurred away; the seam slope depends on vertex spacing (finer spacing resolves steeper local slopes).
- At the proposed final 2 m spacing a 20 km world has about 105 million vertices: roughly 420 MB as floats and about 45 seconds on this machine's thread count (estimated from the 8 m run of 2.8 s). Baking will need 16-bit storage and chunk tiling; not done yet.
- No erosion, rivers, ravines, roads or settlements yet (CLAUDE.md section 6.1 steps 3 to 9).
