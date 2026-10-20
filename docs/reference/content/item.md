# Content type: `item`

**Placeholder schema.** Only identity, tags and an optional material exist so that content references can be exercised. Real item design (stats, slots, stacking, value) has not been decided and will add fields.

- Directory: `content/items/`
- One record per file: `content/items/<name>.yaml`, where `<name>` is the ID without the `item.` prefix.
- C# type: `Realm.Data.Content.ItemDefinition`

## Fields

| Field | Type | Required | Rules |
|---|---|---|---|
| `id` | string | yes | Content ID starting with `item.`. Unique across all content. |
| `name` | string | yes | Display name. Not empty. |
| `tags` | list of strings | no | Same grammar as [material tags](material.md). |
| `material` | string | no | ID of a `material` record the item is made of. Must start with `material.` (`DATA014` otherwise) and must exist (`DATA013` otherwise). |

Unknown fields are errors.

## Complete example

`content/items/oak_log.yaml`:

```yaml
id: item.oak_log
name: Oak log
tags: [raw_material]
material: material.oak_wood
```

If `material.oak_wood` did not exist, `content-lint` would print:

```
Error DATA013 items/oak_log.yaml: Item 'item.oak_log' references material 'material.oak_wood', which does not exist. (fix: Fix the ID or add the material.)
```

## Validation

Codes `DATA001` to `DATA014`: see [validators](../validators.md).
