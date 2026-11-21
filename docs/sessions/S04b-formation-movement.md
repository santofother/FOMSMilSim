---
card: S04b
title: Formation movement (wheeling, facing about, re-forming)
phase: A
agent: Either
preferred: Claude Opus 5.5 · high
alternative: Codex GPT-6 Astra · medium
tier: T1
depends: [S04]
parallel: [S05, S08]
branch: s04b-formation-movement
owns: [src/Sim/Orders/**, src/Sim/Movement/Formation*, src/Sim/Movement/MeleeSpacing.cs, tests/FOMS.Sim.Tests/Formation*]
read: [docs/ARCHITECTURE.md §C4 §C5, docs/playtests/notes.md, handoffs S02 S04]
---
# S04b — Formation movement

## Start this session
- **Preferred:** Claude Opus 5.5 · high
- **Or:** Codex GPT-6 Astra · medium
- Start it in a **new worktree**. Paste:

```text
Read AGENTS.md (repo root) and docs/sessions/S04b-formation-movement.md, plus only the files and handoffs the card lists under read:. Build the card on branch s04b-formation-movement, run tools/run_tests.sh, write docs/handoffs/S04b.md, and land it on main yourself following AGENTS.md step 7. Only stop to ask me if you're blocked.
```

**Goal:** regiments turn and move like drilled infantry, not like a plank spinning on a pin.

**Why (playtest, 2026-11-21):** "when troops rotate they pivot around an axis"; "click a spot in the distance
that isn't directly straight and they move weird"; spearmen feel slow. Today `RegimentOrders.TurnTowards`
rotates the whole block about its centre at `pace / radius` (a 20-wide line takes ~8 s to turn 90°), and
`MoveTo` stops, pivots until within 0.15 rad, walks, then pivots again at the destination. One wing runs
forward while the other walks backwards.

## Steps
1. **Wheel on the march.** A destination off to one side up to ~60° away: the regiment keeps walking and turns
   as it goes (curved path). Limit the turn by the outer flank's speed, but never stop to turn.
2. **Face, don't spin, for big turns.** Turns over ~60°: men turn on the spot and the formation re-forms facing
   the new way. Re-assign slots so each man takes a nearby slot in the new layout (the files become ranks)
   rather than the block rotating. A 180° turn is an **about-face**: the rear rank becomes the front and nobody
   walks more than about a slot. Keep slot assignment deterministic (C3) and compatible with S04's column fill.
3. **Form up at the destination.** On the last stretch, men walk straight to their own slot in the final
   formation (the dragged position and facing). No rigid pivot at the end.
4. Soldiers face the way they walk while moving, and the formation facing once in place (per-soldier yaw).
5. S04 known issue: a regiment that has arrived and is in melee counts as holding, so push and follow-up work
   for regiments that still hold a `Move` order.
6. Keep `Withdraw` (backing away while facing the enemy) and the existing `Move`/`Attack` order API unchanged.

## Tests
`SmallTurnWheelsWithoutStopping` (30° off-axis: the centre moves every tick) ·
`AboutFaceSwapsRanks` (180°: done in < 3 s, no man moves more than 1.5 × slot spacing) ·
`LateralMoveReformsAtDestination` (90° new facing: arrival time ≤ walk time + 4 s) ·
`NoSoldierWalksBackwardsWhileWheeling` · `FormationMovementDeterministic`. All S02/S04 tests stay green.

## Manual check
- Order a line to a point 45° to its left: it curves there without stopping.
- Drag a new facing 90° and 180° from the current one: men face about or re-form, no spinning plank.
- Order a long move across the field and back: it looks like marching, not sliding.

## Not in this card
Pathfinding round obstacles and other regiments (wave B terrain), column / march formations, AI.
