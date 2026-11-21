---
card: S09
title: Grey-box skirmish scenario + battle flow
phase: A
agent: Either
tier: T3
depends: [S03, S05, S06, S07]
parallel: []
branch: s09-skirmish
owns: [data/scenarios/skirmish_*.json, src/Sim/AI/SimpleAI*, src/Game/Battle/Flow*, src/Game/UI/Results/**, src/Game/UI/Deploy/**, tests/FOMS.Sim.Tests/AI*]
read: [docs/ARCHITECTURE.md §C1 §C5, docs/GameDesign.md §3.6 §7, handoffs S02 S03 S05 S06 S07]
---
# S09 — Grey-box skirmish + battle flow

**Goal:** a complete, replayable small battle: the thing we'll judge at P1.

## Steps
1. Scenario `skirmish_plains`: each side has a general, 2 spearmen, 1 swordsmen, 1 pikemen, 1 archers,
   1 light cavalry, 1 heavy cavalry (about 900 soldiers in all). Flat ground, 300 × 300 m.
2. Battle flow: **deployment** (drag your regiments around inside a highlighted zone; "Start battle" button)
   → battle → **results** screen (winner, casualties per regiment, battle time) → restart or quit.
3. `SimpleAI` (in the sim, using orders only): holds a line, advances when its archers are out of ammo or the
   player is close, cavalry goes for flanks and archers, archers fire at will, and it commits reserves when its
   line wavers. Readable and beatable, not smart.
4. Victory: all enemy regiments routed, shattered or dead.
5. `--scenario=skirmish_plains` and make it the default scenario.

## Tests
`AIvsAIFinishes` (AI vs AI on skirmish_plains finishes within 15 min of sim time over 10 seeds, no exceptions) ·
`AIUsesCavalryOnFlanks` (at least one flank or rear charge in most seeds).

## Manual check
- Play three battles. Can you win by using the flanks and cavalry well? Does the AI ever surprise you?

## Not in this card
Smarter AI, terrain, factions (wave B).
