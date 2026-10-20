# 0002. Test framework: xUnit
- Status: accepted
- Date: 2026-10-20

## Context
Open decision D7 (CLAUDE.md section 11): xUnit or NUnit for Core/Data/Sim/Gen/Scripting tests.

## Options considered
1. xUnit — CLAUDE.md default, standard `dotnet new` template, parallel by default.
2. NUnit — also viable, including for Godot-side tests.

## Decision
xUnit. The developer confirmed the default on 2026-10-20.

## Consequences
All test projects use xUnit. Godot-side smoke scenes (M1+) are separate from this decision. Package versions are tracked in [versions](../reference/versions.md).
