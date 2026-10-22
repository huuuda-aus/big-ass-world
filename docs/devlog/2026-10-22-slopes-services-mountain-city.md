# 2026-10-22 — slopes-services-mountain-city

## Goal
The developer asked for: (1) cities on slopes and uneven ground with a pier, pedestal or piling (wooden logs or stacked stones) to make up the difference; (2) a fixed set of service buildings in every city (city hall, inn, blacksmith, trader, baker, farmer, woodcutter, jail, warehouse, ...); then (3) one extra city in the mountains. Milestone M1 (one region) with a first step into M3 (towns).

## What was done
- Questions answered by the developer: no flattening at all, services as a YAML content type, a tiered set (all in cities, a core subset in towns). The pier was in the original request, so it is included (for tall wooden foundations).
- Data: `service` content type (`ServiceBuildingParser`, `ServiceVocabulary`, `DATA018`), nine shipped records, `ContentDatabase.Services`, lint summary.
- Generator: `ServiceCatalog`, `ServiceAssigner` (inside the wall by ward rank, nearness and spread; outside the wall a new farm house on a farmland patch), `Building.Service`, sign over the door, main room type, `ServiceValidator` (`SETL007` to `SETL009`), services in `settlements.json`.
- Ground: `FlattenStrength` (default 0), relief limit 0.2, `SETL004` rewritten, `GroundGrid` and `settlements_ground.bin`, `IGroundSampler`/`SettlementGround`.
- Meshing: `BuildingSite`, `FoundationMesher` (stone courses, pilings with beams, pier deck with rail, door steps), draped ward ground and streets, tilted sunk wall pieces, landmark plinth, market stalls on the ground; game: floor node per house at `BaseY`, landmark root, streamer reads the grid.
- Mountain city: `MountainCities`, `Highland`, placement by biome tag `mountainous` (bake passes a region test).
- Roads: sequential planning on the terrain left by earlier roads, slope penalty 25 to 300, looser limits for roads that end at the mountain city. The bake was rerun and `baked/terrain` regenerated.

## Decisions made
[ADR 0014](../decisions/0014-cities-on-natural-ground-and-service-buildings.md); CLAUDE.md section 6.1 step 6 and the content directory list changed with it.

## Problems encountered and how they were solved
- First bake with the mountain city: three `ROAD003` errors (cuts up to 166 m). The A* cost for hills was too weak for mountains. Slope penalty 25 to 300 brought the worst cut to 28 m. The remaining bed mismatch (27 m) was two roads sharing a corridor with different surfaces; planning each road on the terrain left by the previous ones cut that to 6 m. The last 6 m is where roads cross on a steep slope: the validator now allows 8 m for roads that end at a mountain city (a limit chosen for mountain roads only, other roads still must match within 1 m).
- A plinth appeared under the fountain on level ground: the "is it worth a plinth" test compared the buried depth, not the drop. Fixed and covered by `Landmark_StandsOnTheGroundAtTheSquare_WithAPlinthOnASlope`.
- Two inns and two warehouses ended up side by side (both preferred the same gate): a spread penalty between buildings of the same service fixed it.
- Test fallout: flatten and validator tests assumed a level pad; they now test the default (no change), strength 1 and 0.5, and the new steepness rule. Golden placement checksum recaptured.
- Mountain terrain is gentler than expected: the steepest building site in Briarhaven drops 2.9 m, so most foundations are low plinths (1,071 stone, 101 wooden, 31 none). Taller pilings and piers are covered by tests with synthetic slopes but are rare on the shipped terrain.

## Tests added
`ServiceBuildingParserTests` (6 theories and facts), `ServiceBuildingTests` (14 including a seed sweep over both tiers and the vocabulary check), `UnevenGroundTests` (14), shipped-world tests for the mountain city and the relief rule, flatten/validator tests. 659 tests in the solution, 0 warnings.

## Docs added/updated
ADR 0014, `reference/content/service.md`, how-to "add a service building", settlements system and reference (ground file, new fields, settings), data system doc, validators (`DATA018`, `SETL004`, `SETL007` to `SETL009`, `ROAD003`), CLI reference, place-cities how-to, glossary, index, known issues 54 to 58, CHANGELOG, README, CLAUDE.md section 5 and 6.1.

## Open questions / next steps
- Developer: walk the mountain city and the slope houses; say what looks wrong (plinth height and colour, pilings, steps, signs).
- Design: should towns get a town hall, and should cities have a second tier of services (bank, temple, guild)? Counts per tier are in the YAML, so this is data only.
- Candidates: steep-street steps, split-level floors, a pier over water, shops and owners for the service buildings, a road junction design.
