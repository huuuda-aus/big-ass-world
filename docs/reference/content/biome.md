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

### Shape properties (all optional)

Without these a biome is "base + relief x smooth noise". They give it character. Absent means 0 (off). Not used by ocean biomes.

| Property | Rules | Meaning |
|---|---|---|
| `ridge` | 0 to 1 | Share of the noise that is *ridged*: narrow crests, steep faces and valleys instead of rounded hills. 0.7 to 0.9 for mountain ranges. |
| `terrace` | 0 to 1 | How strongly heights snap to flat steps with short cliff risers (mesas, badlands). The step height is the generator setting `TerraceStepMeters` (30 m). |
| `hill_density`, `mountain_density`, `basin_density` | 0 to 200 | Expected number of that **landmark** per 100 square kilometres of the biome's region. 0 or absent: none. |
| `hill_height_m`, `mountain_height_m`, `basin_height_m` | above 0; required when the density is above 0 | Height of a hill or mountain, depth of a basin, in metres. Each landmark varies by 30 percent either way. |
| `hill_radius_m`, `mountain_radius_m`, `basin_radius_m` | at least 50; required when the density is above 0 | Radius of the footprint in metres (about 30 percent more at the widest). |

Landmarks: a **hill** is a smooth isolated bump; a **mountain** is a ridged, rocky peak that stands alone; a **basin** is a depression that fills with water where it dips below its surroundings, giving ponds and lagoons. They are scattered one per 1 km cell at most per kind, only where the whole footprint lies inside the biome's region (so they never create a seam at a border).

A biome tagged `ocean` is a water biome: it needs `water_depth_m`, and a `base_height_m` on it is ignored with a warning (`TERR008`). On land biomes `water_depth_m` is ignored with a warning. Missing or out-of-range properties are errors when terrain is generated (`TERR006`, `TERR007`), not at load time.

## Erosion and river properties

Read by the erosion step (not by the loader). Required on land biomes; ocean biomes ignore them (the sea floor is neither carved nor relaxed and gets no rain), and a value on an ocean biome gives warning `TERR008`.

| Property | Rules | Meaning |
|---|---|---|
| `rainfall` | required, 0 to 1 | How much water each area adds to the flow. Drives how many rivers there are, and how cloudy the sky is over the biome ([ADR 0022](../../decisions/0022-clouds.md)). |
| `erodibility` | required, 0 to 1 | How strongly flowing water carves the ground; 0 is bedrock. |
| `max_slope_deg` | required, above 0 and at most 90 | Steepest slope in degrees that stays stable; steeper slopes slump. Low values give soft, rounded hills. |
| `river_loss` | required, 0 to 1 | Fraction of a river's flow lost per kilometre while it crosses this biome. 0 loses nothing; high values make rivers thin out (and, with `ErosionSettings.RiversReachWater` off, dry up). |

| `gorge_depth_m` | optional, 0 to 200 | How deep the biome's big rivers cut gorges below the ground beside them, with steep cliff walls (slope 2, about 63 degrees). The depth grows with river flow and fades to nothing near sea level. 0 or absent: no gorge. |

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
