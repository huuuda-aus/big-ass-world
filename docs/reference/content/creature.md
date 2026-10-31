# Content type: `creature`

A creature family with its tiers (CLAUDE.md section 6.5). Tier 1 is the family itself; each further tier is a stronger or different variant that changes only some values, the way *Might and Magic* has goblin, goblin archer and goblin chief.

- Directory: `content/creatures/`
- One record per file: `content/creatures/<name>.yaml`, where `<name>` is the ID without the `creature.` prefix.
- C# types: `Realm.Data.Content.CreatureDefinition` with resolved `CreatureTier`s; parsed by `CreatureParser`; turned into a combatant by `Realm.Sim.Combat.CombatContent.CreateCreature(id, tier, …)`.

## Fields

| Field | Type | Required | Rules |
|---|---|---|---|
| `id` | string | yes | Content ID starting with `creature.`. Unique across all content. |
| `name` | string | yes | Name of the family and of tier 1. |
| `tags` | list of strings | no | Same grammar as [material tags](material.md). Not read by the rules yet. |
| `behaviour` | word | yes | AI profile. Today only `nearest_enemy`: attack the nearest enemy with the strongest attack in range, never with an area attack that would hit itself; when nothing reaches, walk towards it until the longest-reaching attack that would work does. |
| `health` | whole number | yes | Health when unhurt; above 0. |
| `attack_rating` | whole number | no (0) | Added to an attack's bonus for the hit roll. |
| `defence` | whole number | no (0) | Subtracted from an attacker's rating for the hit roll. |
| `speed_m_s` | number | yes | Walking speed in metres per second, 0 to 50. `0` is a creature that never moves (a turret, a plant). A creature walks between attacks, while it recovers. |
| `attacks` | list of IDs | yes | At least one `attack.` ID ([attack](attack.md)). |
| `resistances` | map word to number | no | Percent of damage of a type it ignores, 0 to 100, for example `{ poison: 50 }`. Types not listed resist nothing. |
| `tiers` | list of tiers | no | Tiers 2, 3, … in order. Each tier needs `name` and may give `tags`, `health`, `attack_rating`, `defence`, `speed_m_s`, `attacks` and `resistances`. |

How a tier is built from the family (tier 1), never from the tier before it:
- `health`, `attack_rating`, `defence`, `speed_m_s`: the tier's value if given, else the family's.
- `attacks`: the tier's list **replaces** the family's if given (it may not be empty).
- `resistances`: **merged** over the family's per damage type.
- `tags`: the tier's tags are **added** to the family's.

Not yet in the schema (planned with later milestones): looks (model, billboard or palette swap), loot, spawn and biome affinity.

Unknown fields are `DATA001`; bad values are `DATA023`; an attack ID of another type is `DATA014`, of an attack that doesn't exist `DATA013`. A resistance to a damage type no attack or condition deals is warning `DATA024` (often a typo).

## Complete example

`content/creatures/goblin.yaml`:

```yaml
id: creature.goblin
name: Goblin
tags: [humanoid, goblinoid]
behaviour: nearest_enemy
health: 14
attack_rating: 1
defence: 2
speed_m_s: 4
attacks: [attack.goblin_club]
tiers:
  - name: Goblin Archer
    tags: [ranged]
    health: 10
    attack_rating: 2
    defence: 1
    speed_m_s: 3.5
    attacks: [attack.goblin_bow]
  - name: Goblin Chief
    tags: [leader]
    health: 30
    attack_rating: 3
    defence: 4
    speed_m_s: 4.5
    attacks: [attack.goblin_chief_axe]
    resistances:
      poison: 50
```

Resolved tiers:

| Tier | Name | Health | Rating | Defence | Speed | Attacks | Resistances | Tags |
|---|---|---|---|---|---|---|---|---|
| 1 | Goblin | 14 | 1 | 2 | 4 m/s | goblin_club | none | goblinoid, humanoid |
| 2 | Goblin Archer | 10 | 2 | 1 | 3.5 m/s | goblin_bow | none | goblinoid, humanoid, ranged |
| 3 | Goblin Chief | 30 | 3 | 4 | 4.5 m/s | goblin_chief_axe | poison 50 % | goblinoid, humanoid, leader |

A bad value is reported like this:

```
Error DATA023 creatures/goblin.yaml: Behaviour 'berserk' is unknown. (fix: Use one of: nearest_enemy.)
```

## Shipped creatures

Only `creature.goblin` (above). All values are placeholders, not design.
