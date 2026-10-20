# 2026-10-20 — godot-streaming

## Goal
M1: get Godot running with Terrain3D, verify region streaming (the biggest open risk from ADR 0005), and make the generated world walkable.

## What was done
- Found that every Godot on the machine was the standard edition; the developer chose to have a .NET edition fetched. Downloaded Godot 4.5.1 .NET and Terrain3D 1.0.2 from the official releases, verified checksums (SHA-512 against the official sums file; SHA-256 against GitHub's digest), extracted into their own directories after listing the zip.
- Created `Realm.Game` (Godot.NET.Sdk 4.5.1), vendored the Terrain3D addon (Linux and Windows binaries only), extended the architecture test.
- Read the extension's real API through `ClassDB` instead of guessing; wrote `TerrainBridge`, `RegionStreamer` and the streaming spike; ran about 25 timed flights.
- Added `RegionGrid` and `FrameStats` to Core with tests; the streamer loads by view distance in metres.
- Built the bake pipeline in `Realm.Gen.Bake` (tile format, baker, manifest) with 40 tests; `realm bake-terrain`; baked hill shading and painted rivers and lakes.
- Built the walkable world scene with a fly camera, HUD, auto-walk report and screenshot mode; rendered and inspected views myself.
- Docs: ADRs 0009 and 0010, format reference, Game system doc, three how-tos, versions, known issues 36-42.

## Decisions made
- [0009](../decisions/0009-terrain3d-integration-and-streaming.md): 256-vertex regions; build on worker threads; one batched rebuild per frame; frame-based budget; proposed 4 m spacing (1,024 m regions), pending developer confirmation.
- [0010](../decisions/0010-baked-terrain-format.md): custom checksummed tile format, manifest, baked colour and shading.

## Problems encountered and how they were solved
- **Terrain3D facts that changed the design:** at most 32 x 32 regions (so the 256 m chunks of ADR 0005 cannot be Terrain3D regions at 1 m spacing); 128-vertex regions take 2.3 s per add; 512-vertex rebuilds cost 16 to 34 ms. Measured, not assumed.
- **Frame cost was misattributed at first:** my report matched streaming steps to frames off by one; the long frames showed no steps. Fixed and re-run; every long frame was a shared-texture rebuild.
- **The developer's two points** (the game never needs a big loaded area; walking is far slower than the spike's flight) were right and led to the distance-based load set and the walking measurements. I had to correct my own claim that this loads "1 to 2 regions": with 1,024 m regions and a 500 m view it is typically 2 to 4, capped at 4 below 512 m.
- **Silent no-op edits** (string replacements that did not match) hid two missing methods until the build failed; I now check that replacements landed.
- **Lighting was inverted** in the baker and in the earlier preview images; a new test with the physically correct expectation found it. A vacuous test of mine (a call compared with itself) was replaced by an independent formula.
- **Process management slips:** two `pkill` patterns matched my own command line and killed the shell; process names are matched exactly now.
- **Frame spikes:** with vsync on, the world run showed 44 to 52 ms frames and some runs stalled when the window was hidden; with vsync off the walk is 2.4 ms median with 1 to 2 rare 16 to 36 ms frames. Measurement runs now disable vsync; the remaining spikes are unexplained and logged (issue 37).

## Tests added
- Core: `RegionGridTests`, `FrameStatsTests`. Gen: `RegionTileFileTests`, `TerrainBakerTests`, shading and preview-lighting tests. Architecture: three new rules for `Realm.Game`. Whole solution: 435 tests, 0 warnings. The Godot code is verified by running (spike, auto-walk, screenshots), not by unit tests.

## Docs added/updated
See "What was done".

## Open questions / next steps
- Developer: confirm 4 m terrain resolution (1,024 m regions) or ask for 2.5 m (640 m regions).
- Next candidates: textures and the retro render pipeline (low-resolution viewport, nearest filtering) so the world looks like the game; physics collision for the walker; a 30 minute soak run with GC and OS counters to find the unexplained spikes; origin rebasing.
