# How to add a simulation system

Goal: add logic that runs every tick over a set of components, deterministically.

## Prerequisites
- You know the tick model: [Sim system](../systems/sim.md).
- You have read the rules in [determinism](../explanation/determinism.md).
- The behaviour you are adding is decided. Game rules are the developer's design.

## Steps

1. Define the components your system needs as plain structs, with `///` docs if they are public:
   ```csharp
   public readonly record struct Position(int X, int Y);
   ```
2. Implement `ISystem`. For loops over many entities, iterate the storage directly with spans (no allocation, no lookups):
   ```csharp
   public sealed class WanderSystem : ISystem
   {
       public void Update(SimWorld world, SimClock clock)
       {
           Rng rng = Rng.ForKey(world.Seed, "system", "wander", clock.Tick);
           Span<Position> positions = world.ComponentsOf<Position>();
           for (int i = 0; i < positions.Length; i++)
           {
               positions[i] = new Position(
                   positions[i].X + rng.NextInt(-1, 2),
                   positions[i].Y + rng.NextInt(-1, 2));
           }
       }
   }
   ```
   `world.EntitiesWith<Position>()` gives the entity for each index when you need it (same indices, same order).

   Rules:
   - Randomness only from `Rng.ForKey(...)` as above; never `System.Random` or the wall clock.
   - While you hold a span, do not add or remove a component of that type or destroy one of its entities. Collect such changes in a list and apply them after the loop. (Writing to an existing component is fine.)
   - For code that is not hot, `world.Query<Position>()` is simpler. It returns a copy, so it may list an entity destroyed meanwhile: check `world.IsAlive(entity)` before writing to it.
3. Register it. Order matters: systems run in the order added.
   ```csharp
   var sim = new Simulation(seed);
   sim.AddSystem(new WanderSystem());
   ```
4. Write tests. At minimum copy the pattern in `SimulationTests`: same seed and ticks give identical state, different seeds differ, and chunked runs equal one long run.

## Verify

`dotnet test Realm.sln` passes with 0 warnings. Your determinism test fails if you accidentally use a non-deterministic source.

## Troubleshooting

- `Entity #n does not exist`: you wrote to an entity destroyed earlier in the same tick. Check `IsAlive`.
- Writes seem to vanish: you changed the set of components or entities while holding a span. Fetch a new span after such a change, or apply the change after the loop.
- Slow tick: [profile it](profile-the-simulation.md).
- Different results between runs: look for `System.Random`, `DateTime`, `Dictionary`/`HashSet` enumeration or floating-point time.

## Related
- [Determinism](../explanation/determinism.md)
- [Profile the simulation](profile-the-simulation.md)
