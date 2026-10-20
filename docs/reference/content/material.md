# Content type: `material`

A crafting material: tags plus numeric properties. Recipes (Planned, M4) will select materials by these.

- Directory: `content/materials/`
- One record per file: `content/materials/<name>.yaml`, where `<name>` is the ID without the `material.` prefix.
- C# type: `Realm.Data.Content.MaterialDefinition`

## Fields

| Field | Type | Required | Rules |
|---|---|---|---|
| `id` | string | yes | Content ID starting with `material.`. Lowercase letters, digits and underscores per segment; a segment starts with a letter. Unique across all content. |
| `name` | string | yes | Display name. Not empty. |
| `tags` | list of strings | no | Each tag follows the same segment grammar and has no dots, for example `metal`. Duplicates collapse. Order in the file does not matter. |
| `properties` | map of string to number | no | Keys follow the tag grammar. Values must be finite numbers (no `.nan`, `.inf`). |

Unknown fields are errors, so typos are caught instead of silently ignored.

## Complete example

`content/materials/iron_ore.yaml`:

```yaml
id: material.iron_ore
name: Iron ore
tags: [metal, ore]
properties:
  hardness: 4
  weight: 7.8
  conductivity: 0.2
```

Linting it:

```
$ dotnet run --project src/Realm.Cli -- content-lint
[Info] Data.Loader: Loaded 3 material(s), 3 item(s) from 'content'.
3 material(s), 3 item(s), 0 error(s), 0 warning(s).
```

## Notes

- The values in the shipped samples (`iron_ore`, `copper_ore`, `oak_wood`) are **placeholders, not balanced game design**.
- The property names (`hardness`, `weight`, `conductivity`) are free-form today. A fixed vocabulary is **Planned** and needs a design decision.

## Validation

Codes `DATA001` to `DATA014`: see [validators](../validators.md).
