# 0024. A crowd of capsules on the walking graph, and the world graph
- Status: accepted
- Date: 2026-10-23

## Context
After the settlement walking graphs (ADR 0023) the next step toward living cities (P4, M3) is to put characters on them: dozens in settlements, hundreds in cities, concentrated around the fountain in the main square, each going about a day of its own. The developer asked for simple capsules instead of human-like figures for now, and for the settlement graphs to be joined to the road graph.

## Options considered
1. Godot nodes with navigation agents per character — dozens of nodes per settlement, tied to the engine, no headless tests, and no cheap way to keep far characters alive.
2. Stepping every character every frame in the simulation — cost grows with the population whether or not anyone is looking.
3. Characters whose position is a function of time on the walking graph, a plan per character from the generator, one MultiMesh to draw them — nothing is stepped per frame, a character is woken only when its next event is due, and a character far from the player costs nothing until the player returns, when it catches up.

## Decision
Option 3.
- **World graph**: `WorldNavBuilder` moves every settlement graph into world coordinates and joins them with the roads (each road point becomes a `Road` node, bridge spans become bridge edges, road ends join the outside nodes beyond the gates, roads that meet join each other); `NAV003` reports a settlement the roads do not reach. `OpenGround` was extracted from the graph builder so characters and the generator can ask whether a spot or a straight walk is on open ground.
- **Simulation** (`Realm.Sim.Npcs`, no Godot): `NpcPlan` (role, home, destinations, spots, bedtime and waking time, seed) and `Crowd`. A character is standing, walking a route or inside a building. Loiterers stand at spots on the square and now and then shift (straight across the open ground when it is clear, along the graph otherwise) or run an errand to a nearby building; walkers go between their home and nearby buildings, favouring service buildings; guards stand at their post. Everyone starts for home 90 minutes before bedtime and sleeps inside until waking time. Waits and visits are in game minutes; walking speed is 1.4 m per real second at the normal day length, scaled with the speed of time. A time jump is caught up, a few events at a time.
- **Generator** (`Realm.Gen.Population.CrowdGenerator`): plans for 220 characters in a city, 40 in a town, 12 in a hamlet (settings): guards at the gates, loiterers whose spots are thickest next to the fountain and thin out exponentially with distance, walkers with a home each and three destinations near their home. All choices come from `Rng` keyed by the world seed, the settlement and the character.
- **Game** (`CrowdView`): one MultiMesh of capsules per settlement, only the characters outside written each frame, coloured by role (purple loiterers, orange walkers, red guards). The crowd lives and is drawn only while the player is within 450 m of the wall. Options: `--crowd-scale=F` (0 for none); the HUD shows how many are out.

## Consequences
- 220 characters in the first city cost under a millisecond per frame in the simulation plus 220 transform writes; the first city shows 180 or more of them outside at midday.
- Characters do not collide with the player or each other, do not enter buildings visibly (they vanish at the door), and have no names, faces or needs yet. The same plans will drive real figures later.
- The graph's detours (median 1.5 times the straight distance, some 3 times) make some walks long; denser lane links would shorten them (known issue 75).
