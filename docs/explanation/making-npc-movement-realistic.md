# Making character movement more realistic: options

Status: options 1, 2, 3, 7, 8 and 11 were built on 2026-10-23 ([ADR 0025](../decisions/0025-professions-and-reactions.md)); the rest are still options. Written 2026-10-23, after the first crowd of capsule characters ([ADR 0024](../decisions/0024-crowd-of-capsules.md)).

## Where we are

Characters in a settlement stand, walk along the walking graph, or are inside a building (hidden). They all walk at one speed, turn on sharp corners, pick errands at random, never react to anything and vanish at their door. The aim of this page is "people going about their business with purpose".

## The options

| # | Option | What it adds | Effort | Cost per frame |
|---|---|---|---|---|
| 1 | **Motion polish** | Each person gets their own walking speed. Corners are rounded, with a short ease at start and stop. Each walks a personal lateral offset, so people do not stack on one line. They face their direction of travel (the capsule needs a nose or arrow), and pause at doors. | Small, about a day | Negligible |
| 2 | **Better paths** | People follow the sides of streets and lanes instead of cutting diagonally across yards. This means a denser lane graph and path smoothing, and it also fixes known issue 75 (long detours). | Small to medium | None at run time |
| 3 | **Purposeful days by profession** | Each person gets a job from the generator. The baker works in the bakery from dawn to dusk, merchants stand at stalls, farmers leave by the gate in the morning and return at dusk, guards patrol between gates and the square. Meals happen at the inn, and a market day fills the square. Data-driven (a `profession` content type with a daily timetable), so modders can edit it. | Medium to large, 2 to 3 days | Same as now |
| 4 | **Needs and tasks** (CLAUDE.md section 6.3) | Trips happen for a reason: carry goods from the warehouse to the trader, fetch water, deliver a letter. Characters pick by scoring needs (hunger, work, social, errand). Builds on 3. | Large | Low |
| 5 | **Social behaviour** | Pairs and groups walk together. Two people stop and talk, facing each other for a few minutes. Crowds gather round events. | Medium | Low |
| 6 | **Local avoidance** | Passers-by sidestep each other. It can run only for characters within about 30 m of the player, using a spatial hash. | Medium | Small, near zone only |
| 7 | **Reaction to the player** | They look at the player, step aside, greet, and guards challenge. | Medium | Near zone only |
| 8 | **Reaction to the world** | In rain the square empties and people hurry to shelter. Dawn and dusk rushes, lamplighters at night. Danger makes them flee. | Medium | Low |
| 9 | **Presentation** | Real figures with a walk cycle, doors that open when someone enters or leaves, lit windows at night, footsteps. Tied to open decision D5 (billboards or low-poly models). | Medium to large | Depends on the art |
| 10 | **Travel between settlements** | Merchants with carts, messengers and patrols walk the world graph between settlements. This is the simulation's abstract zone: they are only positions along a road until the player is near. | Medium | Near zero when far |
| 11 | **Debug label** | Aim at a person and see who they are and why they are going where they are ("Bram, baker, going to work"). It makes purpose checkable. | Small | Near zone only |

## Suggested order

1. Options 1, 2 and 11 first, about two days: they make movement look right and let us see purpose.
2. Then option 3, with 8 and 7 added in the same pass, so the day has structure and people react to rain and to the player.
3. Option 10 after that.
4. Option 9 waits until the figures decision (D5).

## Decision needed from the developer

Option 3 needs characters to have a profession and a home, and later a name and traits. That is a game-design call under pillar P4, so: should characters get an identity now?

## Related

[Characters and crowds](../systems/npcs.md), [Navigation](../systems/navigation.md), [known issues](../known-issues.md) 75 and 76, [glossary](../glossary.md).
