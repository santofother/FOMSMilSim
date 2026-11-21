---
card: S05
title: Morale, routing, rallying, the general
phase: A
agent: Either
preferred: Codex GPT-6.1 Sol · high
alternative: Claude Opus 5.5 · medium
tier: T2
depends: [S04]
parallel: [S08]
branch: s05-morale
owns: [src/Sim/Morale/**, tests/FOMS.Sim.Tests/Morale*, tests/FOMS.Sim.Tests/Balance/Morale*]
read: [docs/ARCHITECTURE.md §C4b §C5 §C6, docs/GameDesign.md §1 §3.4 §3.5, handoffs S02 S04]
---
# S05 — Morale, routing, rallying, the general

## Start this session
- **Preferred:** Codex GPT-6.1 Sol · high
- **Or:** Claude Opus 5.5 · medium
- Start it in a **new worktree**. Paste:

```text
Read AGENTS.md (repo root) and docs/sessions/S05-morale.md, plus only the files and handoffs the card lists under read:. Build the card on branch s05-morale, run tools/run_tests.sh, write docs/handoffs/S05.md, and land it on main yourself following AGENTS.md step 7. Only stop to ask me if you're blocked.
```

**Goal:** regiments break for reasons you can understand, and that's how most battles end.

## Steps
1. Morale 0–100 per regiment, with states Eager → Steady → Wavering → Broken → Shattered (C4b). Replace the M0 45% rule.
2. Every input from GameDesign §3.4: casualty *rate* (not only total), flank/rear attack, nearby friendly routs,
   general killed, fatigue, winning or losing the current melee; plus general nearby or in sight, and fresh. Keep
   a per-regiment list of the current modifiers (for a later tooltip: `Regiment.MoraleReasons`).
3. Routing: broken regiments run to their own side's map edge and become `Gone` there. They ignore orders, and
   pursuers can cut them down.
4. Rallying: a broken regiment clear of enemies for N seconds, near the general, reforms with low morale.
   The second break is permanent (Shattered).
5. General: `gb_general` regiment (bodyguard). `UseAbility("rally", target)`: big morale boost to a wavering or
   broken regiment in range, with a cooldown. Killing the general triggers an army-wide morale shock.
6. Events: `RegimentWavering`, `RegimentRouted`, `RegimentRallied`, `RegimentShattered`, `GeneralKilled`.

## Tests
`FlankAttackCausesEarlierRout` · `GeneralNearbyDelaysRout` · `RoutCascadesToNeighbours` (but not across the whole
army) · `RallyWorksOnlyClearOfEnemies` · `ShatteredNeverReturns`.

## Manual check
- Watch a regiment's morale bar drop, then the regiment break and run; rally it with the general.
- Losing the general should feel like a real blow.

## Not in this card
Faction traits like "Never Broken" (wave B), charge shock (S06 calls into your API).
