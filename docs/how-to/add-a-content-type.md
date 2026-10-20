# How to add a new content type (for developers)

Goal: teach `Realm.Data` a new kind of record, for example `recipe`, so it loads, validates and appears in `content-lint`.

## Prerequisites
- You know how `material` and `item` work: [Data system](../systems/data.md).
- The design of the new type is decided. Do not invent schema fields; ask first (CLAUDE.md section 1).

## Steps

1. Add a definition record in `src/Realm.Data/Content/`, for example `RecipeDefinition`, with `///` docs on every member. Use `ContentId` for its ID and for references.
2. Add a parser, for example `RecipeParser.Parse(yaml, location, report)`, modelled on `ItemParser`. Use the helpers in `ParserSupport` (strict deserialize, `ParseId`, `RequireName`, `ParseTags`, `ParseReference`). Collect every problem into the report; return null if there was an error.
3. In `ContentDatabase`, add a read-only dictionary (for example `Recipes`), a constructor parameter, and include it in `Contains`.
4. In `ContentLoader`, add a `TypeSpec<RecipeDefinition>` (directory name, type prefix, parser, ID getter), add its directory to `KnownDirectories`, and call `LoadType` for it.
5. In `ContentValidator.ValidateReferences`, add a check for each reference field (code `DATA013`).
6. If a new problem needs a new code, add it to `ValidatorCodes` with the next free number and to [validators](../reference/validators.md). Never reuse a code.
7. Update `ContentLintCommand` so the summary counts the new type.
8. Add tests: a parser test class, loader tests for the new directory and for dangling references, and make sure `ShippedContentTests` still passes with sample content in `content/<directory>/`.
9. Write the reference page `docs/reference/content/<type>.md` (fields, rules, a complete example) and a how-to `add-a-<type>.md`. Update [Data system](../systems/data.md), the docs index, the glossary, the CHANGELOG.

## Verify
`dotnet build Realm.sln` shows 0 warnings, `dotnet test Realm.sln` passes, and `content-lint` lists the new type's count.

## Troubleshooting
- `CS1591`: a public member lacks `///` docs.
- Unknown-directory warning (`DATA009`): the directory is missing from `KnownDirectories`.

## Related
- [Add a material](add-a-material.md), [Add an item](add-an-item.md)
