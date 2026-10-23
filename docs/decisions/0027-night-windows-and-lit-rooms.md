# 0027. Night windows, lit rooms and glass
- Status: accepted
- Date: 2026-10-23

## Context
The settlements had flat vertex-coloured houses, opaque window panes, and one plain `OmniLight3D` per storey of every detailed house. Nothing glowed at night, rooms were as bright as the street, and nothing tied a lit window to anyone being home. The developer's own CityGen viewer (`~/Develop/citygen/web/viewer.js`) already had transparent windows, dark rooms lit through their openings, lamps in a share of rooms, painted lit windows on far houses and a fake glow thrown on the street. The developer asked to take it from there, and for lit rooms to follow the crowd.

## Options considered
1. Real lights per window — pro: physically right; con: hundreds of lights, none cast shadows anyway, a cost the Forward+ renderer pays per light.
2. Port the CityGen approach (emissive lamps in the surface shader, a painted glow, one additive glow mesh) — pro: tested look, one draw per lit house; con: needs custom shaders and per-vertex room data.
3. Lamps from the hash only (as CityGen) vs. from the crowd — the developer chose the crowd.

## Decision
Option 2, driven by the crowd.
- **Rooms** (`BuildingMesher.Near`): the inside surfaces are split per floor and room into `RoomSurface` (sky fill from `RoomLighting.SkyFill`, lamp flag from `RoomLighting.HasLamp`, floor height). Partition walls give each face to the room it faces; a door between rooms belongs to the room it opens from; the front door stays with the outside.
- **Glass**: window panes are a separate translucent mesh (`BuildingMeshes.Glass`), double-sided and not writing depth.
- **Night lights** (`NightLights`): `Glow` paints a bright quad over each lit window of a far house; `Aura` builds, per house, a half-disc pool on the street, a halo on its wall and patches on neighbouring walls that face it (positions in the city's frame, UVs on a round falloff texture).
- **Lamps**: the room behind the front door always has a lamp; about 45 % of the other rooms do (a hash of building, floor and room, the same on every load).
- **Occupancy**: `Crowd.CountInside` counts people inside by door node; `CityInstance` maps nodes to houses and eases each house's lamp level toward 1 when someone is home (and it is dark) and 0 when empty. That one number drives the interior lamps, the far glow and the street glow (instance shader parameter `lamp_level`).
- **Shaders** (`CityLighting`): the interior shader scales the ambient light by the room's sky fill (`AO`), shades by form (skirting, fade up the wall, darker ceiling) and adds emission for lit rooms; the glow and aura shaders fade in with a global `realm_night_light` set by `DayNightCycle`. Lamps look three times brighter from the street and ease to normal when the player is inside the house.

## Consequences
- Rooms are dark by day unless windows or doors admit light; sun patches stay full strength.
- Houses are lit only while their people are home and the crowd is simulated (within about 450 m of the wall); distant cities are dark, which matches what is not simulated.
- The light thrown on the street and walls is a picture, not a light: it casts no shadows and does not light people or props.
- Interior lamps no longer light the player or the characters, since the real lights are gone.
- Generating a city computes every house's glow once (a second pass over all houses) on the worker thread.
