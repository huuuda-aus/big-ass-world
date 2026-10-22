# Realm (working title)

A single-player, first-person, retro-styled open-world RPG in the spirit of *Might and Magic VI–VIII*. Godot 4 (.NET) front end over a Godot-free C# simulation and generator core.

**Status:** M0 — Foundations, moving into the terrain pipeline. Core, Data, Sim, the terrain pipeline (macro map, height field, erosion, rivers, lakes, bake) and a first playable preview exist: you can walk and fly over the baked 20 km world in Godot. Ground has placeholder textures and a physics walker, and the map has 4 walled cities (one in the mountains) and 5 towns with names, built on the natural ground with foundations where it slopes, each with its service buildings (inn, blacksmith, trader and so on), joined by roads and bridges, that you can walk into; there are no characters or gameplay yet.

- Project rules and design: [CLAUDE.md](CLAUDE.md)
- All documentation: [docs/index.md](docs/index.md)
- Start here: [Getting started](docs/tutorials/getting-started.md)

## Build and test

Requires the .NET 8 SDK (see [versions](docs/reference/versions.md)).

```bash
dotnet build Realm.sln
dotnet test Realm.sln
dotnet run --project src/Realm.Cli -- version
dotnet run -c Release --project tools/Realm.Bench -- --check   # simulation speed budget
dotnet run --project src/Realm.Cli -- bake-terrain              # bake the world (about 40 s)
# then, with Godot 4.5.1 .NET (see docs/how-to/set-up-godot.md): godot --path src/Realm.Game
```

## Try it (for another developer)

Works on Linux and Windows (Terrain3D binaries are vendored for those two only). Only Linux has been run so far.

1. Install [git](https://git-scm.com), [git-lfs](https://git-lfs.com) and the .NET 8 SDK, then run `git lfs install` once.
2. Clone: `git clone https://github.com/huuuda-aus/big-ass-world.git`. The clone downloads the baked terrain (about 210 MB) through Git LFS, which counts against the repository owner's LFS bandwidth quota.
3. Download Godot **4.5.1 .NET** (the "mono" build; the standard build cannot run C# projects) from the [Godot download archive](https://godotengine.org/download/archive/4.5.1-stable/). Terrain3D 1.0.2 is already in the repository. Details: [Set up Godot](docs/how-to/set-up-godot.md).
4. Check the build: `dotnet test Realm.sln`.
5. Run the game: `godot --path src/Realm.Game` (use the full path to the Godot executable if it is not on your PATH).

Controls: W A S D move, Shift run, Space jump, F fly, M map, F1 hide the HUD, Esc release the mouse. More: [Run the game](docs/how-to/run-the-game.md).

If the baked terrain is missing (a clone without LFS), create it with `dotnet run --project src/Realm.Cli -- bake-terrain` (about 40 s).

Not included: a license file, so the code is all rights reserved until one is added.
