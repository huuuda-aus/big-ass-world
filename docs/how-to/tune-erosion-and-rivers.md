# How to tune erosion, rivers and lakes

Goal: change how many rivers and lakes the world has and how deep valleys are cut, using the preview, and know what each knob does.

The shipped biome values and the settings below are placeholders. This is how you replace them with your own.

## Prerequisites
- The solution builds ([getting started](../tutorials/getting-started.md)) and the world map covers the whole world ([add a region](add-a-region.md)).
- You know the [terrain and erosion properties of a biome](../reference/content/biome.md) and have read [preview and tune the terrain](tune-the-terrain.md).

## What controls what

| You want | Change |
|---|---|
| More or fewer rivers in a region | The biome's `rainfall` (more rain gives more flow), or globally `--threshold` (flow in km2 of catchment needed to count as a river) |
| Rivers that fade out in a dry region | The biome's `river_loss` (fraction of flow lost per kilometre; 0 loses nothing) |
| Deeper or shallower valleys | The biome's `erodibility` (0 is bedrock), or globally `--strength` and `--iterations` |
| Softer, rounder hills | A lower `max_slope_deg` in the biome |
| More or fewer lakes | `--min-lake-area` (smallest lake kept, in km2; smaller basins are filled in) |

## Steps

1. Generate the eroded terrain and preview, with region borders and rivers drawn:
   ```bash
   dotnet run --project src/Realm.Cli -- height-field --spacing 32 --erode --png terrain.png
   ```
   You should see an `erosion (...)` line, `rivers: 18 (74.8 km total); lakes: 1`, a list of the longest rivers and `0 error(s), 0 warning(s).` (about 7 s).
2. Open `terrain.png`. Blue lines are rivers (thicker where more water flows), blue patches are lakes, pale lines are region borders.
3. Change the number of rivers with the threshold:
   ```bash
   dotnet run --project src/Realm.Cli -- height-field --spacing 32 --erode --threshold 1   # rivers: 35 (107.2 km total)
   dotnet run --project src/Realm.Cli -- height-field --spacing 32 --erode --threshold 6   # rivers: 7 (36.6 km total)
   ```
4. Change the lakes:
   ```bash
   dotnet run --project src/Realm.Cli -- height-field --spacing 32 --erode --min-lake-area 0.5   # lakes: 14
   dotnet run --project src/Realm.Cli -- height-field --spacing 32 --erode --min-lake-area 15    # lakes: 0
   ```
   Basins smaller than the limit are not removed; they are filled in as sediment, so rivers cross them.
5. Change valley depth. The default strength is 2 (deepest carve 69 m):
   ```bash
   dotnet run --project src/Realm.Cli -- height-field --spacing 32 --erode --strength 10
   ```
   You should see `deepest carve 275.2 m`, and the lake is gone, drained by the deeper valleys. Very high strength (hundreds) flattens the whole landscape, because there is no uplift to balance it.
6. Edit a biome's `rainfall`, `erodibility`, `max_slope_deg` or `river_loss` in `content/biomes/`, run step 1 again, and compare. Rivers in dry biomes (`river_loss` above 0) thin out as they cross them.
7. `--iterations 0` skips hydraulic erosion (deepest carve 0 m, basins still filled), which shows the terrain before erosion.

## Verify
`0 error(s), 0 warning(s)` and a preview that matches what you intended: for example more rivers in the wet marsh than in the dry badlands.

## Troubleshooting
- `TERR006`: a land biome lacks one of the four erosion properties. The message names it, for example `Biome 'biome.badlands' is missing required property 'river_loss'`.
- `TERR007`: a value is out of range, for example `property 'erodibility' is 1.7 but must be between 0 and 1`. Rainfall, erodibility and river loss are 0 to 1; `max_slope_deg` is above 0 and at most 90.
- `TERR008` warning: an ocean biome has erosion properties; they are ignored.
- `TERR010`/`TERR015`: invalid settings, for example a working spacing that does not divide the world.
- `TERR014` after erosion: the seam check also runs on the eroded terrain; see [tune the terrain](tune-the-terrain.md).
- Shipped-world tests (`ShippedErosionTests`, `ShippedHeightFieldTests`) have goldens for the sample content; they fail on purpose after you change it. Update the goldens, or tune in a copy of the content directory (`height-field <dir> ...`).
- Erosion at 8 m spacing takes about 11 s; the preview at 32 m is much faster.

## Related
- [Preview and tune the terrain](tune-the-terrain.md), [Add a biome](add-a-biome.md), [ADR 0008](../decisions/0008-erosion-and-rivers.md), [CLI reference](../reference/cli.md)
