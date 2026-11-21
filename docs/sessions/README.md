# Session cards

One card = one session = one branch. A builder reads `AGENTS.md` + its card + the handoffs of the cards it
depends on. Nothing else unless the card's `read:` names it.

Every card starts with a header block that `tools/status.py` reads:

```
---
card: S02
title: Orders & data (sim side)
phase: A
agent: Either            # Either | Claude | ChatGPT | User
tier: T2                 # T1 hard … T4 chores (models: docs/workflow/WORKFLOW.md)
depends: [S01]
parallel: [S03]          # cards that can run at the same time (owns lists don't overlap)
branch: s02-orders-sim
owns: [src/Sim/Orders/**, ...]
read: [docs/ARCHITECTURE.md §C1 §C5, docs/GameDesign.md §3.2]
---
```

Then: **Goal** (one line), **Steps**, **Tests**, **Manual check** (what the user looks at, 3 lines max),
**Not in this card** (scope fence).

Only the next phase is written in detail. Later cards are written by triage sessions after playtests.
If a card disagrees with `docs/ARCHITECTURE.md`, ARCHITECTURE wins.
