# 0004. Dense component storage and span-based iteration
- Status: accepted
- Date: 2026-10-20

## Context
Profiling known issue 10 (see [Sim performance notes](../systems/sim.md)) showed that the original `SortedDictionary` component store, accessed with `Query` (copy) then `Set` per entity, cost 2.5 ms per system-tick at 10,000 entities, so two systems exceeded the 4 ms sim tick budget (CLAUDE.md section 4.8). Allocation (16 B per entity per query) was not the main cost; per-entity tree lookups were. Every future system (schedules, economy, needs) will iterate components, so the iteration API is hard to change later.

## Options considered
1. Leave as is — fine up to about 1,000 active entities; over budget at 10,000.
2. Dense sorted arrays, `Query` into a reused buffer, `Set` by ID — zero allocation, about 2.7 times faster; still one binary search per entity.
3. Dense sorted arrays plus span-based in-place iteration (`EntitiesWith<T>()`, `ComponentsOf<T>()`) — zero allocation and no per-entity lookup; about 230 times faster in the repo benchmark at 10,000 entities and two systems; the API is lower level.

## Decision
Option 3, chosen by the developer on 2026-10-20.
- Components of one type live in two parallel arrays sorted by `EntityId`. Entities are normally created in increasing ID order, so adding a component is an append.
- `SimWorld.EntitiesWith<T>()` and `SimWorld.ComponentsOf<T>()` return spans over the live storage for per-tick loops.
- `Query<T>`, `Set<T>`, `TryGet<T>`, `Has<T>` and `Remove<T>` stay for code that is not hot.
- The living-entity list is also a sorted array (binary search), because `Set` checks it on every write.
- Structural changes (adding or removing a component of the iterated type, destroying an entity that has it) invalidate the spans. Misuse is memory-safe but writes may be lost, so systems collect such changes and apply them after the loop.
- `tools/Realm.Bench` is kept in the repository so numbers are repeatable; `--check` fails if the span path exceeds the tick budget or allocates.

## Consequences
- Easier: systems that touch thousands of entities fit the tick budget with large margin; the allocation rule holds in the hot path.
- Harder: span iteration needs care around structural changes; adding a component to an older entity inserts in the middle (O(n); 22.6 ms to build 10,000 in reverse order, a setup-time worst case).
- The legacy `Set` path improved only about 15 percent because it now does two binary searches (alive check and store); use spans in hot code.
- Multi-component joins (`Query<A, B>`) are still not provided; a system needing two components walks two parallel spans only if both stores hold the same entities, otherwise it uses `TryGet`. Revisit when a real system needs it.
