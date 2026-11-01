# 2026-11-01 — building roof and foundation gaps

## Goal
The developer reported that generated houses look rough: gaps and overlaps around the roof and foundation, and roofs that read as transparent when seen from underneath. Find and fix the actual geometry bugs (separate from the larger question of adding real textures, tracked separately).

## What was done
- Found the cause of the "transparent roof": `BuildingMesher.Roof` builds the eave-to-apex fan as single-sided triangles (`PropMeshBuilder.Triangle` only emits one winding), so standing under the overhang and looking up shows the sky instead of the underside.
- Found the cause of the gaps: `Roof` and `FoundationMesher` (stone plinth outline, pier deck outline) offset each footprint vertex by a fixed distance along the centroid-to-vertex direction. For a non-square footprint that does not hold the perpendicular distance from every edge at the intended overhang — the long edges of an elongated building undershoot, so the eave or plinth sits closer to the wall than intended, sometimes short of it.
- `ConvexPoly.Outset(distance)`: a proper outward offset, intersecting every edge's own outward-shifted half-plane (mirrors `Inset`'s math, but `Inset`'s `Clip`-from-`this` approach only ever shrinks, so `Outset` starts from a bounding box comfortably larger than the result and clips that down instead).
- `BuildingMesher.Roof` and `FoundationMesher`'s `Loop` call sites now use `Outset` instead of the centroid offset.
- The roof fan now emits a second, reversed-winding triangle per segment in a new colour, `SettlementPalette.RoofSoffit`, so the underside is visible and shaded distinctly from the top (a stand-in for a boarded/rafter look until the texture pass).

## Decisions made (link ADRs)
No new ADR; both are bug fixes with no behaviour choice attached. `RoofOverhang` (0.5 m), `StoneLipMeters` and `DeckWidthMeters` are unchanged.

## Problems encountered and how they were solved
- First attempt reused `Inset` directly with negated distances. That doesn't work: `Inset`'s `Clip` only ever removes area from its starting polygon (`this`), so clipping an already-small polygon with "shifted outward" half-planes still can't recover area the small polygon never had. Fixed by starting `Outset` from a bounding box larger than the expected result and clipping that down with the same half-plane math.
- The first test for `Outset` asserted every outset corner is *at least* the offset distance from every original edge. That's backwards: the offset polygon is the *intersection* of the shifted half-planes, so a far corner can be (and normally is) well inside that bound for an edge it isn't adjacent to. Corrected to: no point exceeds the offset distance from any edge, and each edge has an outset corner that touches its shifted line exactly.

## Tests added
`ConvexPolyTests.OutsetMovesEveryEdgeOutByTheSameDistance_EvenOnAnElongatedRectangle`, `OutsetOfASquareGrowsItBySymmetrically`; `BuildingMesherTests.Far_RoofHasASoffitVisibleFromUnderneath_MatchingTheTopFanOneForOne`, `Far_RoofOverhangsEveryFootprintEdgeByTheSamePerpendicularDistance`. Full `Realm.Gen.Tests` run: 611 passed.

## Docs added/updated
CHANGELOG. This devlog. `docs/systems/settlements.md` history line.

## Open questions / next steps
- The developer separately asked about real textures for buildings (stucco, timber, thatch/tile, stone, wood). `ADR 0015` already has a proposed (not yet accepted) plan for a modular, hand-authored building kit that explicitly rules out a texture atlas in favour of seamless tiling PNGs per material — worth reconciling before sourcing or wiring in any texture, rather than building something ADR 0015 would throw away. Raised with the developer; not started.
- The soffit colour is a flat placeholder like everything else in `SettlementPalette`; it should become a real material in the same pass as the rest of the roof.
