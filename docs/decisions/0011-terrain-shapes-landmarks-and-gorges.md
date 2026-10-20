# 0011. Terrain shapes, landmarks and gorges
- Status: accepted
- Date: 2026-10-20

## Context
Every region was "base height + relief x smooth noise", so a "mountainous" biome was just a large hill. The developer wants lonely hills, a mountain standing in a forest, rocky mountains, rivers with cliffs and lagoons. The developer chose: landmarks **scattered by biome** (not hand-placed) and mountain character **mixed by region** (ridged peaks in alpine areas, terraced cliffs in badlands).

## Options considered
1. Only add more noise octaves — more detail, but still no distinct shapes; rejected.
2. Per-biome shape properties plus landmark scattering plus a gorge step (chosen) — data-driven, deterministic, testable, no new asset types.
3. Hand-placed landmarks in the macro map YAML — full control but more authoring; offered, not chosen. It can be added later next to the scattering.

## Decision
`terrain.height_field` v2:
- The noise lookup is domain-warped (300 m at 1,200 m wavelength) to break up regular blobs.
- `ridge` (0 to 1, per biome, blurred across borders like other parameters) blends smooth fractal noise toward ridged noise (`GradientNoise.Ridged`, at most 4 octaves).
- `terrace` (0 to 1) blends heights toward flat steps with short risers (`TerraceStepMeters`, 30 m).
- Landmarks: each biome may set `hill_*`, `mountain_*`, `basin_*` (density per 100 km2, height or depth, radius). The world is cut into 1 km cells; each kind gets one deterministic candidate per cell (`Rng.ForKey(seed, "terrain", "landmark", kind, cx, cz)`), accepted with probability density/100, sized within +-30 percent, and kept only when a ring of 8 points at 1.3 x radius lies in the same region (no seams). Shapes: hill = smooth bump; mountain = envelope x (0.3 + 0.7 x ridged noise); basin = flat-bottomed depression that the erosion step fills into a lake or lagoon.
- The default blend radius rises from 384 m to 512 m: ridged mountains failed the seam check (TERR014) at 384 m.

`terrain.erosion` v2: `GorgeCarver` runs after thermal relaxation (which would otherwise slump the walls). For each segment of a smoothed river, the floor is `depth` below the ground beside the river and walls rise at slope 2 until they meet the original ground. `depth = gorge_depth_m x smoothstep(flow / 10 km2) x clamp(height / 20 m)`. Vertices take the minimum over segments, so river order does not matter.

Steep ground (slope above 0.5, fully rock at 1.0) is baked and previewed as rock colour so cliffs read as rock.

## Consequences
- Easier: new landscape character comes from YAML properties; every part has tests with hand-worked oracles (landmark counts and containment, terrace formula, gorge profile).
- Harder: more tuning knobs; all shipped values are placeholders. Landmarks are scattered, so a developer cannot yet say "a mountain exactly here" (option 3 is the follow-up).
- Known: the erosion step fills closed valleys in the alpine ridges into two large lakes (about 5 km2 each), which look odd; river cross-sections are carved on the final 8 m grid but the bake resolution is 4 m, so gorges are only a few vertices wide.
- Golden checksums for both generators were regenerated; baked output must be regenerated (`bake-terrain`).
