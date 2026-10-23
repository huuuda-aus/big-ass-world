# 0026. An even crowd, a life indoors, and bodies that do not enter buildings
- Status: accepted
- Date: 2026-10-23

## Context
Three things were wrong after the first run of the professions (ADR 0025): the crowd was packed round the fountain, people popped in and out of houses, and they walked through houses and walls.

## Decisions
- **An even crowd**: "out and about" (the activity formerly called `square`) now uses places spread evenly over the whole settlement: street nodes and door fronts of the walking graph, away from the fountain's own ring, each a little off the node. The crowd is no longer heaped by the fountain (market traders still stand at their stalls). The activity word in profession files is `about`.
- **Half indoors on load**: when a settlement's people are created, half are indoors (at home, or at work if that is what the timetable says) for 20 to 200 more game minutes, half are outside setting off.
- **A life indoors**: visits last 30 to 120 minutes (they were 6 to 45); someone out and about calls on a house nearby 30 percent of the time and stays that long; when it rains, people stay under the roof for at least 20 minutes at a time, and after it stops wait another 10 to 50 minutes before coming out.
- **Bodies are solid**: a body of radius 0.35 m is kept out of buildings, market stalls and the fountain. The walks keep 0.55 m from buildings (foundations and steps included; it was 0.3 m); the lanes people keep to are 0.3 to 0.6 m (were 0.5 to 1.0); and the position that is drawn, whatever pushed it (the lane, a corner cut by the rounding, a step aside from the player, a push from a neighbour), goes through `OpenGround.Constrain`, which moves a body that has strayed inside an obstacle out through the nearest edge, and falls back to the walk's own line if the result would be in water or across the wall away from a gate. The same constraint keeps the easing between people from pushing anyone into a wall.

- **The wall is solid** (added after people were seen walking through a palisade): the stretches of the wall, the corner towers and gate towers of a stone wall, and the watchtowers beside the gates of a palisade are obstacles like houses (`OpenGround` takes the settlement's fortification; a settlement without walls has none). A gate opening is free. Three causes were found: the wall was only a line to cross, so a body could stand in it; the generator runs some streets along the wall near a gate, so street nodes sat inside the wall (such stretches are now skipped and the repair finds a lane round them); and the way out of a gate was taken as straight away from the centre, which at a bend in the wall runs along the wall, so it is now the bisector of the wall's corner (`OpenGround.GateOutward`). Guards flank the gate opening, standing at the road. A walk to a spot that is not on the graph stops at the end of the path if the last leg is not open ground.

## Consequences
- A test follows 220 people for a day and checks, ten game minutes at a time, that nobody is drawn inside a house, a stall or the fountain.
- The graph keeps a little more distance from walls, so very narrow gaps between houses no longer count as lanes; every door of all 13 shipped settlements is still reachable.
- The collision is for drawing; the simulation does not wait for or avoid anybody, and the player does not collide with the characters yet (known issue 76).
