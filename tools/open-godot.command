#!/bin/zsh
# Double-click in Finder (or run from a terminal): opens this project in the Godot .NET editor with the .NET SDK
# visible, so C# builds and project.godot keeps its "C#" feature. Then press F5 in Godot to play.
# See docs/workflow/GODOT.md.
cd "${0:A:h}/.." || exit 1
export DOTNET_ROOT="$HOME/.dotnet" PATH="$HOME/.dotnet:$PATH" DOTNET_CLI_TELEMETRY_OPTOUT=1
GODOT=/Applications/Godot_mono.app/Contents/MacOS/Godot

echo "FOMS: building C# ($(git branch --show-current 2>/dev/null))..."
if ! dotnet build -nologo -v q; then
  echo "Build failed. Press Return to close."; read; exit 1
fi
if [ ! -d .godot ]; then
  echo "FOMS: first import (a crash on exit here is harmless)..."
  "$GODOT" --headless --path . --import >/dev/null 2>&1 || "$GODOT" --headless --path . --import >/dev/null 2>&1
fi
echo "FOMS: opening Godot. You can close this window."
nohup "$GODOT" --path . -e >/dev/null 2>&1 &
disown
