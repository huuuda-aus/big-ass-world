# 2026-10-23 — professions, purposeful movement and reactions

## Goal
Options 1, 2 and 11 of the movement options page, then 3, 7 and 8: realistic motion, a reason for every trip, and reactions to the player and to rain.

## What was done
- Motion polish: speed factor, lane offset, rounded corners (`NavRoute.Smooth`), easing, heading and a nose, doorstep pauses; `NavEdge.Weight` so paths prefer streets.
- `profession` content type and eleven placeholder professions; names; `CrowdGenerator` assigns trades by tier shares and gives each person a workplace and timetable; `Crowd` interprets the timetable.
- `RainModel`, `RainEffect`, shelter and hurrying; looking at and stepping aside from the player, guards challenging; inspector label; `--rain`.

## Decisions made
[ADR 0025](../decisions/0025-professions-and-reactions.md). Identity (a name, a trade, a home) taken as default under P4 because trades and the inspector need it; traits wait.

## Problems encountered and how they were solved
- The first idea of a "sleep" plus a few roles could not express a baker's day; the timetable engine replaced it for people with a profession, and the simple role behaviour stays for plans without a schedule (and for the older tests).
- A character's position is a function of time, so pausing or re-routing a walker mid-way is awkward: rain and the player only change what a standing character does, a walker finishes its walk first, and a character's look at the player is a cosmetic turn of the snapshot.
- The inspector's aim test missed a character right in front of the viewer because it measured to the middle of the capsule; it now tests head, middle and feet.
- Rain never fell with a 0.75 cloud cover and thickness 0.65; the thresholds were tuned until the wet regions rain at their peak weather.

## Tests added
Data: profession parser and shipped content. Sim: timetable (baker inside by day, trader at the stall, farmer in the fields, watchman's round), rain shelter and return, the watch in the rain, looking at the player and the guard's challenge, lane and facing, rain model, weights, rounded routes. Gen: trades and shares, workplaces, names and labels, a day in the city by trade.

## Docs added/updated
ADR 0025, profession reference, add-a-profession how-to, npcs system doc, validators (DATA020), run-the-game, glossary, known issue 77, CHANGELOG, index, the options page.

## Open questions / next steps
Needs and tasks (option 4), social behaviour, avoidance among characters, visible doors and real figures, travel along the world graph.
