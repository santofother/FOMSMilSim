# Commands

Every Godot and dotnet call needs `DOTNET_ROOT`: the .NET 8 SDK lives in `~/.dotnet` and isn't on PATH.

```bash
export DOTNET_ROOT="$HOME/.dotnet" PATH="$HOME/.dotnet:$PATH" DOTNET_CLI_TELEMETRY_OPTOUT=1
export GODOT=/Applications/Godot_mono.app/Contents/MacOS/Godot
```

| What | Command |
|---|---|
| All tests (what every card runs) | `tools/run_tests.sh` |
| Sim tests only (fast, no Godot) | `dotnet test tests/FOMS.Sim.Tests` |
| Build the game assembly | `dotnet build` (repo root) |
| Import (new worktree, or after adding files) | `$GODOT --headless --path . --import` |
| Run the game | `$GODOT --path .` |
| Run a specific scene | `$GODOT --path . res://scenes/<Scene>.tscn` |
| Benchmark (writes `perf_results/*.json`) | `$GODOT --path . res://scenes/PerfTest.tscn -- --benchmark --soldiers=2000 --duration=60` |
| Screenshots at given seconds | add `-- --shots=10,25` (saved to `perf_results/`) |
| Status board | `python3 tools/status.py` (prints to the terminal; `--write` updates `docs/STATUS.md`) |

The first import in a fresh worktree sometimes crashes on exit (signal 11). Run it a second time; it's harmless.

Game CLI args go after `--` and are read with `OS.GetCmdlineUserArgs()`.
