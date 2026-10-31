# 0029. Combat model: one character, real-time on action speed, a tactical pause, target-based
- Status: accepted
- Date: 2026-10-31

## Context
CLAUDE.md left two combat questions open: D3 (real-time, turn-based or a toggle; default "toggle, M&M style") and D4 (party or single character; "ask before M5"). The inspiration is *Might and Magic VI–VIII*: a party of four moving as one body, real-time combat paced by each character's recovery time, an Enter-key switch to full turn-based mode, typed damage and resistances, conditions, skill masteries, monster tiers, and real projectiles. Its known weaknesses are a real-time click-fest with four characters, monsters walking up for free in turn-based mode, and kiting, stair and flying exploits.

Combat touches the simulation, AI, UI, balance, saves and content schemas, so the model has to be fixed before M5 work starts. Nothing about combat is built yet.

## Options considered
1. **Party of four, real-time plus full turn-based mode (M&M).** Faithful. Most UI and balance work; two pacing modes to keep fair.
2. **One character, real-time only.** Simplest. Loses the tactical layer.
3. **One character, real-time paced by action speed, plus a VATS-style tactical pause (modern Fallout).** Keeps M&M's recovery-time pacing and adds a short tactical burst instead of a whole second mode. Needs an action-point resource and a pause UI.
4. **One character, real-time plus full turn-based mode.** The tactical layer of option 1 without the party, but still two modes that must both be balanced.

Further questions decided with the developer:
- Melee targeting: **target-based** (pick a target, range check, roll) or aim-based (hit volumes, swing arcs). Aim-based needs hit detection and animation work the retro look does not justify.
- Area effects: **hit everyone in the area, including the player and bystanders**, or only enemies.
- Death: **wake at a temple with no penalty**, wake with a gold loss (M&M), or reload.

## Decision
- **One player character (D4).** The developer called this "most likely", so it may be reopened, but only before M5 combat work starts; after that it is a rewrite. Companions or hirelings, if they come, are separate NPCs, not a party.
- **Real-time combat paced by action speed (D3).** Every combatant has a time when it is next ready. An action sets that time to now + its recovery; weapon, stats, skill and conditions change recovery. Everyone, the player included, follows the same rule.
- **A tactical pause, VATS style, not a turn-based mode (D3).** A key stops the simulation clock. The player queues targeted actions, each paid from an **action point** (AP) bar and shown with its hit chance from the same formula as real time. On confirm the queue resolves on the normal sim clock, without waiting for recovery between the queued actions, while enemies keep acting on their own timeline; presentation plays it slowed. Then real time resumes. AP refills over time.
- **Target-based attacks**, M&M style: target, range and line-of-sight check, one hit roll, damage roll. No aimed swings or hit volumes for melee. Projectiles (arrows, spells) stay real sim entities so they can be dodged.
- **Area effects hit everything inside them**: the player, allies and bystanders. Harming bystanders has consequences through the normal systems (factions, crime), not special rules.
- **Death has no penalty.** The player wakes at a temple; time passes and the world keeps every change (P1). Which temple (last visited or nearest) is decided when the system is built.
- All rules live in `Realm.Sim`; presentation only animates. Weapons, spells, damage types, resistances, conditions, skills and AP costs are data. Every roll uses `Rng`.

## Consequences
- Easier: one combatant to build, balance and present (one portrait, one inventory, one set of conditions); one timeline and one formula set serve both real time and the pause, so they can't drift apart; no full turn-based mode to keep fair; the pause can be tested headless like the rest of the sim.
- Harder: one character has to carry what a party spread over four (melee, ranged, magic, utility), so skills and build variety matter more; the AP bar and the pause UI are new design work; self-damage from area effects needs clear feedback (an area preview) so it feels fair, not random.
- No death penalty removes one source of tension; difficulty must come from fights themselves and from lost time and world consequences. Revisit if playtests say death feels meaningless.
- Combat is still M5 in the roadmap. Next: an explanation page (`docs/explanation/combat.md`, Planned), then a `Realm.Sim.Combat` spike with tests and a headless `combat-sim` CLI command for balance sweeps.
- CLAUDE.md sections 6.5 and 11 (D3, D4) updated in the same change.
