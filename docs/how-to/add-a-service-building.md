# Add a service building

Goal: make every city (and town) contain one more kind of building, for example a healer's house, and check it is there.

## Prerequisites
- The repository builds ([getting started](../tutorials/getting-started.md)).
- You know what a [service record](../reference/content/service.md) holds.

## Steps
1. Create `content/services/healer.yaml`:
   ```yaml
   id: service.healer
   name: Healer
   tags: [civic]
   counts: { city: 2, town: 1 }
   wards: [craftsmen, merchant]
   near: square
   min_area_m2: 55
   main_room: living_room
   ```
   The file name must match the ID (`healer.yaml` for `service.healer`).
2. Check the record:
   ```bash
   dotnet run --project src/Realm.Cli -- content-lint
   ```
   You should see `10 service(s)` in the summary line and `0 error(s)`.
3. Bake the world so `settlements.json` carries the new service:
   ```bash
   dotnet run --project src/Realm.Cli -- bake-terrain
   ```
   You should see `18 service building(s)` for a city (was 16) in the settlement list.
4. Run the game and walk to a city. The healer's house has a coloured sign hanging over its front door (the colour comes from the service ID, so every healer's sign matches).

## Verify
`dotnet test tests/Realm.Gen.Tests --filter ServiceBuilding` passes: it generates cities of both tiers on several seeds and checks every service is present, inside or outside the wall as asked, and big enough.

## Troubleshooting
- `DATA018 ... unknown value`: use one of the words the message lists.
- `SETL007 ... has 0 of 'service.healer' but needs 2`: the city had no free lot (or, for `outside_wall`, no farmland next to the wall). Lower `counts` or `min_area_m2`.
- `SETL008`: no lot was as big as `min_area_m2`, so the largest free lot was used. Lower `min_area_m2` if the size does not matter.
- Nothing changed in the game: the game reads `settlements.json`, so you must bake again after editing a service.

## Related
- [Service reference](../reference/content/service.md), [settlements system](../systems/settlements.md), [ADR 0014](../decisions/0014-cities-on-natural-ground-and-service-buildings.md).
