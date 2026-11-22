---
card: S04c
title: Regiment contact (lines meet square, no walking through, hold shape)
phase: A
agent: Claude
preferred: Claude Opus 5.5 · high
alternative: Codex GPT-6 Astra · medium
tier: T1
depends: [S04b]
parallel: [S05, S08]
branch: s04c-regiment-contact
owns: [src/Sim/Orders/**, src/Sim/Movement/**, src/Sim/Core/Battle.cs (UpdateSoldier melee flag only), tests/FOMS.Sim.Tests/Contact*, tests/FOMS.Sim.Tests/Formation*]
read: [docs/ARCHITECTURE.md §C4 §C5, docs/playtests/notes.md, handoffs S04 S04b]
---
# S04c — Regiment contact

## Start this session
- **Preferred:** Claude Opus 5.5 · high
- **Or:** Codex GPT-6 Astra · medium
- Start it in a **new worktree**. Paste:

```text
Read AGENTS.md (repo root) and docs/sessions/S04c-regiment-contact.md, plus only the files and handoffs the card lists under read:. Build the card on branch s04c-regiment-contact, run tools/run_tests.sh, write docs/handoffs/S04c.md, and land it on main yourself following AGENTS.md step 7. Only stop to ask me if you're blocked.
```

**Goal:** when regiments meet, they meet as lines: square on, front to front (or into a flank on purpose),
never through each other, and they keep their shape while fighting.

**Why (playtest after S04b, 2026-11-21):** "troop movement is very finicky and not realistic". Screenshots
showed: a regiment ordered past an enemy squeezed into an oval blob beside it; attackers hitting the end of a
line at 90° (T shapes) and mobbing the corner; lines bending into curves and triangles in melee; blue and red
regiments overlapping, men mixed through each other. Causes found in the code:
- **Moving men don't fight and don't stop.** In `Battle.UpdateSoldier`, `melee` is false while a regiment is
  `Moving`/`Engaging`, so its men walk to slots that lie inside the enemy, and enemy separation squeezes them
  into a blob. Nothing stops a `Move` at an enemy line, and nothing stops friendly regiments overlapping.
- **Attack aims at the nearest point of the enemy box.** `RegimentOrders.CloseWith` takes the closest point
  on the enemy's front/flank/rear rectangle and faces it. From anywhere off-centre that's a corner, so the
  attacker arrives angled at the corner. In contact it keeps turning toward that point every tick, so lines
  rotate into each other instead of squaring up.
- **Approach re-plans every tick.** A moving target shifts the contact point; turns over 30° snap
  (`FormationSteering.Reface`) and re-slot the men, so units twitch and shuffle while closing.

## Steps
1. **Pick a face, then line up on it.** When an Attack (or auto-attack) starts, choose the enemy face to hit:
   front, left/right flank or rear, from where the attacker is, with hysteresis so it doesn't flip every tick.
   Approach a point a few metres off that face, centred on the overlap of the two frontages, then close with
   facing = the face's inward normal. Lines meet parallel. Re-choose the face only if the enemy turns or moves
   a lot (state the thresholds in the handoff).
2. **Square up in contact.** In contact, turn toward the enemy face's normal (antiparallel to it for a front
   fight), not toward a point. Small turns only, about the fighting front, as now.
3. **No snap re-faces while engaging a moving target:** wheel instead, so there's no re-slot shuffle mid-approach.
4. **Moving regiments stop at enemies and fight.** A regiment with any order (Move, Attack, auto) whose front men
   come into reach of enemy men stops advancing into them, and its men fight back (`melee` true for men who have
   an enemy in reach, whatever the regiment's activity). A `Move` that runs into the enemy ends there
   (Activity Fighting) instead of pressing through. `Withdraw` keeps its current behaviour.
5. **Friendly regiments don't merge.** A moving regiment doesn't walk into another friendly block: simple local
   avoidance (slow and side-step, or wait), not pathfinding. Two destinations that overlap are nudged apart.
   Friendly men from different regiments never end up interleaved.
6. **Keep the shape in melee.** Men who aren't fighting stay near their slot; the front rank may bow a little
   but a line in a straight fight stays a line. Keep S04's column fill, leash and wrap; tune, don't rewrite.

## Tests
`AttackFromAngleMeetsSquare` (attacker starts 45° off the enemy's front: in contact, facings antiparallel within
15°, contact along most of the shorter frontage) · `AttackFromSideHitsFlankSquare` · `MoveThroughEnemyStopsAndFights`
(Move order whose path crosses an enemy line: stops at the line, men fight, no blue man ends inside red's box) ·
`FriendlyRegimentsDontInterleave` (two friendly moves that cross: no man within 0.5 m of the other regiment's
slots) · `LineHoldsShapeInFrontalMelee` (60 s straight fight: ≥ 80 % of living non-front men within 1.5 slot
spacings of their slot) · `ContactDeterministic`. All S02/S04/S04b tests stay green; S04 balance unchanged.

## Manual check
- Order a line to attack an enemy line from 45° off: it swings round and hits front to front, not corner first.
- Order a line to move straight through an enemy: it stops at their front and fights.
- Watch a 2-minute fight in `sandbox_vs_ai` (the default): lines stay lines, nobody walks through anybody.

## Not in this card
Pathfinding round terrain, real AI (S09), charges and impact (S06), ranged stand-off (S07), morale (S05).
