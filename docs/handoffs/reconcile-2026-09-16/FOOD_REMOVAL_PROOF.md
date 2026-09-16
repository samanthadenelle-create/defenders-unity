# FOOD REMOVAL PROOF — 2026-09-16

Owner ruling to test: *"reconcile that we completely removed food."*

Everything below was measured in the working tree on 2026-09-16 at HEAD `c9327a210`. No claim here
rests on a doc, a memory or an earlier session (CLAUDE.md §11B). Every `file:line` was opened.

## VERDICT

**"Completely removed" is TRUE of the player-facing vocabulary everywhere EXCEPT the raid /
wave end-state spoils screen, which still prints the literal word `Food` and does so
DELIBERATELY, on a WO-1374 ruling that predates today's statement.**

- **Localization: clean.** 0 occurrences of `food` (case-insensitive) in all ten locale files —
  `en, de, es, fr, ja, ko, pt-BR, ru, zh-Hans, ar` under `Assets/Resources/Data/Canonical/`.
- **`canon-strings.json`: clean.** 0 occurrences.
- **The Unity Localization string tables: clean.** 0 occurrences in all eight `.asset` tables under
  `Assets/Localization/Tables/` — `GameStrings Shared Data`, `GameStrings`, and the `_de _en _es _fr
  _pt-BR _ru` variants. This is a **second, separate** string system from the canonical locale JSON
  and had to be checked on its own; `Assets/Localization/Tables/GameStrings Shared Data.asset` is
  dirty in this tree. `Assets/Resources/Localization/` holds nothing but fonts.
- **Catalog display strings: clean.** A scan of every `displayName` / `name` / `label` / `title` /
  `description` / `effect` / `perk` value in all 22 `Assets/Resources/Data/Canonical/*.json` files
  returned exactly two hits, both the *sprite asset name* `currency_food` in `concept-icons.json`
  — an art filename, not text a player reads. The two `kind: "food"` consumables are displayed as
  **"Traveler's Rations"** and **"Hearthfire Stew"**.
- **The wallet rail agrees.** All three surfaces that label this balance re-read at source today:
  `Assets/_Modules/HUD/Kit/HudKitController.cs:3483` → `{ "Wood", "Iron", "Stone", "Crystals" }`;
  `Assets/_Modules/Village/BuildMode/BuildWalletRow.cs:46` → `"Wood", "Iron", "Stone", "Crystals",
  "Gold"`; `Assets/_Modules/HUD/DailyQuestHud.cs:407` → `DetailCardRow("+", "Stone", …)`.
- **One residue, four lines, one file.** See (ii).

**So the removal is not complete, and the gap is an OWNER CALL, not a defect.** It blocks the
*claim* "we completely removed food"; it does not block the commit. The fix is a one-word edit plus
one literal, and it needs no regression change (proved in (ii)).

---

## (i) DELIBERATE FROZEN SAVE-KEY COMPATIBILITY — correct, leave alone

The WO-1163 contract is *one Stone identity over the frozen internal Food save slot*: the C# member
and the displayed word are Stone, the persisted key stays `Food`. That is why the token is still
everywhere and why deleting it would be the bug.

| Surface | Count | Evidence |
|---|---:|---|
| Frozen serialization attributes in `.cs` | **25** | `JsonProperty("Food")` ×8, `FormerlySerializedAs("Food")` ×11, `EnumMember(Value = "Food")` ×6 |
| `.cs` string literals containing `food` (any kind) | **238** across **100** files | 124 in `Assets/Editor/` (regression assertions + proofs), 109 in `Assets/_Modules/` |
| Canonical JSON `food` occurrences (Resources side) | **232** across **22** files | data keys `food` / `buyFood` / `rewardFood` / `costFood` / `foodProductionMult` / `paidFood`, plus `_comment` / `_note` prose |
| `api/` occurrences | **26** | save-wire whitelists only — `api/game/save.js:100` `GUARDED_BALANCES`, `:101` `NESTED_BALANCES`, `:991` `TIME_DERIVED_BALANCES`, `api/game/load.js:32`; `api/game/save.js:988` states the contract in-code |

Canonical examples opened today, all correct:

- `Assets/_Modules/Core/State/BuildJobData.cs:56-57` — `[FormerlySerializedAs("Food")]
  [JsonProperty("Food")] public int Stone;`
- `Assets/_Modules/Core/HudModel/HudModels.cs:119` — `[JsonProperty("Food")] public int Stone`
- `Assets/_Modules/Core/UI/ElarionUiKitObsidian.cs:732` — `CurrencyKind { … [EnumMember(Value =
  "Food")] Stone = 4, … }`
- `Assets/_Modules/Village/World/MineNode.cs:28` — `MineResource { … [EnumMember(Value = "Food")]
  Stone = 2, … }`
- Echo harvest token grammar (`wood:1,food:4,idle,…`) — persisted `<resource>:<level>` strings,
  read-migrated per CLAUDE.md §7.
- Art and catalog IDs, not words: `currency_food`, `hud_food.png`, `Harvest_Food.prefab`,
  `Food_Flour.fbx`, `Windmill_Food_Storefront`, `food_store`, `storage_food`.
- The self-documenting one: `Assets/Resources/Data/Canonical/concept-icons.json` records
  *"CurrencyKind.Food resolves the concept 'stone', never 'food'"*.
- `Assets/Resources/Data/Canonical/building-tiers.json:79-82` is the pattern in miniature — the
  player-facing `effect` reads **"Stone production +10%"** over the frozen modifier key
  `foodProductionMult`.

## (ii) PLAYER-FACING RESIDUE — the only thing that contradicts "completely removed"

**All four lines are in one file: `Assets/_Modules/Village/UI/EndState/EndStateVM.cs`.**

| file:line | What it is |
|---|---|
| `Assets/_Modules/Village/UI/EndState/EndStateVM.cs:650` | `private const string FoodSpoilLabel = "Food";` — the label constant |
| `Assets/_Modules/Village/UI/EndState/EndStateVM.cs:469` | raid-victory spoils row: `AddSpoil(vm, FoodSpoilLabel, credited.Stone)` |
| `Assets/_Modules/Village/UI/EndState/EndStateVM.cs:611` | raid non-victory / retreat spoils row, same call |
| `Assets/_Modules/Village/UI/EndState/EndStateVM.cs:969` | wave-clear banner: `lines.Add(new KeyValuePair<string,int>("Food", pay.Stone))` — **a second hardcoded literal that BYPASSES the constant**, so a one-word edit at `:650` alone would leave this row saying "Food" |

Three measured facts the lead should carry to the owner:

1. **It is deliberate, not drift.** The 27-line doc comment at `EndStateVM.cs:623-649` states that
   WO-1374 set this to `"Food"` because `PROGRAM_RAID_ECONOMY_2026-09-04` §3 enumerates the five
   currencies as Wood / Iron / Food / Gold / Crystals and is declared north star. It then names the
   conflict itself: *"a raid pays '+3,000 Food' and the number the player then watches rise is
   labelled 'Stone'"*, and *"the word is isolated in this ONE constant precisely so the owner's
   ruling is a one-word edit here plus three elsewhere."*
2. **The WO-1163 migration in the dirty tree preserved it on purpose.**
   `git diff -- Assets/_Modules/Village/UI/EndState/EndStateVM.cs` renames the *field* and leaves
   the *word*: `-AddSpoil(vm, FoodSpoilLabel, credited.Food)` → `+… credited.Stone`, and
   `-if (pay.Food > 0) … ("Food", pay.Food)` → `+if (pay.Stone > 0) … ("Food", pay.Stone)`.
3. **No regression pins the word, so the flip is cheap.**
   `Assets/Editor/Regression/RaidPayoutVisibilityRegression.cs:325-337` asserts the FOOD axis **by
   its amount (`+3000`), never by its label**, and says so in-code: *"Pinning the word here would
   freeze one side of an unsettled ruling."* Changing `:650` and `:969` to `"Stone"` needs **no
   test edit**.

## (iii) DEV-FACING AND DEAD — counted, not a contradiction

Six sites print or declare `Food` where no player can see it. Listed for completeness only; none
is player-facing residue and none should change in this reconciliation:

- `Assets/_Modules/DevTools/DevPanelController.cs:488` — `AddMetricRow(_metricsPanel, "food",
  "Food")`, dev panel metric row.
- `Assets/_Modules/HUD/OwnerDevToolsOverlay.cs:314` — `SetStatus("Loaded: +50k Gold/Wood/Iron, +25k
  Food/Crystals.")`, owner dev-tools toast.
- `Assets/_Modules/Core/Debug/DebugCanvasUI.cs:136` — `"Food: {s.Resources.Stone}"`, debug canvas.
- `Assets/_Modules/Village/Troops/RaidScoring.cs:232` and `:234` — `[Tooltip("Food granted at 100%
  destruction…")]` / `[Tooltip("Extra food per earned star.")]`, Unity **Inspector** tooltips,
  editor-only.
- `Assets/_Modules/Village/Buildings/NPCUpgradeStation.cs:44` — a code comment.
- `Assets/_Modules/Village/World/PetHarvestBootstrap.cs:144` — `SpawnNode("Food",
  MineResource.Stone, …)`, a GameObject name.

Dead-code candidates the tree itself flags (not fixed here, no ticket opened):
`StructureRole.FoodStore` / `FoodProducer` — `Assets/_Modules/Core/Catalog/StructureRole.cs:110`,
with a regression message in the tree reading *"StructureRole still declares FoodProducer — no
catalog row claims it"*.

## Secret scan performed in the same pass

The 09-13 inventory's `secret-review.json` is literally `[]` (zero matches). Re-scanned today over
all 484 untracked text files plus every tracked text diff, for `AIza…`, `sk-…`, `ghp_…`,
`postgres://`, `-----BEGIN … PRIVATE KEY`, `xox[baprs]-`, `"private_key"`, `eyJ…` JWTs,
`DATABASE_URL=`, `NEON_`:

- **Tracked diffs: zero hits.**
- **Untracked non-log files (463): zero hits.**
- **`Logs/device/**/logcat_full.txt`: HITS.** Live `firebaseAuthenticationToken` JWTs and
  `firebaseInstallationId` values at `post-lane-a-370139/logcat_full.txt:123135` and
  `pull-20260914-172704-breach-success-playthrough/logcat_full.txt:131783`. `Logs/` is on the
  exclude list for this reason, not merely because it is log output.
