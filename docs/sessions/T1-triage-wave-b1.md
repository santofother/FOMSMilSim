---
card: T1
title: Triage after P1 → wave B1 cards
phase: A
agent: Claude
preferred: Claude Opus 5.5 · medium
alternative: Codex GPT-6.1 Sol · high
tier: T2
depends: [P1]
parallel: []
branch: t1-triage
owns: [docs/sessions/**, docs/PLAN.md, docs/GameDesign.md, docs/ARCHITECTURE.md, data/units/**]
read: [docs/PLAN.md, docs/playtests/P1.md, docs/playtests/notes.md, the "Known issues" sections of all handoffs]
---
# T1 — Triage after P1 (head engineer)

## Start this session
- **Preferred:** Claude Opus 5.5 · medium
- **Or:** Codex GPT-6.1 Sol · high
- Run it in the repo folder itself (no worktree). Paste:

```text
Read AGENTS.md and CLAUDE.md (repo root), then docs/sessions/T1-triage-wave-b1.md. You're head engineer: do this triage card exactly as written, reading only the files it lists. Show me the new wave before you commit.
```

**Goal:** turn the playtest into the next wave.

## Steps
1. If P1 says the core isn't fun yet: write fix and iteration cards for the core **instead of** wave B1, and
   say why in PLAN.md.
2. Bugs → small fix cards (T3/T4, mostly Codex). Trivial tuning → edit `data/units/` directly.
3. Update GameDesign.md wherever the playtest changed the design, and pick up any answered §11 decisions.
4. Write the wave B1 cards from PLAN.md's outline in the same format, with non-overlapping owns lists, a
   parallel column, and an agent split of about two-thirds Codex.
5. Show the user the new wave (titles + one line each) before committing.
