# How to run the game (walkable world)

Goal: bake the terrain, start Godot and walk around the 20 km world.

## Prerequisites
- [Godot (.NET) and Terrain3D set up](set-up-godot.md).
- The solution builds: `dotnet build Realm.sln`.

## Steps

1. Bake the terrain (about 40 s; writes about 202 MB):
   ```bash
   dotnet run --project src/Realm.Cli -- bake-terrain
   ```
   You should see the height field, erosion and `baked 400 region tile(s) of 256 x 256 vertices (1024 m each) to 'baked/terrain'`, and `0 error(s), 0 warning(s)`.
2. Build the game project:
   ```bash
   dotnet build src/Realm.Game/Realm.Game.csproj
   ```
3. Start the game (use your Godot .NET path):
   ```bash
   <godot> --path src/Realm.Game
   ```
   A window opens, the mouse is captured, and you stand beside a river near the middle of the map.
4. Move with the controls below. Press `F` to fly over the whole world; `Esc` frees the mouse.

## Controls

| Key | Walk mode | Fly mode |
|---|---|---|
| W A S D | move | move along the camera direction |
| Mouse | look | look |
| Shift | run (9 m/s instead of 5.5) | 5 times faster (default 80 m/s) |
| Space | jump | rise |
| Q / E | | down / up |
| F | toggle fly | toggle fly |
| T | change the speed of time: normal, 30 times, 300 times, paused | |
| N | show or hide the walking graphs of the settlements (green streets, yellow doors, blue gates, red bridges) | |
| F1 | hide or show the HUD | |
| Esc | release or recapture the mouse | |

A compass strip at the top of the screen shows the bearing you look along (degrees clockwise from north, with N, NE, E ... marked and a tick every 5 degrees); it hides with the rest of the HUD (F1). Looking at a person in a settlement shows their name, trade and what they are doing and why at the bottom of the screen; a guard at a gate speaks when you come close. The HUD shows the game day, time, moon phase and rain, frames per second, frame time, position, ground height, the region you are in, how many regions are loaded or loading, and the cost of the last shared-texture rebuild.

## Options

Pass them after `--`, for example `<godot> --path src/Realm.Game -- --fly --pos=0,0`.

| Option | Effect |
|---|---|
| `--baked=DIR` | Use a different baked terrain directory (default `baked/terrain` at the repository root) |
| `--pos=X,Z` | Start at this position in metres from the world centre |
| `--yaw=DEG`, `--pitch=DEG` | Initial view direction (yaw 0 looks north, 90 looks west) |
| `--fly`, `--altitude=M`, `--fly-speed=M` | Start in fly mode, height above ground, speed |
| `--view-distance=M`, `--fog=DENSITY` | Override how far terrain is loaded and how thick the fog is |
| `--screenshot=PNG`, `--screenshot-after=FRAMES` | Save a screenshot and quit |
| `--autowalk=SECONDS`, `--autowalk-run`, `--report=JSON` | Walk forward automatically, print and save a frame-time report, quit |
| `--rain=X` | Fix the rain intensity (0 to 1) everywhere, for testing; by default it comes from the clouds of each region |
| `--crowd-scale=F` | Multiply the number of characters in every settlement (default 1: 220 in a city, 40 in a town, 12 in a hamlet; 0 for none) |
| `--nav-debug` | Start with the settlements' walking graphs drawn |
| `--time=HOUR`, `--day-minutes=M`, `--time-scale=X` | Start hour of day (default 9), real minutes per game day (default 20), speed of time (0 pauses) |
| `--vsync=true/false` | Vertical sync. On by default; measurement runs (`--autowalk`, `--screenshot`) switch it off unless you pass `--vsync=true`, because vsync adds compositor noise to frame times and can stall a hidden window |
| `--no-capture`, `--no-hud`, `--no-water`, `--flat-ground`, `--no-step` (turn off automatic step climbing) | Debugging switches |

## What you are looking at
- Terrain is the baked eroded height field; colours come from the baked colour map (height palette, baked shading, rivers and lakes). There are no textures, vegetation, buildings or characters yet.
- The sea is a flat plane at height 0. Rivers and lakes are painted on the ground, not rendered as water.
- Walking uses physics: you collide with the terrain and cannot climb slopes steeper than 50 degrees. Fly mode (F) ignores collision. If you fall through at start, report it; a failsafe normally lifts you back.
- Extra options: `--render-scale=0.75` (3D resolution relative to the window, 1 = sharp), `--collision-shape=8`, `--collision-radius=16`, `--colour-map` (old colour-only look), `--no-cities`, `--map`.
- Walk mode loads terrain within 480 m and uses thick fog; fly mode loads 1,600 m and uses thin fog.

## Verify
In walk mode, hold W (or the Up arrow): you move at about 5.5 m/s and stay on the ground. Walking west for a couple of minutes crosses into a new region without a visible stall. `--autowalk=60 --yaw=90 --no-capture` does this for you and prints frame statistics.

## Troubleshooting
- `No baked terrain at ...`: run step 1.
- The terrain looks blurry up close: colours are baked at 4 m per texel and filtered smoothly; textures are planned.
- Black or empty screen: check that the log shows `[world] ... region(s) loaded`.
- See also [set up Godot](set-up-godot.md) troubleshooting.

## Related
- [Baked terrain format](../reference/baked-terrain.md), [Game system](../systems/game.md), [Run the streaming spike](run-the-streaming-spike.md)

## Check step climbing
The walker's automatic stepping has a headless physics test: a staircase with risers from 0.1 to 0.6 m, with and without a door opening.
```bash
cd src/Realm.Game && <godot> --headless --path . res://scenes/step_test.tscn
```
You should see `PASS` for risers up to 0.35 m (climbed) and for 0.4 m and above (blocked), then `[step-test] all passed` (exit code 1 and `FAILED` otherwise). Run it after changing `StepClimber` or the walker's capsule, mass or floor settings.

