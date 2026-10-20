# Determinism

## Why it matters

Given the same inputs, the simulation and generators must produce the same output. That lets us test generators with fixed seeds (golden tests), reproduce a bug from a seed, run seed sweeps in CI, and keep saves small by storing only a seed plus deltas (CLAUDE.md sections 4.2 and 4.5). A non-deterministic bug is nearly impossible to fix; this rule keeps bugs repeatable.

## The rules, and where they are enforced

| Rule | How it is applied | Where |
|---|---|---|
| All randomness comes from `Rng`. Never `System.Random`, `GD.Randf()` or time-based seeds. | PCG32, verified against the reference vector. | [Core](../systems/core.md) |
| Sub-seeds are derived by name, not by consuming a shared stream. | `Rng.ForKey(worldSeed, "dungeon", id, "sector", 3)`. Changing one generator does not reshuffle the others. | Core |
| Never rely on hash-order enumeration. | `TagSet` enumerates sorted. `ContentDatabase` and loaders sort by ordinal ID. `SimWorld` queries return entity order. | Core, Data, Sim |
| Culture must not affect results. | Comparison and ordering use ordinal rules. | `ContentId` |
| Time is integers. | `GameTime` counts whole minutes from an integer tick; no floating-point time. | [Sim](../systems/sim.md) |
| IDs are stable. | `ContentId` is a string; `EntityId` increases and is never reused. | Core, Sim |

## How a system stays deterministic

Per tick, a system draws randomness from a generator built from the world seed, its own name and the tick number:

```csharp
Rng rng = Rng.ForKey(world.Seed, "system", "wander", clock.Tick);
```

That generator depends only on those inputs. So running 100 ticks at once, or 10 ticks ten times, gives the same world (test `RunInDifferentChunks_GivesTheSameResult`), and two systems never steal numbers from each other.

## What is not yet guaranteed

- Floating-point results across different CPUs or runtimes are not guaranteed identical. Baked outputs ship as data, so this matters only for runtime generators. Prefer integer or fixed-point math there (CLAUDE.md section 4.2).
- Logging, UI and rendering are outside the guarantee; they must never feed back into the simulation.
- Golden-output tests for generators and save round-trip tests: **Planned** (no generator or save format exists yet).

## Related
- [Core](../systems/core.md), [Sim](../systems/sim.md), [Add a system](../how-to/add-a-system.md)
