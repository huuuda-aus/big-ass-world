# 0003. YAML content loading: YamlDotNet, strict schemas, validation reports
- Status: accepted
- Date: 2026-10-20

## Context
CLAUDE.md section 3 fixes YAML (via YamlDotNet) for hand-edited content, schema-validated on load. Open points: how strict to be, how errors are surfaced, and which version to pin. Content authors include modders who have never seen the code, so messages must be understandable without knowing internal type names.

## Options considered
1. Lenient loading (ignore unknown fields, throw on first error) — simple, but typos like `hardnes:` silently vanish and authors fix one error per run.
2. Strict deserialisation into a DTO, then explicit validation that collects every problem into a `ValidationReport` with stable codes — more code, but one run shows all problems and CI can assert on codes.
3. External JSON-schema validation — extra dependency and a second schema language to keep in sync with C#.

## Decision
Option 2.
- **YamlDotNet 18.1.0**, the latest stable on NuGet when added. I had no prior knowledge of this version's API, so behaviour was verified by tests (unknown-field rejection with line numbers, `.nan`/`.inf` parsing), not assumed.
- Unknown fields are errors (`DATA001`). The raw YamlDotNet message names an internal DTO type, so the parser rewrites it to `Unknown field 'x'.` and falls back to the raw text if the wording changes.
- Loading never throws on bad content. It returns the valid records plus a `ValidationReport`. Callers decide what fails (the CLI exits 1 on errors).
- Warnings (file name mismatch, unknown directory) do not fail the CLI, but the shipped-content test requires zero issues.
- One record per file for now.

## Consequences
- Easier: modder-friendly messages, all errors at once, CI-checkable codes.
- Harder: each content type needs a parser and validator, not just a DTO.
- Known gaps: errors raised after deserialisation have no line number; the regex depends on YamlDotNet wording. Both are listed in known-issues.
- The loader special-cases `materials`; generalise with a registry when the second type is added.
