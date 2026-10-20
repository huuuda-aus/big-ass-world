# Validator codes

Every validator issue has a stable code. A code is never reused for a different problem. Constants live in `Realm.Data.Content.ValidatorCodes`.

Run content validation with `dotnet run --project src/Realm.Cli -- content-lint`.

## Content loading (`DATA`)

| Code | Severity | Meaning | Typical fix |
|---|---|---|---|
| DATA001 | Error | YAML syntax error, unknown field, or wrong value type. Location includes the line. | Fix the YAML. For `Unknown field 'x'`, check the spelling against the [schema](content/material.md). |
| DATA002 | Error | Required field missing or empty (`id`, `name`). | Add the field. |
| DATA003 | Error | `id` breaks the grammar. | Use lowercase `type.name`, for example `material.iron_ore`. |
| DATA004 | Error | The ID's type differs from where it is defined: a file in the wrong directory (`item.x` inside `materials/`), or a region not starting with `region.`. | Fix the ID prefix or move the file. |
| DATA005 | Error | Two records share an ID. The first one (in sorted file order) is kept. | Give each record a unique ID. |
| DATA006 | Error | A tag breaks the grammar. | Lowercase letters, digits and underscores, starting with a letter. |
| DATA007 | Error | A property name breaks the grammar, or a value is not a finite number. | Rename it or use a real number. |
| DATA008 | Warning | File name differs from the ID's name part. | Rename the file to `<name>.yaml`. |
| DATA009 | Warning | A directory under the content root is not a known content type; its files are ignored. | Fix the directory name, or ignore it if the type is not supported yet. |
| DATA010 | Error | The content root directory does not exist. | Pass the right path to `content-lint`. |
| DATA011 | Error | A file could not be read. | Check permissions and that the file is not locked. |
| DATA012 | Error | A file holds no record. | Add the record or delete the file. |
| DATA013 | Error | A record references an ID that no loaded record defines. Location is the referencing file. | Fix the ID, or add the missing record. |
| DATA014 | Error | A reference field points at a record of the wrong type (for example `material: item.torch`). | Use an ID with the required prefix. |
| DATA015 | Error | `world_size_m` is zero or negative. | Use a positive number of metres. |
| DATA016 | Error | A region polygon is invalid: fewer than 3 vertices, a vertex that is not an `[x, z]` pair, a non-finite number, repeated or collinear vertices, or edges that cross or touch. The message names the region, the ring and the reason. | Fix the vertices; keep the ring simple. |
| DATA017 | Error | A region polygon extends outside the world square. | Move the vertices inside, or raise `world_size_m`. |

Known content types today: `material`, `item`, `biome`, `world`.

## Terrain generator (`TERR`)

Produced by `Realm.Gen.Terrain.MacroRasterizer` when the macro map is rasterised (for example by `realm macro-map`). Constants live in `Realm.Gen.Terrain.TerrainCodes`.

| Code | Severity | Meaning | Typical fix |
|---|---|---|---|
| TERR001 | Error | `world_size_m` is not a multiple of the macro cell size (64 m by default). No field is built. | Use a multiple, for example 20480. |
| TERR002 | Error | Some cells belong to no region (a gap). The message gives the count and example cell centres. | Extend a region so the whole world is covered; ocean is a region too. |
| TERR003 | Error | Some cells are claimed by two regions (an overlap). The message names the pair and the count. | Move the shared border so each area has one region. A region overlapping itself is allowed. |
| TERR004 | Warning | A region owns no cells: it is thinner than a cell or fully overlapped. | Enlarge it or remove it. |
| TERR005 | Error | More regions than the cell encoding holds (65,534). | Merge regions. |

### Height field (also `TERR`)

Produced by `Realm.Gen.Terrain.BiomeTerrainParameters`, `HeightFieldGenerator` and `HeightFieldValidator` (for example by `realm height-field`).

| Code | Severity | Meaning | Typical fix |
|---|---|---|---|
| TERR006 | Error | A biome lacks a property a terrain step requires: `relief_m`, `roughness`, and `base_height_m` (land) or `water_depth_m` (ocean) for heights; `rainfall`, `erodibility`, `max_slope_deg` and `river_loss` (land) for erosion. | Add the property under `properties` in the biome file. |
| TERR007 | Error | A biome property is out of range: negative `relief_m`, `roughness`, `rainfall`, `erodibility` or `river_loss` outside 0 to 1, `max_slope_deg` outside (0, 90], non-positive `water_depth_m`. | Use a value in range. |
| TERR008 | Warning | A property that does not apply is ignored (`water_depth_m` on land, `base_height_m` or any erosion property on ocean). | Remove it. |
| TERR009 | Error | A region uses a biome that is not loaded. | Add the biome or fix the ID. |
| TERR010 | Error | The height-field settings are invalid for this world (spacing must be positive and divide the world size, octaves 1 to 12, wavelength positive, lacunarity above 1, gains 0 <= min <= max <= 1, blend not negative). | Fix the setting. |
| TERR011 | Error | The region field is empty or has gaps, so heights cannot be generated. | Fix the `TERR001` to `TERR005` coverage problems first. |
| TERR012 | Error | Some heights are not finite numbers. The message gives a count and an example position. | Check biome properties and settings. |
| TERR013 | Error | Some heights are beyond the allowed range (4000 m by default). | Reduce `base_height_m`, `relief_m` or `water_depth_m` of the biomes involved. |
| TERR014 | Error | The terrain is steeper than allowed (rise over run 2 by default) where two regions meet. The message gives the count, the worst slope and an example position. | Increase the blend radius, or reduce the height difference between neighbouring biomes. |

The slope in `TERR014` is measured between neighbouring vertices, so it depends on the vertex spacing: finer spacing can show steeper local slopes.

### Erosion and rivers (also `TERR`)

Produced by `Realm.Gen.Terrain.Erosion` (for example by `realm height-field --erode`).

| Code | Severity | Meaning | Typical fix |
|---|---|---|---|
| TERR015 | Error | The erosion settings are invalid: working spacing not positive, not dividing the world or not a multiple of the height-field spacing; iterations outside 0 to 1000; negative strength or blend; thermal iterations outside 0 to 10000; river threshold not positive; lake depth not positive; negative minimum lake area. | Fix the setting. |
| TERR016 | Error | A river's water surface rises more than 0.01 m going downstream. Routing should never allow this, so it indicates a bug. | Report it with the seed and settings. |
| TERR017 | Error | A river ends inconsistently: marked as reaching the sea but above sea level, marked as leaving the world but not on the edge, or marked as joining a river it does not touch. Indicates a bug in river extraction. | Report it with the seed and settings. |

Rivers that dry up (`Dried` mouth) are not an error: it is how river loss shows.

### Baking (also `TERR`)

| Code | Severity | Meaning | Typical fix |
|---|---|---|---|
| TERR018 | Error | The bake settings do not fit the height field: region size below 2, a region width that does not divide the world size, more regions per axis than allowed (32 for Terrain3D 1.0.2), or an odd number of regions per axis (the world origin must be a region corner). | Use a region size and `--spacing` whose product divides the world size into an even number of at most 32 regions, for example 256 vertices at 4 m. |

## Dungeon and population validators

**Planned** (CLAUDE.md section 7). Codes will be added here as each validator lands.
