---
card: S02
title: Orders & data (sim side)
phase: A
agent: Either
preferred: Codex GPT-6.1 Sol · high
alternative: Claude Opus 5.5 · medium
tier: T2
depends: [S01]
parallel: [S03]
branch: s02-orders-sim
owns: [src/Sim/Orders/**, src/Sim/Data/**, data/units/**, data/scenarios/**, tests/FOMS.Sim.Tests/Orders*, tests/FOMS.Sim.Tests/Data*]
read: [docs/ARCHITECTURE.md §C1 §C3 §C4 §C4b §C5 §C7, docs/GameDesign.md §3.1 §3.2, handoff S01]
---
# S02 — Orders & data (sim side)

## Start this session
- **Preferred:** Codex GPT-6.1 Sol · high
- **Or:** Claude Opus 5.5 · medium
- Start it in a **new worktree**. Paste:

```text
Read AGENTS.md (repo root) and docs/sessions/S02-orders-sim.md, plus only the files and handoffs the card lists under read:. Build the card on branch s02-orders-sim, run tools/run_tests.sh, write docs/handoffs/S02.md, and land it on main yourself following AGENTS.md step 7. Only stop to ask me if you're blocked.
```

**Goal:** regiments do exactly what they're ordered, and every unit number comes from JSON.

## Steps
1. `UnitDef`: every stat in GameDesign.md §3.1 (soldiers, melee attack/defence, damage, armour-piercing,
   armour, shield, charge bonus, mass, speed, run speed, morale, stamina, ammo, range, accuracy, traits[]).
   Validation with clear errors (`[Data] gb_x.json: missing melee_attack`).
2. Grey-box units in `data/units/`: `gb_spearmen`, `gb_swordsmen`, `gb_pikemen`, `gb_archers`,
   `gb_light_cav`, `gb_heavy_cav`, `gb_general`. Plausible numbers, all marked as placeholders. Cavalry and
   archers only need to *exist* as data; their behaviour comes in S06/S07.
3. Order execution in `src/Sim/Orders/`, as a per-regiment state machine filling `Regiment.Activity`:
   - **Move**: form at the target with the given width (width → columns; rows follow) and facing. Regiments turn and
     wheel rather than teleport their slots; walk or run.
   - **Attack**: move to contact with the target regiment and engage; follow it if it moves.
   - **Halt**: stop and hold the current formation. **Withdraw**: back away, facing the enemy, at walk speed.
   - **SetFormation**: `Line` and `Loose` (looser spacing). Other formations come with their systems.
   - Regiments with no orders stand still. The M0 "auto-advance" becomes `data/scenarios` behaviour only
     (`"auto_attack": true` per side) so the m0_clash scenario still plays itself.
4. Straight-line movement on `ITerrain` (pathfinding later). `// TODO(B-terrain)`.

## Tests
Move order reaches its target within tolerance with the right width and facing; attack order makes contact;
halt holds position; bad JSON gives a readable error; determinism test still green.

## Manual check
- Run m0_clash: still plays itself.
- (With S03 landed) select a regiment, move it, turn it, widen it, attack with it.

## Not in this card
Input/UI (S03), melee rules (S04), morale (S05).
