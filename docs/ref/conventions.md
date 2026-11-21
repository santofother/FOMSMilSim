# Code conventions

## C#
- Namespaces follow folders: `FOMS.Sim`, `FOMS.Sim.Combat`, `FOMS.Game.View`, …
- `PascalCase` types, methods and public members; `_camelCase` private fields; `camelCase` locals.
- Godot node scripts are `partial` classes, and the file name matches the class name.
- `src/Sim/**` must not reference Godot. Use `System.Numerics` (`Vector2` = ground x,z). The compiler enforces
  this, because the sim is its own project.
- Hot loops in the sim use struct-of-arrays and no allocation per tick. Use `Parallel.For` only where the result
  doesn't depend on thread timing (see Determinism in ARCHITECTURE.md).

## Comments (how agents talk to each other)
- **File header** on every file: what it does, `Owner: Sxx`, public API.
- `///` on every public type and member that other modules use.
- Greppable tags, used exactly like this:
  - `// CONTRACT:` an invariant other modules rely on. Changing it needs the head engineer.
  - `// TUNE:` a gameplay number. Prefer moving it into `data/*.json`.
  - `// TODO(Sxx):` a known gap and the card that will handle it.
  - `// FIX(Sxx):` a minimal fix in a file you don't own.
  - `// WHY:` a non-obvious decision.
  - `// LORE?` a placeholder name or fact waiting for the user.

## Data
- All unit, faction and scenario numbers live in `data/` as JSON. Code holds no balance numbers except defaults
  marked `// TUNE:`.
- JSON uses `snake_case` keys. Every file has an `"id"` that matches its file name.

## Logs
Prefix logs by system: `[Sim]`, `[Orders]`, `[AI]`, `[View]`, `[Test]`. Tests may grep them.

## Tests
- `tests/FOMS.Sim.Tests` (xUnit): unit tests, plus **balance tests**: small scripted battles with asserted
  outcome ranges. Name them after the design rule they check, e.g.
  `BracedPikesBeatFrontalCavalryCharge`.
- Balance assertions use wide ranges (e.g. "cavalry loses 50–90%") so tuning doesn't break them every day.
