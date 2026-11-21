# Workflow: how to run sessions

*For you, the user. Agents don't read this; their rules are in `AGENTS.md`.*

## The loop
1. Run `python3 tools/status.py` and pick a card marked **READY**.
2. Start a new session with the agent and model the card names (table below), **in a new worktree**.
3. Paste the **builder prompt** with the card's name. The card holds every detail; add nothing else.
4. The session builds, tests, writes `docs/handoffs/Sxx.md` and lands its own work on `main`. It only stops to
   ask if it's blocked.
5. Play the build for ~5 minutes using the "Manual check" lines at the end of the handoff. Note anything off in
   `docs/playtests/notes.md` (one line each).
6. If a handoff says `REVIEW` or `PARTIAL`, run a **merge** or **fix** session (prompts below).
7. At a playtest checkpoint (P1, P2…), play properly, write the P-file, then run the **triage** session.

**Why this saves tokens:** every message in a session re-sends the whole conversation. A session that starts from
a one-page card and ends after one feature stays cheap. Handoffs carry what the next session needs, so nobody
re-reads old code or old chats.

**Golden rule:** one session = one card. If you want "one more little thing" in a session that's done, open a
fix session instead. Long sessions are where tokens go.

## Prompts (copy, paste, replace the `<…>`)

**Builder:** use the **Start this session** box at the top of the card. It has the model, the effort, and the
exact prompt, ready to paste. The general form, for cards written later without a box (new worktree; any agent):
```text
You're a builder on FOMS Battle Simulator. Read AGENTS.md, then do card docs/sessions/<S02-orders-sim>.md on branch <s02-orders-sim>. Follow the card's owns list, run the tests, write the handoff, and land it on main yourself following AGENTS.md step 7, without asking me. Only stop to ask if you're blocked.
```

**Fix** (new worktree; Codex Sol medium or Claude Sonnet 5.5 medium):
```text
Read AGENTS.md. Bug: <what happened, what I expected, which card's build>. Find the cause with the smallest read possible, fix it on branch fix-<short-name>, add a test if it can be tested headless, write a 5-line docs/handoffs/fix-<short-name>.md (header block included), and land it on main following AGENTS.md step 7 without asking.
```

**Merge** (the repo folder itself, no worktree; Claude Sonnet 5.5 medium or Codex Sol medium):
```text
You're head engineer on FOMS Battle Simulator, doing a merge session. Merge into main: <branch s04-melee | PR 7 | every branch whose handoff says status: REVIEW>. For each one: update main, read its handoff, merge, keep both sides' intent on conflicts (ask me if unsure), run tools/run_tests.sh, set the handoff's status to DONE, commit. Push main when everything is green (never force). Then list any "Contract change requested" items with your recommendation, without changing contracts yourself.
```

**Triage** (the repo folder, no worktree; Claude Opus 5.5 medium):
```text
You're head engineer on FOMS Battle Simulator, running triage after playtest <P1>. Read AGENTS.md, CLAUDE.md, docs/PLAN.md, docs/playtests/<P1>.md, docs/playtests/notes.md, and only the "Known issues" sections of the handoffs. Turn this into: fix cards for bugs, direct JSON edits for trivial tuning, GameDesign.md updates where the playtest changed the design, and detailed cards for the next wave (same format as the existing cards, with owns/depends/agent/tier). Update docs/PLAN.md. Show me the new wave before committing.
```

**Question** (the repo folder, no worktree; Sonnet 5.5 or Codex Sol, medium):
```text
You're head engineer on FOMS Battle Simulator, answering a question. Make no changes unless I ask. Open only what the question needs; docs/PLAN.md, docs/ARCHITECTURE.md and python3 tools/status.py are the usual start. My question: <question>
```

**Lore** (a chat app, not a coding session; a Claude or ChatGPT Project with the Atlas files uploaded):
```text
I'm designing <the Iron Champion / Valdora's colours / unit names for Kervara>. Using my Atlas as canon, give me 3 options that fit, each in 2–3 lines. Flag anything that contradicts the Atlas.
```
Then copy the chosen answer into `docs/GameDesign.md` as one line. Coding sessions never read lore files.

## Which agent and model
Each card names an **Agent** and a **Tier**.
- **Either**: Claude or Codex; use whichever has usage left. Aim for Codex to take about two-thirds of builder cards.
- **Claude**: head-engineer cards (S01, triage, merges) for consistency, plus Blender work.
- **ChatGPT**: image generation (concept art, banners, icons) in later waves.

| Tier | Codex (ChatGPT) | Claude Code | Used for |
|---|---|---|---|
| T1 hard | GPT-6 Astra, low–medium | Opus 5.5, high | sim core, charges, AI, sieges |
| T2 normal | GPT-6.1 Sol, high | Opus 5.5, medium | most features |
| T3 light | GPT-6.1 Sol, medium (or Luna, high) | Sonnet 5.5, medium | UI, data, well-specified features |
| T4 chores | GPT-6 Luna, low–medium | Haiku 5.5 or Sonnet 5.5 low | docs, renames, small fixes |

Model notes come from your LastMage research (2026-11-07). Re-check when new models ship. Start one tier lower than
you think; if a handoff comes back `PARTIAL` because the card was too hard, rerun the rest one tier up. Save
Fable 5.2 for a T1 card that failed on Opus.

**T1 cards get one cross-check:** after a T1 card lands, run one question session on the *other* vendor:
"Review the diff of branch <x> for correctness bugs only; list at most 5; change nothing." Skip this for
other tiers.

## Starting a session
**Claude Code (desktop app):** New session → folder `FOMSBattleSimulator` → turn on worktree → choose model and
effort → paste the prompt. Merge, triage and question sessions run without a worktree.

**Codex (ChatGPT app or CLI):** open the repo → new task in worktree mode (CLI:
`git worktree add ../foms-s02 -b s02-orders-sim`, then run `codex` there) → choose model and reasoning → paste the
prompt. Codex reads `AGENTS.md` automatically.

## Running cards at the same time
Two cards can run together when the plan says so: their `owns:` lists don't overlap. Practical limit: **3 at
once** (memory on your Mac, plus every finished card is a build you should look at). Each builder retests on the
latest `main` before landing, which catches most hidden clashes.

## Checkpoints for you
- **After S01:** read `docs/ARCHITECTURE.md` §1–3.
- **After every card:** a 5-minute play with the handoff's manual check.
- **P1 / P2 / P3:** real playtests, then triage.
- **Any time:** update `docs/GameDesign.md` §11 decisions; triage picks them up.
