# How to set up Godot (.NET) and Terrain3D

Goal: have a Godot build that can run the C# projects, with the Terrain3D addon, verified.

## Prerequisites
- The .NET SDK 8.0 or later (`dotnet --list-sdks`).
- A graphics card and driver with Vulkan (`vulkaninfo --summary`).

## Steps

1. Download **Godot 4.5.1, .NET edition** for your platform from the official [Godot 4.5.1 archive](https://godotengine.org/download/archive/4.5.1-stable/). Use the file with `mono` in its name (on Linux: `Godot_v4.5.1-stable_mono_linux_x86_64.zip`). The standard edition cannot load C# projects.
2. Verify the download against the official checksums (the `SHA512-SUMS.txt` published with the release) before running it.
3. Extract it into its own empty directory, for example `~/tools/godot-mono-4.5.1/`. Do not put it inside the repository.
4. Check the edition:
   ```bash
   ~/tools/godot-mono-4.5.1/Godot_v4.5.1-stable_mono_linux_x86_64/Godot_v4.5.1-stable_mono_linux.x86_64 --version
   ```
   You should see `4.5.1.stable.mono.official...`. If there is no `mono` in the output you have the standard edition.
5. Terrain3D 1.0.2 is vendored in `src/Realm.Game/addons/terrain_3d` (Linux and Windows binaries only). To replace or upgrade it, download the release zip from the [Terrain3D releases](https://github.com/TokisanGames/Terrain3D/releases), check its SHA-256 against the digest GitHub shows, unpack `addons/terrain_3d` over the old folder, and follow the checks in [versions](../reference/versions.md).
6. Build the game project and import it once:
   ```bash
   dotnet build src/Realm.Game/Realm.Game.csproj
   cd src/Realm.Game && <godot> --headless --path . --import
   ```
   The first restore downloads GodotSharp 4.5.1 from NuGet and takes about a minute.
7. Check that the extension loads. Run any scene; the log must not say the extension failed to load. The game itself fails with a clear message if Terrain3D is missing or its version is not 1.0.2.

## Verify
`dotnet build Realm.sln` shows 0 warnings and `--version` shows `mono`. Then [run the game](run-the-game.md).

## Troubleshooting
- `The Terrain3D extension is not loaded`: the addon folder is missing, or the project was never imported. Run step 6.
- `Terrain3D x.y.z is installed but Realm requires 1.0.2`: you changed the addon. Update `TerrainBridge.RequiredVersion`, re-run the streaming spike and write an ADR before upgrading.
- Restore seems stuck: the first restore of GodotSharp is slow, not hung. Wait about a minute.
- The Godot window does not open: check Vulkan with `vulkaninfo --summary`.

## Related
- [Pinned versions](../reference/versions.md), [ADR 0009](../decisions/0009-terrain3d-integration-and-streaming.md)
