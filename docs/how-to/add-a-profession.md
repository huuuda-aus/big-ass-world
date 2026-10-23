# Add a profession

Goal: give some of the people of a settlement a new trade with its own workplace and daily routine, and check that they follow it.

## Prerequisites
- The repository builds ([getting started](../tutorials/getting-started.md)) and the game runs ([run the game](run-the-game.md)).
- You know what a [profession record](../reference/content/profession.md) holds. If the trade works in a building, that building must be a [service building](add-a-service-building.md).

## Steps
1. Create `content/professions/healer.yaml` (this one works in a new `service.healer` building from the service how-to):
   ```yaml
   id: profession.healer
   name: Healer
   tags: [care]
   workplace: { service: service.healer }
   share: { city: 0.02, town: 0.02 }
   day:
     - { from: "08:00", to: "17:00", do: work }
     - { from: "17:30", to: "19:00", do: about }
   ```
   Quote the times. Leave the filler profession alone: the people nobody else claims stay townspeople.
2. Check the record:
   ```bash
   dotnet run --project src/Realm.Cli -- content-lint
   ```
   You should see `12 profession(s)` and `0 error(s)`; `DATA020` names a bad word and `DATA013` a service that does not exist.
3. Run the game ([rebuild the world](rebuild-the-world.md) is not needed: professions are read from `content/` when a settlement is built):
   ```bash
   <godot> --path src/Realm.Game -- --pos=-7002,-1010 --time=7
   ```
4. Walk up to a city and look at someone: the label at the bottom of the screen says who they are and what they are doing. Find a healer and follow them through the day (press `T` to speed time up).

## Troubleshooting
- **Nobody has the trade**: the shares round down at small counts (2 percent of 40 is under one), or the settlement lacks the service building; raise the share or add the service.
- **The shares add up to more than 1** (`DATA020`): lower some shares.
- **People work but are never seen inside**: they are inside the building; only people outside are drawn.

## Related
- [Characters and crowds](../systems/npcs.md), [ADR 0025](../decisions/0025-professions-and-reactions.md), [add a service building](add-a-service-building.md).
