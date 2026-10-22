#!/usr/bin/env bash
# Regenerates the world, bakes it (tiles, settlements, ground grids, roads, map.png) and restarts the game.
#
# Usage: tools/rebuild-world.sh [--seed N] [--out DIR] [--no-run] [--bake-args "..."] [-- game args]
#   --seed N         world seed (default: the CLI default, 1)
#   --out DIR        baked directory (default: baked/terrain)
#   --no-run         bake only; do not (re)start the game
#   --bake-args STR  extra arguments for `realm bake-terrain`, e.g. "--cities 4 --towns 8"
#   -- ...           everything after -- goes to the game, e.g. -- --fly --pos=2877,-3950
# The Godot binary comes from $GODOT, else ~/tools/godot-mono-4.5.1/... (see docs/how-to/set-up-godot.md).
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
godot="${GODOT:-$HOME/tools/godot-mono-4.5.1/Godot_v4.5.1-stable_mono_linux_x86_64/Godot_v4.5.1-stable_mono_linux.x86_64}"
seed=""; out=""; run=1; bake_args=""; game_args=()

while [[ $# -gt 0 ]]; do
  case "$1" in
    --seed) seed="$2"; shift 2 ;;
    --out) out="$2"; shift 2 ;;
    --no-run) run=0; shift ;;
    --bake-args) bake_args="$2"; shift 2 ;;
    --) shift; game_args=("$@"); break ;;
    *) echo "Unknown argument '$1'. See the header of $0." >&2; exit 2 ;;
  esac
done

cd "$root"
bake=(dotnet run --project src/Realm.Cli -c Release -- bake-terrain)
[[ -n "$seed" ]] && bake+=(--seed "$seed")
[[ -n "$out" ]] && bake+=(--out "$out")
# shellcheck disable=SC2206
[[ -n "$bake_args" ]] && bake+=($bake_args)

echo "== content lint"
dotnet run --project src/Realm.Cli -c Release -- content-lint

echo "== generate and bake"
"${bake[@]}"   # exits non-zero on validator errors, which stops the script

if [[ $run -eq 0 ]]; then echo "== done (not restarting the game)"; exit 0; fi

echo "== stop the running game"
# Anchored to the start of the command line so it matches only the Godot binary, never this script or its parents.
if pkill -f "^[^ ]*Godot_v[^ ]* .*--path src/Realm.Game" 2>/dev/null; then sleep 1; echo "stopped"; else echo "none running"; fi

echo "== build the game"
dotnet build src/Realm.Game/Realm.Game.csproj -v q

[[ -x "$godot" ]] || { echo "Godot not found at $godot. Set GODOT=/path/to/godot." >&2; exit 1; }
echo "== start the game"
extra=(); [[ -n "$out" ]] && extra+=(--baked="$out")
exec "$godot" --path src/Realm.Game -- "${extra[@]}" "${game_args[@]}"
