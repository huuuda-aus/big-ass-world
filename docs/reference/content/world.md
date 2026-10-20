# Content type: `world` (macro map)

The coarse description of the world: its size and the regions that tile it. Design rationale: [ADR 0006](../../decisions/0006-macro-map-format.md).

- Directory: `content/world/`
- One record per file: `content/world/<name>.yaml`, where `<name>` is the ID without the `world.` prefix. The shipped file is `main.yaml` (`world.main`).
- C# types: `Realm.Data.Content.MacroMapDefinition`, `RegionDefinition`

## Coordinates

Metres. The origin is the world centre. X grows east, Z grows south. A world of `world_size_m: 20480` spans -10240 to 10240 on both axes.

## Fields

| Field | Type | Required | Rules |
|---|---|---|---|
| `id` | string | yes | Content ID starting with `world.`. |
| `name` | string | yes | Display name. |
| `world_size_m` | integer | yes | Side of the square world in metres; positive. Must be a multiple of the macro cell size (64 m by default) for the rasteriser. |
| `regions` | list | yes | At least one region. |

Each region:

| Field | Type | Required | Rules |
|---|---|---|---|
| `id` | string | yes | Content ID starting with `region.`. Unique across all content. |
| `name` | string | yes | Display name. |
| `biome` | string | yes | ID of a `biome` record. Must exist. |
| `polygons` | list of rings | yes | At least one ring. Each ring is a list of `[x, z]` vertices, at least 3. |

Polygon rules (all errors): vertices are finite numbers in `[x, z]` pairs; no repeated consecutive vertices; not all on one line; no self-intersection and no touching non-neighbouring edges; completely inside the world (touching the world edge is allowed). Rings have no holes. Winding direction does not matter.

**Coverage rule** (checked by the rasteriser, not the loader): every part of the world must belong to exactly one region. Ocean is an ordinary region whose biome is tagged `ocean`. A cell belongs to the region whose polygon contains the cell's centre. Polygons that share an edge may use identical vertices; each point belongs to exactly one of them.

Unknown fields are errors.

## Complete example

`content/world/main.yaml` (shortened to a small land block in an ocean ring):

```yaml
id: world.main
name: Main world
world_size_m: 20480
regions:
  - id: region.verdant_vale
    name: Verdant Vale
    biome: biome.temperate_forest
    polygons:
      - [[-6000, -6000], [6000, -6000], [6000, 6000], [-6000, 6000]]
  - id: region.outer_sea
    name: Outer Sea
    biome: biome.deep_ocean
    polygons:
      - [[-10240, -10240], [10240, -10240], [10240, -6000], [-10240, -6000]]
      - [[-10240, 6000], [10240, 6000], [10240, 10240], [-10240, 10240]]
      - [[-10240, -6000], [-6000, -6000], [-6000, 6000], [-10240, 6000]]
      - [[6000, -6000], [10240, -6000], [10240, 6000], [6000, 6000]]
```

The shipped `content/world/main.yaml` has six land regions (a 16 km square of land, from -8000 m to 8000 m) inside an ocean ring: Frostpeak Range (alpine peaks), Ashen Highlands (highlands), Mistwood (temperate forest), Sunlit Steppe (dry steppe), Saltmarsh (marsh) and Redrock Badlands (badlands). The land regions share their border vertices exactly, which is how they tile the square.

Report on the shipped world:

```
$ dotnet run --project src/Realm.Cli -- macro-map
world.main: 20480 m square, 320 x 320 cells of 64 m (terrain.macro_raster v1, checksum 0ca2e5c36444e8ae)
  region                       biome                         cells       km2   share
  region.ashen_highlands       biome.highlands               12483      51.1   12.2%
  region.frostpeak_range       biome.alpine_peaks             9453      38.7    9.2%
  region.mistwood              biome.temperate_forest         7241      29.7    7.1%
  region.outer_sea             biome.deep_ocean              39900     163.4   39.0%
  region.redrock_badlands      biome.badlands                 6652      27.2    6.5%
  region.saltmarsh             biome.marsh                   14676      60.1   14.3%
  region.sunlit_steppe         biome.dry_steppe              11995      49.1   11.7%
0 error(s), 0 warning(s).
```

The shipped region names, layout and biomes are placeholders, not design.

## Validation

`DATA001` to `DATA017` at load, `TERR001` to `TERR005` when rasterised: see [validators](../validators.md). Terrain, erosion and river checks (`TERR006` to `TERR017`) use the regions' biomes.
