# WORK ORDER 1805 — The dungeon lantern is never taught, and there is no recourse once it burns out

**Status:** IMPLEMENTED
**Minted:** 2026-09-16 (number PRE-ASSIGNED by the lead; this WO does NOT touch `CLI_LANES_WO_NUMBERS.md`)
**Silo:** Dungeons — `DeNelle.Dungeons` (lantern / composed host) + `DeNelle.Core.Ops` (tunable rail) + `Assets/Resources|StreamingAssets/Data/Canonical/dialogue/dialogues.json`
**Lane disjointness:** Lane A + Lane C touch NO `.unity` scene, NO scene builder, NO art. Lane B (SPEC) would touch `Assets/Editor/RoomForge/DungeonBaker.cs` and therefore must NOT run concurrently with any other RoomForge/bake lane.
**Scheduling:** dungeons are NOT in the two-week video. See §8 — Lane A/C are the first bug lane AFTER the tester build; Lane B stays SPEC.

---

## 0. The owner's report (verbatim, 2026-09-16)

> "we have one issue in the dungeon is that we never really ever go over the mechanics of the torch in
> so nobody understands why the torch runs out and why just become suddenly dark. We need some kind of
> a first time in there to understand the torch and the light and we need a mechanic, they can actually
> get more materials and use it. That was a bug that I didn't want to report."

Three claims to check, and all three are true at source:
1. the mechanic is never explained;
2. it does go **suddenly** dark, not gracefully dim;
3. once it is dark there is effectively no way to get more oil.

---

## 1. ⚠ THREE PREMISES IN THE ROUTING BRIEF ARE WRONG — corrected at source before any spec

Recorded per CLAUDE.md §11B. Every line below was opened 2026-09-16 in this repo at `D:\EoA` on `dev`.

### 1.1 The burn is **200 s, not 62 s**

`Lantern.cs` serializes `_maxOil = 100f` (`:68`) and `_oilDrainPerSec = 1.6f` (`:71`) — but
`Lantern.ApplyBalanceData()` (`Assets/_Modules/Dungeons/Lantern.cs:240-254`) **deliberately overwrites
both from data** on `Awake`, in BOTH pipelines (its own doc-comment `:228-239` says so and says why).
The authored value is:

```
Assets/Resources/Data/Canonical/dungeon-balance.json   ->  "lantern": { "maxOil": 100.0, "oilDrainPerSec": 0.5 }
```
(byte-twin at `Assets/StreamingAssets/Data/Canonical/dungeon-balance.json`; code fallback mirrors it at
`Assets/_Modules/Dungeons/DungeonLanternBalance.cs` — `MaxOil = 100f`, `OilDrainPerSec = 0.5f`.)

**Proving line, from a real device run.** `grep -l "200s to empty" logs/device/*.log` returns seven
files; the timestamped instance below is from `logs/device/2026-08-20-portal.log` at `08-19 19:50:15.315`:

```
I/Unity : [Flow:Dungeon] DungeonLanternBalance loaded (version 1): maxOil=100 drain=0.50/s -> 200s to empty.
```

So: **200 s per flask.** The 62.5 s figure is the pre-WO-1112 state that `dungeon-balance.json`'s own
`_authoringNotes` describes as the defect it was created to end. Do not re-cite 1.6/62 s anywhere.

### 1.2 `_minOilLightFraction 0.35` is **NOT** the floor the player experiences — it really does go dark

The "never fully dark" reading comes from `ApplyRange`/`ApplyIntensity` scaling by
`Mathf.Lerp(_minOilLightFraction, 1f, OilFraction)`. That is only the first of three stages. Read the
whole ladder:

| Stage | Trigger | What the player sees | Source |
|---|---|---|---|
| dim | oil falling | range/intensity lerp down to 35 % of full | `Lantern.cs:516`, `:555` |
| **low oil** | `OilFraction <= 0.25` | flicker SFX loops; flame breath ×3 | `:160`, `:569`, `:580-592` |
| **final warning** | `EstimatedSecondsRemaining <= 30` → **oil ≤ 15, i.e. t ≈ 170 s** | range collapses to `_emptySafetyRange = 1.35f` (`:86`), intensity to `_emptySafetyIntensity = 0.8f` (`:89`), wick flutter ×0.82–1.0, **AND a linear fog wall is written at 0.45 m → 3.2 m** | `:518-531`, `:557-561`, `ApplyDarknessVisibility :352-385` |
| **darkness latch** | `OilFraction <= 0.12`, **t ≈ 176 s** | `IsInDarkness` true → ambush rate multiplier arms | `:167`; `ComposedAmbushDirector.cs:53-58`, `:81` (`inDarkness: true`) |

A 1.35 m light radius behind a 3.2 m fog wall **is** "suddenly dark". It arrives over ~30 s of a
`SmoothStep` collapse at ~2:50 into a run, with no warning the player was taught to read, and then the
ambush multiplier turns on. The owner's word "suddenly" is literally correct and the 0.35 floor does not
protect her. **This paragraph is the symptom; §2 is the defect.**

### 1.3 The oil gauge **already exists** — do not build one

`Assets/_Modules/Dungeons/UI/DungeonHudController.cs` builds a code-first (non-UXML) obsidian card,
360×104 at top-left `(24,-24)`, carrying an oil bar + a burn-time label + a low-oil pill, driven by the
pure `DungeonHudVM`. `ComposedDungeonHost.InstallOilHud()` (`ComposedDungeonHost.cs:150`, body just
below) **installs it if the bake placed none and binds the lantern through `SetLantern`** — and
`Assets/Editor/Regression/ComposedDungeonRunRegression.cs:316-327` FAILS if either half is removed or if
a UXML path is resurrected. The "plus a gauge if none exists" half of the brief is already done and
regression-pinned. Drop it.

> **Not proven:** that the meter is legible on a phone at that size, or that the owner noticed it. No
> dungeon screenshot exists on disk (`logs/device/*.png` has no dungeon frame). If the teach lands and
> the mechanic is still opaque, capture the frame before touching the meter — §7.

---

## 2. THE DEFECT: the teach is fully built, carries the owner's exact words, and is unreachable from every player route

This is the finding. **Nothing has to be written.** The copy the owner asked for already ships.

### 2.1 The copy exists, verbatim, in canonical data

`Assets/Resources/Data/Canonical/dialogue/dialogues.json`, row id **`dun_torch_warden`**, node `warn`
(twin at `Assets/StreamingAssets/Data/Canonical/dialogue/dialogues.json`):

```
Bryn: "No one walks the cottage dark. Take a torch - and mind it burning."
Bryn: "Your light feeds on oil and burns down as you walk. When the flame gutters, stand at the
       glowing oil stones - they fill it back up."
Bryn: "The deep rooms eat the unlit. Come back to me if your light dies."
```

Line 2 is, to the sentence, the teach the owner described ("understand the torch and the light").

### 2.2 Its ONLY caller lives in a scene no player route loads

- The teach is delivered by `TorchWardenDresser.Dress(...)`, called from exactly one production site:
  `Assets/_Modules/Dungeons/DungeonController.cs:1342-1343`.
- `DungeonController` exists in exactly **one** scene on disk. Proven **by component guid**, not by
  class-name text (a `.unity` file references a MonoBehaviour by guid; a name hit could be a GameObject
  name or a field). The guid is `c6840dc822eccff4ebe5bd43ed90cf72`
  (`Assets/_Modules/Dungeons/DungeonController.cs.meta`):

  ```
  grep -l "c6840dc822eccff4ebe5bd43ed90cf72" Assets/Scenes/*.unity Assets/Scenes/DungeonCompose/*.unity
  -> Assets/Scenes/Dungeon_HealersCottage.unity          (one hit, and only that one)
  ```
  **Zero hits across all ten `Assets/Scenes/DungeonCompose/dg_*.unity`.**
  `ComposedDungeonRunRegression.cs:319` asserts the same thing in its own failure text — recorded as
  agreement, not as the evidence.
- **Every player-facing dungeon door routes to a composed `dg_*` scene.** The authored portal table is
  `Assets/_Modules/Village/World/DungeonWorldPortalSpawner.cs`:

  | portal | line | routes to |
  |---|---|---|
  | starter | `:122` | `dg_starter_loop` |
  | vault | `:126` | `dg_sunken_vault` |
  | crypt | `:130` | `dg_bonecrypt` |
  | ember | `:134` | `dg_ember_deep` |
  | granary | `:147` | `dg_folks_granary` |
  | cottage | `:181` | `dg_healers_cottage` |

  That file's own header (`:32-34`) records that the legacy inline fallback onto
  `Dungeon_HealersCottage` / `Dungeon_FolksGranary` was **removed**, and `:149` remaps the legacy bare
  id `"HealersCottage"` onto the composed `dg_healers_cottage`. `DungeonStatusCatalog.cs:190-203` lists
  the same six ids as the player dungeons and `:229` excludes `dg_hollow_roads` /
  `dg_descent_probe` / `dg_stair_rig` / `dg_stairwell_probe` as a crossroads + three test fixtures.

**Conclusion, and it classifies the ticket:** the lantern teach, the crafting pedestal, the ingredient
pickups and the torch recipe are all wired into the Pipeline-B `DungeonController` scene and are
therefore **dead on every route the player can take**. The player meets the oil mechanic for the first
time as an unexplained blackout. That is a **BUG**, exactly as the owner classified it.

> **Not proven:** whether any *runtime-injected* `DungeonPortal` instance can still be pointed at
> `Dungeon_HealersCottage`. `DungeonPortal.cs:34` still carries `_dungeonId = "Dungeon_HealersCottage"`
> as a serialized default, and the scene is still in `ProjectSettings/EditorBuildSettings.asset`
> (line 21). No `_dungeonId` for a `DungeonPortal` appears in any town scene (`grep -rn "_dungeonId"
> Assets/Scenes/*.unity` → only `Dungeon_HealersCottage.unity:29747`, which is the `DungeonController`'s
> own field), and the only spawner is the table above. So the legacy scene is *almost certainly*
> orphaned — but "almost certainly" is a guess and is recorded as one. It does not change Lane A:
> the composed path needs its own teach either way.

### 2.3 Nothing in the tutorial system mentions it either

`grep -ni "lantern\|torch\|oil"` over `Assets/Resources/Data/Canonical/tutorial/tutorial-steps.json`
returns **zero rows**. `canon-strings.json` carries only the *proper noun* "the Lantern of Elarion"
(`:36-37`) — narrative, not mechanics.

---

## 3. THE SECOND DEFECT: the refill budget is finite, one-use, and the starter dungeon gets ONE

### 3.1 Oil stones are consumed, not re-usable

`Lantern.CheckOilStones()` (`:458-487`) tops the flask to `_maxOil` and then adds the stone id to
`_spentOilStones` (`:482`), so **each stone refills exactly once per visit**. There is no prompt and no
label — the refill happens silently by walking inside `radius` (2.5 in every authored entry).
`ComposedOilStone.Start()` calls `ComposedPropVisuals.BuildOilStone` so at least it is *visible*
(WO-1112 added that; the bake had placed a rendererless empty).

### 3.2 The authored counts — parsed from the layout files, not read by eye

`Assets/Resources/Data/Canonical/dungeon-layouts/dg_*.json`, `oilStones[]` length vs `rooms[]` length:

| dungeon | oil stones | rooms | free light ceiling `200 × (1+stones)` | + every field still (`+80 s` each) |
|---|---|---|---|---|
| **`dg_starter_loop`** | **1** | **11** | **400 s (6 m 40 s)** | 480 s |
| `dg_healers_cottage` | 2 | 9 | 600 s | 760 s |
| `dg_bonecrypt` | 2 | 17 | 600 s | 760 s |
| `dg_sunken_vault` | 3 | 14 | 800 s | 1040 s |
| `dg_ember_deep` | 3 | 17 | 800 s | 1040 s |
| `dg_folks_granary` | 3 | 11 | 800 s | 1040 s |
| *(`dg_hollow_roads` 0 / 9, `dg_stair_rig` 0 / 10, `dg_stairwell_probe` 0 / 3 — the four NON-dungeons excluded at `DungeonStatusCatalog.cs:229`; their zero is correct, not a gap)* | | | | |

**Worst-case dark time.** Past the ceiling the run is dark for **the entire remainder, unbounded**, with
`ComposedAmbushDirector` spawning at the `DarknessRateMult` rate. The worst *ratio* is the one the
player meets first: **`dg_starter_loop` — 11 rooms on a 400 s budget, one refill, and no teach.** A
player who explores, fights, reads a lore stone or dies-and-walks-back in the FIRST dungeon in the game
spends the rest of it in a 1.35 m halo with elevated ambushes and no stated way out.

> **Not computed — stone SPACING vs. the burn.** The brief asked for spacing. It is not derivable from
> the layout files alone: `oilStones[].offset` is **room-relative** (e.g. `dg_starter_loop`'s single
> stone is `{ roomId: "side_vault", offset: [-1.8, 0.0, 0.8] }`) and the rooms' world positions are
> computed by `GraphDungeonComposer` walking sockets from the entry, so a true metres-between-stones
> figure requires reading the composer output or the baked scene transforms. **Counts are reported
> instead, and they are the load-bearing axis** — a one-use stone that is 10 m or 100 m away is still
> one refill. If the owner wants spacing, it is one editor query over the baked
> `Assets/Scenes/DungeonCompose/*.unity` transforms; it is deliberately not guessed here.

> **Not proven:** that any real run has actually hit empty. `grep -i "lantern\|Flow:Dungeon\]\|Flow:DungeonOil\]"`
> over `logs/device/*.log` + `logs/f8-inbox/**` returns only the balance-load line and the
> composed-hook line — **no run reached the final warning in any capture on disk.** That is itself a
> finding: see §7, the drain path is silent by construction, so a run that went dark would leave no
> trace to find.

### 3.3 The "get more materials and use it" path — three separate gaps

**(i) There IS a field still, and it is chained to the same scarce stones.**
`ComposedOilStill.cs`: a one-use interact at `Radius 2.8` offering `"Distill oil (flask + cloth)"`,
which spends `ing_oil_flask` ×1 + `tattered-cloth` ×1 from `VillageInventory` for
`RefillFraction = 0.40f` (= 80 s). `ComposedDungeonHost.cs:139-148` attaches one to **every
`ComposedOilStone` marker and only to those** — so *the number of stills equals the number of stones*.
In `dg_starter_loop` that is **one**. The emergency valve is gated behind the resource it is meant to
replace.

**(ii) One of its two ingredients is a material the game cannot even name.**
`ing_oil_flask` is a proper catalogued material (`materials.json:76-81`, with
`iconPath: ItemIcons/ing_oil_flask`) and drops from `loot-tables.json` at 0.12 / 0.35 / 0.55 / 0.60
depending on table. **`tattered-cloth` is not in `materials.json` at all** — that file's own `_comment`
says: *"The remaining legacy scaffolding mats (monster-hide / wild-herb / tattered-cloth /
rare-essence) are NOT listed here - they have no recipes and resolve to the glyph fallback in the
crafting UI."* So the one emergency craft in the game requires an unnamed, unillustrated legacy
scaffolding item. A player cannot plan for it. (The same mat is also required by a live consumable,
`craft-scout-tent-kit` — so this is not unique to the still, but the still is the one where not being
able to plan costs the player the run.)

**(iii) There is no MID-RUN refill from the bag, and the torch is a lie in data.**
- `Lantern.AddOilFraction` (`:200-205`) has **exactly one caller** — proven, not assumed:
  `grep -rn "AddOilFraction" Assets/ --include=*.cs` returns the declaration and
  `ComposedOilStill.cs:47`, and nothing else.
- **The only carry-in that exists is a PRE-ENTRY multiplier, not a refill.**
  `Lantern.ApplyExpeditionBlessing` (`:424-452`) consumes one
  `convenience:lantern-oil-3x-expedition` or `...-2x-expedition` charge out of `GearInventory` on
  `Configure`/`ConfigureStandalone` and stretches the whole visit's burn ×3 or ×2. That is a store
  convenience item spent at the door — it cannot be used once the player is already dark, which is
  precisely the moment the owner is describing.
- **There is no oil consumable.** `grep -ni "oil\|lantern"` over
  `Assets/_Modules/Village/Items/ConsumableUseService.cs` returns **zero lines**, and parsing
  `consumable-recipes.json` shows `ing_oil_flask` appears only as an *ingredient* to three unrelated
  crafts — `craft-cons_emberfire_bomb`, `craft-cons_suppressing_smoke` (2 flasks), and
  `craft-cons_wardens_campfire`. So oil flasks are already a **contested** resource that three
  recipes compete for, and none of the three produces light.
- The `torch` recipe (`crafting-recipes.json`, recipes[0]) promises *"steady light that never runs dry"*
  and toasts *"its light is yours, Keeper - and it will not fade."* **Nothing in the build consumes a
  torch.** `grep -rn "Torches" Assets/ --include=*.cs` resolves only to `NestedTypes.cs:165` (the save
  field), `SaveSchema.cs:889` (clamp), two tests, and `TorchWardenDress.cs:17`, which states it plainly:
  *"the AtbInventory.Torches USE path is unbuilt."* `TorchWardenInteractable.GrantTorchOnce` grants the
  torch precisely as a pre-seed for a mechanic that never landed.
- And the crafting half is unreachable anyway: ingredient pickups are placed only by
  `Assets/Editor/DungeonSceneBuilder.cs:1433-1490` (the legacy cottage bake) and
  `DungeonController.cs:1362-1391` (runtime scatter). `grep -n "Ingredient\|CraftingPedestal"
  Assets/Editor/RoomForge/DungeonBaker.cs` → **no match**: the composed baker places chests, oil stones,
  traps, keys, locks and extracts (`:1379-1388`) and **no ingredients and no pedestal**.
  `grep -c CraftingPedestal Assets/Scenes/DungeonCompose/dg_starter_loop.unity` → `0`.

---

## 4. Classification per CLAUDE.md §13

| # | Finding | Class | Why |
|---|---|---|---|
| 1 | The built `dun_torch_warden` teach never plays on any player route (§2) | **BUG** | Working, authored, owner-approved content is unreachable because its only caller is in a retired scene. Nothing new is needed. |
| 2 | No recourse once the flask is empty; `dg_starter_loop` ships 1 stone for 11 rooms (§3.1-3.2) | **BUG** | The owner called it a bug. A state a player cannot exit or mitigate is a defect regardless of tuning. |
| 3 | The field still is chained 1:1 to the stones it is meant to back up (§3.3 i) | **BUG** | Defeats the purpose of the emergency valve; a one-line placement rule fixes it. |
| 4 | `tattered-cloth` is an unnamed legacy mat used by a live recipe (§3.3 ii) | **BUG** (data-only) | A requirement the UI cannot render. |
| 5 | Drain + stone spacing are not on the remote rail (§6) | **BUG-adjacent / enabling** | `dungeon-balance.json` is NOT on `RemoteCatalogOverrides.Allowlist` (its own `_authoringNotes` says so), so today the owner cannot re-tune darkness without a ~10 min APK rebuild. |
| 6 | Carry-in oil as a bag consumable (§5 Lane B) | **NEW FEATURE** | No seam exists. `ConsumableUseService` has no oil route and `Lantern` has no external add API beyond the still's. |
| 7 | The torch as a longer-burning light upgrade (§5 Lane B) | **NEW FEATURE** | `AtbInventory.Torches` has no use path at all. Promising it in a toast is a separate data-honesty fix. |
| 8 | Ingredient pickups / a pedestal in composed dungeons (§5 Lane B) | **NEW FEATURE** | `DungeonBaker` has no placer and the layout schema has no ingredient block. |

---

## 5. The work, cheapest first

### LANE A — first-time lantern teach. **Status: IMPLEMENTED 2026-09-16 (not yet gated).** Smallest change in the ticket.

> **IMPLEMENTED AS FOLLOWS (see `WORK_ORDER_1805_...RESULT.md` for the full record).** Lead rulings
> applied: **Bryn speaks** it; it fires in **every** composed `dg_*` dungeon on the first lantern-lit
> step; it is **one-shot per save** through the `SeenTutorials` idiom; it **completes on the first
> oil-stone refill** via a new `Lantern.OilStoneUsed` event; and the ambush/darkness beat gets its own
> one-shot line on the `IsFinalWarning` edge. Files: `Assets/_Modules/Dungeons/ComposedDungeonHost.cs`
> (the teach, after `InstallOilHud`), `Assets/_Modules/Dungeons/Lantern.cs` (the two edge events +
> section 7 instrumentation), and a **new** `dun_lantern_intro` row in both canonical `dialogues.json`
> twins. **TWO KEYS, and the difference is load-bearing:** `dun_lantern_intro_shown` latches only when
> the dialogue ACTUALLY ENDED (`EndedWithId`), never on the `Play()` attempt — so a declined or
> unrendered teach leaves the save untouched; `dun_lantern_intro_done` latches on the first refill, i.e.
> the player did the taught thing, and also satisfies the gate. `FeatureFlags.CustomDialogue` is tested
> **before** `Play`, because with it off no View is subscribed and `Play` returns **true** into an
> unrenderable, uncloseable VM with hero input suppressed. The darkness line is a **non-blocking toast**
> (`ElarionUiKitConformance.ShowToast`), deliberately not a modal: it fires while the fog wall closes and
> the ambush multiplier arms. No gauge was added (§1.3); no `.unity` file touched.

**Pre-cleared blocker — the dialogue presenter DOES reach a composed dungeon.** `DialogueService`
(`Assets/_Modules/Core/Dialogue/DialogueService.cs:17`) is a **static class**, and its header states
that `Play()` only raises `Opened` — *"A View (subscribed to Opened) builds its panel"* — so a `Play`
with no live View would return `true`, render nothing, and leave `ActiveVm` open (`IsRunning` true,
hero input suppressed) with no way to close it. That would have made Lane A much more than five lines.
It is **not** a risk here:

```
Assets/_Modules/HUD/DialogueView.cs:24-38
  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
  private static void Bootstrap()  ->  new GameObject("DialogueView"); DontDestroyOnLoad(go); AddComponent<DialogueView>();
```
It is engine-bootstrapped and `DontDestroyOnLoad`, so it is present in every scene including
`dg_*` — it is placed in **no** scene at all (`grep -l "82cae748fb2471b4c9e8ea33a81b5bb4"` over every
scene and prefab → zero hits; the only construction site is its own `Bootstrap`).
**One condition:** that bootstrap `return`s early when `FeatureFlags.CustomDialogue` is off, tracing
*"no dialogue can render"*. The flag reads `defaultOn: true` (`FeatureFlags.cs:175`), so the default
path is live — but Lane A's implementation MUST handle `Play` returning false / the flag being off
with a `FlowTrace.Warn`, and must not latch its one-shot `SeenTutorials` key on a dialogue that never
rendered. (This is the same class of bug as the WO-844 potion lesson cited in `DungeonTreasureCache`:
never mark taught on the attempt, only on the completion.)

**Seam (named, not guessed):** `Assets/_Modules/Dungeons/ComposedDungeonHost.cs`, immediately after
`InstallOilHud()` (`:150`) — i.e. after the lantern is armed (`:131`) and the meter is bound, so the
first thing the player reads and the first thing they can look at agree.

1. **Play the existing dialogue once per save.** Gate on `GameStateService.Instance.State.SeenTutorials`
   with a new one-shot key — follow the `TorchWardenInteractable` idiom exactly
   (`TorchWardenDress.cs:246` the class, `:251` the key const, `:312-334` `GrantTorchOnce`: check the
   key, act, then `svc.MarkTutorialSeen(key)` at `:331`, which persists the key AND any accompanying
   mutation in one `Save()`). Suggested key: `dun_lantern_intro_seen`.
2. **Call `CoreDialogue.DialogueService.Play(...)`.** This satisfies memory rule
   `tutorial-guide-body-one-time-then-images` — **it is a dialogue screen, with no world actor, no
   seating, no navmesh, no despawn lifecycle.** Do **not** spawn a Bryn body in a composed dungeon and
   do **not** reuse `StoryIntroController` (gated on `Onboarded == false`, lives in the Title scene).
3. **Copy — a data-only edit, and it needs one.** The existing `dun_torch_warden` lines assume Bryn is
   standing there ("Take a torch", "Come back to me if your light dies"). Add a **new row**
   `dun_lantern_intro` rather than editing the row the legacy scene still references, carrying the
   owner's sentence intact plus the recourse line she asked for:

   > "Your light feeds on oil and burns down as you walk."
   > "When the flame gutters, stand in a glowing oil stone — it fills the flask back up."
   > "The deep rooms eat the unlit. Mind the meter, Keeper."

   **⚠ OWNER RULING NEEDED — who speaks it.** `dun_torch_warden` is Bryn's, and the copy above is
   hers word-for-word, but Bryn is a cottage NPC and this row plays with no body in any dungeon.
   **Default, if the owner does not rule: keep `Bryn` as the speaker** (the copy is hers and the
   portrait resolves). Do not invent a name. Speaker identity must come from the existing catalog,
   never an invented name table (memory `echo-is-essence-of-guarded-person`). Edit **both twins
   byte-identically**
   (`Assets/Resources/...` and `Assets/StreamingAssets/...`) and per memory
   `canonical-json-edits-binary-only-verify-newlines` patch from HEAD bytes and prove the LF count —
   `dialogues.json` is **already modified in the working tree** on both sides (see `git status`), so the
   CLI must reconcile by explicit path and must not blind-replace either file.
4. **"Completes on the first refill" needs ~5 lines of CODE, not data.** `Lantern.CheckOilStones()`
   (`:458-487`) raises no event and logs nothing when it spends a stone. Add a public
   `event System.Action OilStoneUsed` (or a `FirstRefillTaken` bool + the trace in §7) and have Lane A's
   teach latch closed on it. Do not describe this half as data-only.
5. **No new gauge.** §1.3.

**Acceptance:** entering `dg_starter_loop` on a save with no `dun_lantern_intro_seen` key shows the
dialogue once; re-entering shows nothing; the existing oil meter is on screen while it reads;
`Assets/Scenes/DungeonCompose/*.unity` are untouched.

### LANE C — put darkness on the remote rail. **Status: IMPLEMENTED 2026-09-16 (not yet gated).**

> **IMPLEMENTED AS SPECIFIED — the four keys, `TunableKind.Int` only, every default an identity.**
> `dungeon.lanternDrainPerSecX100` **50**, `dungeon.lanternOilStoneRefillPct` **100**,
> `dungeon.lanternStillRefillPct` **40**, `dungeon.lanternFinalWarningSec` **30**. All five surfaces
> moved in one edit (`CompareDomain` fails on a length mismatch): `RemoteTunables.Registry` +
> `ExpectedDefaults` + `docs/PROD022_TUNABLE_FLAGS.md` rows **#72–#75** + `TUNABLE_KEYS` in
> `api/_lib/tunables.js` + the PRESENTATION block in `api/_lib/tunable-manifest.js`, then
> `api/_lib/tunable-manifest.generated.json` **regenerated** (`TUNABLE_MANIFEST_GEN_OK knobs=75`).
> `docs/reference/TUNABLE_LEVER_INVENTORY.md` §5.8 carries a LANDED note instead of a copy of the
> values. **One design point the spec did not state and the implementation had to settle:** the drain
> and the warning window are ALSO authored in `dungeon-balance.json`, so the consumers treat a row **at**
> its shipping default as *not set* and leave that json as the single authority — a rail that always won
> would have made the json dead data the first time anyone re-authored it. `maxOil` stays off the rail
> (§6/§9). Consumers wired: `Lantern.ApplyBalanceData` (drain + warning window),
> `Lantern.CheckOilStones` (stone %), `ComposedOilStill.TryDistill` (still %).

**Seam:** `Assets/_Modules/Core/Ops/RemoteTunables.cs`. The `dungeon.*` namespace is already
established — `KeyDungeonRoughStoneDropPct = "dungeon.roughStoneDropPct"` at `:927`, its
`DungeonRoughStoneDropPctDefault = 5` at `:918`, and its `TunableSpec` registry row at `:1806`. Copy
that row shape.

**⚠ `TunableKind` is `Bool` and `Int` ONLY** (`RemoteTunables.cs:78-84` — there is no `Float`). The drain
is a float, so register it scaled and say so in the spec text:

| key | kind | identity default | maps to |
|---|---|---|---|
| `dungeon.lanternDrainPerSecX100` | Int | **50** (= 0.50/s, today's authored value — identity) | `Lantern.ApplyBalanceData`, applied over `DungeonLanternBalance.OilDrainPerSec` |
| `dungeon.lanternOilStoneRefillPct` | Int | **100** (a stone tops the flask — identity) | `Lantern.CheckOilStones` |
| `dungeon.lanternStillRefillPct` | Int | **40** (identity: `ComposedOilStill.RefillFraction`) | `ComposedOilStill` |
| `dungeon.lanternFinalWarningSec` | Int | **30** (identity: `Lantern._finalWarningSeconds`) | `Lantern` |

Every default is the value that ships today, so a build with no remote document behaves **bit-identically**.
Prefer the rail over widening `RemoteCatalogOverrides.Allowlist` to `dungeon-balance.json`: the rail is
the proven seam (WO-1763), and the catalog-remote path is flag-gated OFF (`ff.catalogremote`).
**Read at source, not from the json's `_authoringNotes`:** `RemoteCatalogOverrides.Allowlist`
(`Assets/_Modules/Core/Data/RemoteCatalogOverrides.cs:138-145`) holds exactly five paths —
`enemies.json`, `waves.json`, `echoes-balance.json`, `kill-rewards.json`, `siege-stakes.json` —
and `dungeon-balance.json` is **not** among them. Rail order-of-precedence and the
`ProvenanceRemote/Local/Default` trace already exist — do not invent a second resolution path.
Also update the tunable manifest / `docs/reference/TUNABLE_LEVER_INVENTORY.md` §2 in the same commit
(CLAUDE.md §15).

### LANE B — "they can actually get more materials and use it". **Status: SPEC — STILL SPEC after the 2026-09-16 Lane A/C implementation. Deliberately NOT implemented; needs new seams and owner rulings (B1 count-per-band, B2 which mat, B3/B4 build-or-correct-the-copy).**

Four sub-items, in ascending cost. Each needs an owner ruling before it becomes a WO.

**B1 — decouple the field still from the oil stone (cheapest; near-ready).**
Today `ComposedDungeonHost.cs:139-148` attaches a `ComposedOilStill` to each `ComposedOilStone` and
nowhere else. Give the still its own placement (a `stills[]` block in the layout schema, or a
room-band rule in `DungeonBaker` alongside `PlaceComposeOilStones` at `:1655-1690`) so a player holding
a flask has a use for it away from the stones. **Code + data.** Ruling needed: how many per room band.

**B2 — make `tattered-cloth` real, or swap it out (data-only).**
Either promote `tattered-cloth` into `materials.json` with a `displayName`/`glyph` (the WO-850
precedent, which promoted the three torch mats for exactly this reason), or re-point
`ComposedOilStill`'s second ingredient at an already-catalogued mat (`ing_cloth_scrap` is in
`crafting-recipes.json` and reads correctly for a wick). **Owner's call, data-only either way.**

**B3 — oil as a bag consumable (NEW FEATURE, new seam — confirmed, not assumed).**
`Lantern.AddOilFraction` is the right hook and already exists. What does not exist is a route from the
bag: `ConsumableUseService.cs` contains **no** `oil`/`lantern` line (grepped), `ing_oil_flask` is a
*material* (larder) not a *consumable* output, `consumable-recipes.json` has no lamp-oil recipe (all
four `ing_oil_flask`/`tattered-cloth` recipes read out in §3.3 ii/iii — none produces light), and there
is no dungeon-side "use from bag" affordance. Spec:
a `consumable-recipes.json`-authored "Lamp Oil" that `ConsumableUseService` resolves to
`AddOilFraction` when a `Lantern` is live, refused with a reason outside a dungeon. Ingredient supply
already works — `ing_oil_flask` drops at 0.12–0.60 across `loot-tables.json:108/137/147/164/205`, so
the *earning* half of "get more materials" is genuinely already built; only the *using* half is missing.

**B4 — the torch as the longer-burning upgrade (NEW FEATURE), and the data-honesty fix.**
`crafting-recipes.json` promises a light that "never runs dry" and `AtbInventory.Torches` has no use
path (`TorchWardenDress.cs:17`). Either build it (a torch consumed on entry sets
`Lantern._expeditionOilMultiplier` — note `ApplyExpeditionBlessing` (`:424-452`) is already exactly this
shape for the `convenience:lantern-oil-2x/3x-expedition` store items, so the torch should ride that
existing seam and **not** a second one) or change the copy so it stops promising a mechanic that does
not exist. Plus: composed dungeons have no pedestal and no ingredient pickups (§3.3 iii), so shipping
the torch at all requires a `DungeonBaker` placer.

---

## 6. Balance note for the owner (no code needed to read it)

With Lane C landed, the darkness is tunable by feel from the database:
- `dungeon.lanternDrainPerSecX100` **50** → 200 s. **33** → ~300 s. **25** → 400 s.
- Raising `dungeon.lanternStillRefillPct` from 40 to 100 makes a flask-plus-cloth a full refill.
- Keep `maxOil` at 100: it is the meter's 100 % and the denominator of every fraction threshold
  (`_lowOilFraction` 0.25, `IsInDarkness` 0.12, `_minOilLightFraction` 0.35). `dungeon-balance.json`'s
  `_authoringNotes` explains at length why capacity is the wrong knob for duration. Tune the **rate**.

---

## 7. Instrumentation that would have proven each half — and that the drain path does not have

⛔ CLAUDE.md §12: no capture on disk shows a run reaching empty (§3.2), **because nothing logs it.**
Add these before or alongside the fix; they are permanent (§12 — never strip FlowTrace).

| Claim to prove | Line to add | Where |
|---|---|---|
| a stone was spent, and which | `FlowTrace.Step("DungeonOil", $"oil stone '{stoneId}' SPENT: oil {before:F0}->{_maxOil:F0}, {_spentOilStones.Count}/{_oilStones.Count} caches used, +{200f:F0}s")` | `Lantern.CheckOilStones` `:479-483` — today it mutates and returns with **zero** trace |
| the run entered the collapse | `FlowTrace.Once("DungeonOil", "final-warning ENTERED: t={Time.timeSinceLevelLoad:F0}s, stones spent={n}/{total}, range->1.35u, fog wall 0.45..3.2m")` | edge of `IsFinalWarning` in `ApplyDarknessVisibility` `:352` — today the only line there is a **throttled `Atmos`** line about the fog write, which is a different question |
| the flask actually hit zero | `FlowTrace.Once("DungeonOil", "flask EMPTY at t={...}s in scene='{...}' — remaining recourse: stones={unspent}, stills={unused}")` | `Lantern.DrainOil` `:418` — today it clamps to 0 silently |
| the darkness latch armed, and for how long | `FlowTrace.Once` on the `HasBeenInDarkness` edge, printing elapsed-dark seconds on scene exit | `ComposedAmbushDirector.cs:53-58` already has the edge; it does not print the duration |
| the teach fired or was skipped, and why | `FlowTrace.Step("DungeonTeach", $"lantern intro: seen={seen} played={ok} key='{Key}'")` | Lane A's new call site |
| the refill budget vs. the dungeon's size | `FlowTrace.Step` extension on the existing `ComposedDungeonHost.cs:132-134` line: add `rooms=` and `ceiling={200*(1+stones)}s` so **every** dungeon entry self-reports its light budget | `ComposedDungeonHost.cs:132` |

That last one is the cheap standing net: after it lands, any dungeon authored with too few stones
announces itself on entry instead of waiting for the owner's eyes (CLAUDE.md §14).

**Suggested regression** (a `DataRegression` case, not a play session): parse every
`dungeon-layouts/dg_*.json` that `DungeonStatusCatalog` lists as a **player** dungeon and FAIL when
`DungeonLanternBalance.SecondsToEmpty × (1 + oilStones.Count) / rooms.Count` falls below a floor the
owner sets. **Read the burn off `DungeonLanternBalance.SecondsToEmpty`** (which exists for exactly this,
per its own doc comment) — never hardcode `200`, or the test goes stale the first time Lane C moves the
drain, which is the same duplicated-state failure §9 forbids. That pins the §3.2
table so the next authored dungeon cannot silently reintroduce a 1-stone/11-room budget. It must read
the excluded list off `DungeonStatusCatalog.cs:229` rather than hardcoding a second copy (§5/§8
duplicated-state law).

---

## 8. Scheduling — the two-week video window

Dungeons are **not** in the video. Per the lead's brief this ticket is the **first bug lane AFTER the
tester build**, with one argued exception:

- **Lane A is small enough to argue for earlier.** It is one gate + one `DialogueService.Play` + one
  event on `Lantern` + one new `dialogues.json` row. It touches no scene, no bake, no art, no
  Addressables, and therefore needs no R2 push (CLAUDE.md §16). If a tester enters a dungeon at all,
  this is the difference between "the game broke" and "I ran out of oil". **The lead rules.**
- **Lane C** is a rail registration with identity defaults — zero behaviour change until the owner sets
  a value — but it has no felt value during the video window. After.
- **Lane B** stays SPEC and needs owner rulings (B1 count-per-band, B2 which mat, B3/B4 whether to
  build or to correct the copy) before any of it becomes a work order.

---

## 9. What NOT to touch

- ❌ Any `.unity` file. Both landing lanes are code + canonical JSON only.
- ❌ `Assets/Scenes/Dungeon_HealersCottage.unity` and `DungeonController` — the legacy Pipeline-B path.
  Do **not** "fix" the teach by reviving that scene; the composed path is where the player is.
- ❌ `Lantern._maxOil` / `dungeon-balance.json`'s `maxOil` — §6.
- ❌ `_minOilLightFraction`, `_lowOilFraction`, `IsInDarkness`'s 0.12 — the thresholds are consumed by
  the HUD bands (`DungeonHudVM`), the ambush director and the fog collapse. Changing one silently moves
  three systems. Tune the rate (Lane C).
- ❌ Any `FlowTrace` / `Guard` removal (§12, owner ruling 2026-08-09).
- ❌ Adding a second oil HUD, or a UXML one (`ComposedDungeonRunRegression.cs:324-327` fails on UXML,
  and §8 of CLAUDE.md: UXML does not render in builds).
- ❌ `CLI_LANES_WO_NUMBERS.md` — 1805 was pre-assigned by the lead.
- ❌ A hardcoded copy of the excluded-dungeon list, the room counts or the stone counts anywhere in
  code or docs. Read them off `DungeonStatusCatalog` and the layout JSON (the §2/§5/§8 duplicated-state
  law; the §3.2 table in this document is dated evidence, not an authority).

---

## 10. Everything asserted here, and where it was read (all opened 2026-09-16 on `dev`)

`Assets/_Modules/Dungeons/Lantern.cs` · `DungeonLanternBalance.cs` · `ComposedDungeonHost.cs` ·
`ComposedOilStone.cs` · `ComposedOilStill.cs` · `ComposedAmbushDirector.cs` · `DungeonController.cs` ·
`TorchWardenDress.cs` · `DungeonTreasureCache.cs` · `UI/DungeonHudController.cs` · `UI/DungeonHudVM.cs` ·
`Crafting/CraftingData.cs` · `Assets/Editor/RoomForge/DungeonBaker.cs` ·
`Assets/Editor/DungeonSceneBuilder.cs` · `Assets/Editor/Regression/ComposedDungeonRunRegression.cs` ·
`Assets/_Modules/Village/World/DungeonWorldPortalSpawner.cs` · `Assets/_Modules/Village/Buildings/DungeonPortal.cs` ·
`Assets/_Modules/Core/World/DungeonStatusCatalog.cs` · `Assets/_Modules/Core/Ops/RemoteTunables.cs` ·
`Assets/_Modules/Core/FeatureFlags.cs` · `Assets/Resources/Data/Canonical/dungeon-balance.json` ·
`dungeon-layouts/dg_*.json` (all ten, parsed) · `crafting-recipes.json` · `materials.json` ·
`loot-tables.json` · `consumable-recipes.json` · `dialogue/dialogues.json` · `tutorial/tutorial-steps.json` ·
`canon-strings.json` · `ProjectSettings/EditorBuildSettings.asset` · `logs/device/2026-08-20-portal.log`

Plus, for the corrections in §5: `Assets/_Modules/HUD/DialogueView.cs` (+ `.meta` guid),
`Assets/_Modules/Core/Data/RemoteCatalogOverrides.cs`, `Assets/_Modules/Village/Items/ConsumableUseService.cs`,
`Assets/_Modules/Dungeons/DungeonController.cs.meta`.

**Unverified, and named as such — the complete list:**
1. **No capture on disk shows a run reaching empty.** Nothing logs the drain path (§7), so a run that
   went dark would have left no trace to find. This is the single biggest gap in the ticket.
2. **No dungeon screenshot exists** in `logs/device/*.png`, so the existing oil meter's on-device
   legibility is unknown. If the teach lands and the mechanic is still opaque, capture the frame before
   touching the meter (memory `screenshots-are-primary-evidence-for-visual-defects`).
3. **Whether the legacy `Dungeon_HealersCottage` scene remains reachable by any runtime-configured
   portal.** The guid grep proves the scene is the only `DungeonController` host and the spawner table
   proves all six doors route to `dg_*`, but `DungeonPortal.cs:34` still carries the legacy id as a
   serialized default and the scene is still in `EditorBuildSettings.asset:21`. The evidence says
   orphaned; "orphaned" is not proven.
4. **Stone spacing in metres** — not computed, see §3.2. Counts are reported instead.
5. **Whether `FeatureFlags.CustomDialogue` is ON in the owner's actual save.** It reads `defaultOn:
   true` at source, but the flag is PlayerPrefs-backed (`ff.customdialogue`) and her local value was not
   read. Lane A must trace the declined path rather than assume it (§5 Lane A, pre-cleared blocker).

---

*RCA by the read-only RCA + spec lane, 2026-09-16. No code was written, no gate run, no commit made.*
