# Content type: `condition`

A state an attack can put on a combatant, such as poisoned or asleep ([ADR 0029](../../decisions/0029-combat-model-solo-character-hybrid-pause.md)). Attacks apply conditions through their `applies` field ([attack](attack.md)).

- Directory: `content/conditions/`
- One record per file: `content/conditions/<name>.yaml`, where `<name>` is the ID without the `condition.` prefix.
- C# type: `Realm.Data.Content.ConditionDefinition`; parsed by `ConditionParser`; turned into the runtime `Realm.Sim.Combat.Condition` by `CombatContent`.

## Fields

| Field | Type | Required | Rules |
|---|---|---|---|
| `id` | string | yes | Content ID starting with `condition.`. Unique across all content. |
| `name` | string | yes | Display name. |
| `tags` | list of strings | no | Same grammar as [material tags](material.md). |
| `duration_s` | number | yes | How long it lasts once applied, in seconds; above 0, at most 3600. Applying it again restarts the time. |
| `prevents_actions` | true/false | no (false) | The affected combatant cannot act while it lasts (asleep, paralysed). It also cannot start or continue a tactical-pause queue. |
| `breaks_on_damage` | true/false | no (false) | Any damage above zero ends it (a hit wakes a sleeper). Damage fully resisted (0) does not. |
| `pulse` | map | no | Damage dealt at intervals while it lasts: `every_s` (seconds between pulses, above 0, at most `duration_s`), `damage` (whole number, 0 or more, before resistance) and `type` (damage type word, default `physical`). The first pulse comes `every_s` after it is applied; a pulse due on the last tick still lands. |

Times are converted to whole sim ticks (20 per second), rounding half away from zero, at least one tick.

Unknown fields are `DATA001`; bad values are `DATA022`.

## Complete example

`content/conditions/poisoned.yaml`:

```yaml
id: condition.poisoned
name: Poisoned
tags: [toxin]
duration_s: 10
pulse:
  every_s: 1
  damage: 1
  type: poison
```

This deals 1 poison damage every second for 10 seconds: 10 pulses. A bad value is reported like this:

```
Error DATA022 conditions/poisoned.yaml: Field 'pulse.every_s' must be above 0 and at most 10, not 12.
```

## Shipped conditions

| ID | Effect |
|---|---|
| `condition.poisoned` | 1 poison damage per second for 10 s (the goblin archer's arrows) |
| `condition.asleep` | cannot act for 10 s, a hit wakes it (not used by any attack yet) |

All values are placeholders, not design.
