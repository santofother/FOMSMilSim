---
card: S06
title: Charges & cavalry
phase: A
agent: Either
preferred: Codex GPT-6 Astra · medium
alternative: Claude Opus 5.5 · high
tier: T1
depends: [S04, S04b, S04c, S05]
parallel: [S07, S08]
branch: s06-cavalry
owns: [src/Sim/Combat/Charge*, src/Sim/Movement/Cavalry*, src/Game/View/Cavalry*, shaders/cavalry*, tests/FOMS.Sim.Tests/Charge*, tests/FOMS.Sim.Tests/Balance/Charge*]
read: [docs/ARCHITECTURE.md §C4 §C6 §C9, docs/GameDesign.md §2 §3.3 (Charges), handoffs S04 S05]
---
# S06 — Charges & cavalry

## Start this session
- **Preferred:** Codex GPT-6 Astra · medium
- **Or:** Claude Opus 5.5 · high
- Start it in a **new worktree**. Paste:

```text
Read AGENTS.md (repo root) and docs/sessions/S06-charges-cavalry.md, plus only the files and handoffs the card lists under read:. Build the card on branch s06-cavalry, run tools/run_tests.sh, write docs/handoffs/S06.md, and land it on main yourself following AGENTS.md step 7. Only stop to ask me if you're blocked.
```

**Goal:** a cavalry charge into a flank is the most satisfying thing in the game, and charging braced pikes
head-on is a disaster.

## Steps
1. Cavalry movement: momentum, acceleration, turn rate (no turning on the spot), a wider footprint. Riders are one
   soldier with `model: "cavalry"`.
2. Charge: at contact, impact = speed × mass versus the defender's mass and bracing. Strong impacts knock men down
   (a short `Knocked` sub-state, the defender can't fight), deal charge-bonus damage, and send a morale shock
   through S05's API. The charge bonus fades over a few seconds of melee.
3. Bracing: units with the `brace` trait standing still facing the charge (pikes, spears) reverse the impact
   onto the riders: dead and knocked-down horses, a morale shock to the cavalry.
4. Cycle charging: an order to disengage (`Withdraw`) lets cavalry pull out, re-form and charge again.
5. Placeholder rider-and-horse mesh registered as model `"cavalry"` (C9), with gallop and death animation in
   the shader, like the infantry one.
6. Events: `ChargeImpact` (regiment, target, strength).

## Tests
`BracedPikesBeatFrontalCavalryCharge` (cavalry loses 50–90%, pikes lose under 20%) ·
`RearChargeBreaksEngagedInfantry` (breaks within 15 s in most seeds) · `ChargeBonusFades` ·
`CavalryCannotPivotInPlace`.

## Manual check
- A rear charge into an engaged regiment: the impact moment reads clearly (men thrown, a sudden break).
- Charge braced pikes head-on: it should look and feel like a mistake.

## Not in this card
Horse archers and skirmish behaviour (S07 or wave B), wedge formation (wave B).
