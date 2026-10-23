# 0025. Professions, timetables, and reactions to the player and the rain
- Status: accepted
- Date: 2026-10-23

## Context
The first crowd (ADR 0024) was capsules that shuffled about at random. Making the movement read as "people going about their business with purpose" (see the options page `docs/explanation/making-npc-movement-realistic.md`) needs three things: movement that looks like walking, a reason for every trip, and reactions to the world.

## Options considered
1. Hard-code a few roles in the simulation — quick, but the day of a baker could not be changed without code, and modders could not add trades (P7).
2. A `profession` content type with a timetable of activities the simulation interprets; the generator turns each person's profession into a plan — data-driven, testable, and the same pipeline modders use.
3. A full needs-and-utility model now — better in the end, but it needs identity, relationships and a task system first; a timetable is the first step toward it.

## Decision
Option 2.
- **Identity** (the developer's call under P4, taken as default): every character has a name (`NpcNamer`, placeholder lists), a profession, a home and a workplace; traits and relationships come later.
- **Motion**: each character has its own walking speed (0.8 to 1.25 times), keeps to a personal lane on the right of its way (fading out at the ends of a walk), corners of a walk are rounded (`NavRoute.Smooth`), a walk starts and ends slower than its average, characters face the way they walk (a nose on the capsule), and they pause on the doorstep before moving on. Paths prefer streets: a link from door to door across a yard costs 1.35 times its length (`NavEdge.Weight`).
- **Timetables**: a `profession` record names a workplace (a service building, a stall, a gate post, a patrol, the fields) and the blocks of the day; `Crowd` interprets the blocks (work inside the building in spells and step out, stand at a stall or post, patrol the gates and the square, go to the fields beyond the gate, idle in the square, visit the inn, run errands, be home). Each person's day is shifted by up to 25 minutes so nobody moves in lockstep. Shares per settlement tier decide how many people have each trade; the rest are the filler (townspeople).
- **Rain** (`RainModel`): rain comes from the cloud model: regions with a lot of thick cloud (the mountains, the marsh) rain when the slow swing of the weather and the showers lift the cover past a threshold; the game draws falling streaks. When it rains (0.35 and up), people standing in the open take shelter in the nearest building, a few at a time, hurry when they walk (up to 30 percent faster), and come out some minutes after it stops (0.15). The watch stays at its posts.
- **The player**: characters within 7 m look at the player (a walker only glances, and not all do), a walker passing within 2.5 m steps aside, and a guard at a gate within 5 m challenges (the nose turns yellow and the HUD says "Halt! State your business.").
- **Personal space** (added after the first run showed people walking through each other): `Crowd.CollectVisible` collects the people who can be seen and eases apart any two closer than 0.8 m, with a smoothed offset on what is drawn (the simulation does not change). Two people walking toward each other each step to their own right; other pairs ease straight apart; people on the same spot part along a direction fixed by the two of them; nobody is pushed onto ground that is not open (the same open-ground test the walks use).
- **Inspector**: looking at a character shows its name, profession, what it is doing and why, and when it next does something else (key-less, always on while the HUD is on). `--rain=X` fixes the rain for testing.

## Consequences
- A trade or a day is a YAML file; nothing in code changes to add one. A test keeps the activity words of the data and of the simulation in step.
- Rain is only as good as the cloud model: it rains in the highlands, alpine and marsh regions at times (and the weather swings over days); the steppe and badlands stay dry. There is no wetness, sound or wind yet.
- Characters still vanish at their door, do not collide, and have no needs; those are the next steps (needs and tasks, visible doors, real figures).
