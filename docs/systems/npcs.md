# Characters and crowds (as built)

## 1. Purpose and scope
Simulated characters in settlements: plans from the generator (a name, a profession, a home, a workplace and a timetable), a day for each of them on the walking graph, reactions to rain and to the player, and a capsule view with an inspector. Not included: traits, needs, relationships, tasks, dialogue, interiors, collision with the player, characters outside settlements, saves.

## 2. Status
| Feature | Status |
|---|---|
| `NpcPlan`, `Crowd` (standing, walking, inside; timetables; catch-up after a time jump) | Implemented |
| `profession` content type with workplace and day blocks; names; shares per tier | Implemented |
| Motion: own speed, lane, rounded corners, easing, facing, doorstep pause; street-preferring paths | Implemented |
| Rain from the cloud model; shelter and hurrying | Implemented |
| Looking at the player, stepping aside, guards challenging | Implemented |
| Personal space: people eased apart, passing on their right (presentation only) | Implemented |
| Bodies kept out of buildings, stalls and the fountain (`OpenGround.Constrain`) | Implemented |
| Half the people indoors at the start; long stays indoors, longer in the rain | Implemented |
| Inspector label (name, trade, what and why) | Implemented |
| `CrowdGenerator` (guards, loiterers round the fountain, walkers with homes and destinations) | Implemented |
| `CrowdView` (capsules in one MultiMesh) | Implemented (placeholder look) |
| World walking graph joined by the roads (`WorldNavBuilder`) | Implemented (not yet used by characters) |
| Needs and tasks, social behaviour, avoidance that changes the simulation (people only ease apart on screen), travel between settlements, real figures, visible doors | Planned |

## 3. Architecture
```mermaid
flowchart LR
  C[City + NavGraph + OpenGround] --> G[CrowdGenerator]
  G --> P[NpcPlan list]
  P --> W[Crowd: Update by game minutes]
  K[WorldClock] --> W
  N[NavGraph + NavPathFinder] --> W
  W --> V[CrowdView: one MultiMesh]
```
Simulation (`Realm.Sim.Npcs`) knows nothing of the generator or Godot; the generator (`Realm.Gen.Population`) produces plans; `CityInstance` owns the crowd and the view.

## 4. Data
Plans are generated when a settlement is built (a few milliseconds) and are not baked or saved. Counts: 220 per city, 40 per town, 12 per hamlet (`CrowdSettings`); `--crowd-scale` scales them. The professions are read from `content/professions/` (found beside the baked world) when a settlement is built; without them characters get the simple behaviour of a role. Consumes the [`profession`](../reference/content/profession.md) content type.

## 5. Determinism and persistence
Plans and every later choice come from `Rng` (plan seed per character); two crowds with the same plans on the same graph and the same times give the same positions. Nothing is saved yet: on load the crowd starts mid-day from the clock.

## 6. Extension points
A new trade is a YAML file ([how-to](../how-to/add-a-profession.md)); new activities in `ScheduleActivity` and `Crowd.DecideScheduled` (and the word in `ProfessionVocabulary`); per-tier counts in `CrowdSettings`; a `lineClear` delegate for open-ground tests.

## 7. Validation
Tests cover the generator (counts, roles, spots on open ground and thickest by the fountain, homes and destinations reachable, determinism) and a simulated day of 220 characters (nobody in the fountain, the square full by day, the streets empty at night).

## 8. Performance notes
`Crowd.Update` visits each character once per frame and does work only for those whose next event is due; a path search is microseconds. `CrowdView` writes one transform and colour per visible character. The crowd is not updated or drawn while the player is more than 450 m from the city wall.

## 9. Known limitations
Known issues 75 and 76.

## 10. History
[ADR 0023](../decisions/0023-settlement-walking-graph.md), [ADR 0024](../decisions/0024-crowd-of-capsules.md), [ADR 0025](../decisions/0025-professions-and-reactions.md), [ADR 0026](../decisions/0026-even-crowd-indoor-life-and-collisions.md), devlogs [crowd](../devlog/2026-10-23-crowd-of-capsules.md) and [professions](../devlog/2026-10-23-professions-and-reactions.md).
