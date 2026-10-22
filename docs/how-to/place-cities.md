# Place cities on the map, follow the roads and walk into one

## Goal
Bake a world with cities, towns and the roads between them, change how many there are, find them on the map, and walk through a city and into a house.

## Prerequisites
- The repository builds ([getting started](../tutorials/getting-started.md)) and Godot is set up ([set up Godot](set-up-godot.md)).

## Steps
1. Bake the world. Settlements are part of the normal bake:
   ```bash
   dotnet run --project src/Realm.Cli -- bake-terrain
   ```
   You should see a line `settlements: 9 placed on the natural ground` followed by one line per settlement (3 cities, 1 city marked `(mountain)`, 5 towns, each with a generated name and its number of service buildings), then `roads: 17 road(s), 66.6 km, 13 bridge(s), ...` and `0 error(s), 0 warning(s)`. The player's spawn is in the first city's central square.
2. Change the number of settlements:
   ```bash
   dotnet run --project src/Realm.Cli -- bake-terrain --cities 2 --towns 10
   ```
   Other settings (sizes, spacing, how steep the land may be, the number of mountain cities, `FlattenStrength`) are in `SettlementSettings` (`src/Realm.Gen/Settlements/Placement/SettlementSettings.cs`); see [settlements](../reference/settlements.md#settings). If the land cannot hold what you asked for you get warning `SETL002` and fewer settlements.
3. Run the game: `godot --path src/Realm.Game`. You start in a city square beside a fountain. Walk (WASD or the arrow keys) along a street; houses within 35 m turn into detailed houses.
4. Walk through a front door: it is an opening, so you simply walk in. Inside there are rooms, a floor, a ceiling and (in two-storey houses) a stair you can walk up. Each lit house has a lamp per storey.
5. Press **M** for the map: names of every city and town, the roads, and your position. The HUD also says which settlement is nearest, how far and in which direction. Follow a road (a brown strip) and you reach the next place; roads cross rivers on bridges.
6. Walk to a city on a hillside (the mountain city is in the Ashen Highlands, about 5 km north-east of the world centre; press **M** to find it). Houses stand level on stone plinths or wooden pilings where the ground falls away, with steps down to the street. A service building (inn, blacksmith, trader, baker, warehouse, city hall, jail) has a coloured sign over its door; farms and a woodcutter stand outside the wall. To add a kind of service building see [add a service building](add-a-service-building.md).
7. Fly (F) high above to see the whole plan: wards in different ground colours, parks in green, the cathedral, the wall with towers and four gates.

## Verify
The HUD shows `cities: 1 built, 0 building, N detailed houses, in City 1`. A city appears about two seconds after you come within about 800 m of it. If `settlements_ground.bin` is missing, the game warns and builds cities on level ground. `--no-cities` starts the game without them; `--no-roads` on the bake leaves the roads out; `--map` starts with the map open.

## Troubleshooting
- No cities appear: the bake was made without settlements (`--no-settlements`) or before this feature; re-run step 1. The file `baked/terrain/settlements.json` must exist.
- You fall through a house floor right after loading: houses get collision when you are within 35 m; wait a moment or move.
- Frame hitches when a city appears are known ([known issues](../known-issues.md)).

## Related
[Baked settlements](../reference/settlements.md), [settlements system](../systems/settlements.md), [run the game](run-the-game.md), [ADR 0012](../decisions/0012-city-generator-import-and-placement.md).
