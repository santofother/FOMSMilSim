---
card: S03
title: Selection & orders UI
phase: A
agent: Either
preferred: Claude Sonnet 5.5 · medium
alternative: Codex GPT-6.1 Sol · medium
tier: T3
depends: [S01]
parallel: [S02]
branch: s03-selection-ui
owns: [src/Game/Input/**, src/Game/UI/Hud/**, src/Game/View/Markers*, shaders/marker*]
read: [docs/ARCHITECTURE.md §C1 §C4b §C5, docs/GameDesign.md §8, handoff S01]
---
# S03 — Selection & orders UI

## Start this session
- **Preferred:** Claude Sonnet 5.5 · medium
- **Or:** Codex GPT-6.1 Sol · medium
- Start it in a **new worktree**. Paste:

```text
Read AGENTS.md (repo root) and docs/sessions/S03-selection-ui.md, plus only the files and handoffs the card lists under read:. Build the card on branch s03-selection-ui, run tools/run_tests.sh, write docs/handoffs/S03.md, and land it on main yourself following AGENTS.md step 7. Only stop to ask me if you're blocked.
```

**Goal:** you can command regiments the way Total War does. This card only issues orders; S02 makes the sim obey.

## Steps
1. Selection: left-click a regiment, shift-click to add, left-drag box select, Ctrl+1–9 to save a group and
   1–9 to recall it, double-click to select all regiments of that unit type on screen, Esc to clear.
2. Orders: right-click ground = `Move` (keeps current width, faces away from where the regiment is now);
   **right-drag = draw the line** (drag length = width, drag direction = line, facing perpendicular, shown as
   ghost markers while dragging); right-click an enemy = `Attack`; double right-click = run; `H` halt;
   `Backspace` withdraw. Multiple regiments selected: spread them side by side along the dragged line.
3. Markers: a team-coloured banner above every regiment (brighter when selected), destination ghost markers
   for selected regiments, a hover highlight.
4. Regiment cards bar along the bottom: one card per player regiment, with name, alive/count, `Activity`,
   and a morale and fatigue bar (from C4b fields; they stay at default until S04/S05). Click a card to select.
5. `Space` pause (orders still allowed while paused), `+`/`−` game speed (0.5×–3×).
6. Log every issued order as `[Orders] <type> r<id> …`.

## Tests
No headless test is needed for input. `tools/run_tests.sh` must stay green (the smoke run includes your nodes).

## Manual check
- Select, box-select, group-recall all work; the right-drag line preview matches where the regiment ends up (after S02).
- Cards bar updates as regiments take losses.

## Not in this card
Order execution (S02), the tuning panel (S08), minimap and tooltips (wave B).
