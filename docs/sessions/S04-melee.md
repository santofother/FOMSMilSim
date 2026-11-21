---
card: S04
title: Melee v2 + fatigue
phase: A
agent: Either
tier: T1
depends: [S02]
parallel: [S08]
branch: s04-melee
owns: [src/Sim/Combat/Melee*, src/Sim/Movement/**, tests/FOMS.Sim.Tests/Melee*, tests/FOMS.Sim.Tests/Balance/Melee*]
read: [docs/ARCHITECTURE.md §C3 §C4 §C6 §C7, docs/GameDesign.md §1 §3.1 §3.3, handoffs S01 S02]
---
# S04 — Melee v2 + fatigue

**Goal:** melee is decided by stats and position, and fights *look* like two lines grinding, not one blob.

## Steps
1. Per-soldier duel resolution (GameDesign §3.3): hit chance from attack vs defence; damage split into normal
   (reduced by armour) and armour-piercing; a shield blocks a share of frontal hits; **flank and rear hits ignore
   the shield and get an attack bonus** (angle between the attacker and the defender's facing).
2. Melee spacing: soldiers fill the front, rear ranks press up behind, and lines stay roughly lines. Fix M0's
   collapse into a single thin line: engaged regiments keep depth, with a contact zone, not interpenetration.
   Pushing by mass (heavier units gain ground).
3. Fatigue (C4b `Fatigue`): running, fighting and standing still drive it up or down at rates from
   `stamina`. Thresholds (fresh → winded → tired → exhausted) reduce attack, defence and speed.
4. Events: `MeleeHit` (attacker, victim, killed) for later audio and VFX.
5. Performance: 4,000 soldiers in melee at ≤ 4 ms per tick (ARCHITECTURE §4). Report the number in the handoff.

## Tests (balance tests use wide ranges and several seeds)
`EqualRegimentsAreEven` (each side wins 35–65% over 20 seeds) · `FlankedRegimentLosesBadly` ·
`ArmouredBeatUnarmouredOfSameCost` · `TiredRegimentFightsWorse` · `LinesKeepDepthInMelee` (average depth after 30 s
stays above 60% of the starting depth).

## Manual check
- Two regiments meet: a visible front line, the rear ranks pressing in, men falling at the contact line.
- Hitting a regiment that's already fighting in its flank visibly speeds up its losses.

## Not in this card
Morale (S05): regiments still fight to the death or to the old 45% rule. Charges (S06), missiles (S07).
