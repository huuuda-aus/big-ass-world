# Changelog

All notable changes are recorded here. Format: [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

### Added
- Solution skeleton: `Realm.Core`, `Realm.Data`, `Realm.Sim`, `Realm.Gen`, `Realm.Scripting`, `Realm.Cli` and a test project for each, plus `Realm.Architecture.Tests`.
- `Realm.Core.Random.Rng`: seedable PCG32 generator with hierarchical `ForKey` seeding.
- Architecture test enforcing the project dependency rules and the no-Godot rule (CLAUDE.md section 5).
- `realm version` and `realm help` CLI commands.
- Repository config: `.gitignore`, `.gitattributes` (Git LFS rules), `.editorconfig`, `Directory.Build.props`.
- Documentation skeleton, ADR 0001 and 0002.
- `Realm.Core`: `ContentId`, `TagSet`, `ValidationIssue`/`ValidationReport`/`Severity`, logging facade (`Log`, `Logger`, `ILogSink`, `NullLogSink`, `TextWriterLogSink`).
- `Realm.Data`: `material` content type (`MaterialDefinition`, `MaterialParser`), `ContentLoader`, `ContentDatabase`, validator codes `DATA001`-`DATA012`, YamlDotNet 18.1.0.
- `realm content-lint [dir]` CLI command (exit code 1 on errors).
- Sample content: `content/materials/` (`iron_ore`, `copper_ore`, `oak_wood`; placeholder values).
- Docs: material schema, validator codes, "add a material" how-to, Data system doc, ADR 0003.
- `item` content type (placeholder schema: `id`, `name`, `tags`, optional `material` reference), `ItemParser`, `ItemDefinition`; sample items in `content/items/`.
- Cross-record reference checks: `ContentValidator.ValidateReferences`, codes `DATA013` (dangling reference) and `DATA014` (wrong reference type).
- `ContentDatabase.Items`, `Contains`, `TryGetSource`.
- Docs: item schema, "add an item" and "add a content type" how-tos.
- `Realm.Sim`: `TimeSettings`, `GameTime`, `SimClock` (integer tick to game-minute mapping; default pacing 20 ticks per game minute), `EntityId`, `SimWorld` (entities and struct components, ordered queries), `ISystem`, `Simulation` (ordered tick loop).
- Docs: Sim system doc, determinism explanation, "add a simulation system" how-to.
- `SimWorld.EntitiesWith<T>()` and `ComponentsOf<T>()`: allocation-free span iteration over components ([ADR 0004](docs/decisions/0004-dense-component-storage.md)).
- `tools/Realm.Bench`: simulation benchmark; `--check` fails when the span scenario exceeds the 4 ms tick budget or allocates.
- Docs: ADR 0004, "profile the simulation" how-to.
- Docs: ADR 0005 and the world-scale-and-streaming explanation.
- `Realm.Core.Geometry`: `Point2D` and `Polygon` (validity checks, area, bounds, half-open point-in-polygon).
- `biome` and `world` (macro map) content types with `region` records inside maps: `BiomeParser`, `MacroMapParser`, `BiomeDefinition`, `MacroMapDefinition`, `RegionDefinition`; `ContentDatabase.Biomes`, `MacroMaps`, `Regions`; validator codes `DATA015`-`DATA017`; region biome reference check (`DATA013`).
- `Realm.Gen.Terrain`: `MacroRasterizer` (generator `terrain.macro_raster` version 1), `RegionField`, validator codes `TERR001`-`TERR005`.
- `realm macro-map [dir]` CLI command; `content-lint` summary now counts biomes and regions.
- Sample content: `content/biomes/` (three biomes), `content/world/main.yaml` (20,480 m world, three regions; placeholder layout and names).
- Docs: ADR 0006, biome and world references, validator codes, Gen system doc, "add a biome" and "add or change a region" how-tos.
- `Realm.Gen.Terrain` height field: `HeightFieldGenerator` (generator `terrain.height_field` version 1), `HeightField`, `HeightFieldSettings`, `BiomeTerrainParameters`, `GradientNoise`, `HeightFieldValidator`; validator codes `TERR006`-`TERR014`.
- Biome terrain vocabulary: `base_height_m`, `relief_m`, `roughness`, `water_depth_m` (approved by the developer).
- `HeightPreview` and `PngEncoder` (dependency-free PNG writer); `realm height-field [dir] [--seed N] [--spacing M] [--blend M] [--png file]`.
- Docs: ADR 0007, "preview and tune the terrain" how-to.
- `Realm.Gen.Terrain.Erosion` (generator `terrain.erosion` version 1): `ErosionGenerator`, `ErosionSettings`, `ErodedTerrain`, `RiverNetwork` (`River`, `Lake`, `RiverPoint`, `RiverMouth`), `BiomeErosionParameters`, `RiverValidator`; internal `FlowRouter` (priority-flood), `FlowAccumulator`, `StreamPowerEroder`, `ThermalEroder`, `RiverExtractor`; validator codes `TERR015`-`TERR017`.
- Biome erosion vocabulary: `rainfall`, `erodibility`, `max_slope_deg`, `river_loss` (approved by the developer); river and lake rendering in `HeightPreview`, region borders in the preview.
- CLI: `height-field --erode --iterations --strength --threshold --min-lake-area`.
- Sample content: four more biomes (`alpine_peaks`, `dry_steppe`, `marsh`, `badlands`) and a six-region land layout inside the ocean ring (placeholders).
- Docs: ADR 0008, "tune erosion, rivers and lakes" how-to.

- `Realm.Game` (Godot 4.5.1 .NET, Terrain3D 1.0.2 vendored): `TerrainBridge`, `RegionStreamer`, `BakedTerrainSource`, `WorldScene` (walk and fly over the baked world, HUD, sky, fog, sea plane, auto-walk report, screenshots) and the Terrain3D streaming spike.
- `Realm.Gen.Bake`: `RegionTile`, `RegionTileFile` (checksummed binary), `TerrainBaker` (tiles, painted rivers and lakes, baked hill shading), `TerrainManifest`; `TerrainColouring` shared with the preview; validator code `TERR018`.
- `Realm.Core.Geometry.RegionGrid` and `Realm.Core.Diagnostics.FrameStats` (percentiles and hitch counting for the soak test).
- CLI: `bake-terrain`.
- Docs: ADRs 0009 and 0010, baked terrain format reference, Game system doc, "set up Godot", "run the game" and "run the streaming spike" how-tos; versions pinned for Godot, GodotSharp and Terrain3D.

- Physics walker: `CharacterBody3D` with Terrain3D dynamic collision (shape 8, radius 16), 50 degree slope limit, fall-through failsafe; `--collision-shape`, `--collision-radius`.
- Surface textures (`Realm.Gen.Textures`, 64 px grass and rock) applied via Terrain3D auto-shader; low-resolution 3D SubViewport (render scale 0.75) with a full-resolution HUD; walking view distance 300 m.

- Scale reference: `Realm.Gen.Props` low-poly models (one-storey cottage 4.2 m, two-storey house 7.4 m, pine/oak/poplar trees 4 to 10 m) with 15 tests; the game lays a measure tape from the spawn to the world edge with a house and three trees every 100 m (`StripLayout`, 5 tests; segments stream in and out; `T` toggles, `--no-tape`).

### Fixed
- Walking or flying to the edge of the world: the streamer asked for regions that were never baked and failed with a missing-tile error (it hung a start near the edge). It now only wants regions inside the baked world (`StreamingOptions.MinRegion`/`MaxRegion`).
- The world edge showed a stretched "curtain" of terrain; Terrain3D's world background is now off.

- Terrain shapes (`terrain.height_field` v2): ridged noise and domain warping, terracing, and scattered landmarks (hills, mountains, basins) from new optional biome properties (`ridge`, `terrace`, `hill_*`, `mountain_*`, `basin_*`); `LandmarkPlacer`, `LandmarkShaper`, `GradientNoise.Ridged`. See ADR 0011.
- Gorges (`terrain.erosion` v2): `GorgeCarver` cuts cliff-walled gorges along big rivers, from the optional biome property `gorge_depth_m`.
- Steep ground is baked and previewed as bare rock (`TerrainColouring.Albedo` slope parameter).

- Cities (ADR 0012): the CityGen generator imported as `Realm.Gen.Settlements` (plan, wards, wall, streets, lots, buildings, interiors; `Rng`-based, documented, 46 ported tests); placement of 3 cities and 5 towns on flat land with terrain flattening (`SettlementPlacer`, `SettlementTerrain`, `SettlementValidator`, codes `SETL001` to `SETL006`, generator `settlement.placement` v1); baked `settlements.json`; `bake-terrain --cities/--towns/--no-settlements`; the player spawns in the first city.
- Godot-free 3D meshing of cities (`BuildingMesher`, `CityMesher`, `LandmarkMesher`) ported from the CityGen web viewer; in the game `SettlementStreamer`, `CityInstance`, `CityBuild`: cities stream in around the player, detailed enterable houses within 35 m (doors, windows, rooms, stairs, collision, lamps), wall with towers and gates, fountain/pyre/block/market centrepieces. `--no-cities` switches them off.
- Sun shadows in the game (150 m).

- Names and roads (ADR 0013): generated settlement names (`SettlementNamer`; `settlement.placement` v2); `road.planner` v1: a spanning tree plus extra links, A* routes with slope and river costs, graded surface (12 percent), terrain cut and fill under the road, bridges at river crossings (`BridgeMesher`), road colour in the tiles, `roads.json`, validators `ROAD001` to `ROAD006`; `bake-terrain --no-roads`.
- World map screen (M) from the baked `map.png` with roads, names and the player; HUD shows the nearest settlement; cities load from about 2 km when flying.

### Removed
- The scale-reference measure tape, houses and trees were removed from the game (`ReferenceStrip`, `MeasureTape`, `PropMeshConverter`). The Godot-free models (`Realm.Gen.Props`: cottage, town house, three tree kinds, `StripLayout`) and their tests stay for later use.

### Changed
- Generator versions bumped: `terrain.height_field` 1 -> 2, `terrain.erosion` 1 -> 2; golden checksums regenerated. Default blend radius 384 -> 512 m (the ridged mountains failed the seam check at 384 m). Shipped biome values (placeholders) now include landmarks, ridges, terracing and gorges, and the baked world was regenerated.
- The player cannot go within 10 m of the world edge (invisible wall); the sea plane is exactly the size of the world instead of following the player.
- Streaming commit budget in CLAUDE.md section 4.9 is now frame-based (was a fixed 1 ms): one Terrain3D shared-texture rebuild costs about 7 ms with 9 to 12 regions of 256 x 256 vertices. Load set is by view distance, below half a region width.
- Fixed inverted hill-shading direction in `HeightPreview` (light from the north-west): ridges in earlier preview images were lit from the wrong side.
- Architecture test now covers `Realm.Game`; `RiverNetwork` records its working-grid spacing; `TerrainManifest` has value equality.
- Sample world layout replaced: the land is now a 16 km square of six regions (was a 12 km square of two). Shipped-world tests derive their expectations from the content (polygon areas, biome ranges, interior vertices) instead of the hard-coded layout; goldens recaptured.
- `ParameterGrid.PassRadiusCells` shared by height and erosion blending; `BiomePropertyReader` shared by the biome parameter readers. Behaviour unchanged (all earlier tests and goldens passed unmodified).
- `LakeMinDepthMeters` must be positive (0 would make every vertex a lake).
- Sample biomes (`content/biomes/`) now carry placeholder terrain properties.
- `content-lint` and the CLI help list the new commands.
- Null-forgiving operators in `Realm.Data` now carry a comment saying why they are safe (CLAUDE.md section 8.2).
- Decision D2: world size is now about 20 x 20 km (was about 10 x 10 km). CLAUDE.md gains section 4.9 (seamless traversal), open decision D10 (minimum storage), and the M1 gate (Terrain3D spike plus traversal soak test). See ADR 0005.
- Sim component storage is now dense sorted arrays instead of a `SortedDictionary`, and the living-entity list is a sorted array (was `SortedSet`). `SimWorld.Entities` is now `IReadOnlyList<EntityId>`. Behaviour of `Query`, `Set`, `TryGet`, `Has`, `Remove` is unchanged (all existing tests pass unmodified).
- `TimeSettings.Default` (20 ticks per game minute) confirmed by the developer; no longer a placeholder. No behaviour change.
- `ContentLoader` is generic over a per-type spec; parsing helpers shared in `ParserSupport`. `content-lint` summary now counts items.
- CLI exit codes: `1` now means the command ran and found errors. `help` lists `content-lint`.
