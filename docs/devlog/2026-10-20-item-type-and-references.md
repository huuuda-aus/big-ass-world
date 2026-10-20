# 2026-10-20 — item-type-and-references

## Goal
M0 Foundations, third slice: a second content type so the loader stops special-casing `materials`, and the first cross-record reference check (CLAUDE.md section 7.4 content lint).

## What was done
- Extracted shared parsing (`ParserSupport`: strict deserialize, ID, name, tags, typed references) and moved `MaterialParser` onto it. Its 28 existing tests stayed green unchanged, which is the evidence the refactor kept behaviour.
- Added the `item` type (placeholder schema) with an optional `material` reference.
- `ContentLoader` now loads through `TypeSpec<T>`; `ContentDatabase` tracks each record's source file.
- `ContentValidator.ValidateReferences` reports dangling references with the referencing file as location. New codes `DATA013`, `DATA014`.
- Sample items and docs.

## Decisions made
- No ADR: the registry is a small internal change inside one system, not hard to reverse. Recorded in the Data system doc and known issues.
- The `item` fields are a deliberately minimal placeholder. Item design stays the developer's decision.

## Problems encountered and how they were solved
- None. Unknown directories are now warned before types load, so warning order changed slightly; no test depended on it.

## Tests added
- `ItemParserTests` (7), four loader tests (items with sources, dangling reference, cross-file reference, item in `materials/`), one shipped-content test. Data total 40; whole solution 108, 0 warnings.

## Docs added/updated
Item reference, validators (DATA013/014), Data system doc, `add-an-item`, `add-a-content-type`, CLI reference and getting-started outputs, glossary, known issues, index, CHANGELOG.

## Open questions / next steps
- Developer design needed before real content: material property vocabulary, item fields.
- Next: CI (GitHub Actions running build, tests, `content-lint`), once a remote exists; or, if CI waits, the `Rng`-backed stable ID helpers and the first Sim type (world time) to start `Realm.Sim`.
