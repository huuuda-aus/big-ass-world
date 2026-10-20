# Content type: `biome`

A biome describes the character of an area for the terrain generator: identity, tags and numeric properties. The loader accepts any property names; the terrain steps read the eight below (vocabulary approved by the developer: height properties in [ADR 0007](../../decisions/0007-height-field-generation.md), erosion properties in [ADR 0008](../../decisions/0008-erosion-and-rivers.md)).

- Directory: `content/biomes/`
- One record per file: `content/biomes/<name>.yaml`, where `<name>` is the ID without the `biome.` prefix.
- C# type: `Realm.Data.Content.BiomeDefinition`

## Fields

| Field | Type | Required | Rules |
|---|---|---|---|
| `id` | string | yes | Content ID starting with `biome.`. Unique across all content. |
| `name` | string | yes | Display name. Not empty. |
| `tags` | list of strings | no | Same grammar as [material tags](material.md). The tag `ocean` marks water biomes. |
| `properties` | map of string to number | no | Keys follow the tag grammar; values must be finite numbers. |

Unknown fields are errors.

## Terrain properties

Read by the height-field generator (not by the loader). Sea level is 0 m. Heights are in metres.

| Property | Applies to | Rules | Meaning |
|---|---|---|---|
| `base_height_m` | land biomes | required, any number | Typical height above sea level. |
| `relief_m` | all biomes | required, not negative | How far terrain rises above and falls below the base (half the peak-to-peak range). 0 gives perfectly flat terrain. |
| `roughness` | all biomes | required, 0 to 1 | 0 is smooth and rolling; 1 keeps fine detail strong (jagged). |
| `water_depth_m` | `ocean` biomes | required, positive | Depth below sea level; the base height is minus this. |

A biome tagged `ocean` is a water biome: it needs `water_depth_m`, and a `base_height_m` on it is ignored with a warning (`TERR008`). On land biomes `water_depth_m` is ignored with a warning. Missing or out-of-range properties are errors when terrain is generated (`TERR006`, `TERR007`), not at load time.

## Erosion and river properties

Read by the erosion step (not by the loader). Required on land biomes; ocean biomes ignore them (the sea floor is neither carved nor relaxed and gets no rain), and a value on an ocean biome gives warning `TERR008`.

| Property | Rules | Meaning |
|---|---|---|
| `rainfall` | required, 0 to 1 | How much water each area adds to the flow. Drives how many rivers there are. |
| `erodibility` | required, 0 to 1 | How strongly flowing water carves the ground; 0 is bedrock. |
| `max_slope_deg` | required, above 0 and at most 90 | Steepest slope in degrees that stays stable; steeper slopes slump. Low values give soft, rounded hills. |
| `river_loss` | required, 0 to 1 | Fraction of a river's flow lost per kilometre while it crosses this biome. 0 loses nothing; high values make rivers fade out. |

Rivers are not owned by a biome: they emerge from flow and cross region borders. The biome only supplies rainfall, erodibility and loss, blended smoothly across borders like the height properties.

## Complete example

`content/biomes/highlands.yaml`:

```yaml
id: biome.highlands
name: Highlands
tags: [land, mountainous]
properties:
  base_height_m: 400
  relief_m: 350
  roughness: 0.7
  rainfall: 0.8
  erodibility: 0.6
  max_slope_deg: 50
  river_loss: 0
```

An ocean biome (no erosion properties needed):

```yaml
id: biome.deep_ocean
name: Deep ocean
tags: [ocean]
properties:
  water_depth_m: 200
  relief_m: 30
  roughness: 0.3
```

The seven shipped biomes (`deep_ocean`, `temperate_forest`, `highlands`, `alpine_peaks`, `dry_steppe`, `marsh`, `badlands`) and their values are **placeholders**, not design. For example the dry steppe and badlands have low rainfall and high river loss, so rivers thin out there.

## Validation

Codes `DATA001` to `DATA014` apply at load, and `TERR006` to `TERR009` when terrain is generated or eroded: see [validators](../validators.md). Regions that name a biome that does not exist get `DATA013`.
