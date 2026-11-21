---
card: S07
title: Missiles (bows, slings, crossbows, javelins)
phase: A
agent: Either
preferred: Claude Opus 5.5 · medium
alternative: Codex GPT-6.1 Sol · high
tier: T2
depends: [S04, S05]
parallel: [S06, S08]
branch: s07-missiles
owns: [src/Sim/Combat/Ranged*, src/Game/View/Projectile*, shaders/projectile*, tests/FOMS.Sim.Tests/Ranged*, tests/FOMS.Sim.Tests/Balance/Ranged*]
read: [docs/ARCHITECTURE.md §C3 §C4b §C6, docs/GameDesign.md §2 §3.3 (Missiles), handoffs S04 S05]
---
# S07 — Missiles

## Start this session
- **Preferred:** Claude Opus 5.5 · medium
- **Or:** Codex GPT-6.1 Sol · high
- Start it in a **new worktree**. Paste:

```text
Read AGENTS.md (repo root) and docs/sessions/S07-missiles.md, plus only the files and handoffs the card lists under read:. Build the card on branch s07-missiles, run tools/run_tests.sh, write docs/handoffs/S07.md, and land it on main yourself following AGENTS.md step 7. Only stop to ask me if you're blocked.
```

**Goal:** archers matter: they thin a regiment before contact, shields visibly help, and ammo runs out.

## Steps
1. Projectiles simulated in the sim (pooled struct arrays): ballistic arcs with flight time; hits tested
   against soldiers near the landing point through the spatial grid; misses land on the ground.
2. Firing: an `Attack` order on a target in range fires volleys; `SetFireAtWill(true)` picks the nearest target.
   Accuracy falls with range and target movement. `Ammo` (C4b) runs down, and a regiment out of ammo can be
   ordered into melee.
3. Shields block frontal hits only; armour reduces damage; armour-piercing for crossbows. Friendly fire is on.
4. Weapon types from data: bow (long range, arc), sling (cheap, weak against armour), crossbow (slow, flat,
   armour-piercing), javelins (short, heavy, thrown before contact). Use `gb_archers` plus a new `gb_javelinmen`.
5. View: projectiles as one MultiMesh (thin arrows), with a small dust puff on ground hits if it's cheap.
6. Events: `ProjectileFired`, `ProjectileHit`.

## Tests
`ShieldsReduceFrontalCasualties` · `RearVolleysIgnoreShields` · `AmmoRunsOut` · `OutOfRangeDoesNotFire` ·
`ArcherVolleyDeterministic`.

## Manual check
- Arrows visibly arc and fall into a regiment; a shielded regiment taking frontal fire loses fewer men.
- Javelinmen throw just before contact.

## Not in this card
Artillery (wave B2, sieges), skirmisher auto-kiting (wave B AI).
