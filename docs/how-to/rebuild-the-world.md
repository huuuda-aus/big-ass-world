# Rebuild the world and restart the game

Goal: after changing content, settings or generator code, regenerate and bake the whole world and restart the game with one command.

## Prerequisites
- The repository builds and Godot is set up ([set up Godot](set-up-godot.md)). The script looks for Godot at `~/tools/godot-mono-4.5.1/...`; set `GODOT=/path/to/godot` if yours is elsewhere.

## Steps
1. Run:
   ```bash
   tools/rebuild-world.sh
   ```
   It lints the content, runs `bake-terrain` (height field, erosion, settlements with their service buildings, roads; it writes the tiles, `settlements.json`, `settlements_ground.bin`, `roads.json` and the `map.png` used by the in-game map into `baked/terrain`), stops a running game, builds the game project and starts the game. It stops at the first failing step; validator errors in the bake stop it before the game restarts. The bake takes about 20 s in Release.
2. Options:
   - `--seed N` changes the world seed.
   - `--bake-args "--cities 4 --towns 8"` passes options to `realm bake-terrain`.
   - `--out DIR` bakes elsewhere (the game is started with `--baked=DIR`).
   - `--no-run` bakes only.
   - Everything after `--` goes to the game, for example `tools/rebuild-world.sh -- --fly --pos=2877,-3950`.

## Verify
The output shows `== generate and bake` ending in `0 error(s), 0 warning(s)`, then `== start the game`, and the game opens on the new map (press **M**).

## Troubleshooting
- `Godot not found`: set `GODOT`.
- The game did not stop: the script only stops a Godot started as `... --path src/Realm.Game`; close any other instance yourself.
- There is no server: the game is single-player, so "restart" means restarting the game window.

## Related
- [CLI reference](../reference/cli.md#bake-terrain-dir---out-dir---seed-n---spacing-m---region-size-n---no-erode---cities-n---towns-n---no-settlements---no-roads), [run the game](run-the-game.md), [place cities](place-cities.md).
