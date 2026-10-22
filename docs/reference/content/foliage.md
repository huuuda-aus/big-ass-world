# Content type: `foliage`

A species of plant the world generator scatters: a tree, a bush or grass ([ADR 0020](../../decisions/0020-foliage.md)).

- Directory: `content/foliage/`
- One record per file: `content/foliage/<name>.yaml`, where `<name>` is the ID without the `foliage.` prefix.
- C# type: `Realm.Data.Content.FoliageDefinition`; parsed by `FoliageParser`; used by `Realm.Gen.Foliage.FoliageGenerator`.

## Fields

| Field | Type | Required | Rules |
|---|---|---|---|
| `id` | string | yes | Content ID starting with `foliage.`. Unique across all content. |
| `name` | string | yes | Display name. |
| `kind` | string | yes | `tree`, `bush` or `grass`. Grass is baked as a density map and scattered around the player at run time; trees and bushes are baked one by one. |
| `tags` | list of strings | no | Same grammar as [material tags](material.md). The game picks the model from them: `conifer` gives a pine, `dead` a bare tree. |
| `per_hectare` | map biome ID to number | yes | Plants (tufts for grass) per hectare (10,000 m2) in each biome where it grows; a biome that is missing gets none. Each above 0 and at most 600 (trees, bushes) or 20,000 (grass). The biome must exist (`DATA013`). |
| `min_spacing_m` | number | no | Smallest distance between two plants of this species, at least 0.5. Default 2. Not used for grass. |
| `height_range_m` | `[low, high]` | no | Ground heights it grows on. Default `[0, 900]`. Nothing grows below 1.5 m (shore and sea). |
| `max_slope_deg` | number | no | Steepest ground, 0 to 89. Default 35. |
| `scale_range` | `[low, high]` | no | Size multiplier range, 0.1 to 5. Default `[0.8, 1.2]`. Most plants are drawn smaller than the middle of the range (the generator favours small sizes). The placeholder models are about 11 m (oak), 11.5 m (pine) and 8 m (dead tree) tall at scale 1, so the shipped ranges give trees from about 3 m to 20 m. |
| `cluster_m` | number | no | Size of the clumps it grows in; 0 (default) spreads it evenly. |
| `cluster_strength` | number | no | 0 to 1: how empty the gaps between clumps are. Default 0.6. |
| `trunk_radius_m` | number | no | Radius of the collision a walking player bumps into, 0 to 2. Default 0 (none). |

Unknown fields and bad values are errors (`DATA001`, `DATA019`).

## Complete example

`content/foliage/oak.yaml`:

```yaml
id: foliage.oak
name: Oak
kind: tree
tags: [deciduous]
per_hectare: { biome.temperate_forest: 50, biome.highlands: 8, biome.marsh: 4 }
min_spacing_m: 6
height_range_m: [0, 650]
max_slope_deg: 32
scale_range: [0.3, 1.85]
cluster_m: 220
cluster_strength: 0.8
trunk_radius_m: 0.45
```

A bad value is reported like this:

```
Error DATA019 foliage/oak.yaml: Density for 'biome.marsh' must be above 0 and at most 600 per hectare, not 100000. (fix: Leave the biome out to grow nothing there.)
```

## Shipped species

| ID | Kind | Grows in (per hectare) |
|---|---|---|
| `foliage.oak` | tree | temperate forest 50, highlands 8, marsh 4 |
| `foliage.pine` | tree | highlands 35, temperate forest 18, alpine peaks 9 |
| `foliage.dead_tree` | tree | marsh 9, badlands 2, dry steppe 0.8 |
| `foliage.shrub` | bush | temperate forest 35, dry steppe 30, marsh 20, highlands 12, badlands 9 |
| `foliage.meadow_grass` | grass | marsh 3000, temperate forest 2400, dry steppe 1500, highlands 1000, badlands 250 |

All values are placeholders, not design.
