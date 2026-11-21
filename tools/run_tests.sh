#!/usr/bin/env bash
# Every card runs this. Build, sim tests, then a headless smoke run of the Battle scene.
# Exits non-zero on any failure. Owner: S01 (head engineer)
set -euo pipefail
cd "$(dirname "$0")/.."

export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}" PATH="$HOME/.dotnet:$PATH" DOTNET_CLI_TELEMETRY_OPTOUT=1
GODOT="${GODOT:-/Applications/Godot_mono.app/Contents/MacOS/Godot}"

echo "[Test] build"
dotnet build FOMSBattleSimulator.csproj -nologo -v q

echo "[Test] sim tests"
dotnet test tests/FOMS.Sim.Tests -nologo -v q

if [ ! -d .godot/imported ] || [ ! -f .godot/global_script_class_cache.cfg ]; then
  echo "[Test] first import"
  # WHY: the first import in a fresh worktree sometimes crashes on exit (signal 11); a second run is harmless.
  "$GODOT" --headless --path . --import >/dev/null 2>&1 || "$GODOT" --headless --path . --import >/dev/null 2>&1 || true
fi

echo "[Test] smoke run Battle.tscn (5 s)"
log="$(mktemp)"
set +e
"$GODOT" --headless --path . res://scenes/Battle.tscn -- --quit-after=5 >"$log" 2>&1
rc=$?
set -e
if [ $rc -ne 0 ] || grep -qE "ERROR|Unhandled exception" "$log" || ! grep -q "\[Sim\] quit-after" "$log"; then
  echo "[Test] smoke run FAILED (exit $rc):"
  cat "$log"
  rm -f "$log"
  exit 1
fi
grep -E "^\[(Sim|View)\]" "$log" || true
rm -f "$log"
echo "[Test] all green"
