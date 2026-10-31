# 2026-10-31 — combat design

## Goal
Settle the combat model (open decisions D3 and D4) before any M5 work, using *Might and Magic VI–VIII* as the reference.

## What was done
- Walked through how M&M 6–8 combat works (recovery time, real-time/turn-based switch, typed damage, conditions, masteries, monster tiers, real projectiles) and its known weaknesses (click-fest, free approach in turn-based mode, kiting and flying exploits).
- Recorded the developer's decisions in ADR 0029: one character; real-time paced by action speed; a VATS-style tactical pause with action points instead of a full turn-based mode; target-based attacks; area effects hurt everyone inside, the player included; death wakes the player at a temple with no penalty.
- Wrote `docs/explanation/combat.md` (marked Planned) and updated CLAUDE.md sections 6.5 and 11.

## Decisions made (link ADRs)
- [ADR 0029](../decisions/0029-combat-model-solo-character-hybrid-pause.md). The single character is "most likely"; it may be reopened only before M5 combat work starts.

## Problems encountered and how they were solved
- "A turn-based toggle like modern Fallout" could mean VATS (a pause with action points) or Fallout 1–2's full turn-based mode. Asked; the answer was VATS.

## Tests added
- None (design only).

## Docs added/updated
- ADR 0029, `docs/explanation/combat.md`, CLAUDE.md, glossary (action points, ready time / recovery, tactical pause), CHANGELOG, `docs/index.md`.

## Open questions / next steps
- Which temple the player wakes at (last visited or nearest).
- AP pool size and refill rate, recovery times, the hit-chance curve: tuning data, to be set by the `combat-sim` balance sweeps.
- Next: a `Realm.Sim.Combat` spike (timeline with ready times, hit and damage, two conditions, the pause queue) with xUnit tests and a headless `combat-sim` CLI command. Officially M5.
