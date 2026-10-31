# 2026-10-31 — Windows first run

## Goal
Run the project on Windows for the first time (Windows 10, RTX 3070), and fix what stopped a fresh clone from working there.

## What was done
- Installed Godot 4.5.1 .NET (Windows, SHA-512 checked against the release's `SHA512-SUMS.txt`) outside the repository, built, imported and ran the game. It loads into Thornford at 60 fps with terrain streaming.
- Committed the Terrain3D native binaries. The `.gitignore` rule `bin/` (for .NET build output) also matched `src/Realm.Game/addons/terrain_3d/bin/`, so the binaries the README called vendored had never been committed; a fresh clone failed with `GDExtension dynamic library not found`. The folder is now re-included (`!src/Realm.Game/addons/terrain_3d/bin/`) and tracked through Git LFS (`.gitattributes`). The Linux and Windows x86_64 binaries come from `Terrain3D_v1.0.2-stable.zip`, SHA-256 `a071850250ec5e596aa54da61c01d75768774eb379ee997584d426a45f4884a2`, the digest already recorded in `versions.md`.
- Fixed `SettlementManifest.ToJson` on Windows: `WriteIndented` writes `Environment.NewLine`, so the JSON had `\r\n` on Windows and the golden hash in `ShippedWorld_Placement_IsStableGolden` differed. The output is now normalised to `\n`.

## Decisions made (link ADRs)
- No ADR. Normalising with `Replace("\r\n", "\n")` rather than `JsonSerializerOptions.NewLine`, because that property only exists from .NET 9 and the project targets .NET 8.
- No generator version bump: output on Linux, where the golden was captured, is byte-identical.
- Binaries go through LFS, like every other binary in the repository (CLAUDE.md section 3).

## Problems encountered and how they were solved
- The first `git pull` refused to overwrite the `.uid` files Godot had generated during the first import, because the incoming commits add the same files. The local copies were deleted (Godot regenerates them) and the pull went through.

## Tests added
- `SettlementTerrainTests.Manifest_Json_UsesLfLineEndings_OnEveryPlatform`: fails on Windows before the fix, and checks the line endings on any OS, unlike the golden test, which only catches the bug on Windows.

## Docs added/updated
- README (tested on Windows), `docs/how-to/set-up-godot.md` (troubleshooting for the missing-library error, Windows executables), `docs/reference/versions.md` (binaries in LFS), CHANGELOG, this devlog, `docs/index.md`.

## Open questions / next steps
- `TerrainManifest` and `TerrainLocks` also serialise with `WriteIndented`, so a bake on Windows writes `\r\n` into `manifest.json` and `locks.json`. No test hashes those files today; normalising them the same way would keep bakes byte-identical across operating systems.
- `tools/run-game.sh` defaults to the Linux Godot path; on Windows set `GODOT`.
