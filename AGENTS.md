# FOMS Battle Simulator — rules for every coding agent

Read this whole file at the start of every session (Claude Code and Codex both do). It's kept short on
purpose: only open the `docs/ref/` files your card names.

Total War-style real-time battle game (Godot 4.7 + C#) set in the user's *Fire on the Middle Sea* world.
Single player vs AI. No magic, no gunpowder, no plate armour (one exception, see the GDD).

## Roles
- **The user** owns the project, decides everything, plays every build at the checkpoints.
- **Head engineer = Claude Code.** Owns `docs/ARCHITECTURE.md`, contracts, `docs/sessions/`, triage and merge
  sessions.
- **Builders = any agent** (Claude Code or Codex) running one card from `docs/sessions/`. Nothing else.

## A session, every time
1. Read this file and your card `docs/sessions/Sxx-*.md`. Read **only** what the card's `read:` lists (section
   names mean "just that section") plus the handoffs of the cards it depends on. Don't explore the repo.
2. Work on branch `sNN-short-name`, ideally in its own git worktree.
3. Edit only files matching your card's `owns:` list (plus your tests and handoff). Reading anything is fine.
4. Add tests. Sim logic gets `dotnet test` unit or balance tests; they run in seconds without Godot.
5. Run `tools/run_tests.sh`. Everything must pass.
6. Write `docs/handoffs/Sxx.md` from the template in `docs/handoffs/README.md` (max ~25 lines, header block
   required: `tools/status.py` reads it).
7. Commit, then **land on `main` yourself** if all of this holds:
   `git fetch origin && git merge origin/main` → tests green on the merged result → `git push origin HEAD:main`
   (retry from the fetch if the push is rejected) → also `git push -u origin HEAD`.
   **Stop instead** if: a merge conflict is in a file you don't own, tests fail outside your files, or your
   handoff requests a contract change. Push your branch, set `status: REVIEW` in the handoff, and stop.
   Never force-push, never push a red `main`, never rewrite history.

## Cross-agent rules
- Don't review, refactor, restyle or rename code outside your card.
- If something outside your card blocks you, make the smallest fix, mark it `// FIX(Sxx): why`, and list it
  under "Touched outside my card" in the handoff.
- If a contract in `docs/ARCHITECTURE.md` is wrong, don't change it: work around it and write
  "Contract change requested" in the handoff.
- Never delete or weaken another card's tests to make yours pass.

## Token rules (usage is the user's main bottleneck)
- Edit `.cs`, `.tscn`, `.tres`, `.json`, `.gdshader` as text. Never open the Godot editor GUI.
- Prove things with tests and logs, not screenshots. Screenshots only for visual cards, few and small
  (`--shots=` flag, see `docs/ref/commands.md`).
- Read files in parts when you need one section. Don't re-read what you just wrote.
- No subagent fan-outs, no repo-wide audits. Batch independent commands.
- Stuck after ~3 attempts on the same problem: stop, write what you tried in the handoff as `status: PARTIAL`.
- Feel and fun can't be tested by you. Write a 3-line manual check in the handoff for the user instead.

## Lore
Canon lives outside the repo (see `docs/ref/lore.md`). Builders don't read lore files. If your card needs a name
or a fact that isn't in the GDD, use a placeholder marked `// LORE?` and list it in the handoff.
