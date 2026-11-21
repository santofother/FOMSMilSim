# Opening the project in Godot (macOS)

*For you, the user. Agents never open the editor (AGENTS.md); they use `docs/ref/commands.md`.*

## The one rule: start Godot from the terminal
Use **`/Applications/Godot_mono.app`** (Godot 4.7.2 **.NET**). The .NET 8 SDK lives in `~/.dotnet`, and apps
opened from Finder or the Dock can't see it. Godot then starts without C#, can't build the game, and can re-save
`project.godot` without its `"C#"` feature. That's what happened on 2026-11-21. Starting Godot from a terminal
with the two variables below avoids all of that.

## Easiest: double-click the launcher
In Finder, double-click **`tools/open-godot.command`** in the repo folder. It builds the C#, imports the project the
first time, and opens the Godot editor with .NET working. Then press **F5** (Run Project) to play. Your main repo
folder stays on `main` (agents work in their own worktrees), so this always opens the latest landed build.
Pull first to get new work: `git pull`. If macOS blocks the first double-click, right-click it → **Open**.

You can drag the file to the right-hand side of the Dock for one-click launching.

**Running the sandbox from the editor:** Debug → *Customize Run Instances…* → enable *Main Run Args* →
`-- --scenario=s03_sandbox`. Or just press F5 for the default `m0_clash`.

## By hand (what the launcher does)
1. Get the latest code (discard anything the editor changed on its own first):
   ```bash
   cd ~/PersonalProjects/FOMSBattleSimulator && git restore project.godot && git switch main && git pull
   ```
2. Set up the shell (paste once per terminal window):
   ```bash
   export DOTNET_ROOT="$HOME/.dotnet" PATH="$HOME/.dotnet:$PATH" GODOT=/Applications/Godot_mono.app/Contents/MacOS/Godot
   ```
3. Build and import once without the editor. If the import crashes on exit (signal 11), run it again. That's harmless.
   ```bash
   dotnet build && $GODOT --headless --path . --import
   ```
4. Open the editor on the project:
   ```bash
   $GODOT --path . -e
   ```
   You can also use the Project Manager's **Import** button and pick `project.godot`, but only from a Godot you
   started from this terminal.
5. In the editor, press **Build** (hammer, top right) once, then **Run Project** (F5). The main scene is
   `scenes/Battle.tscn`, which opens the `m0_clash` battle.

## Check it worked
- The **MSBuild** panel at the bottom shows a successful build, and the Output panel has no "Unable to find .NET".
- After you close the editor, `git status` shows **no change to `project.godot`**. If it lost `"C#"`, undo it with
  `git restore project.godot` and reopen Godot from the terminal.

## Playing a specific scenario without the editor
```bash
$GODOT --path . -- --scenario=s03_sandbox
```
Scenarios live in `data/scenarios/`. `s03_sandbox` is the order-testing map, where nobody attacks on their own.
`m0_clash` is the default, and both sides advance there. Press R to restart.

## Habits that keep agents and the editor out of each other's way
- Don't commit changes the editor makes by itself (`project.godot`, `.tscn` re-saves) unless you meant them.
- While an agent session is editing scenes, keep those scenes closed in the editor. Saving in the editor would
  overwrite the agent's text edits.
- Each git worktree is its own project folder. Import each one once (step 3) before opening it.

## Optional: make it work from Finder too
Install the .NET 8 SDK system-wide with Microsoft's macOS installer (dotnet.microsoft.com → .NET 8 → macOS
Arm64 installer). It puts `dotnet` where GUI apps can find it, and then double-clicking Godot works.
