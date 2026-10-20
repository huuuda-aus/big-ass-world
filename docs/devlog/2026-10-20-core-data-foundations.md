# 2026-10-20 — core-data-foundations

## Goal
M0 Foundations, second slice: shared primitives in `Realm.Core` and YAML content loading with validation in `Realm.Data`, usable from the CLI.

## What was done
- Core: `ContentId`, `TagSet`, validation report types, logging facade.
- Data: pinned YamlDotNet 18.1.0; `material` content type; strict parser; directory loader; `ContentDatabase`; 12 validator codes.
- CLI: `content-lint [dir]`; log sink wired to stderr.
- Three sample materials with placeholder values.
- Docs: material schema, validators, how-to, Data system doc, Core system doc rewrite, CLI reference, ADR 0003.

## Decisions made
- [0003 YAML content loading](../decisions/0003-yaml-content-loading.md): strict schemas, collect-all-errors reports, stable codes.
- ID grammar: lowercase snake segments, two or more, dot-separated. No open decision (D1-D9) was touched.

## Problems encountered and how they were solved
- YamlDotNet 18.1.0 is newer than my prior knowledge, so its behaviour was verified by tests rather than assumed.
- Unknown-field errors named the internal DTO type (`MaterialParser+MaterialDto`). The parser now rewrites them to `Unknown field 'x'.`, with a test; fallback to the raw message is kept.

## Tests added
- Core (+40, total 55): `ContentIdTests`, `TagSetTests`, `ValidationReportTests`, `LoggingTests`.
- Data (28): `MaterialParserTests`, `ContentLoaderTests` (temp directories), `ShippedContentTests` (lints the real `content/`).
- Whole solution: 96 tests, 0 warnings.

## Docs added/updated
See the list under "What was done". Index, glossary, versions, known issues, getting-started and CHANGELOG updated.

## Open questions / next steps
- Material property vocabulary and real values are a design decision for the developer; samples are placeholders.
- Next: generalise `ContentLoader` into a type registry when the second content type arrives; then the CLI/CI wiring (GitHub Actions) so build, tests and `content-lint` run on every push.
- Remote URL still pending; nothing is committed yet.
