# How to profile the simulation

Goal: measure tick time and allocation of simulation systems, and check them against the budget (sim tick at or below 4 ms, no allocation in hot paths: CLAUDE.md section 4.8).

## Prerequisites
- The solution builds ([getting started](../tutorials/getting-started.md)).
- Always use a **Release** build. Debug numbers are meaningless.
- Close other heavy programs; single runs on a busy machine vary.

## Steps

1. Run the benchmark tool:
   ```bash
   dotnet run -c Release --project tools/Realm.Bench
   ```
2. Read the table. Columns: `ms/tick` (time per simulation tick), `B/tick` (bytes allocated per tick), `gen0/1000t` (garbage collections per 1,000 ticks). Each scenario runs the old `Query` + `Set` style next to the span-in-place style, at 100, 1,000 and 10,000 entities with one and two systems.
3. Check against the budget:
   ```bash
   dotnet run -c Release --project tools/Realm.Bench -- --check
   ```
   Prints `OK` and exits 0 when the span scenario (10,000 entities, two systems) is within 4 ms per tick and allocates 0 bytes. Otherwise it prints `FAILED` and exits 1.
4. To profile your own system, copy the pattern in `tools/Realm.Bench/Program.cs`: build a `Simulation`, add your system, create entities, run 50 warm-up ticks, then time `sim.Run(n)` and read `GC.GetAllocatedBytesForCurrentThread()` before and after.
5. Separate the cost before blaming the obvious thing. The Query-allocation investigation assumed allocation was the problem; timing `Set`, `TryGet` and the copy individually showed lookups were. See [Sim performance notes](../systems/sim.md).

## Verify
For the shipped scenarios you should see the span rows near 0.02 ms per tick at 10,000 entities with two systems, and 0 in `B/tick`.

## Troubleshooting
- `Warning: Debug build`: you forgot `-c Release`.
- Large run-to-run differences: repeat a few times; numbers are single runs and indicative.
- `--check` fails after your change: look at which of the two limits it reports (time or allocation), then check for per-entity `Query`, `Set` or LINQ in the hot loop.

## Related
- [Add a simulation system](add-a-system.md)
- [ADR 0004](../decisions/0004-dense-component-storage.md)
