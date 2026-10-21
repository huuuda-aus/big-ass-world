# Documentation index

Structure follows Diátaxis (CLAUDE.md section 9). Pages marked **Planned** are not written yet.

## Tutorials
- [Getting started](tutorials/getting-started.md): clone, build, test, run the CLI.

## How-to guides
- [Add a material](how-to/add-a-material.md)
- [Add an item](how-to/add-an-item.md)
- [Add a biome](how-to/add-a-biome.md)
- [Add or change a region in the macro map](how-to/add-a-region.md)
- [Preview and tune the terrain](how-to/tune-the-terrain.md)
- [Tune erosion, rivers and lakes](how-to/tune-erosion-and-rivers.md)
- [Set up Godot (.NET) and Terrain3D](how-to/set-up-godot.md)
- [Run the game (walkable world)](how-to/run-the-game.md)
- [Place cities, follow the roads and walk into one](how-to/place-cities.md)
- [Run the streaming spike](how-to/run-the-streaming-spike.md)
- [Add a content type (developers)](how-to/add-a-content-type.md)
- [Add a simulation system (developers)](how-to/add-a-system.md)
- [Profile the simulation](how-to/profile-the-simulation.md)
- Debug a failing seed lands with the first generator: **Planned**

## Reference
- [Pinned versions](reference/versions.md)
- [CLI commands](reference/cli.md)
- [Validator codes](reference/validators.md)
- Content types: [material](reference/content/material.md), [item](reference/content/item.md), [biome](reference/content/biome.md), [world / macro map](reference/content/world.md)
- [Baked terrain format](reference/baked-terrain.md)
- [Baked settlements (`settlements.json`)](reference/settlements.md)
- Save format, scripting API, other content schemas: **Planned**

## Explanation
- [Architecture](explanation/architecture.md)
- [Determinism](explanation/determinism.md)
- [World scale and seamless streaming](explanation/world-scale-and-streaming.md)
- Generate-bake-edit, persistence, sim LOD, retro rendering: **Planned**

## Systems (as-built)
- [Core](systems/core.md)
- [Data](systems/data.md)
- [Sim](systems/sim.md)
- [Gen](systems/gen.md)
- [Game](systems/game.md)
- [Settlements (cities)](systems/settlements.md)

## Records
- [Decisions (ADRs)](decisions/)
  - [0001 Solution skeleton and build conventions](decisions/0001-solution-skeleton.md)
  - [0002 Test framework: xUnit](decisions/0002-test-framework-xunit.md)
  - [0003 YAML content loading](decisions/0003-yaml-content-loading.md)
  - [0004 Dense component storage and span iteration](decisions/0004-dense-component-storage.md)
  - [0005 World scale and seamless streaming](decisions/0005-world-scale-and-seamless-streaming.md)
  - [0006 Macro map format](decisions/0006-macro-map-format.md)
  - [0007 Height-field generation](decisions/0007-height-field-generation.md)
  - [0008 Erosion and rivers](decisions/0008-erosion-and-rivers.md)
  - [0009 Terrain3D integration and streaming](decisions/0009-terrain3d-integration-and-streaming.md)
  - [0010 Baked terrain format](decisions/0010-baked-terrain-format.md)
  - [0011 Terrain shapes, landmarks and gorges](decisions/0011-terrain-shapes-landmarks-and-gorges.md)
  - [0012 City generator import, placement and 3D](decisions/0012-city-generator-import-and-placement.md)
  - [0013 Roads, bridges, names and the world map](decisions/0013-roads-and-bridges.md)
- [Devlog](devlog/)
  - [2026-10-20 Solution skeleton](devlog/2026-10-20-solution-skeleton.md)
  - [2026-10-20 Core and Data foundations](devlog/2026-10-20-core-data-foundations.md)
  - [2026-10-20 Second content type and references](devlog/2026-10-20-item-type-and-references.md)
  - [2026-10-20 Sim foundations](devlog/2026-10-20-sim-foundations.md)
  - [2026-10-20 Sim query profiling](devlog/2026-10-20-sim-query-profiling.md)
  - [2026-10-20 World scale decision](devlog/2026-10-20-world-scale-decision.md)
  - [2026-10-20 Macro map](devlog/2026-10-20-macro-map.md)
  - [2026-10-20 Height field](devlog/2026-10-20-height-field.md)
  - [2026-10-20 Erosion and rivers](devlog/2026-10-20-erosion-and-rivers.md)
  - [2026-10-20 Godot and streaming](devlog/2026-10-20-godot-streaming.md)
  - [2026-10-20 Landscape shapes](devlog/2026-10-20-landscape-shapes.md)
  - [2026-10-21 Cities](devlog/2026-10-21-cities.md)
  - [2026-10-21 Names and roads](devlog/2026-10-21-names-and-roads.md)
- [Known issues](known-issues.md)
- [Glossary](glossary.md)
- [Changelog](../CHANGELOG.md)
