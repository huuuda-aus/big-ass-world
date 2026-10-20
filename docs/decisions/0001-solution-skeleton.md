# 0001. Solution skeleton and build conventions
- Status: accepted
- Date: 2026-10-20

## Context
M0 needs a buildable skeleton that makes the CLAUDE.md section 5 dependency rules real, before any gameplay code exists. Godot is not installed and not needed for the non-Godot projects.

## Options considered
1. Create all projects including `Realm.Game`/`Realm.Editor` now — needs Godot, cannot be built or verified yet.
2. Create only the Godot-free projects now, add Game/Editor at M1 — everything verifiable today.
3. Rely on code review for dependency rules — cheap, but rules erode silently.

## Decision
Option 2, plus an architecture test (not review) for the rules.
- Target `net8.0`, C# 12. **Assumption:** this matches the Godot .NET release chosen at M1. Verify against Godot docs then; change via a new ADR if not.
- Settings live in the root `Directory.Build.props`. `src/Directory.Build.props` adds `TreatWarningsAsErrors` and XML doc generation, so a missing doc comment (CS1591) fails the build.
- `ImplicitUsings` disabled; usings are explicit.
- Dependency rules are enforced by `tests/Realm.Architecture.Tests`, which parses `.csproj` files. It was verified to fail when a forbidden reference is injected.
- Git LFS rules for `baked/` and `assets/` are set in `.gitattributes` now; `git lfs install` is run per clone.

## Consequences
- Easier: rule breaks are caught by `dotnet test`; XML docs are enforced by the compiler.
- Harder: every public member needs docs from day one; Cli is held to the same strictness.
- To do at M1: add Game/Editor, extend the architecture test to them, pin Godot and Terrain3D in versions.md.
