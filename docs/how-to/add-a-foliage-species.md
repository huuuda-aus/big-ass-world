# Add a foliage species

Goal: make a new kind of tree, bush or grass grow somewhere, and see it in the game.

## Prerequisites
- The repository builds ([getting started](../tutorials/getting-started.md)) and the game runs ([run the game](run-the-game.md)).
- You know what a [foliage record](../reference/content/foliage.md) holds.

## Steps
1. Create `content/foliage/birch.yaml`:
   ```yaml
   id: foliage.birch
   name: Birch
   kind: tree
   tags: [deciduous]
   per_hectare: { biome.temperate_forest: 25, biome.highlands: 10 }
   min_spacing_m: 4
   max_slope_deg: 30
   cluster_m: 150
   cluster_strength: 0.7
   trunk_radius_m: 0.3
   ```
   The file name must match the ID.
2. Check the record:
   ```bash
   dotnet run --project src/Realm.Cli -- content-lint
   ```
   You should see `6 foliage species` and `0 error(s)`.
3. Bake the world:
   ```bash
   tools/rebuild-world.sh
   ```
   The bake prints `foliage: N tree(s) and bush(es), M grass cell(s)`; N should be higher than before. `FOL003` warns if your species placed nothing.
4. In the game, fly to the temperate forest (for example `--pos=-4500,500`) and look for the new trees.

## The model
Until hand-made art exists, a species is drawn with a placeholder model chosen by its kind and tags (`FoliageMeshes.StyleFor`): `conifer` gives a pine, `dead` a bare tree, any other tree an oak, any bush a shrub, any grass a tuft. A new species therefore looks like one of those five. Hand-made models are a later step.

## Troubleshooting
- **`DATA013`**: a biome ID in `per_hectare` does not exist. Fix the ID.
- **`FOL003` warning**: no plant was placed. Raise `per_hectare`, widen `height_range_m` or `max_slope_deg`, or check that the biome has land in the right height range.
- **Nothing changes in the game**: the baked `foliage.bin` is old; run the bake again. The game flag `--no-foliage` switches the layer off.
- **Trees on a road or in water**: this is a bug; `FOL001` should have caught it. Save the seed and report it.

## Related
- [ADR 0020](../decisions/0020-foliage.md), [foliage system doc](../systems/foliage.md), [validator codes](../reference/validators.md).
