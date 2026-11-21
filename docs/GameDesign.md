# FOMS Battle Simulator — Game Design Document

*Working draft v0.1 · 2026-11-21 · Battles-only scope*

A real-time tactical battle game in the style of Total War, set in the world of *Fire on the Middle Sea*. Canon comes from `The Histories/World Atlas.md` and the kingdom Atlas entries; when this doc and the Atlas disagree, the Atlas wins.

---

## 1. Vision

Large armies of regiments meet on real terrain. Battles are won by **formation, terrain, timing and morale**, not by magic or unit-stat soup. The fantasy is a grounded one: an empire that never fell and spent two more centuries refining how it makes war, then broke into proud successor kingdoms that each kept a different piece of that craft.

### Design pillars
1. **Morale wins battles, not kills.** Most regiments leave the field by breaking, not by dying. Flanks, charges, losses and leadership matter more than raw attack numbers.
2. **Terrain is a weapon.** Rivers, walls, gates, slopes and the canyon mouth decide where a fight can happen at all.
3. **Every faction plays like its history.** Calderra rides, Kervara holds, Valdora runs the machine. A player should be able to tell factions apart by how they win, not by unit colour.
4. **No magic.** Like the books, everything has a mundane explanation. Faith, pride and reputation are real *in their effect on soldiers*, so they show up as morale, never as spells.

---

## 2. Setting & Technology

**Tech level: a late Roman empire that never fell, plus about 200 years of further development.** In practice it is a Roman / mid-medieval hybrid.

| Area | In the game | Not in the game |
|---|---|---|
| Body armour | Mail, scale, lamellar, padded jacks, segmented plate *pieces* (arms, shoulders), Roman-style helmets | Full plate harness: no knights in plate. **One exception:** Kervara's single armoured champion (see §4.1) |
| Shields | Large oval/rectangular infantry shields, round cavalry shields | — |
| Infantry weapons | Spear, pike (Kervara), spatha/long sword, axes, weighted darts (*plumbatae*), javelins | Gunpowder of any kind |
| Missiles | Composite bows, slings, crossbows (refined from Roman hand-ballistae) | Longbow-armies in plate-piercing roles |
| Cavalry | Mail-clad lancers with stirrups (an assumption, see §11), horse archers, javelin light horse | Fully barded heavy knights or cataphracts |
| Artillery | Scorpions and bolt-throwers, ballistae, onagers, fixed wall engines | Trebuchets *(open question; plausible after 200 years)* |
| Siege | Rams with sheds, ladders, siege towers, sapping, mantlets | — |

**What "no heavy armour" means for design:** missiles stay dangerous all battle, cavalry charges hurt but can't simply bulldoze shield walls, and a formed pike or spear line is the real answer to horse. That one plate-armoured Kervaran then feels like a walking siege engine.

---

## 3. Core Systems

### 3.1 Regiments
The **regiment** is the unit of command, as in Total War. Each regiment is a block of soldiers (typically 60–160 infantry, 30–80 cavalry, 2–6 engines) that shares orders, formation, morale and ammunition. Individual soldiers exist for **visuals and local melee**: they push, fall, get trampled, and slowly break formation under pressure.

Regiment stats (draft):

| Stat | Meaning |
|---|---|
| Soldiers | Head count; casualties reduce it |
| Melee Attack / Melee Defence | Hit and parry chance in melee |
| Weapon Damage / Armour-Piercing | Damage split between normal and armour-ignoring |
| Armour | Reduces normal damage, from melee *and* missiles |
| Shield | Extra block chance vs missiles from the front |
| Charge Bonus | Temporary attack and damage bonus after a charge |
| Mass | How hard the soldiers push through enemies (key for cavalry and the champion) |
| Speed / Run Speed | — |
| Morale | Base resolve (see 3.4) |
| Stamina | How quickly the regiment tires |
| Ammo / Range / Accuracy | Missile units only |
| Traits | Tags such as `Pike`, `Brace`, `Climber`, `Wall-Crew`, `Faithful` |

### 3.2 Formations
- **Line** — standard, adjustable width and depth.
- **Shield Wall / Testudo** — slow, strong vs missiles from front and above, poor at charging.
- **Pike Square / Hedge** (Kervara) — near-immune to frontal cavalry, vulnerable to flanks and missiles.
- **Wedge** (cavalry) — punches through a line, takes more casualties.
- **Loose** — skirmishers; halves missile losses, weak in melee.
- **Rank Rotation** (Valdora) — the front rank swaps out for fresh soldiers to recover stamina in a long melee. A Roman idea preserved by Valdora's "machine" culture.

### 3.3 Combat
- **Melee** is resolved per soldier pair: attack vs defence for hit chance, then armour vs damage. Flank and rear attacks ignore shield and get bonuses.
- **Charges** convert speed × mass into impact: soldiers get knocked down, and the defender takes a morale shock. A braced spear or pike line *reverses* that shock onto the charging cavalry.
- **Missiles** are simulated projectiles with arcs. Shields block from the front only. Friendly fire is on.
- **Fatigue**: running, fighting, slopes and heat drain stamina. Exhausted regiments lose attack, defence and speed.

### 3.4 Morale
Each regiment has morale from 0 to 100. States: **Eager → Steady → Wavering → Broken (routing) → Shattered (won't rally)**.

| Lowers morale | Raises morale |
|---|---|
| Heavy losses, especially sudden ones | General nearby / General's banner in sight |
| Attacked in flank or rear | Winning its current melee |
| Charged by cavalry or the champion | Holding walls, high ground, or a gate |
| Friendly regiments routing nearby | Faction traits (below) |
| General killed | Fresh, untired |
| Exhaustion | Allies arriving (the Kervaran relief force) |

**Rallying**: a broken regiment that gets clear of enemies and is near a general may reform with reduced morale.

### 3.5 Generals & Champions (no magic)
Each army has a **General** regiment (the commander plus a bodyguard). Their abilities are grounded orders with cooldowns, and every one of them is "real":
- **Rally** — a nearby wavering or broken regiment recovers some morale.
- **Hold the Line** — target regiment can't be pushed back for a short time but loses stamina faster.
- **Inspire** — morale boost aura while the general is *in* combat, at real risk to the general.

**Champions** are single named soldiers with very high stats and mass. They fight inside or alongside a regiment, are terrifying to face (morale penalty to enemies nearby), and can be killed.

### 3.6 Terrain
- **High ground**: bonus to melee and missile range; uphill charges are weaker.
- **River & fords**: units in water are slowed and lose formation and defence. Bridges and fords are chokepoints.
- **Fields & farmland**: crops and irrigation ditches around Corivessa slow cavalry and break up charges *(Valdoran farmland is watered by the river from Kervara, see the Atlas)*.
- **Woods / orchards**: block line of sight, hide units, terrible for cavalry.
- **Walls, towers, gates**: see §6.

### 3.7 Line of Sight & Fog
Units only see what is in their line of sight. Hidden deployment exists (woods, behind ridges, and Kervara's hidden paths).

---

## 4. Playable Factions

Three factions to start. Rosters are drafts: aim for **8–10 regiments per faction** in v1. Unit names are placeholders, open for you to rename to fit canon.

### 4.1 Kervara — House Marcellus · "The Unbroken"
*Mountain military kingdom; the best soldiers on the continent, mostly rented out to other kingdoms' wars. Strong in armour and discipline, weak in cavalry.*

**Faction traits**
- **Never Broken** — routing Kervaran regiments rally faster and come back more often. The reputation is the whole national identity.
- **Master Smiths** — best armour values of any faction (still no plate except the champion).
- **Hidden Paths** *(House Harrow)* — on mountain or canyon maps, can deploy one regiment on a secret flank route.

| Regiment | Role | Notes |
|---|---|---|
| Company Pikemen | Anti-cavalry line | `Pike`, `Brace`. The Fourth Company exists in canon |
| Company Heavy Infantry | Line infantry | Mail and scale, large shields, swords. The all-rounder |
| Home Garrison Shieldwall | Defensive infantry | Prince Bram's garrison troops: slower, tougher, high morale |
| Company Crossbowmen | Missile | High armour-piercing, slow reload, best-made crossbows in the world |
| Mountain Skirmishers | Light infantry | Javelins, `Loose`, fast on slopes |
| Scorpion Crews | Light artillery | Bolt-throwers; precise, punish dense blocks |
| Mounted Scouts | Light cavalry | Few in number, mostly for spotting and chasing routers |
| **The Iron Champion** *(name TBD)* | Champion | **The only full-plate soldier in the world.** A very large man, extremely high mass and armour, slow, tires quickly. Near-immune to arrows, causes fear, can smash through a line |

### 4.2 Calderra — House Caldravi · "Pride of the Plains"
*Plains kingdom of the continent's finest horses and largest cavalry. Proud, devout, contemptuous of cleverness — and historically bad at fortifications.*

**Faction traits**
- **Born in the Saddle** — cavalry gets better charge, speed and stamina than any other faction's.
- **Pride** — Calderran morale is high while winning and while advancing, but voluntary withdrawals cost morale, and a broken *noble* regiment hurts nearby morale more than usual. A noble is "expected to have bled for something".
- **Faithful** — a pre-battle blessing gives a morale bonus at the start of the fight that fades over time *(the post-1500 CR turn to faith)*.
- **Blind Spot** — siege equipment is fewer and slower to build than for the other factions, and morale drops while attacking walls. Canon: Calderra "underestimates fortified cities".

| Regiment | Role | Notes |
|---|---|---|
| Royal Horse Guard | Elite shock cavalry | The crown's own bloodlines, the best horses in the kingdom; general's bodyguard tier |
| Vantry Lancers | Shock cavalry | House Vantry's regiments: old-fashioned, proud, devastating charge |
| Plains Riders | Light cavalry | Javelins, fast, chase and harass |
| Mounted Archers | Missile cavalry | Composite bows, skirmish behaviour |
| Calloway Spears | Line infantry | Toll-road levies from House Calloway: solid but unglamorous |
| Shieldbearers | Heavy-ish infantry | Professional foot to hold a centre while the horse wins the battle |
| Plains Slingers | Missile infantry | Cheap, long range, weak vs armour |
| Ram & Ladder Crews | Siege | Slow to deploy (Blind Spot) |
| Onager Battery | Artillery | Needed for the Gate; few in number |

*Colours: Calderran cavalry blues (Prince Dorric wears them at court).*

### 4.3 Valdora — House Corivane · "The Machine at the Gate"
*Masters of the Great Gate and the Canyon Road. Process over glory, excellent at defending, and rock-competent as a matter of course.*

**Faction traits**
- **The Machine** — the best discipline in the game: regiments hold formation longer, and the faction alone can use **Rank Rotation**.
- **Gatekeepers** — bonus morale and defence when fighting on or within 100 m of walls, gates or towers they own.
- **Rock-Competent** — certain regiments can climb walls and cliffs without ladders *(the Gate's canyon face is inspected off pegged ladders and roped stations; noble sons grow up on those inspection ways)*.
- **Kervaran Drill** — fighting alongside Kervaran regiments gives both a small morale bonus *(House Vallenar's joint garrison exercises)*.

| Regiment | Role | Notes |
|---|---|---|
| Gate Wardens | Elite defensive infantry | House Vallenar's garrison; best when on the walls |
| Corivessan Line | Line infantry | The standard, drilled, rotating line |
| Canyon Climbers | Light infantry | `Climber`: scale walls and cliffs, ambush from high ground |
| Gate Archers | Missile | Composite bows, bonus range from towers |
| Marcher Riders | Medium cavalry | Prince Varen's punitive frontier riders: ruthless, good at running down routers |
| Dellorin Horse | Light cavalry | Foothill horses from House Dellorin's stock |
| City Militia | Levy infantry | Corivessa's citizens, defence only, low morale |
| Wall Engines | Fixed artillery | Ballistae and onagers mounted on the Gate and towers |
| Toll-House Crossbows | Missile | Castellon customs guards, short range but accurate |

---

## 5. Main Scenario — The Battle of Corivessa *(working title)*

**Calderra vs Valdora, with Kervara as Valdora's ally**, on the plains before Corivessa and at the Great Gate.

### 5.1 The map
From west to east:
1. **The Oravian plains**: open grassland, Calderra's ideal ground. The Calderran host deploys here.
2. **Valdoran farmland**: fields, irrigation ditches, orchards, farmsteads, all watered by **the river that comes out of the canyon from Kervara's basin**. This is cavalry-unfriendly ground with a few bridges and fords.
3. **Corivessa**: the capital, built directly against the Gate. Plains-facing walls, towers and a main gate.
4. **The Great Gate**: the monumental wall-fortress-tower complex spanning the canyon mouth.
5. **The canyon mouth and Canyon Road**: the eastern map edge, where Kervaran reinforcements enter.

The geography tells the story: Calderra wants the fight in zone 1, Valdora wants it in zones 2–4.

### 5.2 Phases
Playable as one long battle or as separate linked battles:

| Phase | What happens | Design focus |
|---|---|---|
| **A. The Field** | Valdora can sally onto the plains or hold the farmland line. Calderra tries to force an open-field fight | Cavalry vs drilled infantry, terrain choices |
| **B. The Walls** | If Valdora holds, Calderra must assault Corivessa's walls | Sieges, ladders, rams, wall engines, the Blind Spot trait |
| **C. The Relief** | At a set time (or triggered by events), the Kervaran relief force arrives through the canyon and the Gate | Reinforcements, timing, the Iron Champion's entrance |

**Victory**
- *Calderra*: break the Valdoran army in the field, **or** take the city's central plaza / gatehouse before the relief force decides the battle.
- *Valdora / Kervara*: hold the city until the relief force arrives, then break the Calderran army, or kill the Calderran general.

### 5.3 Who commands (suggestions, all open)
- **Calderra**: Prince Dorric Caldravi, who commands the royal cavalry, or Prince Landric, the glory-seeking heir.
- **Valdora**: Lord Warden Ardin Vallenar on the Gate; Prince Varen leading the cavalry.
- **Kervara relief**: Prince Bram Marcellus with the home garrison (his wife Livia was born Corivane, so it's personal), or Lord Marshal Warrick Dravenne with the mercenary companies.
- **Narrative hook:** Princess Alys, King Tavian's sister and Varen's wife, is inside the city.

### 5.4 Supporting battles (later)
Smaller maps that teach the systems before Corivessa:
1. **Field skirmish on the Oravian plains** (Calderra vs Valdora): open-field basics.
2. **Canyon ambush on the Canyon Road** (Kervara vs Calderran raiders): chokepoints and Hidden Paths.
3. **Kervara vs Calderra on the plains**: pike vs horse, the cleanest test of the rock–paper–scissors.

---

## 6. Sieges

- **Walls** have walkways, towers, and segment health. Defenders on walls get height and cover bonuses.
- **Gates** have health; rams attack them. Defenders can sally out through an open gate.
- **Ladders** are fast but leave climbers vulnerable; **siege towers** are slow but deliver formed troops. Valdoran climbers don't need either.
- **Wall engines** fire from towers and can be captured.
- **The Great Gate** itself is a set piece: much stronger than the city walls, with layered gates and killing grounds. Assaulting it should feel close to impossible without numbers and time. Canon: Valdora "can halt armies many times its size at the gate".
- **Victory point**: capturing and holding the plaza or gatehouse.

---

## 7. AI

- **Battle AI** works in layers: an army-level plan (attack, defend, delay), then a role for each group of regiments (centre, flank, reserve), then each regiment's own micro (target choice, formation, charge timing).
- **Faction personality**: Calderra AI seeks open ground and big charges; Valdora AI holds terrain and walls; Kervara AI advances steadily and protects its flanks.
- v1 AI only needs to be *competent and readable*, not brilliant.

---

## 8. Camera, Controls & Presentation

- Free RTS camera (pan, rotate, zoom from general's-eye to overhead) and a tactical map overlay.
- Box-select, drag-to-form-line (drag width = line width), group hotkeys, pause-and-order.
- **Art style: TBD.** Stylized low-poly makes it far easier to find assets and build them in Blender; realistic is harder and slower. See §11.

---

## 9. Technical Direction

**Engine:** Godot 4 + C#. **The M0 performance test passed on 2026-11-21:** ~205 fps at 2,000 soldiers and ~61 fps at 10,000 on an M1 Pro, with no optimisation (see `docs/PerfTest-M0.md`). Unity stays the fallback.

- **Simulation vs rendering split.** Simulation runs in plain C# data structures (regiments and soldier arrays) at a fixed tick. Rendering reads that state. This makes the sim testable and portable if the engine ever changes.
- **Rendering soldiers**: GPU instancing (`MultiMeshInstance3D` in Godot) with **vertex animation textures** baked in Blender.
- **Movement**: flow fields for regiment-level pathing, plus simple local steering and separation per soldier.
- **Data-driven units**: all regiment stats in data files (JSON or Godot resources), so balancing never needs code changes.
- **Determinism** (nice to have): a fixed-tick sim makes replays and later multiplayer possible.

### Performance test (Milestone 0)
In Godot 4, build a flat map with **2,000 soldiers in 20 regiments** that march in formation, clash and fall, at **60 fps** on your machine. If it passes, commit to Godot. If it fails after reasonable optimisation, switch to Unity before any real content is built.

---

## 10. Roadmap

Moved to `docs/PLAN.md` (phases, session cards, playtests). Live progress: `python3 tools/status.py`.

---

## 11. Open Questions

1. **The Iron Champion:** what is his name and canon? Who is he, which house or company, why is he the only one in plate?
2. **Is the Battle of Corivessa canon** (when and why does Calderra march), or a game-only "what if"?
3. **Stirrups and couched lances:** okay to assume? They're a big part of how Calderran cavalry would feel.
4. **Trebuchets:** in or out? After 200 more years of Roman engineering they're plausible.
5. **Art style:** stylized low-poly or more realistic?
6. **Army size goal:** Total War scale (thousands per side) or smaller, more readable battles (a few hundred)?
7. **Faction colours and banners** for Kervara and Valdora (Calderra already has its cavalry blues).
