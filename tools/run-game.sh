#!/usr/bin/env bash
# Builds (unless skipped) and starts the game against the already-baked terrain.
# For regenerating the world first, use tools/rebuild-world.sh instead.
#
# Usage: tools/run-game.sh [--no-build] [-- game args]
#   --no-build   skip `dotnet build` (use the last build as-is)
#   -- ...       everything after -- goes to the game, e.g. -- --fly --pos=2877,-3950
# The Godot binary comes from $GODOT, else ~/tools/godot-mono-4.5.1/... (see docs/how-to/set-up-godot.md).
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
godot="${GODOT:-$HOME/tools/godot-mono-4.5.1/Godot_v4.5.1-stable_mono_linux_x86_64/Godot_v4.5.1-stable_mono_linux.x86_64}"
build=1; game_args=()

while [[ $# -gt 0 ]]; do
  case "$1" in
    --no-build) build=0; shift ;;
    --) shift; game_args=("$@"); break ;;
    *) echo "Unknown argument '$1'. See the header of $0." >&2; exit 2 ;;
  esac
done

cd "$root"

echo "== stop the running game"
# Anchored to the start of the command line so it matches only the Godot binary, never this script or its parents.
if pkill -f "^[^ ]*Godot_v[^ ]* .*--path src/Realm.Game" 2>/dev/null; then sleep 1; echo "stopped"; else echo "none running"; fi

if [[ $build -eq 1 ]]; then
  echo "== build the game"
  dotnet build src/Realm.Game/Realm.Game.csproj -v q
fi

[[ -x "$godot" ]] || { echo "Godot not found at $godot. Set GODOT=/path/to/godot." >&2; exit 1; }
echo "== start the game"
exec "$godot" --path src/Realm.Game -- "${game_args[@]}"
