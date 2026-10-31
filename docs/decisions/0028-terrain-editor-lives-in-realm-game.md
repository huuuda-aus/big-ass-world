# 0028. The terrain editor lives inside Realm.Game, not a separate Realm.Editor project
- Status: accepted
- Date: 2026-10-30

## Context
CLAUDE.md section 5 lists `Realm.Editor` as its own project ("Godot editor plugins (C#)") with the dependency rule `Editor → Game, Gen, Data`. Building the first editor tool (terrain sculpting, section 6.1) against that rule hit a problem: Godot's .NET build compiles exactly one C# project per Godot project (the one named in `project.godot`'s `[dotnet]` section, here `Realm.Game.csproj`). Only that project's assembly, and whatever it references, is loaded into the running editor. An editor plugin needs `TerrainBridge`-adjacent Godot types (`MeshInstance3D`, `EditorPlugin`, Terrain3D's own data) to show a live preview, so it needs Godot. A separate `Realm.Editor.csproj` that `Realm.Game.csproj` does not reference would never be loaded by the Godot editor; one that `Realm.Game.csproj` does reference inverts the stated arrow (`Game → Editor`), and `Editor → Game` at the same time is circular.

## Options considered
1. Keep `Realm.Editor` as a separate project, referenced by `Realm.Game.csproj` (so the editor compiles the plugin in), and drop the "Editor → Game" direction from CLAUDE.md. Two project files to maintain for one Godot project; the separation buys no real isolation since both still run inside the same editor process.
2. Give the plugin its own Godot project with its own `project.godot`, imported as an addon. Not how Godot plugins work: an addon is a folder of scripts inside the consuming project, not a second Godot project.
3. Put editor-plugin code inside `Realm.Game`, under `Editor/` (namespace `Realm.Editor`, registered as a dock plugin via `addons/realm_editor/plugin.cfg`), and keep as much logic as possible in the already Godot-free `Realm.Gen` so it stays unit-tested.

## Decision
Option 3. `TerrainEditorPlugin`, `TerrainEditorPanel` and `TerrainEditSession` live in `src/Realm.Game/Editor/`; the brush math (`TerrainTileEditor`), the click-to-brush raycast (`RegionTileRaycast`) and the manifest checksum fold (`TerrainManifestChecksum`) live in `src/Realm.Gen/Bake/` and are covered by `Realm.Gen.Tests`, same as every other generator. The split mirrors CLAUDE.md section 4.1 (simulation separate from presentation) applied to the editor: the Godot-free half is the part worth testing; the Godot half is thin glue (a dock, a ray from the camera, a mesh rebuild).

CLAUDE.md section 5's solution layout and dependency diagram should be read as describing where editor logic belongs conceptually (what section 9.6's "Realm.Editor" documentation covers), not a second physical `.csproj`. The known-issues and `docs/systems/game.md` "planned" row for `Realm.Editor` are retired by this ADR; future editor tools (dungeon, NPC) follow the same split unless a concrete need for real isolation appears.

## Consequences
- Easier: one Godot project to build and open; no circular project references; brush and raycast logic is unit-tested without Godot.
- Harder: `Realm.Game`'s assembly now carries editor-only code at runtime (dead weight in exported builds; Godot strips `[Tool]` plugin behaviour from exports but not the IL). Acceptable for now; revisit with a build-config split if export size becomes a problem.
- Any future subsystem's editor tool (dungeon editor, NPC table editor) should default to this same split rather than re-opening the separate-project question each time.
