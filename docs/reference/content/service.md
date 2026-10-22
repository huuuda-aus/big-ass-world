# Content type: `service`

A building every settlement of the listed tiers must contain: city hall, inn, blacksmith and so on ([ADR 0014](../../decisions/0014-cities-on-natural-ground-and-service-buildings.md)).

- Directory: `content/services/`
- One record per file: `content/services/<name>.yaml`, where `<name>` is the ID without the `service.` prefix.
- C# type: `Realm.Data.Content.ServiceBuildingDefinition`; parsed by `ServiceBuildingParser`; turned into the generator's `ServiceSpec` by `Realm.Gen.Settlements.Placement.ServiceCatalog`.

## Fields

| Field | Type | Required | Rules |
|---|---|---|---|
| `id` | string | yes | Content ID starting with `service.`. Unique across all content. |
| `name` | string | yes | Display name. Not empty. |
| `tags` | list of strings | no | Same grammar as [material tags](material.md). |
| `counts` | map tier to number | yes | How many a settlement of each tier gets. Tiers: `city` (including the mountain city), `town` and `hamlet`. A tier that is missing gets none. Each count 1 to 8 (`DATA018` otherwise). |
| `wards` | list | no | Wards inside the wall it may stand in, most preferred first: `market`, `merchant`, `craftsmen`, `slum`. A ward not listed is allowed but ranked last. Empty means any. Not allowed with `outside_wall: true`. |
| `near` | string | no | Where in the ward it prefers to stand: `any` (default), `square` (near the central square), `gate`, `wall`. |
| `outside_wall` | bool | no | `true` builds it as a new house on farmland next to the wall (farmer, woodcutter). Default `false`: it takes over an existing lot. |
| `min_area_m2` | number | no | Smallest footprint it needs, in square metres. Default 40. If no lot is big enough the largest free lot is used and `SETL008` warns. |
| `foundation` | string | no | `stone` or `wood` forces the foundation style on sloping ground. Default: chosen from the ward. |
| `main_room` | string | no | Room type of the main ground-floor room: `hall` (default), `kitchen`, `living_room`, `shop`, `storage`, `workshop`. A `shop` or `hall` is the entrance room; the others take the biggest other room. |

Unknown fields and unknown words are errors (`DATA001`, `DATA018`).

## Complete example

`content/services/inn.yaml`:

```yaml
id: service.inn
name: Inn
tags: [civic, hospitality]
counts: { city: 2, town: 1 }
wards: [merchant, craftsmen]
near: gate
min_area_m2: 80
main_room: kitchen
```

A bad word is reported like this:

```
Error DATA018 services/inn.yaml: Field 'near' has unknown value 'moon'. (fix: Use one of: any, gate, square, wall.)
```

## Shipped services

| ID | City / town / hamlet | Where | Notes |
|---|---|---|---|
| `service.city_hall` | 1 / 0 | market or merchant ward, near the square | stone foundation |
| `service.inn` | 2 / 1 | merchant or craftsmen ward, near a gate | |
| `service.blacksmith` | 2 / 1 | craftsmen or slum ward, near the wall | workshop |
| `service.trader` | 3 / 1 | market or merchant ward, near the square | shop |
| `service.baker` | 2 / 1 | craftsmen or merchant ward | kitchen |
| `service.jail` | 1 / 0 | slum or craftsmen ward, near the wall | stone foundation |
| `service.warehouse` | 2 / 1 | craftsmen or slum ward, near a gate | storage |
| `service.farmer` | 2 / 1 / 1 | outside the wall | wood foundation |
| `service.woodcutter` | 1 / 1 / 1 | outside the wall | wood foundation |

The cathedral is not a service: it is part of every city's plan.

## Validation

Parser codes `DATA001` to `DATA018` ([validators](../validators.md)). After generation, `SETL007` to `SETL009` check that a city has every service.
