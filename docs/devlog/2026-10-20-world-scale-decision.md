# 2026-10-20 — world-scale-decision

## Goal
Before starting the terrain generator, settle the scale question: walk 30 minutes with no visible loading and no stutter.

## What was done
- Worked out the numbers (speed, chunk crossing rate, precision, baked size) and the storage comparison (HDD, SATA SSD, NVMe) with waiting times for walking and for fast travel.
- Checked current Terrain3D documentation and issues for region streaming. Results were incomplete, so the feasibility is recorded as unverified.
- Recorded the decisions: CLAUDE.md D2, D6 note, new D10, new section 4.9, M1 gate; ADR 0005; explanation page; glossary; known issues 15 and 16.

## Decisions made
- [ADR 0005](../decisions/0005-world-scale-and-seamless-streaming.md): world about 20 x 20 km; visible transitions only for fast travel, dungeon/interior entry and initial load; streaming strategy and soak-test gate.
- Not decided: walking speed, chunk size, draw distance, vertex spacing, minimum storage (D10). All listed as assumptions.

## Problems encountered and how they were solved
- Terrain3D streaming behaviour could not be confirmed from search results (a region-streaming issue is open; a PR mentions a shader rebuild cost). Treated as the top risk with a spike as the first M1 task, and the fallback (own tiled mesh behind `TerrainBridge`) recorded.
- The developer's storage answer was a request for a comparison rather than a choice, so D10 stays a proposal.

## Tests added
None (decision and documentation only).

## Docs added/updated
ADR 0005, `explanation/world-scale-and-streaming.md`, CLAUDE.md (D2, D6, D10, 4.9, M1), glossary, known issues, index, CHANGELOG.

## Open questions / next steps
- Developer: confirm D10 (SSD recommended with HDD-friendly layout), walking speed.
- Next: YAML polygon macro map in world metres, with tile-aligned output, in `Realm.Gen.Terrain`.
