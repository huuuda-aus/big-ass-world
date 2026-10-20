# 2026-10-20 — sim-query-profiling

## Goal
Profile known issue 10 (`SimWorld.Query<T>()` allocates) before changing anything (CLAUDE.md 4.8: profile before optimising), then fix what the profile shows.

## What was done
1. Throwaway Release benchmark (outside the repository): a system that queries one component and `Set`s it for every entity; 100, 1,000 and 10,000 entities; time, bytes and gen0 counts.
2. Broke the cost down (query copy, `Set`, `TryGet`, raw enumeration), then prototyped dense parallel-array storage in three iteration styles.
3. Recorded the findings and asked the developer to choose between three fixes; they chose dense storage with index-based (span) iteration.
4. Implemented it: dense `ComponentStore<T>`, sorted-array living-entity list, `EntitiesWith<T>()` / `ComponentsOf<T>()`.
5. Added `tools/Realm.Bench` to the repository with a `--check` budget gate; verified its exit code 0 within budget and 1 with a deliberately impossible budget.
6. Confirmed day length: `TimeSettings.Default` is no longer called a placeholder.

## Decisions made
- [ADR 0004](../decisions/0004-dense-component-storage.md): dense storage plus span iteration.
- The developer accepted the default day length (20 ticks per game minute; 24 real minutes at 20 Hz).

## Problems encountered and how they were solved
- The issue's premise was partly wrong. Allocation is 16 B per entity per call and GC activity is low. The real cost was per-entity `SortedDictionary` lookups (`Set` for all 10,000 entities: 2.2 ms versus 0.31 ms for the copy). The first benchmark showed only the total; the breakdown found the cause.
- Removing the allocation alone helped little in the prototypes (1.0 to 0.9 ms), so a reused buffer would not have been worth it.
- The legacy `Query` + `Set` path improved only about 15 percent, because `Set` now does two binary searches (alive check and store). This is documented; hot code uses spans.
- An early exit-code check printed the exit code of `tail`, not the tool. Rechecked without a pipe.
- Result at 10,000 entities with two systems: 4.2 ms/tick and 320 KB/tick (old style) versus 0.018 ms/tick and 0 B/tick (spans).

## Tests added
- `DenseStorageTests` (11): parallel spans, in-place writes, `Set` during iteration, out-of-order inserts, growth past initial capacity, removal from front/middle/end, remove-all-and-refill, destroyed middle entity, `IsAlive` with gaps over 200 entities, and an assertion that span iteration allocates 0 bytes (stable over 3 runs).
- All 33 existing Sim tests passed unchanged. Whole solution: 152 tests, 0 warnings.

## Docs added/updated
ADR 0004, `how-to/profile-the-simulation.md`, `how-to/add-a-system.md`, `systems/sim.md` (performance notes, status, extension points), `known-issues.md` (10 reworded, 13 and 14 added), README, CHANGELOG, index.

## Open questions / next steps
- Multi-component joins (`Query<A, B>`) once a real system needs two components together.
- Misuse detection for stale spans (a debug-only version check) if it bites.
- Next: terrain pipeline start in `Realm.Gen` (needs the macro map format decision) or the save-state skeleton.
