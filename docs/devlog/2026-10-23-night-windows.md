# 2026-10-23 — night windows
## Goal
Take the transparent windows and the day/night lighting from the CityGen viewer and bring them into Realm, with lit rooms following the crowd.

## What was done
- `RoomLighting` (sky fill per room, lamp per room), `RoomSurface` and the split of `BuildingMesher.Near` into outside, glass and per-room inside surfaces.
- `NightLights`: glow quads for far houses and the aura (street pool, wall halo, neighbour patches).
- `Crowd.CountInside`; `CityInstance.UpdateLights` eases each house's lamp level toward whether someone is home.
- Game: `CityLighting` (interior, glass, glow and aura materials and the `realm_night_light` global), `PropMeshConverter.ToRoomMesh` / `ToAuraMesh`, `DayNightCycle` sets the darkness.
- Removed the per-storey `OmniLight3D`.

## Decisions made
[ADR 0027](../decisions/0027-night-windows-and-lit-rooms.md): port CityGen's approach; the crowd decides which houses are lit (the developer's choice).

## Problems encountered and how they were solved
- `PropMesh` refuses an empty triangle list, so a house with no lit window or no window crashed the city build; `Glow` and `BuildingMeshes.Glass` are now nullable (`PropMeshBuilder.BuildOrNull`). The tests only used houses with windows, so the crash showed up in the game, not in the tests.
- Partition walls belong to two rooms; each face goes to the room it faces by the side of the room's centroid.
- The aura needs a depth bias toward the camera (no polygon offset in a Godot shader); it is done in the vertex shader.

## Tests added
`NightLightingTests` (sky fill bounds, lamp share, glass per window, room surfaces, glow per lit window, aura range and level falloff), `CountInside` in `ScheduleCrowdTests`.

## Docs added/updated
ADR 0027, `systems/settlements.md`, known issue 47, glossary, CHANGELOG.

## Open questions / next steps
Not seen yet: the inside of a lit room from within. The glow strength and the interior fill were taken as they were in CityGen and may want tuning. Shutters (a window that admits nothing), textures, door opening and people showing through the glass are not done.
