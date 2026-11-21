# Handoffs

One file per finished session: `docs/handoffs/<card>.md` (fix sessions: `fix-<name>.md`). Max ~25 lines.
The header block is required, because `tools/status.py` builds the status board from it.

```markdown
---
card: S04            # or fix-<name>
status: DONE         # DONE | REVIEW | PARTIAL | IN PROGRESS
branch: s04-melee
date: 2026-11-22
agent: Codex GPT-6.1 Sol high
---
# S04 — Melee v2 + fatigue

## What exists now
- 3–6 bullets a later builder needs to know.

## Public API (what other cards can use)
- Types, methods, events, data fields you added.

## Tests
- Which tests you added; `tools/run_tests.sh` result; perf number if the card asks for one.

## Manual check (for the user, max 3 lines)
- What to look at in the build and what "good" looks like.

## Touched outside my card
- none / file + why (also marked `// FIX(Sxx):` in place)

## Contract change requested
- none / what and why

## Known issues / next steps
- …
```
