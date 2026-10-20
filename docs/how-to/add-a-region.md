# How to add or change a region in the macro map

Goal: carve a new region out of the world and prove that every part of the world still belongs to exactly one region.

The worked example cuts a "Mirror Marsh" corner out of Redrock Badlands in the shipped sample map. It reuses the existing `biome.marsh`.

## Prerequisites
- The solution builds ([getting started](../tutorials/getting-started.md)).
- You know the [world schema](../reference/content/world.md): metres, origin at the world centre, X east, Z south.
- The biome you want exists ([add a biome](add-a-biome.md)). The example uses `biome.marsh`.

## Why you cannot just add a polygon

Regions must tile the whole world: no gaps, no overlaps, and polygons have no holes. A new region placed on top of existing land overlaps it. You must also **shrink the old region** so the new area is no longer inside it.

## Steps

1. Open `content/world/main.yaml`.
2. Change Redrock Badlands' polygon so it no longer includes the south-east corner (x from 5000 to 8000, z from 5500 to 8000). The new outline walks around the notch:
   ```yaml
   - id: region.redrock_badlands
     name: Redrock Badlands
     biome: biome.badlands
     polygons:
       - [[3000, 3500], [8000, 2500], [8000, 5500], [5000, 5500], [5000, 8000], [2000, 8000]]
   ```
3. Add the new region, using the same vertices along the shared edges:
   ```yaml
   - id: region.mirror_marsh
     name: Mirror Marsh
     biome: biome.marsh
     polygons:
       - [[5000, 5500], [8000, 5500], [8000, 8000], [5000, 8000]]
   ```
4. Check coverage:
   ```bash
   dotnet run --project src/Realm.Cli -- macro-map
   ```
5. You should see the new region in the table and no errors:
   ```
     region.mirror_marsh          biome.marsh                    1833       7.5    1.8%
     region.redrock_badlands      biome.badlands                 4819      19.7    4.7%
   0 error(s), 0 warning(s).
   ```
   Mirror Marsh (1,833) plus the smaller Redrock Badlands (4,819) equal the original Badlands (6,652 cells), and no `(unassigned)` row appears.
6. Run `dotnet test Realm.sln`. Editing the shipped sample map changes its goldens, so `MacroRasterizerTests`, `ShippedHeightFieldTests` and `ShippedErosionTests` have golden checksum tests that fail on purpose: see the last troubleshooting item. The coverage and range oracles do not depend on the layout and keep passing.

## Troubleshooting
- `TERR003` overlap: you added the new polygon but did not shrink the old one. If you add the square without step 2, you get `Regions 'region.mirror_marsh' and 'region.redrock_badlands' overlap on 1833 cell(s)`.
- `TERR002` gap: you shrank a region and nothing covers the freed area. The message gives example coordinates.
- `TERR004` warning: a region is thinner than a 64 m cell and owns no cells.
- `DATA016`: the polygon is invalid; the message says which ring and why (for example edges that cross).
- `DATA017`: a vertex is outside the world. For `world_size_m: 20480` the range is -10240 to 10240 on both axes.
- Golden checksum tests (`ShippedWorld_Checksum_IsStableGolden`, `ShippedWorld_Erosion_Checksum_IsStableGolden`) fail after you edit the shipped sample: expected. The two analytic expectations for the sample (39,900 sea cells and 62,500 land cells in `MacroRasterizerTests`) also describe the 16 km land square. Update them and the CHANGELOG, or test your layout in a copy of the content directory (`realm macro-map <dir>`).

## Related
- [World schema](../reference/content/world.md), [validators](../reference/validators.md), [add a biome](add-a-biome.md)
