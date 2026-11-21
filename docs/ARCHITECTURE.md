# Architecture

*Head engineer's document. Builders read only the sections their card names. v1 · 2026-11-21*

## 1. The big idea: simulation and game are separate

```
 data/*.json ──► FOMS.Sim (pure C#, no Godot) ◄── tests/FOMS.Sim.Tests (xUnit, headless, seconds)
                    ▲ Issue(Order)      │ read-only state + Events
                    │                   ▼
 FOMS.Game (Godot C#): input, camera, rendering, UI, audio, scenes
```

- **`FOMS.Sim`** is the whole battle: soldiers, regiments, orders, movement, combat, morale, terrain queries and
  the AI. It has no idea Godot exists. That makes it testable in milliseconds, deterministic, and portable.
- **`FOMS.Game`** turns player input into `Order`s, steps the sim, and draws or plays whatever the sim's state
  and events say.
- The **AI is part of the sim** (`FOMS.Sim.AI`) and uses the same `Order`s as the player. So AI vs AI battles run
  headless in tests.

## 2. Projects and folders (ownership in brackets)

| Path | What | Owner |
|---|---|---|
| `src/Sim/FOMS.Sim.csproj` | Class library, net8.0, no Godot reference | S01 |
| `src/Sim/Core/` | `Battle` (tick loop, RNG, events), `SoldierStore` (struct-of-arrays), `Regiment`, `SpatialGrid` | S01 |
| `src/Sim/Data/` | `UnitDef`, `ScenarioDef`, JSON loader (S01 skeleton, S02 completes) | S01, S02 |
| `src/Sim/Orders/` | Order execution, formation slots (the `Order` types themselves are in Core, S01) | S02 |
| `src/Sim/Movement/` | Steering, separation, melee spacing (S04); cavalry movement (S06, its own files) | S04, S06 |
| `src/Sim/Combat/` | Melee (S03), charges (S05), ranged (S06) — one file each | S03/S05/S06 |
| `src/Sim/Morale/` | Morale, routing, rallying, generals | S04 |
| `src/Sim/Terrain/` | `ITerrain` + `FlatTerrain` (S01); heightmap and water (wave B) | S01, B-cards |
| `src/Sim/AI/` | Battle AI | wave B |
| `FOMSBattleSimulator.csproj` (root) | Godot project; references `FOMS.Sim`; excludes `src/Sim/**` and `tests/**` | S01 |
| `src/Game/Battle/` | `BattleScene`: loads a scenario, ticks the sim, interpolation | S01 |
| `src/Game/View/` | `SoldierRenderer` (MultiMesh per model), projectiles, markers | S01, then cards |
| `src/Game/Camera/` | `RtsCamera` | S01 |
| `src/Game/Input/` | Selection, drag-to-form, order hotkeys | S02 |
| `src/Game/UI/` | HUD, unit cards (S02); tuning panel (S07) | S02, S07 |
| `src/PerfTest/` | M0 benchmark scene. Keep working; don't extend | — |
| `shaders/`, `assets/` | Shaders, models, textures, audio | per card |
| `data/units/`, `data/factions/`, `data/scenarios/` | All balance numbers | per card |
| `tests/FOMS.Sim.Tests/` | One test file per sim module | the module's card |
| `tools/` | `run_tests.sh`, `status.py` | S01 (head engineer) |

**Shared file rule:** `data/units/*.json` is shared. Any card may **add** fields its system needs, but it
doesn't change values another card set (tuning belongs to the user and triage).

## 3. Contracts (change only through the head engineer)

**C1 — Sim boundary.** `FOMS.Sim` never references Godot. `FOMS.Game` reads sim state read-only and changes it
**only** through `Battle.Issue(Order)` (and `Battle.ApplyDefs` for tuning, C7).

**C2 — Fixed tick.** `Battle.Step()` advances exactly `Battle.TickDt = 1/30 s`. The game runs 0–4 ticks per
frame and interpolates between `PrevPos` and `Pos` with `alpha = accumulator / TickDt`.

**C3 — Determinism.** Same scenario + same seed + same orders on the same ticks = the same battle, which makes
replays and balance tests possible.
- Randomness only from `Battle.Rand(soldier, salt)`, a hash of (seed, tick, soldier, salt). Never
  `System.Random`, never time.
- A `Parallel.For` phase writes only to its own soldier's slots. Effects on others (damage) are applied in a
  sequential phase afterwards, or with order-independent integer `Interlocked.Add`.
- No logic depends on `Dictionary` or `HashSet` iteration order.

**C4 — Soldiers and regiments.** Soldier data is struct-of-arrays in `SoldierStore` (`Pos`, `PrevPos`, `Vel`,
`Yaw`, `Hp`, `State`, `StateTime`, `RegimentId`, …). Each regiment owns one **contiguous index range**
`[First, First + Count)`. Arrays only grow between ticks (reinforcements append a new range). Dead soldiers stay
in place (state `Dead`); soldiers who leave the map become `Gone`.

**C5 — Orders.** `abstract record Order(int RegimentId)`; first set:
`Move(Vector2 frontCentre, Vector2 facing, float width, bool run)`, `Attack(int targetRegiment, bool run)`,
`Halt`, `SetFormation(FormationKind)`, `SetFireAtWill(bool)`, `Withdraw`, `UseAbility(string ability, int target)`
(target −1 = none). The types live in `src/Sim/Core/Orders.cs` (S01). Orders are queued and applied at the start of
the next tick. The player and the AI use the same types.

**C4b — Regiment read model.** `Regiment` exposes the fields UI and AI read, all defined by S01 (other cards fill
them in): `Id, Team, UnitId, Count, Alive, Centre, Facing, Width, Order (current), Activity` (Idle, Moving,
Engaging, Fighting, Routing, Rallying, Shattered), `Morale` (0–100), `MoraleState`, `Fatigue` (0–1), `Ammo`.

**C6 — Events.** After each `Step()`, `Battle.Events` holds that tick's events as a list of structs
(`SoldierKilled`, `ChargeImpact`, `ProjectileFired`, `RegimentRouted`, `RegimentRallied`, …). It's cleared at the
start of the next tick. View, audio and UI read it; there are no C# `event`s inside the sim.

**C7 — Data-driven stats.** Every unit number comes from `data/units/<id>.json` → `UnitDef`. Field names follow
the stat table in GameDesign.md §3.1. `Battle.ApplyDefs(defs)` updates live regiments' stats (for hot reload);
it doesn't change head counts.

**C8 — Space and units.** Metres, seconds, radians. Ground position is `Vector2(x, z)`. Yaw rotates a model
facing −Z around +Y: `yaw = atan2(-dir.x, -dir.y)`.

**C9 — Rendering.** One `MultiMesh` per soldier model type (`UnitDef.model`, e.g. `"infantry"`,
`"cavalry"`). `SoldierRenderer` keeps a registry: a card adds a model by registering a mesh builder under a
new name, without editing the renderer. `INSTANCE_CUSTOM` = (anim phase, state,
state time, team + variant). Animation moves to vertex-animation textures in wave B without changing this
layout.

## 4. Performance budget (M1 Pro, 1600×900 @2×)
Target **60 fps with 4,000 soldiers** on screen in a full battle (terrain, UI, projectiles). Sim ≤ 4 ms per
tick at 4,000 soldiers. M0 measured ~205 fps at 2,000 soldiers with no LODs (`docs/PerfTest-M0.md`). Any
card that drops the benchmark more than 15% says so in its handoff.
