# Baked terrain format

What `realm bake-terrain` writes and what the game streams. Design: [ADR 0009](../decisions/0009-terrain3d-integration-and-streaming.md). Code: `Realm.Gen.Bake` (no Godot dependency).

## Directory layout

```
baked/terrain/
  manifest.json
  region_-10_-10.tile
  region_-10_-9.tile
  ...
  region_9_9.tile
```

One tile per region. The default sizes give 20 x 20 = 400 tiles, about 202 MB. Files are written to a `.tmp` name and moved into place, so a crash never leaves a half-written tile.

## Regions

Region (0, 0) starts at the world origin (the world centre) and extends east (+X) and south (+Z). A region is `regionSize` vertices wide at `spacingMeters` metres, so `regionExtentMeters = regionSize x spacingMeters`. Coordinates run from `minRegion` to `maxRegion` on each axis. A region holds the field vertices from its own corner up to, but not including, its neighbour's corner, so adjacent regions share no duplicate edge vertices.

The default is 256 vertices at 4 m, so regions are 1,024 m wide: 4 x 4 streaming chunks of 256 m and 16 x 16 macro cells of 64 m. Terrain3D 1.0.2 allows at most 32 regions per axis; the world uses 20.

## `manifest.json`

```json
{
  "format": 1,
  "world": "world.main",
  "worldSizeMeters": 20480,
  "spacingMeters": 4,
  "regionSize": 256,
  "regionExtentMeters": 1024,
  "regionsPerAxis": 20,
  "minRegion": -10,
  "maxRegion": 9,
  "seed": 1,
  "generators": ["terrain.height_field v1, terrain.erosion v1"],
  "minHeight": -218.16107177734375,
  "maxHeight": 655.7567749023438,
  "riverCount": 18,
  "lakeCount": 1,
  "spawnX": -912,
  "spawnZ": 2688,
  "checksum": "22c8eb1cf80badfa"
}
```

| Field | Meaning |
|---|---|
| `format` | Manifest format version (currently 1). |
| `world` | Content ID of the macro map that was baked. |
| `generators` | The generator IDs and versions that produced the terrain. |
| `seed` | World seed used by the height field. |
| `spawnX`, `spawnZ` | Where the player starts: beside the middle of the longest river, otherwise the world centre. |
| `checksum` | FNV-1a fingerprint of every tile's checksum in region order (16 hex digits). Identical inputs give an identical value. |

## `.tile` file

Little endian. Total size is `24 + size x size x 8 + 8` bytes.

| Offset | Type | Content |
|---|---|---|
| 0 | 4 bytes | Magic `RTIL` |
| 4 | uint32 | Format version (1) |
| 8 | int32 | Region X |
| 12 | int32 | Region Z |
| 16 | int32 | Size (vertices per side) |
| 20 | float32 | Spacing in metres |
| 24 | float32 x size x size | Heights in metres, row by row from north to south, each row west to east |
| next | uint32 x size x size | Colours, packed 0xAABBGGRR (red in the lowest byte, the byte order of an RGBA8 image) |
| last 8 | uint64 | FNV-1a checksum of all bytes before it |

The reader rejects a short file, bad magic, an unknown version, a size outside 2 to 4096, a length that does not match the size, and any checksum mismatch, each with a distinct message (`InvalidDataException`).

## What is in the colour map

- A height ramp (placeholder palette): deep and shallow water, lowland, hills, rock, snow.
- **Baked hill shading** lit from the north-west (a surface rising toward the south-east faces the light and brightens), using the height field's own neighbours so tile edges are seamless. Terrain3D has no textures yet, so this is what makes relief visible.
- Rivers painted blue as a stripe whose half-width is `max(2 m, 0.6 x sqrt(flow in km2))`, and lake cells painted blue. Rivers follow the 32 m routing grid, so they have a stair-step look ([known issue 30](../known-issues.md)).

## Related
- [CLI: `bake-terrain`](cli.md), [Run the game](../how-to/run-the-game.md), [Game system](../systems/game.md)
