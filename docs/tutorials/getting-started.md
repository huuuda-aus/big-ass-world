# Getting started

Goal: build the solution, run the tests and call the CLI.

## Prerequisites

- .NET 8 SDK (`dotnet --list-sdks` shows an `8.0.x` entry). Exact version: [versions](../reference/versions.md).
- Git. Git LFS is needed once binary assets exist (`git lfs install`).
- Godot is **not** needed yet. It arrives with milestone M1.

## Steps

1. Clone the repository and enter it.
2. Build everything:
   ```bash
   dotnet build Realm.sln
   ```
   You should see `Build succeeded` with `0 Warning(s)`. Warnings are errors in `src/`, so any warning fails the build.
3. Run all tests:
   ```bash
   dotnet test Realm.sln
   ```
   You should see `Passed!` for every test project.
4. Run the CLI:
   ```bash
   dotnet run --project src/Realm.Cli -- version
   ```
   You should see `Realm.Cli 1.0.0.0`.
5. Validate the shipped content:
   ```bash
   dotnet run --project src/Realm.Cli -- content-lint
   ```
   You should see `3 material(s), 3 item(s), 7 biome(s), 7 region(s), 0 error(s), 0 warning(s).`
6. Check the world map covers the whole world:
   ```bash
   dotnet run --project src/Realm.Cli -- macro-map
   ```
   You should see a table of seven regions (six land regions and the sea) and `0 error(s), 0 warning(s).`
7. Generate the terrain and a preview picture:
   ```bash
   dotnet run --project src/Realm.Cli -- height-field --spacing 32 --erode --png terrain.png
   ```
   You should see a height range of about -218 m to 651 m, a per-region table, an erosion line (`deepest carve 69.2 m`), `rivers: 18 (74.8 km total); lakes: 1` and `0 error(s), 0 warning(s).` Open `terrain.png`: dark blue sea around six land regions with pale borders, thin blue rivers and one lake.

8. Optional: walk around the world. Set up Godot first ([set up Godot](../how-to/set-up-godot.md)), then bake and run:
   ```bash
   dotnet run --project src/Realm.Cli -- bake-terrain
   <godot> --path src/Realm.Game
   ```
   See [run the game](../how-to/run-the-game.md) for controls.

## Troubleshooting

- `dotnet: command not found`: install the .NET 8 SDK.
- `CS1591` build error: a public type or member is missing its `///` XML doc comment. This is intentional (CLAUDE.md section 8.2).
- Architecture test fails: a project reference or a Godot reference breaks the dependency rules. See [architecture](../explanation/architecture.md).

## Related
- [CLI reference](../reference/cli.md)
- [Add a material](../how-to/add-a-material.md)
