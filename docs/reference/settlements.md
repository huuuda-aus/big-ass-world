# Baked settlements (`settlements.json`)

Written by `realm bake-terrain` next to the terrain tiles and read by the game. It lists where the cities stand and the seeds they are generated from; the cities themselves are regenerated at runtime, deterministically. Code: `Realm.Gen.Settlements.Placement.SettlementManifest`.

## Example

```json
{
  "format": 1,
  "world": "world.main",
  "seed": 1,
  "generators": ["settlement.placement v1"],
  "settlements": [
    {
      "id": "settlement.city_1",
      "name": "Thornford",
      "tier": "City",
      "x": 5686,
      "z": 566,
      "rotation": 3.114592,
      "groundHeight": 115.0,
      "wallRadius": 292.13,
      "padRadius": 322.13,
      "citySeed": 1234567890,
      "maxBuildings": 1200,
      "cathedral": true,
      "centre": "Fountain"
    }
  ]
}
```

(Numbers are illustrative; the real file has 8 entries.)

## Fields

| Field | Meaning |
|---|---|
| `format` | File format version; currently 1. Other values are rejected (`SETL006`). |
| `world`, `seed` | The world map ID and the world seed the settlements were placed with. |
| `generators` | Generator versions that produced the list. |
| `id` | Stable identifier. Cities are `settlement.city_N`, towns `settlement.town_N`. |
| `name` | Display name, generated from syllables (`Thornford`, `Oakley`); a placeholder until names are designed. |
| `tier` | `City` or `Town`. |
| `x`, `z` | World position of the central square in metres. |
| `rotation` | Rotation of the city plan about its centre in radians (counter-clockwise in plan coordinates; the game node turns by minus this angle about the vertical axis). |
| `groundHeight` | Height of the flat ground under the city, metres above sea level. |
| `wallRadius` | Distance from the centre to the farthest wall corner. |
| `padRadius` | Radius of the flattened ground (wall radius plus 30 m). |
| `citySeed`, `maxBuildings`, `cathedral`, `centre` | The inputs of `CityGenerator.Generate` (`Settlement.ToCityParams()`). `centre` is `None`, `Fountain`, `Pyre`, `ExecutionBlock` or `Market`. |

## Coordinates

A city is generated in its own plane (metres, central square at the origin, plan x and y). `Settlement.ToWorld(plan)` rotates by `rotation` and moves to (`x`, `z`); plan y becomes world Z. Meshes (`Realm.Gen.Settlements.Meshing`) use a local frame with X = plan x, Y = height, Z = plan y, so the game puts a node at (`x`, `groundHeight`, `z`) rotated by minus `rotation` about Y.

## Settings

`SettlementSettings` (all placeholders): 3 cities of 1,200 houses (wall radius about 290 to 310 m) and 5 towns of 400 houses (about 190 m); minimum spacing 2,500 m; pad margin 30 m and blend 100 m; candidate grid 128 m; height range 15 to 320 m; relief limit 0.1 x radius; water clearance 40 m; river bonus 0.25 within 1,500 m; world edge margin 400 m.

# Baked roads (`roads.json`)

Written next to `settlements.json` ([ADR 0013](../decisions/0013-roads-and-bridges.md)); code: `Realm.Gen.Settlements.Roads.RoadManifest`. The road surface itself is already in the terrain tiles (graded and painted); this file holds the centre lines and the bridges.

```json
{"format":1,"world":"world.main","seed":1,"generators":["road.planner v1"],
 "roads":[{"id":"road.1","from":"settlement.city_1","to":"settlement.city_2","width":6,
   "points":[[5421.5,433.2,115.0],[5413.6,429.8,115.0]],
   "bridges":[{"x":-3982,"z":-271,"yaw":0.68,"length":31.87,"width":6,"startHeight":62.95,"endHeight":62.86,"first":41,"last":45}]}]}
```

| Field | Meaning |
|---|---|
| `points` | Centre line about 8 m apart: world X, world Z, surface height in metres above sea level. The first and last points are at a city gate. |
| `bridges[].x`, `z` | World position of the bridge centre. |
| `bridges[].yaw` | Plan angle of the bridge along the road, `atan2(dz, dx)`; the game turns the mesh by minus this angle about the vertical axis. |
| `bridges[].length`, `width` | Deck length between the abutments and deck width, in metres. |
| `bridges[].startHeight`, `endHeight` | Deck height at each end. |
| `bridges[].first`, `last` | Indices of the road points where the bridge starts and ends. |

# Baked map (`map.png`)

A 1,024 pixel overview of the final terrain (heights coloured and hill-shaded, rivers and lakes), north (minus Z) at the top, covering the whole world square. The game's map screen (M) shows it with the roads and settlement names drawn on top.
