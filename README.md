# Realm (working title)

A single-player, first-person, retro-styled open-world RPG in the spirit of *Might and Magic VI–VIII*. Godot 4 (.NET) front end over a Godot-free C# simulation and generator core.

**Status:** M0 — Foundations, moving into the terrain pipeline. Core, Data, Sim, the terrain pipeline (macro map, height field, erosion, rivers, lakes, bake) and a first playable preview exist: you can walk and fly over the baked 20 km world in Godot. There are no textures, characters or gameplay yet.

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
