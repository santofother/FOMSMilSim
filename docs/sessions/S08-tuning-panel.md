---
card: S08
title: Live tuning panel
phase: A
agent: Either
preferred: Codex GPT-6.1 Sol · medium
alternative: Claude Sonnet 5.5 · medium
tier: T3
depends: [S02]
parallel: [S04, S05, S06, S07]
branch: s08-tuning
owns: [src/Game/UI/Tuning/**]
read: [docs/ARCHITECTURE.md §C7, docs/ref/conventions.md §Data, handoffs S01 S02]
---
# S08 — Live tuning panel

## Start this session
- **Preferred:** Codex GPT-6.1 Sol · medium
- **Or:** Claude Sonnet 5.5 · medium
- Start it in a **new worktree**. Paste:

```text
Read AGENTS.md (repo root) and docs/sessions/S08-tuning-panel.md, plus only the files and handoffs the card lists under read:. Build the card on branch s08-tuning, run tools/run_tests.sh, write docs/handoffs/S08.md, and land it on main yourself following AGENTS.md step 7. Only stop to ask me if you're blocked.
```

**Goal:** you can change any unit number while the game runs, so feel-tuning costs no agent tokens.

## Steps
1. `F2` toggles a side panel. It shows the `UnitDef` of the selected regiment's unit: every numeric field as a
   slider plus a number box, traits as checkboxes. Changes apply immediately through `Battle.ApplyDefs`.
2. **Save** writes `data/units/<id>.json` (pretty-printed, same key order, unknown keys kept).
   **Revert** reloads from disk.
3. File watcher: if a JSON file in `data/units/` changes on disk (you edited it in a text editor), reload and
   apply it automatically.
4. Also on the panel: game speed (0.1×–4×) and a "restart scenario" button.
5. The panel builds itself from whatever fields `UnitDef` has (reflection), so later cards' new stats appear
   without changes here.

## Tests
`tools/run_tests.sh` green (the panel must not break the smoke run).

## Manual check
- Raise the spearmen's armour mid-fight and watch them hold longer; save, restart, and the change is still there.

## Not in this card
Editing scenarios or factions (later, if needed).
