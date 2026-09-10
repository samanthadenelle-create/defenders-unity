# Defenders of the Realm — Agent Rules & Project Memory

This file is read by every agent (UI and CLI) before starting work.
Rules here are **non-negotiable**. Do not skip them to ship faster.

**⛔ PREFLIGHT GATE (owner directive 2026-06-28, BINDING):** before you TOUCH ANY CODE or START
DEBUGGING — unprompted, or whenever the owner says "answer the preflight gate" — open
`PREFLIGHT_GATE.md` (repo root) and answer **YES + a one-line proof** to every applicable item.
A single NO / "I think so" / unproven YES = STOP; you have not earned the edit. The owner should never
again have to remind a CLI to read canon, be the SME, instrument first, or update canon — the gate makes
those a hard yes/no checklist.

**READ FIRST, EVERY SESSION (owner directive 2026-06-20, BINDING):** load
`SESSION_CANON_LOADER.md` (repo root) — the at-a-glance SME primer (core rules +
current state + key files) the owner pastes to start a session. It is the fast path;
the depth below + the deep-dive docs remain binding. THEN do the mandatory catalog read.

**MANDATORY FIRST STEP (BINDING — read before ANY work, every session):** read
`docs/MASTER_CATALOG.md` — the exhaustive, file-by-file catalog of every
class/method/asset/scene/data-file/doc, with the architecture map + a prioritized
stale/risk ledger. Then read the relevant `docs/MASTER_CATALOG/<area>.md` section
for whatever you're about to touch. It is verified **from the actual code, NOT from
comments** (comments lie — e.g. `HeroLocomotion`'s "pure transform" header hides that
it's a `NavMeshAgent`; trusting it mis-diagnoses every movement bug). **Be the
subject-matter expert BEFORE you change anything.** No fixing, building, or
claiming-fixed on assumptions — this supersedes guess-and-grep. Keep the catalog
current when systems change.

**Architecture law (BINDING):** read `docs/ARCHITECTURE_PRINCIPLES.md` first. The
project follows the owner's **HP B2B** architecture — bounded context per component,
scope deliberately limited; **presentation is a separate layer that never touches the
objects**; queue by **player-felt vs. holistic leverage**, never smuggle structural
refactors into player-facing work. **Decision lens: what is right, not what is easy** —
when they diverge, name it explicitly. We stay true-north by **holding each other
accountable to the real goal: deliver QUALITY, not fast.**

**Navigation:** new session? read `docs/HANDOVER.md` first — the single operator's manual
(how we work + the binding rules + this-session's new canon + the build/gate/bake cycle + resume points).
Then, before grepping or exploring, check the README system —
`PROJECT_INDEX.md` (root files), `Assets/README.md` (asset folders),
`Assets/_Modules/README.md` (code module map; each module has its own README),
`docs/README.md` (docs index). Keep these updated when you add/move files.
For architecture orientation, **`docs/ARCHITECTURE.md` is the single authoritative hub**
(HP B2B lens + assembly map + world/scene + data/catalog + save + build mode + instrumentation;
it indexes the per-area deep-dives). Read it before the individual `*_ARCHITECTURE.md` docs.

---

## 0. CRITICAL: Linux Mount ↔ Windows Sync Is Unreliable

**UI (Claude) must NEVER edit `.cs` files via bash or the Linux mount path.**

The Linux mount (`/sessions/.../mnt/defenders-unity/`) does NOT sync reliably
to the Windows **repo root**.

**⚠ THE REPO ROOT IS MACHINE-DEPENDENT — never hardcode it (owner ruling 2026-08-09).** It is
`C:\eoa` on one machine and `D:\eoa` on another. This line used to name `C:\EoA\` as "the project's
home", which is why a seat on the other machine could follow canon to a path that does not exist.
Write paths **relative to the repo root** (`Assets/...`, `logs/debug/...`, `tools/...`) in every doc,
work order and script; resolve the absolute root at runtime, never from a doc.
*(History: cloned fresh from GitHub `feat/tower-core-loop`, with the gitignored `Assets/polyperfect`
and `Assets/Quaternius` packs copied in from the old `Documents\defenders-unity` location.)*
Writes via bash can truncate, duplicate, or interleave — producing garbled Windows
files that fail to compile even though the mount shows them as correct.

**Rules:**
- UI writes `.cs` files using the **Write / Edit tools only** (Windows path)
- UI never uses `cat >`, `echo >>`, or any bash redirect to fix `.cs` files
- If a `.cs` file is broken on Windows, **only CLI fixes it** (directly on Windows)
- UI "fixing" a file on the mount will corrupt the Windows version further

**Symptom:** brace check passes on mount (`69/69`) but CLI sees a garbled file
(`82/95`). This means the mount and Windows are out of sync. Stop writing, tell CLI.

---

## 1. MANDATORY: C# File Quality Gate

After editing **any** `.cs` file, run this check before reporting done:

```python
python3 -c "
import sys
path = 'Assets/path/to/File.cs'
content = open(path).read()
opens  = content.count('{')
closes = content.count('}')
if opens != closes:
    print(f'BRACE MISMATCH in {path}: {opens} open vs {closes} close')
    sys.exit(1)
print(f'Braces balanced ({opens}) ✓')
"
```

Run for **every file you touched**. If the check fails, fix before returning.
**Never ship a C# file with mismatched braces.** CLI will revert it and the
work order goes back to the queue.

**NUL-byte guard (WO-434):** the compile gate (`DeNelle.Editor.CompileGate.Run`)
now also scans every `.cs` under `Assets/` for embedded/trailing NUL bytes (`\x00`)
and REJECTS the gate (withholds the `COMPILE_GATE_OK` marker) if any are found —
this catches §0 mount-garbled files that look byte-clean on Windows/HEAD but carry
NULs that poison a commit and break compilation.

---

## 2. Work Order Protocol

- **UI (Claude) — NEVER touches code (BINDING, owner 2026-06-13).** UI does RCA, work
  orders/specs, narrative, screenshots/images/mockups, board grooming. It does **not** write
  or edit `.cs` (no exceptions — supersedes any prior "UI writes code with a gate" reversal).
  Code it wants written goes to CLI as a spec/work order.
- **CLI (me):** writes + build-verifies **ALL** code. Sole git committer. Owns batchmode execution.
- **Owner (Samantha):** PM, catalogs, routes, makes all final creative decisions.

### Creating work orders
- Save to `WorkOrders/WORK_ORDER_NNN_short_name.md` (moved out of root 2026-06-22 to declutter; the numbering authority `CLI_LANES_WO_NUMBERS.md` + `WO_AUDIT_*.md` stay at root)
- Mark **Status: READY TO IMPLEMENT** when spec is complete
- Include files to edit, acceptance criteria, and what NOT to touch
- **WO-numbering authority = the `CLI_LANES_WO_NUMBERS.md` banner. SOLE authority — nothing else.** Not the filesystem max, not `MASTER_PIPELINES_BACKLOG_2026-06-06.md` (that is a lane/backlog doc, NOT a number source — treating it as a second authority is how numbers collide), and **never a number copied into any other doc** (every copy goes stale; point at the banner instead).
  - **TWO DISJOINT BLOCKS are in use** (2026-08-02, after FIVE two-seat collisions in one day): **main line → CLI**, and **a reserved block → the UI seat**. The blocks are disjoint so both seats mint in parallel without reading each other's state. **⚠ THE BLOCK RANGES ARE DELIBERATELY NOT WRITTEN HERE — read them off the banner.** This line used to name "860–899" for the UI seat; that block was CLOSED (full at 899) and the seat moved on, so the number sat here stale and kept re-seeding the very collision it was meant to prevent. A number copied into a second doc is the bug, even when it was right the day it was written.
  - **THE RULE: each seat bumps ITS OWN banner row in the SAME edit as the mint.** A mint written to disk without bumping the banner is the collision — that is what broke 5× on 08-02, including by the CLI. Collisions resolve **first-on-disk-and-referenced-wins**.
  - Still slot every new WO into a lane in the master backlog doc — but take the NUMBER from the banner.
- **LIVE BOARD = `BOARD.html` (repo root), GENERATED from the repo** — run `python tools/board_build.py`
  (2 s; parses `WorkOrders/*.md` statuses + RESULT markers + the numbering banner). The repo IS the
  source of truth; the board is a derived view and cannot drift. Regenerate at session boot and before
  any board read. **Notion is RETIRED as the board (owner ruling 2026-08-08):** the old mirror's
  workspace was reachable by NO seat (CLI has no MCP auth; the UI connector authed to a different
  personal workspace — the DB 404s), which is how items were getting lost. Do not hand-mirror to
  Notion; `NOTION_SOURCE_OF_TRUTH.md` is superseded. (History: Linear → Notion → derived board.)

### Completing work orders
- CLI saves a `WorkOrders/WORK_ORDER_NNN_short_name.RESULT.md` when done and verified
- **The WO's own `**Status:**` line is flipped to DONE in the SAME COMMIT as the work** — the board
  is DERIVED from it (`python tools/board_build.py`), so there is no second system to mark.
  **⚠ LINEAR IS RETIRED and so is Notion (owner ruling 2026-08-09 / 2026-08-08).** The history is
  Linear → Notion → the derived board; this line used to say "UI marks the matching Linear issue as
  Done" and that instruction was dead. Do not mark, mirror, or read either. See `docs/BOARD.md`.

---

## 3. Scene Files — Hard Rules

- **NEVER hand-edit `Village.unity`** — corruption-on-resave history.
  Always rebuild via `Defenders > Week 3 > Build Village Scene`
  (batchmode: `DeNelle.Editor.VillageSceneBuilder.BuildVillage`)
- **Never run a bake if the Unity editor is open** — project lock.
- Bake commands go in a work order for CLI. UI does not fire batchmode.

---

## 4. Polyperfect Assets

- Pack location: `Assets/polyperfect/Low Poly Ultimate Pack/`
- **Gitignored** — re-import on fresh clone via `Defenders/Art/Fix Polyperfect URP Materials`
- Always use `_M` quality tier prefabs: `_M/Prefabs_M/<Category>_M/`
- Catalog: `docs/polyperfect-asset-catalog.md` — check before referencing any prefab name
- On missing prefab: `Debug.LogWarning` (not error) — pack may not be imported

---

## 5. Assembly Structure

| Assembly | Namespace | Contents |
|---|---|---|
| `DeNelle.Core` | `DeNelle.Core.*` | Interfaces, enums, pure data (IDamageableStructure, IVillageHud, IAudioService, CoreServices, MusicTrack, SfxId) |
| `DeNelle.Village` | `DeNelle.Village` | Enemy, EnemyBrain, WaveManager, HeartController, HeroHealth, buildings, gates |
| `DeNelle.HUD` | `DeNelle.HUD` | VillageHudController — passive display only, never references Village |
| `DeNelle.Audio` | `DeNelle.Audio` | AudioService, SfxClipLibrary |
| `DeNelle.BattleATB` | `DeNelle.BattleATB` | ATBCombatManager, BattleController |
| `DeNelle.Editor` | `DeNelle.Editor` | VillageSceneBuilder, AnimatorSetup — editor-only |

> ### ⚠ THE TABLE ABOVE IS A SUBSET, NOT THE MAP (corrected 2026-08-09, owner ruling on RULES.md C-6)
> ⛔ **DO NOT RESTATE THE COUNT HERE — COUNT THEM:** `find Assets/_Modules -name '*.asmdef' | wc -l`
> (plus `Assets/Data/DeNelle.Data.asmdef`, which lives outside `_Modules/`). This line said **19** from
> 2026-08-09 until 2026-09-06, when the command returned **25**. A hand-maintained count tracking a live
> tree is duplicated state and fails exactly like the stale WO block (§2) and the retired table below.
> **READ THE `.asmdef` — it is the authority on what may reference what.** The six rows above are a
> convenience list of the ones you touch most; they are not the dependency graph.

**Cross-assembly rule — ONE invariant, and it is the one that is actually enforced:**

> ## ⛔ `DeNelle.HUD` NEVER REFERENCES `DeNelle.Village`, in either direction.
> `Assets/_Modules/HUD/DeNelle.HUD.asmdef` references `DeNelle.Core` + `DeNelle.Data` ONLY.
> `Assets/_Modules/HUD/AdminOverlay.cs` reaches a Village type by **reflection precisely because the
> asmdef forbids the reference** — that reflection is evidence of the rule, not a violation of it.
> Cross-module calls go through `CoreServices.Hud` / `CoreServices.Audio`, always with `?.`.

**⚠ THE OLD RULE HERE — *"Village → Core only. HUD → Core only"* — WAS FALSE AND IS RETIRED.**
The authority is the file: `Assets/_Modules/Village/DeNelle.Village.asmdef`, whose `references` array
runs `:4-28`. Read there 2026-09-06, its DeNelle entries are `DeNelle.Core` (`:5`),
`DeNelle.Commerce` (`:6`), `DeNelle.BattleATB` (`:7`), `DeNelle.AI` (`:8`), `DeNelle.Cosmetics` (`:9`),
`DeNelle.Data` (`:10`), `DeNelle.Pets` (`:11`) and `DeNelle.Audio` (`:20`), alongside Unity and
third-party assemblies. **That reading is dated, not an authority — open the file.**
⛔ This paragraph named **`DeNelle.Wallet`** from 2026-08-09 until 2026-09-06 and that string is NOT in
the file; the commerce/wallet-facing reference is `DeNelle.Commerce` (`:6`). A seat "restoring" the
listed name would have added a reference to an assembly this module does not use — the copy failing in
the same breath as the rule it was written to state. A seat reading the retired rule literally would
instead have rejected working code as a violation. Same duplicated-state drift that produced the stale
WO number block (§2) and the hardcoded repo root (§0). **Do not restore a hand-maintained dependency
table here; point at the `.asmdef` instead.**

---

## 6. Key Interfaces (all in `DeNelle.Core.Combat` / `DeNelle.Core.*`)

- `IDamageableStructure` → `Assets/_Modules/Core/Combat/IDamageableStructure.cs`
  Implemented by: HeartController, HeroHealth, Building, Tower, Gate, WallSegment
- `IVillageHud` → `Assets/_Modules/Core/HUD/IVillageHud.cs`
  Implemented by: VillageHudController. Resolved via `CoreServices.Hud`
- `IAudioService` → `Assets/_Modules/Core/Audio/IAudioService.cs`
  Implemented by: AudioService. Resolved via `CoreServices.Audio`

---

## 7. Naming & Canon

- Village name: **Elarion** (never "Avalon" — retired, DESIGN-DECISIONS.md #1)
- Heart of Elarion: the world tree / stone reliquary at scene centre (0,0,0)
- No Keep building — removed (DESIGN-DECISIONS.md #3)
- Hero tag: **`Player`** (one tag per GameObject — locomotion, camera, HUD, triggers all `FindWithTag("Player")`; set in `HeroControlEnsurer.Ensure`, WO-450)
- Enemy AI finds the hero by **component** (`FindFirstObjectByType<HeroLocomotion>()`), NOT a `HeroTarget` tag — that tag was never declared; a GameObject has only one tag
- ⛔ **ENEMY SPAWNS RESOLVE BY COMPONENT, NEVER BY TAG. The `SpawnPoint` TAG DOES NOT EXIST**
  (verified 2026-08-24: `ProjectSettings/TagManager.asset` declares exactly **four** tags — `Tower`,
  `Building`, `HeartTarget`, `Player`). ⚠ `FindGameObjectsWithTag("SpawnPoint")` **THROWS on an
  undeclared tag**, and this line previously asserted the tag existed — which is how WO-1038 shipped:
  `CastleDefensePlansService` read that tag, **every scan died there, and the drop never spawned**
  (owner F8 seq 2434-2442; RCA recorded in-code at `CastleDefensePlansService.cs:266-274`).
  The two places that still WRITE the tag wrap it in `try/catch` for exactly this reason
  (`Editor/CastleHubBuilder.cs:2912`, `Editor/Village2Playable.cs:711`).
  **The real seam:** `WaveSpawnPoint` (`Village/Waves/WaveSpawnPoint.cs:29`), found by
  `FindObjectsByType<WaveSpawnPoint>()` (`WaveManager.cs:1209`) — there is **no registry**. Each marker
  carries `SpawnId`, **`GateIndex` (0 N / 1 E / 2 S / 3 W)**, `Direction`, `GatePosition`,
  `HeadingToGate`. `CastleSpawnPointInjector` injects **20** at runtime (5 per side x 4 sides), and
  **skips entirely if any `WaveSpawnPoint` already exists**. ⚠ `FindObjectsByType` is UNORDERED — use
  `WaveSpawnResolver` for deterministic ordering (it exists because a boss entered from a random side
  every session).
- **Home hub scene = `Main_Castle_Overworld`** (MergedWorld ON, one navmesh). ⚠ `MainCastle_Hall.unity`
  still exists on disk as a LEGACY file — it is NOT the hub; that ambiguity keeps re-seeding stale docs.
  `Village.unity` + `OuterWorld.unity` are DELETED from the tree.
- Player-facing renames (canon-strings.json): tagline = **"Echoes of a Forgotten Civilization"**
  (retired "Hold the last light", 2026-07-24); HUD "Pets" → **"Echoes"**; HUD "Work" → **"Queues"**.
- **BOTTOM ACTION BAR — the 08-01 retirement is REVERSED (owner ruling WO-911 Q10+Q13, 2026-08-06).**
  The 2026-08-01 rule ("the bar Queues BUTTON is RETIRED; the Builders chip is the one Queues entry")
  **no longer holds as written.** The rule it protected — **exactly ONE Queues entry** — still holds; the
  entry MOVED. Current canon:
  - The **`Upgrade` bar face is RE-POINTED** to the unified **Manage/Queues** screen (`PanelId.Manage`,
    `ManageScreenPanel`). It is **RE-POINTED, NOT ADDED**: `ActionBarButtonId.Upgrade = 6` keeps its
    value, its widget id (`upgradeButton`) and its `hud-areas.json` row, which is what dissolves the
    8th-face problem entirely. It is now **always applicable in town**, not context-gated on a focused
    building.
  - **Map LEFT the bar.** ⚠ **`FeatureFlags.MapTab` NO LONGER EXISTS — it was DELETED 2026-09-05
    (WO-1396), read `Assets/_Modules/Core/FeatureFlags.cs:843-849`.** This line used to say Map was
    "a tab inside Bag, feature-flagged OFF (`FeatureFlags.MapTab`, PlayerPrefs `ff.maptab`)"; that
    flag gated a Bag-side door (`InventoryUIBuilder.OpenRealmMap`) that shipped OFF and was never
    offered, so the map had a dormant second door and no live one. **The Realm Map's ONE public door
    is now the Journey deck card** (`PlayerDeckWorkspace`, `PlayerDeckKind.Journey` -> `PanelId.RealmMap`),
    and `PublicNavigationRetirementRegression` pins that the flag and the Bag route stay gone.
    `ActionBarButtonId.Map` stays **dormant at ordinal 4** — never renumber it, the face arrays are
    indexed by ordinal.
  - ⛔ **THE BOTTOM BAR THE PLAYER TOUCHES IS THE ADAPTIVE PEACEFUL DOCK, NOT `HudActionBarModel`.**
    `HudKitController.BindActionBar` **returns early** whenever the peaceful dock exists — it
    disables every legacy bar face and never subscribes the model at all. So `HudActionBarModel`,
    `ComputeMask` and `MaxVisibleFaces` do **not** drive what ships, and no reasoning about the
    shipped bar may start from that constant. The dock is built by
    `HudKitController.BuildAdaptivePeacefulDock` and laid out in reference pixels by
    `HudDockSlotLayout` / `DeNelle.Core.UI.HudDockLayout`.
    ⛔ **DO NOT WRITE A FACE COUNT, A FACE LIST OR AN ORDER INTO THIS FILE — RUN THE ORACLE.**
    `HudActionBarRegression.CheckMeasuredPeacefulDock` (WO-1467) **builds the real dock** and reads
    the count, the captions, their left-to-right order, the touch floor and the label fit **out of
    the built tree**. It is the single authority, it cannot go stale, and it is the answer to
    "how many faces does the bar have" — never a sentence here.
    ⚠ **THIS LINE WENT STALE THREE TIMES AND THAT PATTERN IS THE REAL FINDING.** It said "SIX faces"
    flat; the 2026-08-26 correction cost a felt-test report and an RCA opened against working code;
    by 2026-09-03 the correction was itself wrong (commit `a17dfa126`: *"The doc is stale, not the
    code."*); and on 2026-09-06 the replacement was wrong in a new way — it restated the constant,
    described a `Talk`-is-conditional `ComputeMask` branch that no longer exists, and pointed at a
    source-text lint as the dock's coverage. Hand-maintained state in canon tracking live code is
    **duplicated state**, exactly like §2's stale WO-number block, §5's retired dependency table and
    §16's copy-pasted R2 verify. **The cure is not a better copy — it is deleting the copy.**
    `HudActionBarModel.ButtonCount` is a **different axis** — the enum-identity / array bound, and
    the one that must never be renumbered. Read it, and the ordinals, off
    `Assets/_Modules/Core/HudModel/HudActionBarModel.cs`; the ordinal pins live in
    `SessionShapeRegression` `Case5_BarShape`.
  - The right-column **Builders chip SURVIVES as a STATUS GLANCE ONLY** (count/timer + the inline peek
    rail). Its old **double-tap door is retired**, so the bar face is the single entry.
- **Echo harvest affinity is a MATCH BONUS, NEVER a lock** (owner ruling WO-830, 2026-08-02): each of the
  six Echoes carries a unique affinity, but **the player picks each Echo's harvest resource** from a
  picker — matching that Echo's affinity pays an **ADDITIVE MATCH BONUS**. Never gate an Echo to one
  resource.
  **⚠ "DOUBLES the yield" WAS FALSE AND IS RETIRED (corrected 2026-08-16, WO-1108 §2).** The bonus is an
  additive term inside a spec-SUM, not a multiplier: `LaneContribution = baseContributionPerEcho +
  (match ? preferredLaneMatchBonus : 0) + perLevelBonus*(level-1)`
  (`EchoBonusCalculator.LaneContribution`). Live values in `echoes-balance.json`: base **0.02**, match
  **+0.03**, perLevel 0.01 — so a matched Lv1 Echo reads **+5% vs +2%** (2.5x the per-echo TERM, ~+3%
  absolute on the aggregate), which is the owner's own "+5% not 55%" ruling recorded in that file's
  `_authoringNotes`. A seat implementing the retired sentence literally would have shipped a ~20x buff.
  **Maren harvests Crystals, not Repairs.** Persisted token grammar = **`<resource>:<level>`**
  (e.g. `harvest:3` → `wood:3`); read-migrated, no schema bump.
- **Echo REPAIR is PASSIVE and COUNT-DRIVEN — never an assignment** (owner ruling WO-1108, 2026-08-16:
  *"the number of pets that we have just passively takes towards healing"*). The WO-811 "Repair
  structures" picker chip is **RETIRED**; `EchoBonusCalculator.RepairFractionsPerSecond()` sums **every
  OWNED Echo** (count x level), so adding an Echo raises the mend rate with no assignment change. A
  stored `repair:N` token **read-migrates to that Echo's affinity harvest resource** — never to `idle`,
  which would silently zero its yield. No schema bump. The pace knob `repairFractionPerHour` now lives
  in `echoes-balance.json` (0.35, re-tuned down from the old code-only 2.0 so a full roster lands near
  the old single-Echo rate instead of 6x-ing it).
- **The Echo has exactly ONE appearance owner: `EchoWorldPresence`** (WO-1108 Lane B, 2026-08-16). It
  escorts the player to the gate, vanishes, and returns **once** after the battle — one owner, one
  lifecycle, no second spawner. `PetDeployer.DespawnEcho` (`Assets/_Modules/Pets/PetDeployer.cs:442`)
  is the **FIRST despawn path in the game**: nothing had ever removed a deployed pet before, so treat
  it as the seam, not as one of several. Pinned by `Editor/Regression/EchoWorldPresenceRegression.cs`.
- **Ranger: the BOW is an ACTION-BAR ability (slot Q); the PRIMARY attack is the melee/dagger sweep**
  (owner ruling WO-1105 R5, 2026-08-16). `ranger.q` (Quick Shot) has **always** been slot Q, and Q is
  the class's LOCKED basic — only W/E/R are loadout-swappable. The path that also fired the shot from
  the PRIMARY input is deleted (`FirePrimary` / `FireRangedPrimary` / `ResolveRangedTarget` /
  `ResolvePrimaryFace`), so **the phone's one attack button never spends an arrow**. Primary is the
  melee sweep for every class.
  - ⚠ **DISPUTED 2026-09-06 — WO-1504 is open and the owner has not ruled. Do NOT act on either side.**
    The bullet above says the primary is the melee sweep for EVERY class; the code still DERIVES it per
    class. `HeroAbilities.TryGetRangedPrimary`
    (`Assets/_Modules/Village/Hero/HeroAbilities.cs:499-515`) returns TRUE when the locked Q is a
    long-range `strike`/`drainshot`, and its own `FlowTrace.Once` message (`HeroAbilities.cs:507-513`)
    reads *"the primary attack input fires THIS, and the melee sweep becomes the offhand verb."* It has
    LIVE callers, not dead ones: `PlayerAttackController.cs:859-860` computes
    `basicIsMelee = _abilities == null || !_abilities.TryGetRangedPrimary(EffectiveRange(), out _)`, and
    `HeroTargetIndicator.cs:277` gates auto-acquire on the same call. That assignment is PINNED by
    `Assets/Editor/Regression/PrimaryFallbackRegression.cs:388-390`, which FAILS if it is removed — so
    the seam cannot simply be deleted to match the prose. Ticket:
    `WorkOrders/WORK_ORDER_1504_ranger_primary_input_still_fires_quick_shot.md`. Until it is ruled, this
    bullet is CONTESTED, not canon.

---

## 8. Pipeline State Quick Reference — A POINTER TABLE, NOT A SNAPSHOT

⛔ **NO LIVE NUMBER, VERSION, COUNT OR FLAG STATE MAY BE WRITTEN INTO THIS SECTION.** Converted
2026-09-06 (WO-1481) from a dated "refreshed 2026-08-02" snapshot that had rotted where it stood: it
named a save-schema version that `Assets/_Modules/Core/State/SaveSchema.cs:41` had already moved three
bumps past, and it cited `RepoProps.cs:69` for the level ceiling while the const actually sits at
`Assets/_Modules/Core/Catalog/RepoProps.cs:111`. (Both re-read at source 2026-09-06; the wrong values
are deliberately not repeated here, because repeating one is how this section rotted.) **The copy is the bug, not the
value inside it** — the identical duplicated-state failure §2, §5 and §16 each describe in their own
words. Do not "refresh" a row below; read the authority and leave this file alone. Row shape matches
`PIPELINE_STATE.md:15-22`, which already works this way.

| Fact you want | Authority to read it from (each path opened 2026-09-06) |
|---|---|
| Everything current | the newest `CANON_GROUND_TRUTH_<date>.md` at repo root, plus `KEY_FACTS.md` |
| Save schema version + the per-version changelog | the const at `Assets/_Modules/Core/State/SaveSchema.cs:41` — read the CONST, never the file's own header at `:11-18`, which records that it was itself two versions stale |
| Structure / upgrade level ceiling | `RepoProps.MaxStructureLevel` (`Assets/_Modules/Core/Catalog/RepoProps.cs:111`) — the SINGLE ceiling; never re-hardcode one |
| Storage container capacity ladder | `Assets/_Modules/Core/Economy/StorageCapsCatalog.cs` (with `TownBankCapacity.cs`, same folder) |
| Queue depth cap vs. build concurrency | `BuildTimerConfig.queueDepthPerLine` (`Assets/_Modules/Core/Catalog/BuildTimerConfig.cs:269`) vs `freeBuildSlots` (`:231`) — DIFFERENT AXES, and `:249` says so in the code |
| Whether wave rosters are authored or generated | `Assets/Resources/Data/Canonical/waves.json:14` (`_RETIRED_batchFields`) + `Assets/Data/Tests/WaveDataTest.cs` |
| Feature flag defaults, and whether a flag still exists at all | `Assets/_Modules/Core/FeatureFlags.cs` — plus, for a RETIRED flag, the regression that pins its ABSENCE |
| Assembly dependencies | the `.asmdef` files themselves (§5) |
| Which gate marker proves which run | `Assets/Editor/Regression/DataRegression.cs:14-22` — it names each entry point and its own distinct marker |
| Suite counts | never a doc: the `<n>/<n>` inside the marker, on a FRESH log |
| Board / ticket status | `BOARD.html`, regenerated by `python tools/board_build.py` |
| Next free WO number | the `CLI_LANES_WO_NUMBERS.md` banner rows (§2) |
| Remote content paths / R2 | `Assets/AddressableAssetsData/AddressableAssetSettings.asset` (§16) |

**Rulings that carry no live number, so they stay here as prose:**
- Defend-the-Tower (PatriciaLight): **REMOVED (2026-06-09)** — module + scene gone; only the art under `Assets/Resources/PatriciaLight/tower2/` was kept (directory listed present 2026-09-06)
- Home hub: **`Assets/Scenes/Main_Castle_Overworld.unity`** (merged world, one navmesh); **Village2** = raid target. `Assets/Scenes/Village.unity` and `Assets/Scenes/OuterWorld.unity` are DELETED — both paths returned "No such file or directory" when listed 2026-09-06
- **⛔ `RaidHeroSpawner` NEVER EXISTED — do not go looking for it** (WO-1109, 2026-08-16). The raid scenes never had a hero spawner class; the **emergency pill-hero was the NORMAL path**, not a fallback, and it carried **no abilities at all**. Raids now carry the real hero across. `git log -S"RaidHeroSpawner"` returns only the commits saying it never existed — a session that hunts for it loses a morning to a class that has never been in the repo.
- **Storage containers climb through the full structure ladder** (WO-1108b, 2026-08-16): each level multiplies that resource's cap and the cost doubles per step — read the ladder off `Assets/_Modules/Core/Economy/StorageCapsCatalog.cs` and the ceiling off `RepoProps.MaxStructureLevel` (`Assets/_Modules/Core/Catalog/RepoProps.cs:111`), never off this line. Wood+iron only (WO-947 — containers are regular structures); upgrade TIME is deliberately not authored (`StartUpgrade` derives the tier from the target level and reuses the existing curve). ⛔ **`RepoProps.MaxStructureLevel` is the SINGLE ceiling** — it replaced a scatter of hardcoded literals across BuildModeController, StructureCardVM, three suites, an EditMode test and StorageCapsCatalog's fallback array. **Never re-hardcode a level ceiling, and never copy its value into a doc** — this bullet cited `RepoProps.cs:69` until 2026-09-06, by which date the const had moved to `:111`.
- Village wave loop: WIRED — **`waves.json` `enemies[]` batches are INERT** (`_smartComposition:1` → WaveManager generates rosters). **The WO-783 D1 ruling is CLOSED** (owner, 2026-07-30): both `WaveDataTest` cases were rewritten to assert the batches are EMPTY, so a re-add now FAILS. Any doc calling this "open" is stale.
- ⛔ **Save schema: DO NOT WRITE THE NUMBER HERE — read it off `SaveSchema.CurrentVersion`** (`Assets/_Modules/Core/State/SaveSchema.cs`, and read the const, not the file's own header comment, which has itself been two versions stale). This line said **v38** from 2026-08-06 until 2026-09-06, by which date the const had moved three bumps on; the const carries a full changelog for every version in its trailing comment, so there is nothing a copy here can add. History kept as prose, deliberately without a current number: WO-773 brought the Obsidian queue; WO-834 the `everBuiltStructureIds` blank-town baked standdown; WO-911 the per-job PAID BASKET (`paidWood/paidFood/paidIron/paidCrystals/paidMagic` on `BuildJobData`) — the precondition for cancel refunding **100% of what was paid, flat** (ruling Q1), with a pre-basket job refunding ZERO and saying so; WO-934 the ARMY LOADOUT BANK (`ArmyStorage.loadouts`, 3 named composition presets, plus `activeLoadout`); WO-1235 the recipe unlock gate; WO-823 Phase E the first-raid soft gate. The **Obsidian multi-channel queue** (Builder/Train/Research) is the single home for ALL timed work, now with a **DEPTH cap of 5 PER LINE** (`BuildTimerConfig.queueDepthPerLine` — a different axis from `freeBuildSlots`, which stays 2; **never** implement the cap by raising concurrency) and an **Echo-gated, crystal-priced extra slot** (`BuildTimerService.TryBuySlot`, ruling Q6: each Echo above 2 unlocks the RIGHT to buy, crystals complete it); Realm Map (WO-826) shipped
- Store / monetization: the live model = **player-built town** (strategic placement ALWAYS ON — the flag was removed; movable functional storefronts + vendor NPCs). PackStore/packs.json exist — do NOT greenfield — but the old Village.unity store scene-wiring is a dead path
- Pre-ship gates = `COMPILE_GATE_OK` + `REGRESSION_OK <n>/<n> suites` + `UI_CAPTURE_OK` (open the PNGs)
  **+ `R2_PARITY_OK` on any build that reaches a device or a store — the content-ship gate, §16.**
  **The markers are now DISTINCT per entry point (2026-08-02)** — `DataRegression.RunAll` emits
  `REGRESSION_OK <n>/<n> suites`, `RegressionSuite.RunAll` emits `CHECKIN_SUITE_OK`,
  `SessionRegression.RunAll` emits `SESSION_GUARDS_OK` — the mapping is written down at
  `Assets/Editor/Regression/DataRegression.cs:14-22`, read it there. ⛔ **The suite COUNT is not written
  here** (this line carried one until 2026-09-06, one sentence before telling you not to). Until 08-02 all three
  printed the identical `REGRESSION_OK`, so a 22-case suite's pass read as the full suite's pass — which
  is exactly how the check-in gate ran the wrong one unnoticed. **Never restate the count here**; read it
  off the marker. (Also fixed today: `tools/regression/checkin_gate.ps1` did not PARSE under PowerShell
  5.1, so no stage of it had been running at all.)
- UXML in builds: does NOT work — always use code-built UI (learned the hard way)

---

## 9. Parallel Lane Rules

These lanes never conflict — run them simultaneously:
- **World/Environment** (VillageSceneBuilder, art) — architect lane
- **VFX/Audio** (VFXManager, AudioService) — no gameplay dependencies
- **Monetization/Backend** (WO-72–80) — fully isolated
- **Combat/AI** (EnemyBrain, ATB) — code only, no scene files

VillageSceneBuilder.cs is a **serialization bottleneck** — only ONE agent/branch
touches it at a time. Coordinate through work orders.

---

## 10. Before Reporting Done — Checklist

- [ ] Brace balance check passed on every `.cs` file edited
- [ ] No `.unity` scene files hand-edited
- [ ] No new `System.Reflection` usage introduced in bridge scripts
- [ ] `using DeNelle.Core.Combat;` present in any file implementing `IDamageableStructure`
- [ ] Null-conditional operators (`?.`) used on all cross-module service calls
- [ ] Work order acceptance criteria reviewed line by line

---

## 11. Orchestration & Delegation (how we work — non-negotiable)

The lead session is the **ORCHESTRATOR**, not a solo worker. Division of labor:

- **Orchestrator (lead session):** triage each issue + form the hypothesis **flow-first** — what
  *should* happen given the state (is this state even expected?), NOT culprit-hunting through stack
  traces. Route each focused task to its own agent. **Batch-gate**, **sole-commit** by explicit path,
  push only on owner OK. Hold the through-line + roundtable with the owner. Do **NOT** do the deep
  digging yourself — orchestrate; let the agents go deep.
- **Agents:** each does **ONE** focused task (one diagnosis OR one fix). Run them in **parallel**.

**Parallelize despite the single Unity gate + sole-committer:**
- Read-only **diagnosis/verify** agents are gate-free → fan out many at once.
- For **implementation**, fan out **edit-only** agents on **file-disjoint silos** (§9 lanes; same-file
  work = one agent). Tell them NOT to gate/commit. Then the orchestrator **batch-gates the combined
  tree once** (`COMPILE_GATE_OK`) and **commits each lane by explicit path**. The single Unity gate +
  the one committer are the coordination point, kept by the orchestrator — so no agent stands idle.

**Discipline:** flow-first triage → an agent verifies AND does it. Commit local; **push only after the
owner retests/confirms** (felt/gameplay) or a regression passes (data/logic) — "push the ones that
passed." Ambiguous tickets (no repro / screen / stack) **bounce back for detail** — never work blind.

**THE PIPELINE NEVER IDLES (owner directive 2026-08-10):** *"we should always have a agent assigned
and queued... so the dev work continues."* While READY tickets exist, the agent pool stays loaded —
on any lane/workflow completion, the orchestrator immediately tops up with the next disjoint-lane
READY ticket(s). Pin-blocked tickets park with their pins surfaced so unblocking is one owner word.
Gate + commit cadence stays singular (one gate, one committer) — the parallelism lives in the lanes.

**THE CADENCE IS HOOK-ENFORCED EVERY 5 MINUTES (owner directive 2026-09-09):** *"create a rule that
reminds you every 5 minutes of those rules so as you start to go off track it brings you back."*
`.claude/hooks/orchestration-cadence.ps1` re-injects the rule on `PostToolUse` at most once per five
minutes of work and unconditionally on `SubagentStop`; the rule text lives in ONE place,
`.claude/hooks/ORCHESTRATION_CADENCE.md` — never restate it here or anywhere else. The shape it
enforces: **the lead ASSIGNS SME agents, they do the work and HAND IT BACK as a claim, the lead
verifies + gates + COMMITS.** And the half that was missing: **THE AGENT THAT OWNS A TICKET UPDATES
THE BOARD** — its hand-back is incomplete until it has flipped the WO's own `**Status:**` line (and
written the `.RESULT.md` when done) and reported both paths; a lane that returns without the flip
goes back to the lane, the lead does not do it silently. The lead regenerates `BOARD.html` and commits
the flip in the SAME commit as the work. (Why: nine landed tickets sat READY on 09-04, one on 09-05,
ten flagged by the board on 09-09 — every one a flip left "for the end".)

**Multi-session reconciliation (FIRM RULE — always followed):** multiple sessions/agents edit the
SAME working tree. There is exactly **ONE committer** (the lead/CLI). When another session or an agent
worktree leaves changes in the tree, the committer **SAVES their work and merges only the diffs into
the correct branch by EXPLICIT PATH** — review each diff (guard the mount-sync/garble risk, §0), stage
by path, never `git add -A`, never blind-replace a file, never let a second session commit/push (two
committers duel on `.git/index.lock` → stale locks + false "pushed", see memory `sole-git-committer`).
Other sessions write + signal "ready"; the one committer reconciles. This is non-negotiable.

---

## 11B. ⛔ NEVER GUESS. PROVE IT, OR SAY YOU HAVEN'T. (owner directive 2026-09-03, BINDING)

> Owner, verbatim: ***"we never Guess, always must be proven to be true. Documention is the same is
> must be proven followed not gone off script without explicit permission"***

Two halves. Both are hard rules, not aspirations.

### A. A CLAIM WITHOUT EVIDENCE IS NOT A CLAIM

**Every factual statement must be traceable to something read or measured THIS SESSION** — a captured
log line, an HTTP response, a file opened at source, a marker on a fresh log, a hash. Not to a memory,
not to a doc's summary of the code, not to what was true last week, and *never* to a plausible
inference.

- **State the evidence with the claim.** "R2 is fine" is worthless; `HTTP 200, 143738 bytes,
  catalog_2026.09.04.354266.bin` is a fact.
- ⛔ **"Probably", "should be", "I believe", "it looks like" are ADMISSIONS OF A GUESS.** If those words
  fit, the honest sentence is **"I have not proven this"** — say that instead, and say what would prove
  it. An unproven thing named as unproven is useful. An unproven thing stated as fact is a lie that
  costs someone a day.
- **When something CANNOT be proven from here, that is a finding to record, not a box to tick.** The
  live signing certificate had never been captured, so a "matches the live release" tick would have
  been fiction; it is written down as unprovable, with the one cheap way to close it.
- ⛔ **A number, a filename, a line number or a version copied from a doc is HEARSAY** until re-read at
  source. This repo's most expensive bugs are all copied state that went stale (§2, §5, §16).

*Forged 2026-09-03, twice in one evening:* `fps=40` was read from a log and turned into a frame-budget
theory, asserted twice, while `timeScale=0.28` sat unread in the same output — the owner asked *"Why
did you guess"*. Hours later a `GET` against a POST-only endpoint returned 400 and was reported as a
server defect; it was the wrong call shape. Both times a real measurement was used to support a
conclusion it did not support. **Measuring something is not the same as measuring the right thing.**

### B. FOLLOW THE DOCUMENTED PROCEDURE, AND PROVE YOU FOLLOWED IT

When a checklist, runbook or SOP exists — `PREFLIGHT_GATE.md`, `START_HERE.md`,
`publishing/SUBMIT_CHECKLIST.md`, `docs/TICKET_PIPELINE.md`, the skill runbooks — **read it to the end
and execute it as written.**

- ⛔ **Reading the top and improvising the rest is going off script.** *(2026-09-03: 45 lines of the
  submission checklist were read, then apksigner was invoked ad hoc — while the document itself
  specified the gate, the fields to record, and the stop rules. The owner: "did you read how to do or
  guess?")*
- ⛔ **Deviating requires the owner's EXPLICIT permission, asked for in advance** — not a deviation
  explained afterwards.
- **Fill the record in as you go.** A checklist with blank fields has not been executed, however much
  work happened alongside it.
- **If the procedure is wrong or stale, SAY SO and get a ruling** — then fix the document in the same
  change (§15). Silently doing something better is still going off script, and the next seat inherits a
  doc that lies.

⚠ **This does NOT license bouncing solvable problems back.** Fixing something yourself and reporting
it afterwards is *required* (memory `fix-it-silently-dont-bring-me-the-problem`); what is forbidden is
**claiming** it without proof, or **skipping** a written procedure without asking.

---

## 12. Debugging Directive — INSTRUMENT, don't guess (BINDING)

> ### ★ THE HARD GATE (owner 2026-06-21 — BINDING on EVERY agent + CLI, forever) ★
> **No code edit on a non-trivial bug until you can cite CAPTURED DATA that proves the cause.**
> Instrument FIRST — loggers that step IN and OUT of each item (`FlowTrace.Enter/Step/Warn/Fail`,
> `Guard`), run it (prefer **HEADLESS** to self-serve — the AutoPilot fleet + `break-log.jsonl` +
> on-load state dumps), read the trace, let the data **PINPOINT the dead step**, then fix THAT.
> - **Static code-reading LOCATES candidates; it NEVER CONCLUDES the cause.** An inferred root is a guess.
> - **Never inference-fix. Never guess-and-ship.** A "plausible fix" *feels* like progress and is the
>   slow path (N blind cycles); instrumenting *feels* slow and is the fast path (one read).
> - This is the **OPENING MOVE, unprompted** — not a fallback after a guess fails. If you can't point
>   to the data line that proves it, you have not earned the edit.
> - The methodology is to be **passed to every agent ever spawned, every CLI, every session.** Propagate it.
>
> *Lesson forged 2026-06-21:* the castle "pink floor" was guessed at for 3 cycles (a terrain theory that
> was WRONG); one headless FloorDiag dump then named the real cause — colorless URP/Lit floor tiles — in
> a single read. Memory: `never-inference-fix`.
>
> *Forged again 2026-08-20:* every enemy in the owner's build was a capsule. **Two static theories were
> proposed first** — a duplicated `[BuildTarget]` token in the Local path variables, then a stale content
> build — and **both were wrong**, at the cost of an hour. One grep of the DEVICE log settled it in one
> line: `RemoteProviderException : Unable to load asset bundle from : https://pub-...r2.dev/Android/
> enemy_art_assets_enemyfam-hollow_....bundle` / `UnityWebRequest result : ProtocolError : HTTP/1.1 404
> Not Found`. The bundles had never been pushed (§16). The device had the answer the whole time.

Owner mandate (2026-06-13, from B2B-scale practice): **we do not guess at bugs — we
instrument the flow and let the data tell us where it dies.** Repeated symptom-patching
on a hypothesis wastes credits and owner time. The standing loop:

1. **Trace the flow first.** Add `DeNelle.Core.Diagnostics.FlowTrace` calls at every
   meaningful step/branch of the failing system (request → resolve → fallback → render).
   `Step` (reached it), `Warn` (fallback/anomaly), `Fail` (hard stop), `Throttle` (hot
   loop, ~1/sec), `Once` (first hit), `Measure` (perf scope: `using var t = FlowTrace.
   Measure("Sys","what", warnAboveMs:16f)`). Every line is `[Flow:<system>]`-tagged and
   captured by the F8 `BreakCaptureHarness` (break-log.jsonl + Player.log).
   **On a FRAME path, use the 4-arg `Measure` overload — it is the town/raid frame-cost instrument
   (WO-1483, added 2026-09-06):** `using var _ = FlowTrace.Measure("Perf", "X.Update", 4f, 1f);`
   (`Assets/_Modules/Core/Diagnostics/FlowTrace.cs:308`). It does NOT log on dispose — it accumulates
   into a table and warns at most once per interval, because the 3-arg form on a per-frame site floods
   the log and evicts the boot window out of the device logcat ring, destroying the evidence you
   instrumented for (the reasoning is written at `FlowTrace.cs:293-300`; memory
   `logcat-ring-buffer-destroys-evidence`). Live examples: `Assets/_Modules/Pets/PetHeroLeash.cs:266`
   and `Assets/_Modules/Village/Buildings/Progression/ResourceCollectorService.cs:67`. The shape is
   pinned by `Assets/Editor/Regression/FrameBudgetMeasureRegression.cs`. **Never fix a frame-cost
   ticket before these scopes have NAMED the dominant cost in ms.**
2. **Guard every risky object op.** Wrap parse / list-build / service-lookup / UI
   construction in `Guard.Try(...)` / `Guard.TryEach(...)` — one bad object logs (via
   `FlowTrace.Fail`) and is skipped, never silently blanks a screen. **No silent failures:**
   a catch that swallows without logging is forbidden.
3. **Capture real data, then look.** A run produces `[Flow:*]` lines that pinpoint the
   dead step — fix THAT, don't re-theorize. Split every "shows nothing" into *data-empty*
   vs *built-but-invisible* vs *threw-and-skipped* using the trace, before touching code.
4. **Prefer headless capture.** These traces run in a headless batchmode play session
   (no owner playtest needed) — use it to self-serve diagnosis on passive flows
   (spawns, roster, seam-online, catalog load) before asking the owner to retest.

Helpers live in `Assets/_Modules/Core/Diagnostics/` (`FlowTrace.cs`, `Guard.cs`,
`BreakCaptureHarness.cs`).

**⛔ NEVER STRIP FLOWTRACE (owner ruling 2026-08-09, BINDING).** Instrumentation is PERMANENT. Once a
system is proven stable you may eventually **FLAG IT OFF** (`FlowTrace.Enabled=false`) — the calls
**STAY IN THE CODE**. This line used to read *"set `FlowTrace.Enabled=false` **(or strip calls)**"*,
which licensed deleting the very net §12 exists to build: a stripped `Warn`/`Fail`/`Guard` turns a
logged failure back into a SILENT one, and the next regression in that system starts from zero
evidence instead of a trace. Removing instrumentation is never "cleanup" — it discards the only asset
that makes the NEXT bug cheap. Leave traces in while a system is being stabilised, and leave them in
afterwards. See `docs/INSTRUMENTATION_STANDARD.md` §1.4.

**Authoring standard:** how to write code to this directive from the first line —
toggle/lifecycle, where-to-instrument checklist, Guard usage, regression authoring,
conventions — lives in `docs/INSTRUMENTATION_STANDARD.md`. §12 is the *rule*; that doc is
the *method*.

---

## 13. Ticket Pipeline — QA → CLI → PO (BINDING)

Owner directive (2026-06-20): the playtest/bug backlog runs through a **role-separated**
pipeline. **Full spec: `docs/TICKET_PIPELINE.md` (BINDING — read it before working tickets).**
In one breath:

- **PO** (the owner) pulls a ticket from the QUEUE (F8 `break-log` flags), sets its **SILO**,
  routes to QA; and **after deploy felt-verifies + CLOSES** it (headless can't judge feel).
- **QA Triage = read-only agents.** Classify **NEW FEATURE vs EXISTING** first. NEW (not built)
  → back to PO as a spec/WO — never RCA-fix the unbuilt. EXISTING → read-only RCA → push to CLI.
- **CLI** (this seat, **sole committer**) validates + implements + **headless-verifies**
  (`CompileGate` + AutoPilot fleet / `DataRegression`) + deploys → hands to PO. Never claims
  fixed on faith (§5/§12).
- **Shared board = `BOARD.html`, DERIVED from `WorkOrders/*.md`** (`python tools/board_build.py`).
  The hand-off log is the WO / ticket markdown itself — stage + silo + who-has-it live in the file,
  so the record and the work are the same artifact. **Log every hand-off there.**
  **⚠ THE TASK LIST IS RETIRED (owner ruling 2026-08-09)**, as are Notion (08-08) and Linear (08-09).
  This line used to name the Task list as the shared board. There is now exactly ONE board and it is
  derived — nothing to mirror, nothing to fall behind. See `docs/BOARD.md`.
- **Role separation is non-negotiable:** QA doesn't write, CLI doesn't classify-triage, PO closes
  (not CLI). Read-only constraint on early triage.

---

## 14. F8 Live-Triage Watcher (BINDING — every CLI session, forever)

Owner directive (2026-06-23): **the owner is NEVER the bug detector** (memory
`never-dragdrop-or-manual-playtest`). Whenever the owner is (or is about to start) felt-testing,
every F8 flag / error / softlock the harness records must surface on the CLI **without the owner
saying "rearm" or "watch"** — the CLI **triages it LIVE** (RCA from the captured line + screenshot).

**Claude Code seats: the listener is HOOK-ENFORCED (owner directive 2026-08-10).** `.claude/settings.json`
(committed, all seats) arms three hooks: SessionStart auto-starts the daemon; UserPromptSubmit injects any
un-acked capture at turn start; a Stop-hook background poller (`.claude/hooks/f8-poll-rewake.ps1`, 10 s
cadence, single-instance across seats via a repo lock) REWAKES the idle seat the moment a capture lands.
The old per-turn-poll discipline (`.cursor/rules/f8-auto-triage.mdc`) stopped being followed within a
month — the harness now executes it instead of trusting the seat to.

**Persistent daemon (primary — no manual re-arm):**
- **Start once:** `powershell -File .claude\skills\run-defenders\f8-watch-start.ps1` (idempotent).
  Runs `f8-watch-daemon.ps1` hidden; watches `break-log.jsonl` + Editor/Player logs forever.
- **Inbox:** `logs/f8-inbox/` — daemon writes `LATEST_CAPTURE.md` + bumps `PING.json` on each capture.
- **⚠ THE INBOX IS A QUEUE, NOT A SLOT (WO-965, 2026-08-10).** `LATEST_CAPTURE.md` + `PING.json` hold
  only the **NEWEST** capture; the record is the append-only **`logs/f8-inbox/QUEUE.jsonl`** plus one
  `capture-*-seq<N>.md` per capture. `f8-check-inbox.ps1` surfaces the **OLDEST un-acked** capture and
  a `pending=N` count; **`f8-ack.ps1` acks exactly ONE** — keep triaging until it reports `NO_CAPTURE`.
  *Why it is written this hard:* the two files used to be single slots, so a burst overwrote itself and
  an ack of the newest seq silently closed everything below it — on 2026-08-10 the seat acked seq 2306,
  next saw 2309, and the owner's **2307 + 2308 never reached any seat**. Never ack "the latest"; never
  assume the newest capture is the only one.
- **Agent poll:** `.cursor/rules/f8-auto-triage.mdc` (alwaysApply) — every turn run `f8-check-inbox.ps1`
  FIRST; session start also launches background `f8-watch-poll.ps1` (notify on `F8 INBOX PING`).
- **After triage:** `f8-ack.ps1`, then re-launch `f8-watch-poll.ps1`.
- **Stop:** `f8-watch-stop.ps1` when done for the day.

Fires ONLY on real captures (F8 `flagged` / error / exception / softlock); `session_start` +
`scene_loaded` startup noise is filtered; re-baselines on a fresh Play session.

On a fire the daemon **AUTO-HARVESTS** the recent `[Flow:*]` / `[FeatureFlags]` / Guard / exception
lines into `LATEST_CAPTURE.md`. **TRIAGE FROM THOSE LINES — read the already-captured data FIRST,
before any code-read, any agent, any theory.** Spawning a code-reading agent before reading the
harvested trace is the banned failure (memory `never-inference-fix`). The owner must NEVER have
to ask "did the data show that" — the look is structural.

Then RCA from the data + screenshot, and route per §13 (CLI implements + headless-verifies; PO
felt-verifies + closes). The owner just plays and F8s.

**Legacy fallback:** `bash .claude/skills/run-defenders/f8-watch.sh` (one-shot; re-arm after each fire).

---

## 15. Canon Maintenance — keep the docs from going stale (WO-520, BINDING)

Owner directive (2026-06-26): we did one painful fleet-scale audit of all 1090 `.md` files
(`CANON_READINESS_LEDGER_2026-06-26.md`) because the canon had drifted weeks behind reality. **Never
again at that scale.** The standing rule, so canon updates stay 5-minute tasks:

- **The single live anchor = `CANON_GROUND_TRUTH_<date>.md` at repo root.** It states current reality
  (branch, hero rig, combat model, world/seam, in-flight status). Keep exactly ONE current; supersede the
  old one by date. Every session and every agent checks docs against it.
- **Update canon in the same breath as the change.** Any commit that changes architecture/state (branch,
  hero rig, a pillar's scope, a scene's role, a removed/added system, a creative-canon decision) MUST update
  the relevant load-bearing doc in the SAME commit/PR — or, if deferred, add a one-line `STALE:` flag at the
  top of that doc naming what's now wrong. A state change with no canon update is an incomplete change.
- **Load-bearing set (the read-first canon) — keep these green:** `SESSION_CANON_LOADER.md`,
  `docs/HANDOVER.md`, `PIPELINE_STATE.md`, `docs/MASTER_CATALOG.md`, `PROJECT_INDEX.md`, the relevant
  `docs/*ARCHITECTURE*` / `docs/COMBAT_PIVOT_NORTHSTAR.md`, and this file.
- **Frozen, never rewrite:** dated point-in-time ledgers (OVERNIGHT_*, MORNING_*, dated HANDOVER_*,
  RESULT files, dated session reports). If one reads as current, add a `⚠ SUPERSEDED <date>` banner — do
  not rewrite the body. Backlog WOs are frozen by their date; an UNDATED WO asserting current state
  (branch / "#1 priority now" / "fix before go-live") is STALE and must be banner-fixed or dated.
- **Weekly 5-minute audit:** skim the load-bearing set above against the ground-truth anchor; fix or flag.
- **Never guess** — every canon update is sourced from HEAD commits / working tree / the live auto-memory
  index / verified summaries, never from assumption (§12 discipline applies to docs too).

---

## 16. Shipping Content — the art lives on the R2 CDN, not in the APK (BINDING)

Owner ruling 2026-08-20: ***"wire the r2 push into the ship chain."***

> ## ⛔ ENEMY AND STRUCTURE **ART** IS SERVED REMOTELY, AND A MISSING PUSH FAILS **SILENTLY**.
> `Remote.LoadPath` = `https://pub-ab6dfaf1b3d74ca78891876611ccb832.r2.dev/[BuildTarget]`
> (`Assets/AddressableAssetsData/AddressableAssetSettings.asset`). **There is NO local fallback** —
> `Assets/Resources/Enemies` and `Assets/Resources/Structures` no longer exist. A build whose bundles
> were never uploaded **installs perfectly, launches perfectly, and plays**: tinted **CAPSULE** enemies,
> placeholder buildings, **no error on screen**. The game does not stall — that is deliberate — so the
> only detector left is the owner's eyes, which is exactly the thing §14 exists to never rely on.

**⚠ THE LOAD-BEARING SENTENCE — read it twice: BUNDLE NAMES ARE CONTENT-HASHED.** Every content build
produces **new filenames**, so **EVERY content build needs ITS OWN push. A push from a previous build can
never cover this one.** The bucket looking full proves nothing. The previous build still working proves
nothing. This is the whole trap, and it is why "I pushed yesterday" is never an answer.

**The sanctioned path is now ONE FILE: `tools\r2-ship.ps1`** — push + verify, marker-judged, **exit 16**
on failure. It is called by `morning-ship-chain.ps1` and `overnight-apk-build.ps1` (both **BLOCK**) and by
`install-apk-to-seeker.ps1` with **`-WarnOnly`** (a deliberately-offline sideload is legitimate *there and
only there*). Before 2026-08-20 the push+verify pair was **copy-pasted into the two chains and had ALREADY
DRIFTED**: overnight pushed *then* verified; morning **ONLY VERIFIED** and printed a "FIX: run this by
hand" message — and a gate whose remedy is "a human remembers a second command" is not a gate. Same
duplicated-state failure as the stale WO number block (§2) and the retired dependency table (§5). **Do not
re-inline the push or the verify into any chain, script, doc or work order — call the one file.**

**⛔ PUSH THE PARENT; VERIFY THE EXPLICIT TARGET. The asymmetry is real and it is the trap.**
- `--push ServerData` (the **PARENT**). `--push ServerData/Android` **FLATTENS the keys to the bucket
  root**, where the game never looks — and it reports **`R2_PUSH_OK` while uploading 103 unreadable
  objects** (observed 2026-08-20).
- `--verify-catalog ServerData/Android` (the **EXPLICIT** target), because `ServerData` holds both
  `Android` and `StandaloneWindows64` and the tool refuses to guess.
- Both forms are hardcoded **exactly once**, inside `r2-ship.ps1`. Retyping either by hand is how one of
  them comes back wrong.

**Judge by the MARKER on a FRESH log — `R2_PUSH_OK`, `R2_PARITY_OK` — NEVER the exit code.** This repo's
runners exit 0 on refusals and FAILs (§8; memory `gates-report-success-without-proving-it`). Marker
absence on a fresh log is a **FAILURE**, not an unknown. `R2_PARITY_OK` is a pre-ship gate — see §8.

**⛔ A RAW `adb install` OF A HAND-BUILT APK BYPASSES ALL OF IT.** That is precisely what happened on
2026-08-20: the CLI built with `run-unity-method.ps1` and installed with `adb install` directly, touching
**none** of the three scripts that hold the gate. That hole is residual and the rule closes it:
**installing or distributing a build goes THROUGH THE SCRIPTS, never through raw `adb`.**

**⛔ AND NOTHING LEAVES THIS MACHINE OUT OF SYNC — the `pre-push` hook (owner directive
2026-08-21: *"make sure anything pushed always is in sync with R2"*).** `.githooks/pre-push`
(tracked, wired by `git config core.hooksPath .githooks` — **set it once per clone**, it is
local config and does not travel with the tree) **REFUSES `git push` whenever anything under
`ServerData/` is NEWER than `Builds/r2-parity.log`**, or that log lacks `R2_PARITY_OK`. The
invariant is *the proof must postdate the bytes it claims to prove* — no network needed. A
docs-only push passes untouched, because `ServerData/` did not change and the existing proof
still postdates it. **There is deliberately NO override flag:** every one of the three
incidents above was a human expected to remember a second command, so a flag would restore
the exact hole. To clear a real block, run the one sanctioned path — `tools
2-ship.ps1`.

**Why this is written this hard — it has now happened THREE TIMES:**
- **2026-08-18** — an APK sat ready to install whose enemy bundle had never been uploaded. Caught **by
  hand**. Commit `16e22dba3` conceded in its own body: *"NO GATE COULD HAVE CAUGHT THIS."*
- **2026-08-19** — a real **Android** APK shipped carrying **StandaloneWindows64** content, with every
  other marker green (WO-1124).
- **2026-08-20** — the owner played a build in which **EVERY enemy was a capsule**. The CLI had re-run the
  Addressables grouper and re-applied packing that day, **re-hashing every bundle**, and never pushed. Two
  wrong causes were theorised before the device log named it in one line (§12).
