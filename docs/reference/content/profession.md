# Content type: `profession`

What people of a trade do: where they work and how they spend the day ([ADR 0025](../../decisions/0025-professions-and-reactions.md)).

- Directory: `content/professions/`
- One record per file: `content/professions/<name>.yaml`, where `<name>` is the ID without the `profession.` prefix.
- C# type: `Realm.Data.Content.ProfessionDefinition`; parsed by `ProfessionParser`; turned into character plans by `Realm.Gen.Population.CrowdGenerator`.

## Fields

| Field | Type | Required | Rules |
|---|---|---|---|
| `id` | string | yes | Content ID starting with `profession.`. Unique across all content. |
| `name` | string | yes | Display name. |
| `tags` | list of strings | no | Same grammar as [material tags](material.md). Special tags: `filler` (people nobody else claims become this; exactly one profession should have it), `leisure` (drawn as an idler), `watch` (guards: stay out in the rain and wear the guard colour). |
| `workplace` | map | no | Where they work: `service: <service id>` (a service building, for example `service.baker`) or `kind: <kind>` with a kind of `none` (default), `stall` (a stand by a market stall), `gate_post` (a post just inside a gate), `square`, `patrol` (a round of the gates and the square) or `fields` (out through a gate, out of sight). A service the settlement lacks gives its people the filler profession. |
| `share` | map tier to number | yes | Share (0 to 1) of a settlement's people of each tier: `city`, `town`, `hamlet`. A tier that is missing gets none. The shares of all professions for a tier add up to at most 1; whoever is left over becomes the filler. |
| `day` | list of blocks | yes | The day: each block is `{ from: "HH:MM", to: "HH:MM", do: <activity> }`. Times are quoted. A block that ends before it starts wraps past midnight, `to: "00:00"` is midnight. Blocks may not overlap; minutes no block covers are spent at home. |

Activities (`do`): `home` (inside the home), `work` (at the workplace: inside the service building, or standing at the stall or post), `about` (out and about: standing at one place in the settlement and now and then moving to another or calling on a house), `inn` (in the inn), `errand` (visiting buildings near the home or the square), `patrol` (a round of the patrol, pausing at each stop), `fields` (out in the fields).

Unknown fields and unknown words are errors (`DATA001`, `DATA020`); a service that does not exist is `DATA013`.

## Complete example

`content/professions/baker.yaml`:

```yaml
id: profession.baker
name: Baker
tags: [food, craft]
workplace: { service: service.baker }
share: { city: 0.04, town: 0.05 }
day:
  - { from: "05:30", to: "17:00", do: work }
  - { from: "18:30", to: "21:00", do: inn }
```

A bad word is reported like this:

```
Error DATA020 professions/baker.yaml: Day block has unknown activity 'dance'. (fix: Use one of: home, work, about, inn, errand, patrol, fields.)
```

## Shipped professions

| ID | Workplace | Day |
|---|---|---|
| `profession.idler` | none | out and about from 09:30 to 19:30 |
| `profession.townsperson` (filler) | none | errands, out and about, the inn |
| `profession.baker`, `blacksmith`, `labourer`, `woodcutter`, `innkeeper` | the service building | work by day, the inn in the evening |
| `profession.trader` | a stall | work 08:00 to 18:00, the inn |
| `profession.farmer` | the fields | out 06:00 to 17:30, then out and about |
| `profession.guard` | a post at a gate | work 06:00 to 18:00 |
| `profession.watchman` | patrol | 18:00 to 06:00 |

All values are placeholders, not design.
