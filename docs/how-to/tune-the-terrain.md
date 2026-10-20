# How to preview and tune the terrain

Goal: generate the world's height field, look at it, change a biome's terrain values and check that nothing breaks.

The shipped biome values and tuning settings are placeholders. This is how you replace them with your own.

## Prerequisites
- The solution builds ([getting started](../tutorials/getting-started.md)).
- The world map covers the whole world ([add a region](add-a-region.md)).
- You know the [terrain properties of a biome](../reference/content/biome.md).

## Steps

1. Generate a preview. A coarse spacing is fast; use 32 m while iterating:
   ```bash
   dotnet run --project src/Realm.Cli -- height-field --spacing 32 --png terrain.png
   ```
   You should see a line such as `world.main: 641 x 641 vertices at 32 m, seed 1 ... checksum ...`, a height range of -218.2 m to 651.9 m, a per-region table of seven regions and `0 error(s), 0 warning(s).` (about 0.3 s).
2. Open `terrain.png`. North is at the top, deep water is dark blue, lowland green, hills brown, rock grey and peaks white; the shading is lit from the north-west, and pale lines mark region borders. (Add `--erode` to also get erosion, rivers and lakes: [tune erosion and rivers](tune-erosion-and-rivers.md).)
3. Edit a biome in `content/biomes/`. To make the highlands much taller, change `relief_m: 350` to `relief_m: 900` in `highlands.yaml`.
4. Run step 1 again. With the shipped settings you will probably see a seam error, because the mountains now meet the sea too steeply:
   ```
   Error TERR014 height field: 56 step(s) across region borders are steeper than 2 (rise over run); the worst is 2.38, for example (5696, -8032). (fix: Increase the blend radius, or reduce the height difference between neighbouring biomes.)
   ```
5. Fix it in one of two ways, then run again:
   - Lower the height difference between neighbouring biomes (smaller `relief_m` or a shallower sea).
   - Widen the blend: `dotnet run --project src/Realm.Cli -- height-field --spacing 32 --blend 640`. With 640 m the example above passes (that example was measured with the old 384 m default; the default is now 512 m).
6. Try other seeds: `--seed 2`. Different seeds give different mountain shapes; the biome values stay the same.
7. When you like the result, run `dotnet test Realm.sln`. The shipped-world golden tests describe the sample values and will fail after you change them on purpose; update them (see troubleshooting).

## Making mountains, lone hills and lagoons

The shape properties in the [biome reference](../reference/content/biome.md#shape-properties-all-optional) change what a region looks like, not just how high it is.

1. Rocky ridges: add `ridge: 0.8` to a mountainous biome. Re-run step 1 and open the PNG: smooth hills turn into crests and valleys.
2. A mountain standing alone in a forest: add to `temperate_forest.yaml`
   ```yaml
   mountain_density: 6        # per 100 km2 of forest
   mountain_height_m: 480
   mountain_radius_m: 1000
   ```
   Re-run; the preview shows a rocky peak or two inside the forest. Raise the density for more; lonely hills use `hill_*`.
3. Ponds and lagoons: add `basin_density`, `basin_height_m` (depth) and `basin_radius_m`. They show up as lakes when you add `--erode`, because erosion fills any basin that is large and deep enough ([lakes](tune-erosion-and-rivers.md)).
4. Mesas and stepped cliffs: `terrace: 0.8` on a dry biome.
5. Cliff-lined rivers: add `gorge_depth_m: 40` to a rocky biome and use `--erode`.

If the seam error reappears (TERR014), lower the `ridge` of the biome next to the sea or reduce `relief_m`; the default blend is now 512 m.

## Verify
You should see `0 error(s), 0 warning(s)` and a preview image that matches what you intended: for example taller, whiter highlands.

## Troubleshooting
- `TERR006` missing property: a name is misspelled (`relif_m`) or absent. The message names the property to add, for example `Biome 'biome.highlands' is missing required property 'relief_m'`. The loader accepts any property name, so only the terrain step notices.
- `TERR007` out of range: for example `roughness: 1.5`. Roughness is 0 to 1; relief cannot be negative; water depth must be positive.
- `TERR008` warning: you gave `water_depth_m` to a land biome or `base_height_m` to an ocean biome; it is ignored.
- `TERR013`: a height beyond 4000 m (default). Lower the biome numbers.
- `TERR014`: see step 5. The check is on the slope between neighbouring vertices across region borders, so it also depends on `--spacing`: always check at the spacing you intend to ship.
- `TERR010`: `--spacing` must divide the world size (20480): use 8, 16, 32, 64, and so on.
- `ShippedWorld_Checksum_IsStableGolden` (in `ShippedHeightFieldTests`) fails after you change `content/biomes/`: expected. The same applies to the interior-range test, which encodes the sample values. Update them, or leave the samples and tune in a copy of the content directory (`height-field <dir>`).
- Generation at 8 m spacing takes a few seconds and about 26 MB. Spacing 2 m would take roughly 16 times longer and about 420 MB; it is not the default.

## Related
- [Add a biome](add-a-biome.md), [Add a region](add-a-region.md), [CLI reference](../reference/cli.md), [ADR 0007](../decisions/0007-height-field-generation.md)
