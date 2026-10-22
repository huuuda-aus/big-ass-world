# CLI reference (`Realm.Cli`)

Run from the repo root: `dotnet run --project src/Realm.Cli -- <command>`.

Exit codes: `0` success, `1` the command ran and found errors, `2` unknown command.

Log messages (Info and above) go to stderr. Results go to stdout.

## `version`

Prints the assembly version.

```
$ dotnet run --project src/Realm.Cli -- version
Realm.Cli 1.0.0.0
```

## `content-lint [dir]`

Loads and validates all content under `dir` (default `content`). Prints one line per issue, then a summary. Exit code `1` if any error, `0` otherwise (warnings do not fail it). Issue codes: [validators](validators.md).

```
$ dotnet run --project src/Realm.Cli -- content-lint
[Info] Data.Loader: Loaded 3 material(s), 3 item(s), 7 biome(s), 1 world map(s) with 7 region(s), 9 service(s) from 'content'.
3 material(s), 3 item(s), 7 biome(s), 7 region(s), 9 service(s), 0 error(s), 0 warning(s).
```

With a typo in a file:

```
$ dotnet run --project src/Realm.Cli -- content-lint /tmp/broken
[Info] Data.Loader: Loaded 0 material(s), 0 item(s), 0 biome(s), 0 world map(s) with 0 region(s), 0 service(s) from '/tmp/broken'.
Error DATA001 materials/x.yaml:3: Unknown field 'bogus'. (fix: Check the field names and value types against docs/reference/content/material.md.)
0 material(s), 0 item(s), 0 biome(s), 0 region(s), 0 service(s), 1 error(s), 0 warning(s).
```

## `macro-map [dir]`

Loads the content in `dir` (default `content`), then rasterises each `world` record onto the macro cell grid and prints a per-region coverage table, the generator ID and version and a checksum. Errors (content problems and `TERR` coverage problems) are listed after the table. Exit code `1` on any error.

```
$ dotnet run --project src/Realm.Cli -- macro-map
[Info] Data.Loader: Loaded 3 material(s), 3 item(s), 7 biome(s), 1 world map(s) with 7 region(s) from 'content'.
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

With a hole in the map the table gains an `(unassigned)` row and the run ends with, for example:

```
Error TERR002 world.main: 6204 cell(s) belong to no region, for example around (6048, 32), (6112, 32), (6176, 32). (fix: Extend a region (an ocean region counts) so that every part of the world is covered.)
1 error(s), 0 warning(s).
```

## `height-field [dir] [--seed N] [--spacing M] [--blend M] [--erode] [--iterations N] [--strength S] [--threshold KM2] [--min-lake-area KM2] [--png file]`

Loads the content, rasterises each world, generates its height field and prints statistics: vertex count, spacing, seed, generator version, checksum, time, height range and per-region min, max and mean. Defaults: `dir` is `content`, seed 1, spacing 8 m, blend 512 m. With `--png`, writes a coloured, hill-shaded preview image (at most 1024 pixels per side) with pale region borders. Validator issues (`TERR`) are printed after the table. Exit codes: `0` ok, `1` errors, `2` bad arguments.

With `--erode` the step also erodes the terrain and finds rivers and lakes (the preview then shows rivers and lakes in blue). Erosion options: `--iterations N` (default 20), `--strength S` (default 2), `--threshold KM2` (flow needed for a river, default 2) and `--min-lake-area KM2` (smallest lake kept, default 4). The eroded output also lists the longest rivers and every lake. See [tune erosion and rivers](../how-to/tune-erosion-and-rivers.md).

```
$ dotnet run --project src/Realm.Cli -- height-field --spacing 32 --erode --png terrain.png
world.main: 641 x 641 vertices at 32 m, seed 1 (terrain.height_field v1, checksum bd0e4d4af943dd45, 0.29 s)
  height range: -218.2 m to 651.9 m
  region                           min m     max m    mean m
  region.ashen_highlands           -12.7     571.4     392.3
  region.frostpeak_range           -48.4     651.9     353.4
  region.mistwood                  -84.8     384.3      61.4
  region.outer_sea                -218.2     150.0    -187.7
  region.redrock_badlands          -53.6     357.5     229.5
  region.saltmarsh                -139.5     141.2       6.6
  region.sunlit_steppe             -20.0     343.2     122.9
  erosion (terrain.erosion v1, checksum be3febf89a49287a, 6.88 s): height range -218.2 m to 651.4 m, deepest carve 69.2 m
  rivers: 18 (74.8 km total); lakes: 1
    river 0: 15.8 km from (-2624, -1856) to Sea, max flow 33.2 km2
    river 1: 14.7 km from (-3808, -4032) to Sea, max flow 21.0 km2
    river 2: 2.3 km from (-3616, -736) to Confluence of river 0, max flow 9.3 km2
    river 3: 4.8 km from (-3904, 3584) to Confluence of river 0, max flow 27.2 km2
    river 4: 2.1 km from (960, 2848) to Confluence of river 0, max flow 17.6 km2
  ...
    lake 0: 4.87 km2 at 355 m near (-2933, -4630)
  preview: terrain.png (641 x 641)
0 error(s), 0 warning(s).
```

Timings depend on the machine. A seam problem looks like this (here with `--blend 256`):

```
Error TERR014 height field: 1088 step(s) across region borders are steeper than 2 (rise over run); the worst is 2.53, for example (-7688, -8008). (fix: Increase the blend radius, or reduce the height difference between neighbouring biomes.)
```

## `bake-terrain [dir] [--out dir] [--seed N] [--spacing M] [--region-size N] [--no-erode] [--cities N] [--towns N] [--no-settlements] [--no-roads]`

Runs the whole terrain pipeline (macro map, height field, erosion and rivers, then settlements) and bakes the result to region tiles and a manifest for the game ([format](baked-terrain.md)). Defaults: `dir` is `content`, output `baked/terrain`, seed 1, spacing 4 m, region size 256 vertices (1,024 m regions), erosion on, 3 cities, 1 mountain city and 5 towns. The settlement step places the cities on the natural ground (nothing is flattened, [ADR 0014](../decisions/0014-cities-on-natural-ground-and-service-buildings.md)), gives each the service buildings of its tier from `content/services/`, validates them (`SETL`), writes `settlements.json` and the terrain around each city `settlements_ground.bin` next to the tiles ([format](settlements.md)) and puts the player's spawn in the first city's central square. `--cities` and `--towns` change the counts; `--no-settlements` skips the step. Unless `--no-roads` is given, the roads between the settlements are then planned, graded into the terrain and painted ([ADR 0013](../decisions/0013-roads-and-bridges.md)), and `roads.json` and `map.png` (an overview image for the in-game map) are written next to the tiles. Exit codes: `0` ok, `1` errors, `2` bad arguments.

```
$ dotnet run --project src/Realm.Cli -c Release -- bake-terrain
  height field: 5121 x 5121 vertices at 4 m (3.1 s)
  erosion: deepest carve 63.1 m, 16 river(s), 2 lake(s) (9.9 s)
  settlements: 9 placed on the natural ground (2.1 s)
    settlement.city_1      Thornford      City  at (5686, 566), ground 115 m, wall radius 292 m, up to 1200 houses, 16 service building(s)
    settlement.city_2      Hollowgate     City  at (3006, -1090), ground 117 m, wall radius 300 m, up to 1200 houses, 16 service building(s)
    settlement.city_3      Langton        City  at (-4922, -442), ground 71 m, wall radius 308 m, up to 1200 houses, 16 service building(s)
    settlement.mountain_1  Briarhaven     City  at (2877, -4291), ground 469 m (mountain), wall radius 299 m, up to 1200 houses, 16 service building(s)
    settlement.town_1      Oakley         Town  at (-1962, -1578), ground 70 m, wall radius 196 m, up to 400 houses, 7 service building(s)
    ... (towns 2 to 5)
  roads: 17 road(s), 66.6 km, 13 bridge(s), terrain graded under them (1.6 s)
  baked 400 region tile(s) of 256 x 256 vertices (1024 m each) to 'baked/terrain' (1.3 s)
  height range -218.2 m to 913.6 m; spawn (5687, 576); checksum 39c69dbc42230ea7
0 error(s), 0 warning(s) (19.8 s total).
```

`--region-size` must divide the world and give an even number of regions per axis, at most 32 (Terrain3D's limit); otherwise `TERR018` is reported and nothing is written. Timings are from the development machine with the Release build (Debug is several times slower).

## `help`

Prints usage. This is also the default when no command is given.

```
$ dotnet run --project src/Realm.Cli -- help
Usage: realm <command>
Commands: version, content-lint [dir], macro-map [dir], height-field [dir] [--seed N] [--spacing M] [--blend M] [--erode] [--iterations N] [--strength S] [--threshold KM2] [--min-lake-area KM2] [--png file], help
```

## Planned

`generate`, `bake`, `validate`, `seed-sweep`, `docs-gen` (CLAUDE.md section 5). None exist yet.
