# 2026-10-20 — erosion-and-rivers

## Goal
M1 terrain pipeline, steps 3 and 4: hydraulic and thermal erosion, rivers and lakes, driven by per-biome properties (CLAUDE.md section 6.1).

## What was done
- Agreed with the developer how biomes feed this step: `rainfall`, `erodibility`, `max_slope_deg`, `river_loss`; rivers emerge from flow and cross borders (the biome only supplies inputs); ocean biomes ignore them.
- Built `FlowRouter` (priority-flood), `FlowAccumulator` (rainfall in effective km2, loss per km), `StreamPowerEroder` (implicit step), `ThermalEroder`, `RiverExtractor` (rivers, lakes, basin fill), `RiverValidator`, and the `ErosionGenerator` orchestration with `ErodedTerrain` and `RiverNetwork`.
- Preview: rivers and lakes in blue, region borders in pale lines. CLI: `--erode`, `--iterations`, `--strength`, `--threshold`, `--min-lake-area`.
- Replaced the sample world: six land regions plus the ocean, with four new placeholder biomes (the developer said they could only see two biomes).
- Rewrote the shipped-world tests to derive their expectations from the content.
- Docs: ADR 0008, tune-erosion-and-rivers how-to, updates to biome, world, validators, CLI, Gen system doc, tune-the-terrain, add-a-region (new tested example), add-a-biome, glossary, known issues 29-35, tutorial, index, CHANGELOG.

## Decisions made
- [0008 Erosion and rivers](../decisions/0008-erosion-and-rivers.md).
- Defaults chosen from experiments: strength 2, minimum lake area 4 km2, lake depth 2 m (placeholders for the developer).

## Problems encountered and how they were solved
- **Lakes everywhere.** The first version produced 194 lakes on the old sample: raw noise has many closed basins and gentle erosion never breached them. Raising erosion drained them but flattened the landscape (strength 500 over 60 iterations: deepest carve 574 m, highest point 576 m to 224 m). The fix was structural: fill small and shallow basins as sediment and keep only large lakes, with moderate strength.
- **`LakeMinDepthMeters = 0` made every vertex a lake.** Found by a failing test; validation now requires a positive value.
- **My tests assumed things the design does not promise:** exact identity under "no erosion" ignored basin fill; "rivers sorted by polyline length" is actually sorted by flow-path length to the outlet (docs fixed); "full river loss gives only dried rivers" ignored that parameters blend across the coast; terrain in the small test world was too gentle for slope tests and had tiny catchments for carving. Tests now isolate the effect they check (for example compare against the same run with erodibility 0).
- **Alpine peaks next to the sea failed the seam check** (base 700 m: slope 2.57). Measured base 400 m with relief 600 m (passes), 800 m relief fails; kept 600.
- **Hand-written fences.** An early edit of the CLI reference left an empty code block and a stale command; found by inspecting the file and fixed.
- The artifact preview page was published twice so the developer could look at the result.

## Tests added
- Erosion: 92 tests under `tests/Realm.Gen.Tests/Erosion` (router, accumulator, stream power, thermal, river extractor, biome parameters, generator, shipped world); preview region borders (2). Whole solution: 381 tests, 0 warnings.

## Docs added/updated
See "What was done". Every example output in the docs comes from a real run on the current content.

## Open questions / next steps
- Developer: tune the look (river threshold, strength, lake size) and the real region layout and biome values.
- Next: Godot. The installed builds are the standard edition (4.4.1, 4.5.1, 4.7), not the .NET edition that the C# projects need; a decision is needed before `Realm.Game` can be created.
