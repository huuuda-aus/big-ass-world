# 2026-10-22 — day and night cycle

## Goal
A sun and a moon cycling around the planet (no seasons), as the base for lighting and NPC schedules.

## What was done
- `WorldClock` (Sim), `CelestialMath` and `CelestialSettings` (Core), `DayNightCycle` and a sky shader (Game); HUD time and moon phase; `--time`, `--day-minutes`, `--time-scale` and the `T` key.

## Decisions made
[ADR 0021](../decisions/0021-day-night-cycle.md).

## Problems encountered and how they were solved
- Stars showed too brightly at dusk: their brightness now follows the night factor squared.
- Ambient light from the sky would re-render a cube map as the sky changes: ambient is a colour and sky reflections are off.

## Tests added
`CelestialMathTests` (noon, sunrise and sunset, no seasons, new and full moon, phase cycle, illumination, daylight) and `WorldClockTests`.

## Docs added/updated
ADR 0021, how-to run the game, game system doc, glossary, known issue 73, CHANGELOG.

## Open questions / next steps
Window and street lights at night, sunrise and sunset colour tuning, wind and weather, seasons (a declination term in `CelestialMath`), clock in saves, driving the tick clock from `WorldClock`. Next: the street graph for the NPC slice.
