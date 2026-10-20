# How to run the streaming spike

Goal: measure how Terrain3D region streaming affects frame time, for a chosen region size, view distance and speed. Results so far: [ADR 0009](../decisions/0009-terrain3d-integration-and-streaming.md).

The spike flies a camera in a straight line at constant speed over procedurally generated regions (not the baked world), so it measures streaming mechanics, not content.

## Prerequisites
- [Godot (.NET) and Terrain3D set up](set-up-godot.md), and `dotnet build src/Realm.Game/Realm.Game.csproj` done.

## Steps

1. Run a flight (opens a window for about 30 s):
   ```bash
   <godot> --path src/Realm.Game res://scenes/terrain_spike.tscn -- \
     --speed=120 --distance=3000 --region-size=256 --spacing=4 \
     --update-per-add=false --update-per-remove=false --build-on-worker=true \
     --commits-per-frame=6 --removals-per-frame=6 --view-distance=480 --unload-margin=100 \
     --report=/tmp/spike.json
   ```
2. Read `/tmp/spike.json`. Key fields: `frameMs` (mean, p50, p95, p99, p999, max, hitches), `streaming.adds/removes/updates` (main-thread milliseconds per step), `streaming.generateMsWorker` (worker time per region), `gc`, and `worstFrames` (the longest frames with the streaming steps that happened in them).
3. Change one option at a time and compare.

## Options
`--seed`, `--speed` (m/s), `--distance` (m), `--region-size` (64 to 2048), `--spacing` (m), `--collision` (0 none, 1 dynamic, 3 full), `--view-distance` (m), `--unload-margin` (m), `--start-x` (m), `--in-flight`, `--commits-per-frame`, `--removals-per-frame`, `--budget-ms`, `--update-per-add`, `--update-per-remove`, `--build-on-worker`, `--altitude`, `--report`.

## Verify
With the options in step 1 you should see about 6,900 frames, a median frame time near 3.7 ms, `adds.mean` about 0.1 ms and `updates.mean` about 3 to 7 ms (single runs on the development machine; yours will differ).

## Troubleshooting
- A frame near 2 to 3 seconds per add: you used `--region-size=128`; that size is pathologically slow in Terrain3D 1.0.2.
- `Flight ... does not fit the region grid`: the distance is longer than the 32 region slots allow; it is shortened.
- Numbers vary between runs: they are single runs on a desktop; repeat and compare medians.

## Related
- [Run the game](run-the-game.md), [ADR 0009](../decisions/0009-terrain3d-integration-and-streaming.md)
