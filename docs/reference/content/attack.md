# Content type: `attack`

One thing a combatant can do to a target: a weapon swing, a bow shot or a spell ([ADR 0029](../../decisions/0029-combat-model-solo-character-hybrid-pause.md)). Creatures list their attacks ([creature](creature.md)); how attacks resolve is described in [the combat system](../../systems/combat.md).

- Directory: `content/attacks/`
- One record per file: `content/attacks/<name>.yaml`, where `<name>` is the ID without the `attack.` prefix.
- C# type: `Realm.Data.Content.AttackDefinition`; parsed by `AttackParser`; turned into the runtime `Realm.Sim.Combat.Attack` by `CombatContent`.

## Fields

| Field | Type | Required | Rules |
|---|---|---|---|
| `id` | string | yes | Content ID starting with `attack.`. Unique across all content. |
| `name` | string | yes | Display name. |
| `tags` | list of strings | no | Same grammar as [material tags](material.md), for example `melee`, `ranged`, `spell`. Not read by the rules yet. |
| `recovery_s` | number | yes | Seconds before the user may act again after using it; above 0, at most 60. Converted to whole ticks (20 per second). |
| `range_m` | number | yes | Furthest target distance in metres; above 0, at most 1000. |
| `damage` | dice | yes | `NdS`, `NdS+B`, `NdS-B` or a whole number, for example `1d8+2` or `5`. At most 100 dice of up to 1000 sides, bonus up to ±10000. No spaces, lowercase `d`. A roll never goes below 0. |
| `damage_type` | word | no (`physical`) | Matched against the target's resistances. Lowercase letters, digits and underscores. Any word works; there is no fixed list. |
| `attack_bonus` | whole number | no (0) | Added to the user's attack rating for the hit roll. |
| `area_radius_m` | number | no (0) | 0 is a single-target attack with a hit roll. Above 0 (at most 100) is an area attack: no hit roll, and it damages every living combatant within this radius of the target, **the user included**. |
| `ap_cost` | whole number | no (1) | Action points it costs when queued in the tactical pause; 0 to 100. |
| `applies` | map | no | A condition it may apply to whoever it hits and leaves alive: `condition` (a `condition.` ID) and `chance` (percent, 0 to 100, rolled per victim). |

Unknown fields are `DATA001`; bad values are `DATA021`; `applies.condition` pointing at another type is `DATA014`, at a condition that doesn't exist `DATA013`.

## Complete example

`content/attacks/goblin_bow.yaml`:

```yaml
id: attack.goblin_bow
name: Goblin bow
tags: [ranged]
recovery_s: 1.5
range_m: 25
damage: 1d6
attack_bonus: 1
applies:
  condition: condition.poisoned
  chance: 20
```

A bad value is reported like this:

```
Error DATA021 attacks/goblin_bow.yaml: Field 'damage' is not a dice expression: '1D6'. (fix: Use NdS, NdS+B, NdS-B or a whole number, for example 2d6+1.)
```

## Shipped attacks

| ID | Recovery | Range | Damage | Notes |
|---|---|---|---|---|
| `attack.longsword` | 1.2 s | 2 m | 1d8+2 physical | the `combat-sim` hero |
| `attack.fireball` | 2 s | 25 m | 2d6+2 fire | area 3 m, 5 AP; the hero |
| `attack.goblin_club` | 1.3 s | 2 m | 1d6 physical | goblin |
| `attack.goblin_bow` | 1.5 s | 25 m | 1d6 physical | +1 to hit, 20 % poison; goblin archer |
| `attack.goblin_chief_axe` | 1.6 s | 2 m | 1d10+1 physical | goblin chief |

All values are placeholders, not design.
