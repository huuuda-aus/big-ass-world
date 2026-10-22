# 0021. Day and night: a world clock, a sun and a moon
- Status: accepted
- Date: 2026-10-22

## Context
The world had a fixed noon. NPC schedules (P4), lighting and mood all need a time of day. There are no seasons yet, so only the daily cycle is needed.

## Options considered
1. Rotate one light and fade the sky colour — cheap, but no moon, no stars and no phases.
2. Model the planet: a sun on the celestial equator turning about a polar axis at a fixed latitude, a moon lagging it by a phase, a sky shader that draws both and the stars — still cheap, and the same maths later gives seasons (a declination term) and NPC light needs.
3. Use an asset-store sky — no, content stays ours and moddable.

## Decision
Option 2.
- `Realm.Sim.Time.WorldClock`: continuous game days advanced by real time (default 20 real minutes per game day, start 09:00), a time scale (0 pauses), and `Time` as the same `GameTime` (whole minutes) the tick clock uses, so schedules can read it later. It is separate from `SimClock` (which counts ticks); the tick loop will be driven from it when the simulation runs in the game.
- `Realm.Core.Geometry.CelestialMath` and `CelestialSettings`: from the time of day and the day count, the unit directions of the sun and the moon (X east, Y up, Z south), the moon's phase, its illumination and a smooth daylight factor. Latitude 40 degrees, a 12 game-day moon cycle and a 5 degree wander of the moon's path are placeholders. The sun follows the same path every day.
- `Realm.Game.World.DayNightCycle`: two `DirectionalLight3D`s (the sun with shadows, the bluish moon without), a sky shader (gradient, glow, sun disc, a moon disc lit by the sun so its phase is real, twinkling stars that turn with the hour angle), and ambient and fog colours from one palette. Ambient light is a colour, and reflections from the sky are off, so the sky never re-renders a cube map as the time changes.
- Game options `--time`, `--day-minutes`, `--time-scale`; key `T` cycles the speed (1, 30, 300 times, paused); the HUD shows the day, time and moon phase.

## Consequences
- Day and night are free at run time (two lights, one sky shader, a few uniforms per frame).
- Nothing uses the clock yet except the sky. Saves do not store it (to do with the save format, known issue 73). Building windows, torches and NPC behaviour at night are later steps.
- Night is dark but readable (ambient colour floor plus the moon light); tuning is a look decision.
