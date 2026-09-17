# WORK ORDER 1805 — RESULT (Lanes A + C implemented; Lane B remains SPEC)

**Status:** IMPLEMENTED
**Implemented:** 2026-09-16, branch `dev`, by the WO-1805 implementation lane
**Gate:** ⛔ **NO UNITY GATE WAS RUN AND NO COMMIT WAS MADE** — per the brief. `COMPILE_GATE_OK`,
`REGRESSION_OK` and the new suite's own marker are all **unproven**. Every claim below that could be
measured from here was measured and is quoted with its evidence; everything that needs Unity is listed
under §6 as unproven.

---

## 1. What shipped

### Lane A — the composed dungeon now teaches its own oil mechanic

The defect was never missing copy: the teach existed in the owner's words (`dun_torch_warden`) and was
delivered only by `TorchWardenDresser` → `DungeonController`, a class in exactly one scene on disk
while every player portal routes to a composed `dg_*` scene. So the player met the oil mechanic as an
unexplained blackout.

| Piece | Where |
|---|---|
| The teach copy, a NEW row (`dun_lantern_intro`), 3 lines, speaker **Bryn** | `Assets/Resources/Data/Canonical/dialogue/dialogues.json` + `Assets/StreamingAssets/Data/Canonical/dialogue/dialogues.json` (byte-identical twins) |
| The play site, after `InstallOilHud()` so the first thing read and the first thing lookable-at agree | `Assets/_Modules/Dungeons/ComposedDungeonHost.cs:196` (`Guard.Try(Sys, "install lantern teach", InstallLanternTeach)`), body at `:487` |
| The completion event (a stone was spent) | `Lantern.OilStoneUsed`, raised inside `Lantern.CheckOilStones` |
| The darkness-beat edge event | `Lantern.FinalWarningEntered`, raised on the `IsFinalWarning` edge in `Lantern.TrackOilEdges` |
| The darkness line, one-shot, non-blocking | `ComposedDungeonHost.LanternGutteringLine` = `"Your flame gutters - find an oil stone."` via `DeNelle.Core.UI.ElarionUiKitConformance.ShowToast(..., Danger, lifeSeconds: 4.5f)` |

**The copy, verbatim as authored (ASCII hyphens only):**

> Bryn: "Your light feeds on oil and burns down as you walk."
> Bryn: "When the flame gutters, stand in a glowing oil stone - it fills the flask back up."
> Bryn: "The deep rooms eat the unlit. Mind the meter, Keeper."

**A dialogue screen, never a world actor** (memory `tutorial-guide-body-one-time-then-images`): no Bryn
body is spawned in a dungeon — no seating, facing, navmesh, despawn lifecycle or stall risk.
`StoryIntroController` was deliberately not reused (it is gated on `Onboarded == false` and lives in the
Title scene).

#### ⚠ TWO KEYS, and the difference is the whole safety of the feature

| Key | Latched when | Why it is separate |
|---|---|---|
| `dun_lantern_intro_shown` | the dialogue **actually ended** (`DialogueService.EndedWithId == dun_lantern_intro`) | This is "one-shot means one-shot": a save that has read it never reads it again, which is what keeps the teach **absent** when a session opens straight into `dg_ember_deep` at the boss. |
| `dun_lantern_intro_done` | the player's **first oil-stone refill** (`Lantern.OilStoneUsed`) | The teach's *completion* — the player did the thing it described. It also satisfies the intro gate, so someone who learned by doing is never lectured afterwards. |
| `dun_lantern_guttering_shown` | the guttering toast was shown | The darkness beat's own one-shot. |

⛔ **NOTHING is latched on the ATTEMPT.** A `Play()` that renders nothing leaves the save untouched —
the WO-844 potion-lesson class of bug the ticket named at §5 A1.

#### ⛔ The soft-lock guard, and why it is an ORDERING and not a return-value check

`FeatureFlags.CustomDialogue` is tested **before** `DialogueService.Play`. With that flag off,
`DialogueView.Bootstrap` never subscribed `Opened` (`Assets/_Modules/HUD/DialogueView.cs:24-38`, read at
source), so `Play` returns **true**, sets `ActiveVm`, makes `IsRunning` true and suppresses hero input —
with no panel on screen and no way to close it. **The return value cannot detect that.** The declined
path traces a `Warn` and leaves the teach owed. `DialogueService.IsRunning` is also checked, so an
entry that lands on top of another conversation stands down instead of stomping it.

### Lane C — the darkness is on the remote rail, at identity

Four `TunableKind.Int` rows (there is no `Float` on this rail — `RemoteTunables.cs:78-84`):

| key | default | = today's behaviour, proved against |
|---|---|---|
| `dungeon.lanternDrainPerSecX100` | **50** | `dungeon-balance.json`'s authored `oilDrainPerSec` `0.50` ×100, read through `DungeonLanternBalance` |
| `dungeon.lanternOilStoneRefillPct` | **100** | a stone tops the flask — `Min(max, oil + max*1.0)` is arithmetically identical to the old `_oil = _maxOil` from any level |
| `dungeon.lanternStillRefillPct` | **40** | `ComposedOilStill.RefillFraction` `0.40f` (now a `public const` so the oracle can pin it) |
| `dungeon.lanternFinalWarningSec` | **30** | `Lantern.DefaultFinalWarningSeconds` (new named const; the serialized field now initialises from it) |

**Consumers wired:** `Lantern.ApplyBalanceData` (drain + warning window), `Lantern.CheckOilStones`
(stone %), `ComposedOilStill.TryDistill` (still %).

#### ⚠ ONE DESIGN POINT THE SPEC DID NOT STATE, SETTLED HERE AND STATED OUT LOUD

The drain and the warning window are **also authored in `dungeon-balance.json`**. A rail row that simply
won would have made that json **dead data** the first time anyone re-authored it — the json would say
`0.40` and the build would keep burning `0.50` from a default nobody chose. So both consumers treat a row
**at its shipping default** as *not set* and leave the json as the single authority; **only a deviation
overrides**. That is identity by construction and keeps exactly one authority per value. The two refill
percents have no json twin, so they apply directly.

`maxOil` was deliberately **not** registered (§6/§9): it is the meter's 100 % and the denominator of
`_lowOilFraction` 0.25, `IsInDarkness` 0.12 and `_minOilLightFraction` 0.35, so a row on it moves the HUD
bands, the ambush director and the fog collapse at once. The new suite **FAILS** if any future key carries
`maxOil` in its name.

**All five surfaces moved in one edit** (`CompareDomain` fails on a length mismatch, not only on a missing
key):

1. `Assets/_Modules/Core/Ops/RemoteTunables.cs` — 4 default consts + 4 key consts + 4 `TunableSpec` rows
2. `Assets/Editor/Regression/RemoteTunablesDefaultsRegression.cs` — 4 `ExpectedDefaults` literals
3. `docs/PROD022_TUNABLE_FLAGS.md` — owner-facing rows **#72–#75**
4. `api/_lib/tunables.js` — 4 `TUNABLE_KEYS` entries
5. `api/_lib/tunable-manifest.js` — 4 hand-authored PRESENTATION cards (area `misc`, plain English, safe range)

then `api/_lib/tunable-manifest.generated.json` **regenerated from the tree** (never hand-edited).
`docs/reference/TUNABLE_LEVER_INVENTORY.md` §5.8 carries a **LANDED** note that points at the Registry
rather than restating the numbers.

### §7 instrumentation — the drain path was silent by construction; it is not any more

⛔ CLAUDE.md §12: these are **permanent**. Before this ticket **no capture on disk could prove a run had
ever reached empty** — not because it never happened but because nothing logged it.

| Trace | Site |
|---|---|
| `oil stone '<id>' SPENT: oil a->b ... N/M caches used` (+ the seconds it bought, derived from the live drain, never a literal) | `Lantern.CheckOilStones` |
| `flask EMPTY at t=..s in scene=.. - remaining recourse: unspent oil stones=N/M` | `Lantern.DrainOil` (`Once`, on the edge) |
| `final-warning ENTERED: t=..s, oil, ~Ns left, caches spent, window=..s` | `Lantern.TrackOilEdges` (`Once`, on the edge) |
| `darkness latch ARMED` + `darkness TOTAL on lantern teardown: Ns below the 0.12 latch` | `Lantern.TrackOilEdges` / `Lantern.OnDisable` |
| `lantern intro: seen=.. played=.. key='..'` and the guttering twin | `ComposedDungeonHost.InstallLanternTeach` / `HandleLanternFinalWarning` |
| **The standing net:** `lantern armed standalone: stones=N rooms=R ... authored burn=Bs -> free light ceiling=Cs for R room(s) = X s per room before the dark` | `ComposedDungeonHost.ArmHeroPillars` (the §7 extension of the existing line) |

⛔ **THE EDGES USE `Step` PLUS A PER-VISIT BOOL, NOT `FlowTrace.Once` — AND THE WO'S SUGGESTED
`FlowTrace.Once` WOULD HAVE BEEN A BUG.** `Once` is **session**-scoped: it adds `system + "/" + key` to a
static `s_seen` set that only `ResetSession()` clears (`Assets/_Modules/Core/Diagnostics/FlowTrace.cs:228-237`,
read at source). With a fixed key, the FIRST dungeon of a session would have traced the final warning, the
empty flask and the darkness latch — and **every dungeon after it would have reached empty in silence**,
which is exactly the evidence gap these lines exist to close. `_finalWarningReported` /
`_flaskEmptyReported` / `_darknessReported` are reset in `Configure` / `ConfigureStandalone`, so each edge
still prints exactly once **per visit**, which is what §7 actually wanted.

Every edge is edge-triggered, never per-frame: a per-frame line here evicts the boot window out of
the Android logcat ring and destroys the evidence it was added to collect (memory
`logcat-ring-buffer-destroys-evidence`; the same reasoning the existing fog `Throttle` carries).
`_layout = LoadLayout()` was moved **above** the lantern arm purely so the net can print `rooms=`; it is a
pure read of `_composeRoot` + `Resources` and the ambush director still reads the same `_layout`.

### New oracle

`Assets/Editor/Regression/DungeonLanternTeachRegression.cs` (+ `.cs.meta`), namespace
`DeNelle.Editor.Regression`, marker **`DUNGEON_LANTERN_TEACH_OK`** / `DUNGEON_LANTERN_TEACH_FAIL`,
4 cases:

- `[teach-copy]` — twins byte-identical; the `dun_lantern_intro` row exists **by id** (read off
  `ComposedDungeonHost.LanternIntroDialogueId`, so the two cannot drift); every line's speaker is
  **Bryn**; the row is dash-clean; and the copy names **both** the oil and the stone (a teach without the
  recourse leaves the player where the ticket found them).
- `[teach-one-shot]` — the three keys are non-empty; `MarkTutorialSeen` is still the latch;
  `FeatureFlags.CustomDialogue` appears **before** `DialogueService.Play` in code; the shown key is
  marked **after** the Play call (never before); `Lantern` declares **and raises** `OilStoneUsed` inside
  `CheckOilStones`; `FinalWarningEntered` is declared; the host subscribes both; the guttering line is
  non-empty and dash-clean; the beat uses `ShowToast` (a modal there would suppress hero input exactly
  while the ambush multiplier arms).
- `[tunable-identity]` — **not a self-comparison.** Each rail default is compared to the **real consumer
  authority**: the authored json through `DungeonLanternBalance.OilDrainPerSec`,
  `ComposedOilStill.RefillFraction`, `Lantern.DefaultFinalWarningSeconds`, and 100 for a top-up. Plus:
  no registered key may put `maxOil` on the rail.
- `[entry-net]` — `ArmHeroPillars` reads `DungeonLanternBalance.SecondsToEmpty` and contains **no bare
  `200`** (matched on **code only**, so the prose that mentions 200 s cannot satisfy or fail it); the
  trace prints `rooms=` and `ceiling=` (matched on **raw** text on purpose — those tokens live inside the
  string literal the code-only stripper removes, and the assertion is about what the line PRINTS); and
  the four drain-path trace edges are still present.

Comments and string literals are stripped through the shared
`ComposedDungeonRunRegression.StripCommentsAndStrings` before every structural assertion, so a lint
cannot be satisfied by a mention. Every scanning case fails when it finds nothing.

**⛔ REGISTRATION LINE FOR THE ORCHESTRATOR — `DataRegression.cs` was deliberately NOT edited by this
lane (it is the lead's file). Add, covenant-style:**

```csharp
if (!DeNelle.Editor.Regression.DungeonLanternTeachRegression.Run(out var dungeonLanternTeach))
    failures.Add(dungeonLanternTeach);
else log.AppendLine("[dungeon-lantern-teach] " + dungeonLanternTeach);
```

Standalone entry point: `DeNelle.Editor.Regression.DungeonLanternTeachRegression.RunAll`.

---

## 2. Lane B — still SPEC, deliberately

Nothing in B1–B4 was implemented. Each needs a new seam **and** an owner ruling: B1 stills per room band,
B2 which mat replaces/promotes `tattered-cloth`, B3 a bag-side oil consumable (`ConsumableUseService` has
no oil route), B4 whether the torch becomes the long-burn upgrade or the copy stops promising it. The WO's
Lane B section is unchanged apart from its status line.

---

## 3. Files touched (all paths absolute-from-repo-root)

**Code**
- `Assets/_Modules/Dungeons/Lantern.cs`
- `Assets/_Modules/Dungeons/ComposedDungeonHost.cs`
- `Assets/_Modules/Dungeons/ComposedOilStill.cs` *(Lane C's still % — the WO's own Lane C table names this file as that knob's consumer)*
- `Assets/_Modules/Core/Ops/RemoteTunables.cs`
- `Assets/Editor/Regression/RemoteTunablesDefaultsRegression.cs`
- `Assets/Editor/Regression/DungeonLanternTeachRegression.cs` **(new)** + `.cs.meta` **(new)**

**Data (binary-patched, twins proven identical)**
- `Assets/Resources/Data/Canonical/dialogue/dialogues.json`
- `Assets/StreamingAssets/Data/Canonical/dialogue/dialogues.json`

**Backend / docs**
- `api/_lib/tunables.js`
- `api/_lib/tunable-manifest.js`
- `api/_lib/tunable-manifest.generated.json` *(regenerated, never hand-edited)*
- `docs/PROD022_TUNABLE_FLAGS.md`
- `docs/reference/TUNABLE_LEVER_INVENTORY.md`
- `WorkOrders/WORK_ORDER_1805_dungeon_lantern_oil_never_taught_and_no_refuel_path.md` (Status + Lane A/B/C prose)
- `WorkOrders/WORK_ORDER_1805_dungeon_lantern_oil_never_taught_and_no_refuel_path.RESULT.md` (this file)

**Not touched, as instructed:** any `.unity`, any dungeon layout, `TutorialFlow.cs`,
`PlayerDeckWorkspace.cs`, `CastleDefensePlans*`, `RaidSelection*`, `PackStore.cs`,
`Assets/Editor/Regression/DataRegression.cs`, `CLI_LANES_WO_NUMBERS.md`, `Assets/Editor/RoomForge/DungeonBaker.cs`.

---

## 4. Proofs actually measured this session

**Canonical twins — binary patch, counted before and after.** The two files were **already modified in
the working tree by other lanes today**, so the patch was taken from the CURRENT bytes, not from HEAD, and
the pre-state was hashed first:

```
before (BOTH): sha256 26b86b3fd085b4fc… size 69846  LF 1676  CRLF 1601  BOM False   <- identical to each other
after  (BOTH): sha256 858e02dc1198e9c2… size 71568  LF 1700  CRLF 1625  BOM False   <- identical to each other
delta: +24 lines, ALL CRLF (bare-LF count unchanged at 75), +1722 bytes, 0 non-ASCII bytes added
```

`json.load` parses both; **46** dialogue rows, **no duplicate ids**; the new row's three speakers read
`['Bryn','Bryn','Bryn']`.

**Brace gate (the gate's own rule, not the raw one-liner)** —
`python tools/gate_brace.py` over all six `.cs` files: `GATE_BRACE_SUMMARY bad=0 of 6`, exit 0.
Raw counts also balanced (100/100, 103/103, 10/10, 44/44, 86/86, 38/38). **NUL scan: 0 NUL bytes in all
six** (`NUL_CLEAN`).

**Manifest generator** — `node tools/gen-tunable-manifest.mjs` →
`TUNABLE_MANIFEST_GEN_OK knobs=75 -> api/_lib/tunable-manifest.generated.json (rewritten)`.

**Node tests** — `node --test test/*.test.js` → `tests 761  pass 760  fail 0  todo 1`.
`node --test test/tunables-manifest.test.js` → **27/27 pass**, including *"every knob the build registers
is reachable in exactly one owner-facing area"*, *"the human contract document names every knob the build
registers"*, *"the shipped default is always inside the range the page will offer"* and *"the manifest is
7-bit ASCII"*. (The single `todo` is `test/heartbound-contract.test.js` *"the streak reaches
heartbound_state"*, tagged **WO-1693 finding 2** — pre-existing, unrelated to these files.)

**⚠ A FINDING WORTH THE LEAD'S ATTENTION:** the regeneration also picked up **seven knobs another lane
landed in `RemoteTunables.Registry` today without regenerating the manifest** —
`town.regenSuppressSecondsAfterHit`, `town.regenPctDuringWave`, `wave.hpGrowthPctPerWave`,
`wave.dmgGrowthPctPerWave`, `wave.maxCountPct`, `wave.countCapPct`, `wave.maxSimultaneousPct`. Their rows
are therefore carried in **this** lane's generated-file diff. That is correct (the file must be derived
from the current tree), but it means the generated JSON had been sitting **drifted** — `--check` would
have said `TUNABLE_MANIFEST_DRIFT` at any point today before this run.

---

## 5. Where the code was read from, not assumed

`Lantern.cs` · `ComposedDungeonHost.cs` · `ComposedOilStill.cs` · `DungeonLanternBalance.cs` ·
`TorchWardenDress.cs` (the one-shot idiom, `:246-334`) · `Core/Dialogue/DialogueService.cs` ·
`HUD/DialogueView.cs:18-45` (the flag-off bootstrap) · `Core/FeatureFlags.cs:175`
(`CustomDialogue defaultOn: true`) · `Core/State/GameStateService.cs:1335-1342` (`MarkTutorialSeen` =
key + `Save()`) · `Core/Ops/RemoteTunables.cs` (kind enum `:78-84`, `Int` resolution, the
`dungeon.roughStoneDropPct` row shape) · `RemoteTunablesDefaultsRegression.cs` (`ExpectedDefaults`,
`CompareDomain`, `ParseDocTable` cell indices) · `tools/gen-tunable-manifest.mjs` (the strict parse:
bare int consts only) · `api/_lib/tunables.js` · `api/_lib/tunable-manifest.js` ·
`CopyHygieneRegression.cs:99-215` (dialogue twins byte-parity + the em/en dash rule) ·
`ComposedDungeonRunRegression.cs` (the source-lint idiom + the public stripper) ·
`Core/UI/ElarionUiKitConformance.cs:380-430` + `ElarionUiKit.cs:1368` (`ShowToast`, `ToastTone`) ·
`DeNelle.Dungeons.asmdef` / `DeNelle.EditorRegression.asmdef` (both already reference what the new code
needs — **no asmdef edit**).

---

## 6. ⛔ UNPROVEN — named, not hidden

1. **It has not been compiled.** No Unity gate was run (per the brief), so `COMPILE_GATE_OK` is unproven
   and so is the new suite's `DUNGEON_LANTERN_TEACH_OK`. The brace/NUL checks above prove file hygiene,
   **not** that it builds.
2. **No run has played the teach.** Nothing here was observed in a play session or a capture. That the
   dialogue renders on the frame after a composed scene load under `LoadSceneWithFade` is **not proven** —
   `DialogueView` is `RuntimeInitializeOnLoadMethod` + `DontDestroyOnLoad` and the teach fires from the
   one-frame-deferred `ArmHeroPillars`, which is *why* the `IsRunning`/flag guards and the `Warn` paths
   exist rather than an assumption.
3. **`RemoteTunables.Int` inside `Awake` is unverified at runtime.** It is documented as never-throwing
   and resolves to the default with no remote table; existing consumers call it from static getters and
   lazily (`HeartfireCharges.cs:157`, `StructureContentWarmer.cs:204`), not demonstrably from an `Awake`.
   Both new reads are `Guard.Try`-wrapped, so the worst case is a logged skip and the authored value.
4. **`FeatureFlags.CustomDialogue` in the owner's own save was not read** (PlayerPrefs `ff.customdialogue`,
   `defaultOn: true` at source). The declined path is traced rather than assumed — §5 Lane A's own
   condition.
5. **`Lantern.OnDisable` is not provably the dungeon's exit.** The composed lantern is added to the
   **carried** hero, whose root is `DontDestroyOnLoad`, so the darkness-TOTAL line reports "since this
   component was last armed" and says so in the log. Whether it also means "this lantern can be alive
   while the TOWN is active" is the open WO-1602 fog question and was **not** investigated here — flagged,
   not fixed.
6. **On-device legibility of the existing oil meter is still unknown** (no dungeon screenshot exists on
   disk). §1.3 stands: if the teach lands and the mechanic is still opaque, capture the frame before
   touching the meter.
7. **Whether the guttering beat should be per-SAVE or per-RUN is an owner call.** It shipped **one-shot
   per save**, matching the "one-shot" ruling. Making it once per *run* is a one-line change (drop the
   `SeenTutorials` key and reset a bool in `InstallLanternTeach`) if she wants the reminder every delve.
8. **The guttering beat is a TOAST, not a dialogue row.** Chosen because the alternative suppresses hero
   input exactly while the fog wall closes and the ambush multiplier arms; no toast seam exists on
   `IVillageHud` or `DungeonHudController`, so `ElarionUiKitConformance.ShowToast` (Core) is used. If the
   lead wants Bryn's voice and portrait on that beat instead, it becomes a second `dialogues.json` row and
   a `Play` call on the same event — no new seam.
9. **The `[tunable-identity]` drain check is weaker than it looks, deliberately stated.** It compares the
   rail default to `DungeonLanternBalance.OilDrainPerSec`, which falls back to the built-in `0.5f` when the
   json is unreadable — so it **cannot distinguish "the json authors 0.5" from "the json failed to load and
   the fallback is 0.5"**. That is acceptable (the fallback deliberately mirrors the json, and
   `ComposedDungeonRunRegression` [lantern-burn] already pins the authored file itself), but it is not the
   stronger check it appears to be.
10. **The board bucket.** `**Status:** IMPLEMENTED, NOT YET GATED` leads with `IMPLEMENTED`, so
    `tools/board_build.py` buckets this row as **Done** (leading-keyword test, `board_build.py:187`) even
    though nothing has been gated. The label was dictated by the brief; flagging it so a Done row is not
    mistaken for a gated one.
11. **The §7 "suggested regression" (light-budget floor per dungeon) was NOT built.** It needs a floor the
   **owner** sets and must read the excluded-dungeon list off `DungeonStatusCatalog`. The standing entry
   net now *reports* the budget on every entry, which is the cheap half.

---

*Lane A + Lane C implemented 2026-09-16. Lane B remains SPEC. No gate, no commit — the lead gates and
commits (CLAUDE.md §11).*
