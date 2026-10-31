# 2026-10-30 — terrain editor edit locks

## Goal
Close the biggest gap flagged right after the terrain editor landed (known issue 78): `bake-terrain` silently overwrote any hand-sculpted region. Build the edit locks CLAUDE.md section 4.4 calls for.

## What was done
- `Realm.Gen.Bake`: `RegionLock` and `TerrainLocks` (a locked-region set, JSON round-trip, `Load`/`Save` against a baked directory alongside `manifest.json`).
- `TerrainBaker.Bake` takes an optional `TerrainLocks`: inside the parallel region loop, a locked region with an existing tile keeps it (its checksum is read via `RegionTileFile.ReadChecksum` instead of recomputing), everything else bakes as before. Reporting a `TERR019` info issue per kept region happens in a sequential pass after the parallel loop, since `ValidationReport` is documented not thread-safe.
- `Realm.Cli bake-terrain`: loads `locks.json` from the output directory by default; `--no-locks` ignores it.
- `TerrainEditSession`: now also opens, holds and persists `TerrainLocks`. `Save()` locks the region automatically (so the editor's own output is protected by default, not as an opt-in step easy to forget); a new `Unlock()` removes that protection.
- `TerrainEditorPanel`/`Plugin`: a lock-status label and an "Unlock region" button.

## Decisions made (link ADRs)
- No new ADR: this fills in a mechanism CLAUDE.md section 4.4 already specified, not a new architectural choice.
- **Save auto-locks** rather than locking being a separate manual step. Reasoning: an unlocked hand edit is a time bomb (the next `bake-terrain` erases it with no warning), so the safer default is "saved implies protected," with an explicit, visible "Unlock region" for the deliberate opt-out.
- Locks live in `locks.json` next to the tiles and manifest, not in a separate location, so they travel with the baked artefact they protect (same reasoning as `manifest.json` itself).

## Problems encountered and how they were solved
- First draft of the bake-loop test called `report.Add(...)` from inside `Parallel.For`; caught by `ValidationReport`'s own "not thread-safe" doc comment before it shipped. Fixed by marking kept regions in a `bool[]` inside the parallel loop (each thread only ever touches its own index) and reporting them in an ordinary sequential loop afterwards.
- A planned assertion comparing two `TerrainLocks` records with `Assert.Equal` failed: C# record equality on an `IReadOnlyList<T>` property is reference equality, not element-wise (the same reason `TerrainManifest` already hand-writes its own `Equals` for `Generators`). Fixed the test to compare `.Regions` contents instead of the whole record.

## Tests added
- `tests/Realm.Gen.Tests/Bake/TerrainLocksTests.cs` (new): contains/lock/unlock/replace-note, JSON round trip, load-with-no-file, save-then-load through the real file system.
- `tests/Realm.Gen.Tests/Bake/TerrainBakerTests.cs`: `Bake_RespectsLocks_KeepingTheExistingTileAndReportingIt`, `Bake_WithNoLockOnARegion_OverwritesItsHandEditAnyway`.
- Full `Realm.Gen.Tests` suite still green; `Realm.Cli`, `Realm.Game` and the whole solution build clean (0 warnings).

## Docs added/updated
- `docs/reference/baked-terrain.md` (`locks.json` format), `docs/reference/validators.md` (`TERR019`), `docs/reference/cli.md` (`--no-locks`).
- `docs/how-to/sculpt-terrain.md`: save-locks/unlock steps, troubleshooting update.
- `docs/systems/game.md`: editor diagram and text updated for the lock flow; known-limitations line.
- `docs/known-issues.md`: issue 78 marked fixed.
- `CHANGELOG.md`.

## Open questions / next steps
- The dungeon and NPC editors (still planned) will want the same mechanism at their own granularity (room, NPC); revisit whether `TerrainLocks`'s shape generalises or each needs its own.
- `manifest.json`'s `minHeight`/`maxHeight` still don't update after an edit that exceeds them (known issue 80, unrelated to locks, still open).
