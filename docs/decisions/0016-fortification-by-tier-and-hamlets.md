# 0016. Fortification by settlement tier, and hamlets
- Status: accepted
- Date: 2026-10-22

## Context
Every settlement had the same tall stone wall with towers and gate towers. The developer asked for the fortification to depend on the settlement type: big walls for cities, smaller wooden walls for settlements, no walls for hamlets.

## Options considered
1. A `fortification` field in the baked settlement record chosen by the placer — flexible, but one more field to keep in step with the tier.
2. Derive it from the tier (chosen) — the tier already says what the settlement is; a data table can replace the `switch` later when settlements become a content type.

## Decision
- New tier `Hamlet` beside `City` and `Town`. `Settlement.Fortification` is computed from the tier: `City` gets `Stone` (the existing 7 m wall, towers and gate towers), `Town` gets `Palisade`, `Hamlet` gets `None`.
- **Palisade** (`CityMesher.Palisade`): a fence of sharpened logs (0.44 m thick, 3.2 to 4.0 m tall, five-sided, tapering to a point, each a slightly different brown), standing on the ground along the wall line with a gap at every gate and a wooden watchtower (four posts, a platform and a pointed roof) either side of each gate. No corner towers. The logs have collision as part of the wall mesh.
- **Hamlets**: at most 12 houses (`HamletBuildings`), all slum-style (`CityParams.Humble`: every ward but the market is a slum), no cathedral and no centrepiece, only lowland (`HamletMaxHeightMeters` 200 m: the roads to hamlets in the high mountains needed cuts of 60 m and more), named with their own endings (`-thorpe`, `-cot`, `-holt`, `-fold`, ...), five by default placed after the towns (`SettlementSettings.Hamlets`, `--hamlets N`; the 2,500 m spacing and the lowland limit leave room for five in the shipped world). The service tiers gain `hamlet` (the farmer and woodcutter only). Hamlets are joined to the road network like any settlement; the map draws them smaller than towns. `settlement.placement` is v4 (golden recaptured).

## Consequences
- Easier: a new fortification kind is an enum value and one mesher branch; the kit plan (ADR 0015) can add wall pieces per region the same way.
- Harder: the road network now has 27 roads (107 km) because every hamlet is linked.
- Not done: a ditch or earth bank for hamlets, corner watchtowers, a hamlet landmark (a well), gates that close, hamlet-specific services (an inn, a shrine), and wall type by region.
