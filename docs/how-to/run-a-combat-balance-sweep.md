# How to run a combat balance sweep

Goal: find out how a fight plays out over thousands of runs (win rate, fight length, health left) and fail a check when it drifts out of a band.

## Prerequisites
- The .NET 8 SDK and a built solution (`dotnet build Realm.sln`).
- Background: [Combat system](../systems/combat.md). Scenarios are still defined in code (spike).

## Steps
1. List the scenarios:
   ```
   $ dotnet run --project src/Realm.Cli -- combat-sim --list
   duel             hero (longsword, fireball) against one goblin with a club, 1.5 m apart
   goblin-ambush    hero against two club goblins at 1.5 m and a goblin archer with poison arrows at 12 m
   ```
2. Run one (1000 fights by default):
   ```
   $ dotnet run --project src/Realm.Cli -- combat-sim goblin-ambush
   combat-sim goblin-ambush: 1000 fights, seed 1
     win rate      97.4 % (974 won, 26 lost, 0 timed out)
     fight length  mean 9.1 s, median 9.3 s, p90 12.1 s
     health left   47.0 % on average in a win
   ```
3. Change a number, for example the goblin's club in `src/Realm.Cli/CombatScenarios.cs` from `new Dice(1, 6)` to `new Dice(1, 8)`, and run again to compare. Use more fights (`--fights 10000`) for smaller differences; it takes about a second.
4. To check a band, for example in CI, add `--min-win-rate` and `--max-win-rate` (fractions 0 to 1):
   ```
   dotnet run --project src/Realm.Cli -- combat-sim goblin-ambush --min-win-rate 0.85 --max-win-rate 0.99
   ```
   Exit code `0` inside the band, `1` outside it (the last line says why).
5. To add a scenario, add a `case` to `CombatScenarios.Build` and a line to `CombatScenarios.All`. Give every combatant a unique ID; the hero is team `0` (`CombatScenarios.HeroTeam`).

## Verify
The same scenario, seed and fight count always print the same numbers. A different `--seed` gives slightly different numbers.

## Troubleshooting
- **Many timeouts:** a melee combatant can't reach its target (there is no movement yet), so the fight never ends. Put melee fighters within their attack's range, or raise `--max-seconds` if the fight is just long.
- **`Unknown scenario`:** check `--list`; names are case-sensitive.
- **Win rate looks wrong:** remember the hero doesn't use the tactical pause in these runs, and `NearestEnemyBrain` skips area attacks that would hit itself.

## Related
- [CLI reference: combat-sim](../reference/cli.md#combat-sim-scenario---fights-n---seed-n---max-seconds-s---min-win-rate-f---max-win-rate-f---list)
- [Combat explanation](../explanation/combat.md), [ADR 0029](../decisions/0029-combat-model-solo-character-hybrid-pause.md)
