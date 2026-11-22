# The Plan

*For you, the user. One page: where we're going, in what order, and what you do at each step.
How to run a session is in `docs/workflow/WORKFLOW.md`. Live progress: run `python3 tools/status.py`.*

## The shape of it

```
Phase 0  Foundation      S01                          one session, Claude
Phase A  Core combat     S02 … S09                    mostly one at a time, you play every build
   ▼  P1 — FUN CHECK: is a grey-box fight fun?  (if no: fix the core, don't add content)
Phase B  Real battles    waves B1, B2 (cards written after P1)   parallel, 2–3 sessions at a time
   ▼  P2, P3 playtests
Phase C  Corivessa       the main scenario (cards written after P3)
Phase D  Polish + release
```

**Why this order:** the fun of a Total War-style game lives in a handful of interactions: lines holding,
flanks collapsing, cavalry hitting home, a regiment breaking at the right moment. We get those right in grey
boxes first, while changes are cheap. Content (factions, sieges, Corivessa, art) only starts once P1 says yes.

**Why only Phase 0–A has detailed cards:** later cards get written in triage sessions after each playtest,
because playtests always change the plan. Writing them now would waste tokens on work that will be thrown away.

Every card's **Start this session** box has the exact model, effort and prompt to paste.
Parallel pairs use different vendors on purpose, so both subscriptions work at the same time.

## Phase 0 — Foundation
| Card | What you get | Model to use |
|---|---|---|
| **S01** Foundation | Code split into the pure sim, the Godot game and tests; a battle scene that runs the M0 armies; test runner and status board working | Claude Opus 5.5 · medium *(or Codex GPT-6 Astra · low)* |

**Your checkpoint:** read `docs/ARCHITECTURE.md` §1–3 (2 pages). It's the last cheap moment to change direction.

## Phase A — Core combat (grey boxes)
| Card | What you get | Model to use | Can run alongside |
|---|---|---|---|
| **S02** Orders & data (sim side) | Unit stats from JSON; regiments obey move, attack, halt and formation orders | Codex GPT-6.1 Sol · high *(or Claude Opus 5.5 · medium)* | S03 |
| **S03** Selection & orders UI | Click and box-select, right-click to move or attack, drag to set line width and facing, regiment cards bar | Claude Sonnet 5.5 · medium *(or Codex GPT-6.1 Sol · medium)* | S02 |
| **S04** Melee v2 + fatigue | Combat driven by stats (attack, defence, armour, armour-piercing, shields), flanking bonuses, regiments that tire | Claude Opus 5.5 · high *(or Codex GPT-6 Astra · medium)* | S08 |
| **S04b** Formation movement | Lines wheel while marching, face about and re-form instead of spinning on the spot *(added after the S03 playtest)* | Claude Opus 5.5 · high *(or Codex GPT-6 Astra · medium)* | S05, S08 |
| **S04c** Regiment contact | Lines meet square (front to front or into a flank), moving units stop and fight instead of walking through, friendly units don't merge, lines keep their shape in melee *(added after the S04b playtest)* | Claude Opus 5.5 · high *(or Codex GPT-6 Astra · medium)* | S05, S08 |
| **S05** Morale, routing, general | Wavering, breaking, routing, rallying; a general with a Rally order | Codex GPT-6.1 Sol · high *(or Claude Opus 5.5 · medium)* | S08 |
| **S06** Charges & cavalry | Cavalry, charge impact and knockdowns, braced spears and pikes that stop horses | Codex GPT-6 Astra · medium *(or Claude Opus 5.5 · high)* | S07, S08 |
| **S07** Missiles | Bows, slings, crossbows, javelins; arcs, ammo, shields that block from the front | Claude Opus 5.5 · medium *(or Codex GPT-6.1 Sol · high)* | S06, S08 |
| **S08** Tuning panel | Press F2 to change any unit stat while the game runs; saves to JSON | Codex GPT-6.1 Sol · medium *(or Claude Sonnet 5.5 · medium)* | S04–S07 |
| **S09** Skirmish scenario | A 6v6 grey-box battle on the plains with deployment, a simple enemy, and a win/lose screen | Codex GPT-6.1 Sol · high *(or Claude Opus 5.5 · medium)* | — |

Suggested order: S01 → (S02 ‖ S03) → S04 → S04b → (S04c ‖ S05) → (S06 ‖ S07) → S09. S08 can run alongside any of S04–S07.

**After each card:** play the build for 5 minutes (the handoff ends with a 3-line "what to check").
Write anything that feels wrong in `docs/playtests/notes.md`, one line each. Don't fix things mid-phase
unless they block you; the notes feed the triage session.

### P1 — Fun check (you, maybe a friend)
Play the skirmish several times. Answer in `docs/playtests/P1.md`:
1. Did a line *holding* feel good? Did a flank *collapsing* feel good?
2. Did the cavalry charge feel heavy? Did braced pikes feel like a wall?
3. Did routs happen for reasons you understood?
4. Could you tell what was happening without reading numbers?
5. Did you want to play again?

Then run the **triage session** (T1). It turns your answers into either fix cards (if the core isn't fun yet)
or the wave B1 cards.

## Phase B — Real battles (outline; detailed after P1)
**Wave B1** (parallel): terrain (hills, rivers, fords, line of sight) · battle AI v1 · the three factions'
rosters and traits as data · UI pass (unit cards, minimap, tooltips) · art pipeline test (one real soldier
modelled and animation-baked in Blender) · performance (LODs, cut-outs for far-away soldiers).
→ **P2**: a field battle with the real factions on real terrain.

**Wave B2** (parallel): sieges (walls, gates, ladders, rams, towers, wall engines) · generals' abilities and
the Iron Champion · deployment phase and victory conditions · audio v1 · faction models and banners.
→ **P3**: a small siege.

## Phase C — The Battle of Corivessa (outline)
The map (plains → farmland and river → city walls → the Great Gate → the canyon), its three phases, the
Kervaran relief force, siege AI, set dressing. → **P4**.

## Phase D — Polish and release (outline)
Menus, settings, the supporting battles as a short campaign, builds, and a store page if you want one.

## Decisions waiting on you (answer whenever; they don't block Phase A)
From GameDesign.md §11: the Iron Champion's name and story, whether Corivessa is canon, stirrups (assumed yes),
trebuchets, art style (decide before wave B1), army size, faction colours (decide before wave B2).
