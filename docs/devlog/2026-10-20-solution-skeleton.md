# 2026-10-20 — solution-skeleton

## Goal
M0 Foundations: git repo and a verified solution skeleton.

## What was done
- `git init` on `main`. Remote not set yet.
- Root config files, Godot-free projects with the section 5 reference graph, one test project per library.
- `Rng` (PCG32 + `ForKey`) with tests; CLI `version`/`help`; architecture test.
- Docs skeleton.

## Decisions made
- [0001 Solution skeleton](../decisions/0001-solution-skeleton.md)
- [0002 xUnit](../decisions/0002-test-framework-xunit.md)

## Problems encountered and how they were solved
- No .NET SDK or git-lfs installed; the developer installed them (sudo needs a real terminal).
- Templates repeated `TargetFramework`/`ImplicitUsings`/`Nullable`, overriding `Directory.Build.props`; removed from every csproj.
- First architecture test run failed on a clean tree: `.csproj` references use `\`, which `Path.GetFileNameWithoutExtension` does not split on Linux. Fixed by normalising separators; confirmed the test then passes clean and fails when a forbidden reference is injected.

## Tests added
- `RngTests` (15): pcg32 reference vector, determinism, `ForKey` golden/order/type rules, bounds, coverage.
- `DependencyRuleTests` (13 cases): reference graph and no-Godot rule.

## Docs added/updated
README, CHANGELOG, docs index, getting-started, versions, CLI reference, architecture, Core system doc, glossary, known issues, ADR 0001/0002.

## Open questions / next steps
- Remote URL pending from the developer.
- Next: `Realm.Core` logging facade and stable ID types, then `Realm.Data` content loading (needs YamlDotNet pin).
- D8 (Lua runtime) still open; not needed until M7.
