# 0010. Baked terrain format: region tiles, baked colour and hill shading
- Status: accepted
- Date: 2026-10-20

## Context
The game needs the generated terrain as data it can stream (CLAUDE.md section 4.4: generate, bake, edit; section 4.9: seamless streaming). [ADR 0009](0009-terrain3d-integration-and-streaming.md) fixed the streaming unit at one Terrain3D region of 256 x 256 vertices at 4 m (1,024 m). Terrain3D has no textures in the project yet, so a bare height map would render flat and unlit. Loading must not depend on Godot (tests, CLI), and a damaged file must never reach the renderer.

## Options considered
1. EXR or R16 images (CLAUDE.md lists them as importable by Terrain3D) — standard tools, but heights are 32-bit floats, colours need a second image, and reading needs Godot or a codec.
2. Terrain3D's own `.res` region files — would tie the bake step to Godot and to one addon version.
3. A small custom binary tile with a checksum, plus a JSON manifest — trivial to read and write in plain C# (unit-testable), one contiguous read per region (ADR 0005), and the game converts to Terrain3D images in memory.

## Decision
Option 3. See the [format reference](../reference/baked-terrain.md).
- One `.tile` file per region: magic, version, region coordinates, size, spacing, float32 heights, packed RGBA colours, FNV-1a checksum. The reader rejects short, wrong, unknown-version and damaged files with distinct messages; writes go through a temporary file and a move.
- `manifest.json` records geometry, seed, generator versions, height range, river and lake counts, spawn point and a fingerprint of all tile checksums.
- Heights are written as 32-bit floats, 256 x 256 x (4 + 4) bytes = 512 KB per region, about 202 MB for the 20 km world. 16-bit storage is a later optimisation.
- The colour map bakes a placeholder height palette, **hill shading lit from the north-west** (using the field's own neighbours so tile edges are seamless), and rivers and lakes painted blue. Until real textures exist this is what makes relief and water visible; it is cheap and fits the retro look.
- The baker lives in `Realm.Gen.Bake` (no Godot). `realm bake-terrain` runs the whole pipeline; the game only reads tiles.
- Tiles are added to the Git LFS rules in `.gitattributes`.

## Consequences
- Easier: the format is covered by unit tests (round trip, every failure mode, tiling against the field by world coordinate, hill shading against an independent formula, painted rivers and lakes by geometry); loading is one file read per region on a worker thread.
- Bug found by those tests: the first hill-shading formula (and the one in the preview image code) lit the terrain from the wrong side. Both were fixed and a test pins the direction.
- Harder: a format change needs a version bump and a reader for the old one; there is no tooling to inspect tiles except the code.
- Not done: texture splat data, control maps, vegetation and prop instancing data, navigation, per-region metadata for NPCs (all planned), and chunk-level (256 m) streaming for content other than terrain.
