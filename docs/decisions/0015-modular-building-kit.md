# 0015. Modular building kit (plan)
- Status: proposed
- Date: 2026-10-22

## Context
Buildings are generated entirely by code (`CityGenerator` cuts a rectangular lot, `InteriorGenerator` makes rooms, `BuildingMesher` builds flat-colour walls, roof and foundation). There is no asset import. The developer will draw the art (Godot, Photoshop) and wants a library of tagged pieces that the generator picks and places, with: no snap-to-grid and no preset sizes; several medieval roof types; per-region architecture later (crucial); interior walls as hand-crafted pieces (later: interiors stay random for now); seamless tiling textures.

## Options considered
1. Whole models per building — cannot fit arbitrary footprints and would disagree with the generated interior (door and window positions).
2. A modular kit chosen by tags, with pieces that fit any wall length (chosen).
3. Keep everything procedural and only add textures — cheapest, but no hand-made architecture.

## Decision (plan, not built yet)
- **Plan, then pieces.** Gen produces a *building plan*: a list of pieces with transforms and the data to choose them (role, floor, side, opening, style tags). The game instantiates the pieces. `BuildingMesher` shrinks to the procedural fallback (and the foundation, until foundation pieces exist).
- **No grid.** A wall of any length is a run of **feature pieces** at free positions (door, window, corner post: fixed size, never distorted) and **fill pieces** (plain wall) that **tile** to take up the rest. The developer chose tiling over stretching: fill pieces are authored as a repeating section plus end caps, with seamless textures, so a wall is the end cap, N repeats and the other end cap; a leftover shorter than one repeat is absorbed by shifting the features or by a short-section variant. Roofs tile along the ridge in the same way and have separately authored gable ends; the pitch comes from the authored roof piece (several roof pieces of different pitch and form: gable, hip, half-hip, gambrel, jetty, with dormer and chimney attachments), picked by span, ward and region.
- **Tags** (set by the developer on each piece scene; exported to a kit manifest YAML that Gen reads, so Gen stays Godot-free): `role` (wall, corner, roof, roof-end, floor, ceiling, stair, foundation, door, window, chimney, dormer, interior-wall, doorway), `floor` (ground, upper, any), `side` (exterior, interior), `opening` (none, window, door), `style` (region and architecture tags), plus the repeat length, end-cap pieces, nominal size, weight and sockets. Selection is a seeded weighted query, so the same seed gives the same city.
- **Region architecture** is data: an `architecture_style` content type per region lists allowed tags, roof family weights, floors, jetties, window and door density, wall materials.
- **Interiors** stay random subdivisions for now; interior wall pieces come later through the same tags (`side=interior`).
- **Landmarks** (cathedral, city hall) may be whole-model set pieces: the generator reserves a lot of the model's footprint.
- **Validation**: per style, every role has a piece, every wall length from 3.5 m up can be filled, every opening has a feature piece; a kit manifest that fails does not load.
- **Performance**: near buildings use one MultiMesh per piece type per city; distant buildings are merged into one mesh each on a worker thread (the piece mesh data must be readable off the main thread). Collision comes from simple authored boxes per piece, not the mesh.
- **Texture supply** (the developer asked for advice): because walls and roofs tile, a texture atlas does not work (atlases cannot repeat). Supply each material as its own **seamless PNG**, 64 or 128 px square (power of two; 256 only for hero pieces), sRGB, no alpha unless it is a cut-out, with no mip-map baking (Godot makes them; nearest filtering for the retro look). One file per material and style, named `<style>/<material>.png` (for example `highlands/wall_plaster.png`, `highlands/roof_slate.png`, `common/mud.png`). A style's materials are packed into a Texture2DArray at load so one draw call covers a whole style. Pieces use UV 0..1 per repeat section. A piece scene or glTF should have its pivot at the start of the section on the wall line, +X along the wall, +Y up, +Z outward, in metres.

## Consequences
- Easier: new architecture is art and YAML; the generator's layout, interiors and validators do not change.
- Harder: the plan must carry piece choices, so the building mesher is replaced; far LOD merging needs mesh data off the main thread; every region needs a complete set of pieces before it can appear.
- Not decided: foundation pieces (plinth segments, posts) versus procedural; compound buildings (L shapes, wings), which would combine several rectangles; whether interior wall pieces need sockets.
- First step when approved: a prototype with five pieces (corner, plain wall section with end caps, window wall, door wall, gable roof) to prove tiling, tag queries and the plan.
