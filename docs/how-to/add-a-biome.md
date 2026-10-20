# How to add a biome

Goal: add a biome that regions can use, and confirm it loads.

## Prerequisites
- The solution builds ([getting started](../tutorials/getting-started.md)).
- You know the [biome schema](../reference/content/biome.md), including the terrain properties the height-field step reads.

## Steps

1. Choose an ID, for example `biome.salt_marsh`.
2. Create `content/biomes/salt_marsh.yaml`. The file name is the ID without the `biome.` prefix.
   ```yaml
   id: biome.salt_marsh
   name: Salt marsh
   tags: [land, wetland]
   properties:
     base_height_m: 2
     relief_m: 4
     roughness: 0.2
     rainfall: 0.7
     erodibility: 0.2
     max_slope_deg: 25
     river_loss: 0
   ```
   The first three set the terrain height; the last four drive erosion and rivers (all required on land biomes). Use the tag `ocean` for water biomes: they take `water_depth_m` instead of `base_height_m` and need no erosion properties. The values here are examples, not recommendations.
3. Lint:
   ```bash
   dotnet run --project src/Realm.Cli -- content-lint
   ```
4. You should see the biome count go up by one and `0 error(s), 0 warning(s)`.
5. To use it, point a region at it: [add a region](add-a-region.md). Terrain properties are checked when terrain is generated: [tune the terrain](tune-the-terrain.md) and [tune erosion and rivers](tune-erosion-and-rivers.md).

## Troubleshooting
- `Unknown field 'x'` (DATA001): only `id`, `name`, `tags`, `properties` exist.
- `DATA008` warning: the file name does not match the ID.
- `TERR006` to `TERR008` (when generating or eroding terrain): a terrain or erosion property is missing, out of range or does not apply to this kind of biome.
- Full list: [validators](../reference/validators.md).

## Related
- [Add a region](add-a-region.md), [Tune the terrain](tune-the-terrain.md), [Tune erosion and rivers](tune-erosion-and-rivers.md), [Add a material](add-a-material.md)
