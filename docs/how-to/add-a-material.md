# How to add a material

Goal: add a new material to the shipped content and confirm it loads.

## Prerequisites
- The solution builds ([getting started](../tutorials/getting-started.md)).
- You know the [material schema](../reference/content/material.md).

## Steps

1. Choose an ID, for example `material.tin_ore`. Lowercase letters, digits and underscores only.
2. Create `content/materials/tin_ore.yaml`. The file name is the ID without the `material.` prefix.
   ```yaml
   id: material.tin_ore
   name: Tin ore
   tags: [metal, ore]
   properties:
     hardness: 2
     weight: 7.3
   ```
3. Lint the content:
   ```bash
   dotnet run --project src/Realm.Cli -- content-lint
   ```
4. You should see `0 error(s), 0 warning(s)` and the material count go up by one.
5. Run `dotnet test Realm.sln`. The shipped-content test fails on any issue, including warnings.

## Troubleshooting

- `Unknown field 'x'` (DATA001): a misspelled field name. Only `id`, `name`, `tags`, `properties` exist.
- `DATA005` duplicate ID: another file already uses that ID.
- `DATA008` warning: the file name does not match the ID. Rename the file.
- Full list: [validators](../reference/validators.md).

## Related
- [CLI reference](../reference/cli.md)
- [Data system](../systems/data.md)
