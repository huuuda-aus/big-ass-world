# How to hand-sculpt a baked terrain region

Goal: open the Godot editor, raise or lower part of one baked region with the terrain editor, and save it back to the `.tile` file.

This is the basic terrain editor from CLAUDE.md section 6.1 ("World Generator" editing step). It edits the *baked* output, not the generator settings: use [tune the terrain](tune-the-terrain.md) to change how `bake-terrain` generates a region in the first place.

## Prerequisites
- A baked terrain directory exists at `baked/terrain` (run `dotnet run --project src/Realm.Cli -- bake-terrain` if not; see [rebuild the world](rebuild-the-world.md)).
- You can open `src/Realm.Game` in the Godot 4.5.1 .NET editor ([set up Godot](set-up-godot.md)).
- You know which region you want to edit. `manifest.json` in the baked directory gives the region range (`minRegion` to `maxRegion` on each axis); region (0, 0) starts at the world's centre.

## Steps

1. Open `src/Realm.Game` in the Godot editor. The **Realm Terrain Editor** dock appears (bottom-left by default) once the project finishes importing; if it does not, check **Project > Project Settings > Plugins** that "Realm Terrain Editor" is enabled.
2. Open or create a scene with a 3D view (for example `scenes/world.tscn`), so there is an edited scene root the preview mesh can attach to. The dock's "Open a scene in the editor first." status means this step was skipped.
3. In the dock, set **Region X** and **Region Z** to the region you want, and press **Load region**. A flat-shaded mesh of that region appears at its real world position, using the same baked colours the game shows.
4. Pick a brush **mode** (Raise, Lower, Smooth, Flatten), a **radius** (in vertices; the shipped regions are 256 vertices at 4 m, so a radius of 6 covers about a 48 m circle) and a **strength** (metres per dab for Raise/Lower, a 0–1 blend rate for Smooth/Flatten).
5. Turn on **Sculpt (left-drag in the 3D view)**. Left-drag over the mesh in the 3D viewport to paint. The height data updates live; the mesh itself only redraws when you release the mouse (so dragging stays responsive — see troubleshooting if that redraw feels slow).
6. For **Flatten**, the target height is captured from wherever you first click, so the first dab of a stroke sets the "floor" or "ceiling" the rest of the stroke pulls toward.
7. Press **Save**. This writes `region_<x>_<z>.tile` (temp file, then move — a crash mid-save cannot corrupt it), updates `manifest.json`'s checksum, and **locks the region** (`locks.json`) so a later `bake-terrain` keeps your edit instead of silently regenerating over it. **Revert** discards unsaved changes and reloads the tile from disk instead, without touching the lock.
8. If you want a later re-bake to be free to regenerate this region again, press **Unlock region**. The dock's status line always shows whether the loaded region is locked.
9. Run the game (`dotnet run --project src/Realm.Game -- --pos=<world X>,<world Z>`, or just walk there) to see the edit in the real streaming terrain. You should see your change exactly where you sculpted it.

## Troubleshooting
- **"Load failed: No baked terrain at '...'"**: the default directory is `baked/terrain` next to the repository root; bake it first, or check the path in `TerrainEditorPlugin.DefaultTerrainDirectory()` if you keep baked data elsewhere (there is no `--baked` option on the editor dock yet).
- **"Load failed: Regions run from X to Y on each axis."**: the region coordinate is outside the manifest's range; check `manifest.json`.
- **Nothing happens when I drag**: make sure "Sculpt" is checked and a region is loaded; dragging outside the loaded region's bounds (or on empty space, since there is no ground plane) does not hit anything.
- **The mesh looks wrong near one edge after editing close to it**: expected (known issue 79) — the editor only shades using this region's own vertices, so a border vertex can shade slightly differently than its neighbour region.
- **My edit disappeared after I re-baked the world**: check whether the region is actually locked (the dock's status line, or `locks.json` in the baked directory) — **Save** locks automatically, but **Unlock region** or `bake-terrain --no-locks` both remove that protection on purpose.
- **Redrawing the mesh is slow**: a 256-vertex region is about 130,000 triangles rebuilt in one call; this is unoptimised (known issue 81). Use a smaller region size when iterating, or keep sessions to one region at a time.
- **Trees near my edit look buried (or floating)**: expected (known issue 82) — the editor doesn't touch `foliage.bin`, so a tree's baked ground height no longer matches terrain you raised or lowered under it.

## Related
- [Baked terrain format](../reference/baked-terrain.md), [Tune the terrain](tune-the-terrain.md), [Game system](../systems/game.md#terrain-editor)
- [ADR 0010](../decisions/0010-baked-terrain-format.md) (the format this edits), [ADR 0028](../decisions/0028-terrain-editor-lives-in-realm-game.md) (why the editor lives in `Realm.Game`)
