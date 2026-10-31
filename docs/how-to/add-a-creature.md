# How to add a creature family with tiers

Goal: add a new creature family, for example wolves with a stronger dire-wolf tier, together with any new attacks and conditions it needs, and see it fight in `combat-sim`.

## Prerequisites
- A built solution (`dotnet build Realm.sln`).
- Read the references once: [creature](../reference/content/creature.md), [attack](../reference/content/attack.md), [condition](../reference/content/condition.md).

## Steps

1. **Add the attacks.** One file per attack in `content/attacks/`, named after the ID:

   `content/attacks/wolf_bite.yaml`
   ```yaml
   id: attack.wolf_bite
   name: Wolf bite
   tags: [melee, natural]
   recovery_s: 1.0
   range_m: 1.5
   damage: 1d6+1
   ```

   `content/attacks/dire_wolf_bite.yaml`
   ```yaml
   id: attack.dire_wolf_bite
   name: Dire wolf bite
   tags: [melee, natural]
   recovery_s: 1.2
   range_m: 2
   damage: 2d6
   applies:
     condition: condition.bleeding
     chance: 25
   ```

2. **Add any new condition** the attacks apply, in `content/conditions/`:

   `content/conditions/bleeding.yaml`
   ```yaml
   id: condition.bleeding
   name: Bleeding
   duration_s: 6
   pulse: { every_s: 2, damage: 2 }
   ```

3. **Add the family** in `content/creatures/`. The top-level values are tier 1; each entry under `tiers` is the next tier and changes only what it lists:

   `content/creatures/wolf.yaml`
   ```yaml
   id: creature.wolf
   name: Wolf
   tags: [beast, pack]
   behaviour: nearest_enemy
   health: 12
   attack_rating: 2
   defence: 2
   speed_m_s: 6
   attacks: [attack.wolf_bite]
   tiers:
     - name: Dire Wolf
       health: 28
       defence: 3
       speed_m_s: 5.5
       attacks: [attack.dire_wolf_bite]
       resistances: { cold: 30 }
   ```

4. **Lint the content:**
   ```
   dotnet run --project src/Realm.Cli -- content-lint
   ```
   You should see your counts go up (for example `3 condition(s), 7 attack(s), 2 creature(s)`) and `0 error(s)`. In this example you will also see one warning:
   ```
   Warning DATA024 creatures/wolf.yaml: Creature 'creature.wolf' resists 'cold', but no attack or condition deals that damage type. (fix: Check the spelling, or ignore this if the damage type comes later.)
   ```
   That is expected until something deals cold damage; remove the resistance or add a cold attack to clear it.

5. **Try it in a fight.** Scenarios are still listed in code: add a case to `CombatScenarios.Build` in `src/Realm.Cli/CombatScenarios.cs` (and a line to `CombatScenarios.All`), for example:
   ```csharp
   case "wolf-pack":
       fight.Add(Hero(content), NearestEnemyBrain.Instance);
       var wolf = ContentId.Parse("creature.wolf");
       fight.Add(content.CreateCreature(wolf, 1, 2, MonsterTeam, new Point2D(12, 0)), content.BrainFor(wolf));
       fight.Add(content.CreateCreature(wolf, 1, 3, MonsterTeam, new Point2D(-12, 0)), content.BrainFor(wolf));
       fight.Add(content.CreateCreature(wolf, 2, 4, MonsterTeam, new Point2D(0, 15)), content.BrainFor(wolf));
       return fight;
   ```
   Then run `dotnet run --project src/Realm.Cli -- combat-sim wolf-pack`. See [Run a combat balance sweep](run-a-combat-balance-sweep.md).

## Verify
`content-lint` shows `0 error(s)`, and `combat-sim` with your scenario prints a win rate with no `timed out` fights. The wolves run in from 12 to 15 m at their `speed_m_s` and bite once in reach.

## Troubleshooting
- **`DATA013` … uses attack 'attack.x', which does not exist:** a typo in `attacks`, or the attack file is missing or failed to parse (look for its own error above).
- **`DATA021` damage is not a dice expression:** use lowercase `d` and no spaces: `2d6+1`, not `2D6 + 1`.
- **`DATA023` tier needs a name:** every entry under `tiers` needs `name`.
- **`DATA002` missing `speed_m_s`:** every creature needs a speed; use `0` for one that never moves.
- **Fights time out:** a creature with `speed_m_s: 0` and only melee attacks never reaches anyone; give it a speed or a ranged attack.
- **A tier fights with the wrong weapon:** a tier's `attacks` replaces the family's list; repeat any attack you want to keep.
- **`DATA008` warning:** the file name doesn't match the ID; `creature.wolf` lives in `wolf.yaml`.

## Related
- References: [creature](../reference/content/creature.md), [attack](../reference/content/attack.md), [condition](../reference/content/condition.md), [validators](../reference/validators.md)
- [Combat system](../systems/combat.md), [ADR 0029](../decisions/0029-combat-model-solo-character-hybrid-pause.md)
