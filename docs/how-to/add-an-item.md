# How to add an item

Goal: add an item, optionally linked to a material, and confirm the reference resolves.

## Prerequisites
- The solution builds ([getting started](../tutorials/getting-started.md)).
- You know the [item schema](../reference/content/item.md). It is a placeholder; expect it to grow.

## Steps

1. Choose an ID, for example `item.iron_nugget`.
2. If it should be made of a material, make sure that material exists (see [add a material](add-a-material.md)).
3. Create `content/items/iron_nugget.yaml`:
   ```yaml
   id: item.iron_nugget
   name: Iron nugget
   tags: [raw_material]
   material: material.iron_ore
   ```
4. Lint:
   ```bash
   dotnet run --project src/Realm.Cli -- content-lint
   ```
5. You should see the item count go up by one and `0 error(s), 0 warning(s)`.

## Troubleshooting

- `DATA013`: the `material` ID does not exist. Check spelling or add the material.
- `DATA014`: the `material` field points at something that is not a material.
- `Unknown field 'x'` (DATA001): only `id`, `name`, `tags`, `material` exist.
- Full list: [validators](../reference/validators.md).

## Related
- [Add a content type](add-a-content-type.md)
