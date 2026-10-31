# 2026-11-01 — combat content

## Goal
Make attacks and creatures data (CLAUDE.md sections 4.3 and 6.5), so combat can be tuned and extended in YAML instead of code.

## What was done
- Three content types in `Realm.Data.Content`: `condition` (`ConditionDefinition`, `ConditionParser`), `attack` (`AttackDefinition`, `AttackParser`) and `creature` (`CreatureDefinition` with resolved `CreatureTier`s, `CreatureParser`), plus `CombatVocabulary`, the internal `CombatParserSupport` and four validator codes (`DATA021`–`DATA024`). Registered in `ContentLoader`, `ContentDatabase`, `ContentValidator` and `content-lint`.
- Shipped content: `creature.goblin` (Goblin, Goblin Archer, Goblin Chief), `attack.longsword`, `attack.fireball`, `attack.goblin_club`, `attack.goblin_bow`, `attack.goblin_chief_axe`, `condition.poisoned`, `condition.asleep`.
- `Realm.Sim.Combat.CombatContent` converts content into runtime attacks, conditions and creature combatants; `CombatBrains` maps behaviour words to brains.
- `Dice` moved to `Realm.Core.Random` and gained `TryParse`; the runtime `AttackDefinition`/`ConditionDefinition` became `Attack`/`Condition`.
- `combat-sim` loads content (`--content dir`), and a new `goblin-chief` scenario uses tier 3. `duel` and `goblin-ambush` give exactly the same numbers as the code-defined spike, which checks the seconds-to-ticks conversion end to end.

## Decisions made (link ADRs)
No new ADR; these follow CLAUDE.md section 6.5 and ADR 0029:
- **One record per creature family, tiers inside it.** Tier 1 is the family's own values. A tier overrides `health`, `attack_rating`, `defence`; its `attacks` replace the list; `resistances` merge per type; `tags` are added. Tiers derive from the family, never from the tier before, so reordering or removing a tier doesn't change the others.
- **Conditions are their own content type**, because attacks refer to them and the same condition (poison) is shared by many attacks.
- **Times in seconds** in YAML (`recovery_s`, `duration_s`, `every_s`), converted to whole ticks at load, rounding half away from zero, at least one tick.
- **Damage types stay open words.** Instead of a fixed list, warning `DATA024` flags a resistance to a type that nothing deals, which catches typos without deciding the list of damage types now.
- **Dice as text** (`1d8+2`), the form players and modders know.
- **The hero stays in code**: the player character is not a creature. Its attacks are content.
- `Dice` belongs in Core: both Data (parsing) and Sim (rolling) need it, and Data cannot reference Sim.

## Problems encountered and how they were solved
- Name clash: content records follow the `XDefinition` convention, which the spike had already used for its runtime types. Renamed the runtime types to `Attack` and `Condition`.
- `ContentLoaderTests.UnknownDirectory_IsAWarning_AndIgnored` used `creatures/` as its example of an unknown directory; it now uses `recipes/`. Same check, a directory that is still unknown.
- Walked through the new how-to on a copy of the content (wolf family, bleeding condition, a temporary `wolf-pack` scenario): it works as written, including the expected `DATA024` warning for the `cold` resistance.

## Tests added
- `Realm.Core.Tests/DiceTests` (parsing, round trip; the dice roll tests moved here from Sim).
- `Realm.Data.Tests/CombatContentParserTests` (all three parsers, every error code), three loader tests (linking, dangling references, `DATA024`) and a shipped-content test for the goblin family.
- `Realm.Sim.Tests/Combat/CombatContentTests` (tick conversion, attack and condition mapping, tiers to combatants, behaviour words, shipped creatures fighting).

## Docs added/updated
New: `docs/reference/content/condition.md`, `attack.md`, `creature.md`, `docs/how-to/add-a-creature.md`. Updated: `docs/reference/validators.md`, `docs/reference/cli.md` (combat-sim `--content`, current content-lint output), `docs/systems/data.md` (also brought up to date with foliage and profession), `docs/systems/combat.md`, `docs/how-to/run-a-combat-balance-sweep.md`, `docs/explanation/combat.md`, glossary (creature family, tier), CHANGELOG, `docs/index.md`.

## Open questions / next steps
- Creature looks, loot, spawn and biome affinity and movement speed are not in the schema yet; they come with presentation (D5), M5 loot and M6 spawning.
- `combat-sim` scenarios could become data too (a `scenario` file listing creatures, tiers and positions).
- Next for combat: movement, so melee creatures close in and fights stop depending on hand-placed positions.
