---
card: S01
title: Foundation (sim/game split, battle scene, tests)
phase: 0
agent: Claude
preferred: Claude Opus 5.5 · medium
alternative: Codex GPT-6 Astra · low
tier: T2
depends: []
parallel: []
branch: s01-foundation
owns: [src/Sim/**, src/Game/**, tests/**, data/**, scenes/Battle.tscn, FOMSBattleSimulator.csproj, project.godot, tools/run_tests.sh, .gitignore]
read: [docs/ARCHITECTURE.md, docs/ref/commands.md, docs/ref/conventions.md, src/PerfTest/BattleSim.cs, src/PerfTest/PerfTest.cs]
---
# S01 — Foundation

## Start this session
- **Preferred:** Claude Opus 5.5 · medium
- **Or:** Codex GPT-6 Astra · low
- Start it in a **new worktree**. Paste:

```text
Read AGENTS.md (repo root) and docs/sessions/S01-foundation.md, plus only the files and handoffs the card lists under read:. Build the card on branch s01-foundation, run tools/run_tests.sh, write docs/handoffs/S01.md, and land it on main yourself following AGENTS.md step 7. Only stop to ask me if you're blocked.
```

**Goal:** the project structure from ARCHITECTURE.md §2 exists and works end to end, using the M0 armies.

## Steps
1. `src/Sim/FOMS.Sim.csproj` (net8.0 class library). Root Godot csproj: `ProjectReference` to it, and
   `<Compile Remove>` for `src/Sim/**` and `tests/**`.
2. Port the M0 sim (`src/PerfTest/BattleSim.cs`) into `src/Sim/Core/`: `Battle`, `SoldierStore`, `Regiment`
   (all C4b fields, unused ones at defaults), `SpatialGrid`. Make it follow C2 (fixed tick), C3 (seeded hash
   RNG), C4 (contiguous ranges) and C6 (`Events` with `SoldierKilled` and `RegimentRouted` for now).
3. `src/Sim/Core/Orders.cs`: all C5 order records. `Battle.Issue()` queues them and logs `[Orders]`.
   Execution is S02's job; until then regiments keep the M0 "advance and fight" behaviour.
4. `src/Sim/Data/`: minimal `UnitDef` (id, name, model, soldiers, hp, the M0 combat numbers) and `ScenarioDef`
   (seed, sides, regiments with unit id, position, facing) with a `System.Text.Json` loader.
   `Battle.ApplyDefs()` per C7.
5. `src/Sim/Terrain/ITerrain.cs` + `FlatTerrain` (height 0, walkable everywhere).
6. `data/units/gb_spearmen.json` and `data/scenarios/m0_clash.json` reproducing the M0 layout (20 × 100).
7. Godot side: `scenes/Battle.tscn` (new main scene) + `src/Game/Battle/BattleScene.cs` (loads
   `--scenario=<id>`, default `m0_clash`; steps the sim; interpolates; `--quit-after=SEC`).
   `src/Game/View/SoldierRenderer.cs` with the model registry (C9), using the M0 mesh and shader (copy them into
   `src/Game/View/` and `shaders/soldier.gdshader` stays shared). `src/Game/Camera/RtsCamera.cs`.
   **Leave `src/PerfTest/` and `scenes/PerfTest.tscn` untouched and working.**
8. `tests/FOMS.Sim.Tests` (xUnit): determinism (two runs, same seed → identical position hash after 900 ticks;
   different seed → different), contiguous ranges, M0 battle produces a winner within 3 minutes of sim time.
9. `tools/run_tests.sh`: build, `dotnet test`, then a headless smoke run of `Battle.tscn` with `--quit-after=5`
   that fails on any `ERROR` or unhandled exception. Exit non-zero on failure.
10. `.gitignore`: add `perf_results/*.png`, `tests/**/bin`, `tests/**/obj`, `src/Sim/bin`, `src/Sim/obj`.

## Tests
`tools/run_tests.sh` green. M0 benchmark still runs (`docs/ref/commands.md`).

## Manual check
- `$GODOT --path .` opens the Battle scene: two blue/red armies march, clash and rout, like M0.
- The PerfTest scene still works.

## Not in this card
Order execution, selection, new combat rules, UI.
