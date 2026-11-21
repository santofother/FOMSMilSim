# Milestone 0 — Performance Test Results

*2026-11-21 · Godot 4.7.2 (.NET) · Apple M1 Pro (10 CPU threads) · Metal Forward+*

**Verdict: PASS.** The goal was 2,000 animated soldiers in 20 regiments at 60 fps. Godot ran it at **~205 fps**, and still held **~60 fps at 10,000 soldiers** with no optimisation work yet. Recommendation: commit to Godot 4 + C#.

## Results (90 s per run: march → clash → routs, 5 s warm-up)

| Soldiers | Avg FPS | 1% low FPS | Worst frame | Sim ms / tick (30 Hz) | Upload ms / frame |
|---:|---:|---:|---:|---:|---:|
| 2,000 | **204.8** | 140.9 | 18.1 ms | 0.77 | 0.25 |
| 5,000 | **108.2** | 80.7 | 36.9 ms | 1.43 | 0.46 |
| 10,000 | **61.0** | 49.8 | 72.2 ms | 2.55 | 0.83 |

Raw JSON is in `perf_results/`.

### What the numbers say
- **Rendering is the bottleneck, not the simulation.** Even at 10k soldiers the simulation costs about 2.5 ms per tick at 30 ticks a second, roughly 4% of a frame. The rest is the GPU drawing ~1,000-triangle soldiers about 5 times each (the main pass plus 4 shadow cascades). That's about 10M triangles a frame at 2k soldiers.
- **That means the headroom is large and well understood.** Standard fixes not yet applied: mesh LODs (distant soldiers at 100–200 triangles), fewer shadow cascades or a shorter shadow distance for soldiers, and billboard impostors for very distant regiments. Total War itself relies on all of these.
- **Occasional hitches** (worst frames of 18–72 ms, sim spikes up to 39 ms) most likely come from the battle restarting (large array allocations) and thread-pool warm-up. Fix later with pooled or reused buffers.

## What the test covers
- 20 regiments (10 a side, two lines of 5) marching in formation, wheeling toward the nearest enemy, charging, fighting man-to-man, taking casualties, and **routing at 45% strength**. Battles end and restart automatically.
- Per-soldier simulation: spatial-hash neighbour search, separation and pushing, target selection, attack cooldowns, hit and damage rolls. Multithreaded with `Parallel.For`, in plain C# that doesn't depend on the engine (`src/PerfTest/BattleSim.cs`).
- Rendering: **one GPU-instanced MultiMesh** for all soldiers, with dynamic shadows from a directional sun and interpolation between simulation ticks.
- Placeholder spearman (helmet, mail, shield in team colour, spear), ~1,014 triangles, animated in the vertex shader: walk cycle, spear thrust, death fall.

## What it does NOT cover (be honest about these)
- **Real animation.** Limbs are animated procedurally in the shader. Real models would use vertex animation textures (VAT) baked in Blender. That adds one or two texture reads per vertex, which is cheap, but the art pipeline is still unproven.
- No terrain, buildings, walls, vegetation, UI, sound, projectiles, cavalry or AI. Each will cost some frame time; the 2k-soldier result has more than 3× headroom for them.
- Only one machine tested. The window was 1600×900 at 2× Retina scale, so about 3200×1800 pixels rendered.

## How to run it
Build once, then launch.

```bash
cd /Users/santofothergill/PersonalProjects/FOMSBattleSimulator && DOTNET_ROOT=$HOME/.dotnet $HOME/.dotnet/dotnet build
```

Interactive mode (keys `1`/`2`/`3`/`4` switch to 1k/2k/5k/10k soldiers, `R` restarts, `Space` pauses, WASD/QE/mouse wheel/right-drag move the camera):

```bash
cd /Users/santofothergill/PersonalProjects/FOMSBattleSimulator && DOTNET_ROOT=$HOME/.dotnet /Applications/Godot_mono.app/Contents/MacOS/Godot --path .
```

Benchmark mode (writes a JSON file to `perf_results/` and quits):

```bash
cd /Users/santofothergill/PersonalProjects/FOMSBattleSimulator && DOTNET_ROOT=$HOME/.dotnet /Applications/Godot_mono.app/Contents/MacOS/Godot --path . -- --benchmark --soldiers=2000 --duration=90
```

Other flags: `--regiments=N`, `--warmup=S`, `--camera=close`, `--shots=10,25,40` (saves screenshots at those seconds).

You can also open the folder in the Godot editor (`/Applications/Godot_mono.app`) and press Play.
