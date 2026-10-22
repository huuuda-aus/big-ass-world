# Changelog

All notable changes are recorded here. Format: [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

### Added
- Bridge ramps and wood: both bridge types get a gentle 4 m ramp (5 percent) at each end that carries the road up the 20 cm lift, in the bridge's own material. The timber bridge is split into timber and stone parts (`BridgeMesher.BuildParts`) and the timber is drawn with a generated wood-grain texture (`SurfaceTextureGenerator.Wood`, projected from three axes so the planks run along the deck).
- Bridge checks: `ROAD007` (a bridge deck steeper than `RoadSettings.MaxBridgeGrade`, default 0.2). Road bridges record the lowest ground under each abutment (`RoadBridge.StartFootingHeight`, `EndFootingHeight`, in `roads.json`) and both the timber and the arched bridges build their abutments down to 1 m below it, so a bank that falls away no longer leaves one hanging.
- Foliage (ADR 0020): `foliage` content type (`DATA019`) with five placeholder species; `Realm.Gen.Foliage` generator, exclusion raster, validator (`FOL001`-`FOL003`), `foliage.bin` and grass scatter; `bake-terrain` writes `foliage.bin`; the game draws trees and bushes per chunk with MultiMeshes (near and far models), scatters grass near the camera and gives nearby trunks collision. `--no-foliage` game flag.
- Flowing water (ADR 0019): rivers get a falling water level and a carved bed, lakes a flat level; `water.bin` holds the baked water meshes and the game draws them with a shader that scrolls ripples downstream at a speed from the slope, with foam on cascades. `ErosionSettings.CutRiverChannels`, `RiverNetwork.Channels`; erosion generator version 3 (terrain, placement and bake goldens changed).
- Opaque water: rivers and lakes use a third Terrain3D texture (`SurfaceTextureGenerator.Water`, 64 px blue ripples, glossy) assigned through the control map in `TerrainBridge`, with a neutral colour tint; the sea plane is nearly opaque. Terrain3D control-map layout (auto bit 0, base id bits 27-31) is assumed from 1.0.2 and confirmed by screenshot.
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
- Two errors at game start (`Resource file not found: res://` and `Error loading resource: ''`): `TerrainBridge` set the camera and read the material before the Terrain3D node was in the scene tree, which made the addon initialise outside the tree. It now does both when the node is ready (`TerrainBridge.Initialise`).
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

- Service buildings (ADR 0014): new content type `service` (`ServiceBuildingDefinition`, `ServiceBuildingParser`, `ServiceVocabulary`; validator code `DATA018`; `content/services/` with city hall, inn, blacksmith, trader, baker, jail, warehouse, farmer, woodcutter); `ServiceCatalog` and `ServiceAssigner` put them in every city and town (a tiered set: all in cities, a core subset in towns; farm buildings outside the wall); a coloured sign over each door; the main room takes the service's room type; `ServiceValidator` (`SETL007` to `SETL009`); `settlements.json` carries each settlement's services. How-to "add a service building" and the `service` reference.
- Cities on uneven ground (ADR 0014): `BuildingSite` (floor just above the highest ground under the footprint), `FoundationMesher` (stone plinth, wooden pilings, pier with a deck, steps from the door), ground, streets, wall, towers, centrepiece (with a plinth) and market stalls laid over the terrain (`IGroundSampler`, `SettlementGround`, `FlatGround`); `GroundGrid` and `GroundGridFile` bake `settlements_ground.bin`; `SettlementSettings.FlattenStrength` (0 to 1, default 0).
- A fourth city in the mountains: `SettlementSettings.MountainCities` (default 1) places `settlement.mountain_1` in a `mountainous` biome (330 to 1,100 m, relief up to 0.35 x radius); `Settlement.Highland`. On the shipped world it is Briarhaven in the Ashen Highlands at (2877, -4291), 469 m.
- Road corridors: `RoadSettings.HighlandMaxCutFillMeters` (45 m) and `HighlandBedToleranceMeters` (8 m) for roads that end at a mountain city.
- Walking climbs steps and ledges up to 0.35 m automatically (`StepClimber`); taller ones need a jump. `--no-step` turns it off. The first version failed on ordinary 0.2 to 0.3 m risers, because the capsule's rounded bottom touches a low riser at a tilted contact normal that was mistaken for a slope; it now detects being blocked by progress made, not by the normal. `scenes/step_test.tscn` is a headless physics check (12 cases) for it.
- Arrow keys move like WASD (Up/Down forward/back, Left/Right strafe).
- `tools/rebuild-world.sh`: lints content, bakes the world (including `map.png`), stops the running game, rebuilds it and restarts it; how-to "rebuild the world and restart the game".
- A compass strip at the top of the screen (`CompassBar`, `CompassMath`): the bearing you look along under a fixed marker, the cardinal points and a tick every 5 degrees sliding past as you turn; the HUD text moved below it; `CompassMath` (bearing from yaw, wrap, offset, point name) has tests in `Realm.Core.Tests`.
- The cross layout is only for small settlements (ADR 0017, revised): at most 24 houses, which have no palisade (a town gets a palisade only above 24 houses, `SettlementSettings.SmallSettlementMaxBuildings`); cities and towns are radial. Hamlets are 8 to 22 houses (`HamletMinBuildings`, `HamletBuildings`, drawn per hamlet), are crosses (`CrossShare` default 1) with shallower rows, a smaller square and short arms, and the number of houses falls with the distance from the crossroads. `settlement.placement` v9; goldens recaptured.
- Rivers vary along their length and always reach water (ADR 0018): `RiverWidth` now changes the width with the slope (gentle reaches wide, steep narrow), smooth noise, pools, a narrow source and an estuary at the sea (a pond where a river dries up), within 0.55 to 3.4 times the base and never jumping between points; `ErosionSettings.RiversReachWater` (default true): a river that loses water gets thin instead of drying up halfway, so it ends at the ocean, the world edge or another river; the overview map draws the real widths. The bridge abutment clipping is fixed: the timber road bridge's stone abutments were flush with the road and clipped through the terrain, they now sit under the deck; the arched city bridge's abutments no longer extend behind its ends. The road validator also leaves out a hairpin where a road crosses itself. Erosion and placement goldens recaptured; three hamlets fit now (was four); `settlement.placement` v8.
- Wider rivers and a river city (ADR 0018): rivers are 3.5 times wider (`RiverWidth`: half width max 7 m, 2.1 x sqrt(flow)) with a subtle wobble of two sine waves along their length, stored per point (`RiverPoint.HalfWidthMeters`) and used by the baker, `RiverRaster` and `WaterMask`; a river city (`SettlementSettings.RiverCities`, `settlement.river_N`, `bake-terrain --river-cities N`) that a river runs through untouched, planned 1.35 times larger so the houses on the two banks come to about the cap, with a bank buffer (`CityParams.WaterBufferMeters`, 8 m) and one or two arched stone bridges (`CityBridgeFinder`, `City.Bridges`, `ArchBridgeMesher`, walkable collision); validator `SETL010`; `settlement.placement` v7; tests `RiverCityTests`. The shipped world has 5 cities (the river city is Highford), 27 roads and 24 bridges.
- Denser settlements, waterfront settlements and a second layout (ADR 0017): a second row of houses behind the first, facing a lane (rows 10 m deep, 6.5 m in slums; a city of 250 houses has a wall radius of about 150 m instead of 175 m); `IWaterSampler`, `CityParams.Water`: houses in water are dropped, farm buildings are kept out of it and the wall is cut where it would stand in water, running 2.5 m into it with a tower or logs at each cut end (`CityMesher.LandRuns`); `GroundGrid` carries a water flag per point from the river, lake and sea data (`settlements_ground.bin` format 2, so the world must be re-baked); placement keeps the centre dry, allows water in the outer wards and prefers waterfront sites (`WaterFreeCentreShare`, `WaterfrontBonus`; water is left out of the relief survey; `SETL005` now only for water in the centre); a cross layout (`SettlementLayout.Cross`, `CrossLayoutGenerator`): two streets crossing at a 56 m square, four arms of different length chosen so the houses fit the cap, two rows of houses along each side, a plus-shaped wall with a gate at the end of each arm; `Settlement.Layout`, `SettlementSettings.CrossShare` (0.5), `bake-terrain --cross-share F`. `settlement.placement` v6; four hamlets now (the waterfront sites use up room); tests `WaterfrontTests`, `CrossLayoutTests`.
- Settlement outlines hug the houses: a city is now sized by whole wards (the largest ward count whose houses fit under `MaxBuildings`) instead of trimming houses one by one, so the wall no longer encloses bare outer wards; `CityParams.Parks` (off for settlements) removes the random empty park wards. `MaxBuildings` is now an upper limit that is met within a ward (233 to 250 of 250). The warehouse's minimum area dropped from 90 to 60 m2 so towns can hold one. `settlement.placement` v5; the world was re-baked (city wall radius about 165 m).
- Fortification by tier and hamlets (ADR 0016): `Fortification` (`Stone` for cities, `Palisade` for towns, `None` for hamlets; `Settlement.Fortification`), a wooden palisade of sharpened logs with wooden gate towers (`CityMesher.Palisade`), a `Hamlet` tier (at most 12 slum-style houses, no wall, lowland only, own name endings, farmer and woodcutter), `SettlementSettings.Hamlets` (5), `HamletBuildings`, `HamletMaxHeightMeters`, `bake-terrain --hamlets N`; `settlement.placement` v4; `FortificationTests`. The shipped world now has 4 cities, 5 towns and 5 hamlets, 27 roads (107 km).
- Tests: `ServiceBuildingParserTests`, `ServiceBuildingTests`, `UnevenGroundTests`, flatten and validator tests for the new rules; the whole solution has 659 tests.

### Removed
- The ground and street mesh of each city (`CityMesher.Ground`, `CityMeshes.Ground`, `SettlementPalette` ward ground and street colours stay unused): houses stand on the natural terrain; paths and roads come later (known issue 55).
- The scale-reference measure tape, houses and trees were removed from the game (`ReferenceStrip`, `MeasureTape`, `PropMeshConverter`). The Godot-free models (`Realm.Gen.Props`: cottage, town house, three tree kinds, `StripLayout`) and their tests stay for later use.

### Changed
- Bridges stand 20 cm above the ground at their ends: the timber road bridge deck is lifted by `DeckLift` and the arched city bridge roadway by `EndLift` (was 5 cm).
- Taller trees: placeholder models are about 11 m tall at scale 1 and the shipped scale ranges give trees from about 3 m to 20 m, favouring small sizes; oak and pine spacing and density adjusted; foliage generator version 2.
- Palisade is built of overlapping round logs (two staggered rows, varying radius 0.18-0.25 m and height, flat tops, sunk 0.6 m) instead of widely pointed stakes; no gaps.
- Two building styles (`BuildingStyle`): `Rich` as before, and `Slum` for slum wards and farmland: small (at most about 5.8 x 5.2 m, lots of 34 m2), one storey and one room, mud-brown walls, dark roof, no half-timbering, 15 cm walls and at most one window. Mud is colour only: no texture yet (known issue 60).
- Thick walls: outer walls are 30 cm (rich) or 15 cm (slum); walls between rooms are solid 15 cm boxes with real doorways (jambs and lintels); floors and ceilings are 30 cm slabs with visible edges round the stairwell. The collision follows.
- Settlement sizes: a city has at most 250 houses (was 1,200) and a town at most 50 (was 400); city wall radius is about 180 m (was about 300) and a town about 100 m. A city now uses the smallest ward count that holds its houses, so the outer slums are not trimmed away. `settlement.placement` golden recaptured; the world re-baked (17 roads, 83.7 km, 15 bridges).
- `RoadValidator` leaves out the bed check where two roads run together or cross (the later road's bed wins; the junction has no design yet, issue 51); cut and fill are still checked everywhere.
- Every building now has a rectangular floor plan (`CityGenerator.FitRectangle`: the biggest rectangle in the lot with its front on the street edge; the triangle-to-quad `CutApex` is gone) and `SideGap` rose from 0.9 to 1.4 m, so neighbours stand at least 2.8 m apart. Lots too shallow (under 3 m) or narrow (under 3.5 m) get no house, so a city holds fewer houses (the mountain city 1,043 of up to 1,200). City layouts, wall radii (about 310 m for a city, 210 m for a town) and the placement golden changed; the baked world was regenerated (17 roads, 66.8 km, 15 bridges).
- Cities are built on the natural ground; the terrain under them is no longer flattened (`FlattenStrength` 0). The settlement relief limit rose from 0.1 to 0.2 x pad radius and `SETL004` now checks relief and height range instead of levelness. `settlement.placement` 2 -> 3 (golden recaptured: four cities, new sites). The player spawns in the first city as before.
- Roads are planned one after another, each on the terrain left by the roads before it; the slope penalty rose from 25 to 300; `road.planner` 1 -> 2. The shipped world now has 17 roads (66.6 km) and 13 bridges (was 15 roads, 53.5 km, 10 bridges). `RoadValidator` names where the worst mismatch is.
- The wall is now sunk 2 m into the ground and follows it (its mesh spans -2 m to the tower tops).
- The baked world was regenerated (`baked/terrain`: new `settlements_ground.bin`, new roads and a mountain city).
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
