# WO-1430 RESULT — Group B: the five authored fields no production code read

**Lane:** FIELDS (edit-only), 2026-09-09. **Branch:** `dev`. **Status:** IMPLEMENTED (Group B: 2 of 5 wired) —
awaiting gate; 3 need an owner ruling.
**Group A** (three doorless panels) was resolved separately in `bb51b8b9c` and is untouched here.

> **Nothing in this file was run.** No Unity, no gate, no commit — this lane is edit-only. Every marker,
> suite count and compile result is therefore **unproven by this seat** (§11B). §5 lists exactly what
> is unproven and who can close it.

---

## 1. Per-field verdict

| Field | Verdict | Evidence that decided it |
|---|---|---|
| `unlockMethod` (`Cosmetics/CosmeticCatalog.cs:47`) | **WIRED** | The claim was real but the WO's diagnosis was half-wrong — see §2.1. |
| `requiresHero` (`Core/Quests/DailyQuests.cs:39`) | **WIRED** | Half-built gate; the sibling `requiresFeature` is read 6 lines away. See §2.2. |
| `levelCurve` (`Village/Harvest/EchoBalanceCatalog.cs:74`) | **STOP — owner ruling** | Wiring the promise means naming a second curve's formula, i.e. changing Echo balance. §3.1. |
| `visibilityRule` (`Core/Data/CardCollectionCatalog.cs:44`) | **STOP — owner/architect ruling** | The client declares a **string**; the server emits a **JSON object**. The shapes disagree. §3.2. |
| `expiry_behavior` (`Core/Data/CardCollectionCatalog.cs:107`) | **STOP — owner/architect ruling** | The client is never told an item **expired** — expiry is discarded at parse. §3.3. |

---

## 2. What was wired

### 2.1 `unlockMethod` — it was read for a LABEL; it is now a GATE

**The oracle's own `why` string was wrong, and the correction is the finding.** It said *"the only code
that touches the key is `EconomyMetaCatalogRegression.cs:129-138`"*. Read at source 2026-09-09:
`Assets/_Modules/HUD/CosmeticShopPanel.cs:402` **does** read it —
`s_defUnlockMethodField?.GetValue(def)` — **by reflection**, because `DeNelle.HUD` may not reference
`DeNelle.Cosmetics` (CLAUDE.md §5). The scan is structurally blind to reflection, so it under-reported,
which is the safe direction it documents at `AuthoredFieldReaderRegression.cs:66-69`.

That read is **display only**: `CosmeticShopPanel.cs:447` picks the caption `"Earn via play"` vs
`"Unavailable"`. Read-for-display is not honoured. What was genuinely missing was a **gate**:
`CosmeticOwnershipService.GrantAchievement` accepted **any** id the catalog knew, so the earned-through-play
door and a purchase door were indistinguishable, and `Village/Progression/TierSystem.cs:198`
(`GrantAchievement(milestone.AchievementCosmeticId)`) would hand out a row authored `"buy"` for free.

**Data measured at source (not copied):** `grep -c '"unlockMethod"'` = **37** in
`Assets/Resources/Data/Canonical/cosmetics.json` **and** `Assets/StreamingAssets/Data/Canonical/cosmetics.json`;
`grep -o '"unlockMethod": *"[a-z]*"' | sort | uniq -c` = **37 × `"achievement"`** in both twins. **No JSON was
changed.**

**Change:**
- `Assets/_Modules/Cosmetics/CosmeticCatalog.cs` — `AchievementUnlock` / `BuyUnlock` consts, and
  `public static bool IsAchievementUnlock(CosmeticDef def)` (plus a by-id overload) as **the one definition**,
  naming `def.UnlockMethod`. The pre-existing `CosmeticDef.IsAchievement` property now **delegates** to it, so
  there is one definition rather than two that can drift. This is the exact recipe the oracle wrote for itself
  at `AuthoredFieldReaderRegression.cs:368-371`.
- `Assets/_Modules/Cosmetics/CosmeticOwnershipService.cs` — `GrantAchievement` resolves the def, and
  **refuses** with `FlowTrace.Warn("Cosmetics", ...)` when `IsAchievementUnlock` is false.

**Deliberately NOT done:** an unknown id still no-ops **silently**. `Commerce/PackStoreVM.cs:285-319` calls
`GrantAchievement` first for pack SKUs that are not cosmetics rows and then falls through to
`MarkCosmeticOwned`; warning on that miss would fire on every pack purchase. The real anomaly — a known row
with the wrong unlock method — is the one that traces. `MarkCosmeticOwned` is left ungated for the same reason.

**Live behaviour change today: NONE.** 37/37 rows are `"achievement"`, so nothing is refused. The gate exists so
that authoring `"buy"` tomorrow is honoured instead of ignored.

### 2.2 `requiresHero` — the half-built sibling gate

`requiresFeature` is read at `DailyQuests.cs:486`; `requiresHero` was declared one line above it (`:39`) and read
nowhere. **Provenance measured 2026-09-09:** `git log -S"requiresHero" -- Assets/_Modules/Core/Quests/DailyQuests.cs`
returns exactly one commit — `1a64930d7` *"Daily quests, second dungeon, six canonical data tables"* — which
introduced the field with no reader. The only doc that mentions it is `WORK_ORDER_558_quest_pack.md:51`
(CLOSED 2026-08-26): *"No `requiresHero` (single-Knight north star)."* `daily-quests.json` authors it on **zero**
rows.

**⚠ THE SEMANTICS ARE AN INTERPRETATION, NOT A SPEC — flagged so it can be overruled cheaply.** A save holds
exactly one hero (`SaveSchema.PersistedState.heroClass`, live at `GameStateService.Instance.State.HeroClass`,
`HeroClassOpt` ∈ {None, Mage, Knight, Ranger, Cleric}); a search for an owned-hero roster
(`UnlockedHeroes|OwnedHeroes|HeroRoster`) returned **nothing**. So *"requires hero X"* can only mean *"the
player's chosen class is X"*. If the owner means a party member or an unlocked companion instead,
`HeroRequirementMet` is the **one** function to change and the pinned table is the one thing to rewrite.

**Change** (`Assets/_Modules/Core/Quests/DailyQuests.cs`, inside `DailyQuestService`):
- `public static bool HeroRequirementMet(string requiresHero, HeroClassOpt current)` — **pure**, so it is
  pinnable without a service. Blank ⇒ no requirement; `None` ⇒ not met; an unrecognised or **numeric** name ⇒
  `FlowTrace.Warn` + not met (`Enum.TryParse` otherwise accepts `"2"` as `Knight`, which would silently resolve
  authored nonsense to a real hero).
- `private static HeroClassOpt CurrentHeroClass()` — null-safe read of the live save.
- `RollOne` now skips a template whose hero requirement is unmet, immediately after the `RequiresFeature` skip.

**Fails closed and loud** — never silently eligible. Handing a player a daily naming a hero they are not is the
"quest board lies" failure that `FeatureShipped`'s own header describes.

**Live behaviour change today: NONE.** Zero rows author `requiresHero`.

---

## 3. The three that STOP — one ruling each, nothing invented

### 3.1 `levelCurve` — **ruling: retire it, or name the second curve's formula**
`echoes-balance.json` authors `"linear"`; the doc-comment at `EchoBalanceCatalog.cs:73` calls it *"reserved for
future curves"*. `EchoBonusCalculator.LaneContribution` (`:475-480`) already **is** the linear curve —
`BaseContributionPerEcho + PerLevelBonus × (level − 1)` — so the only authored value is honoured **by accident**,
not by reading the field.

A dispatcher that reads the name and implements only `"linear"` would satisfy the oracle without adding a curve;
that was **rejected as gaming the oracle**, on the Group A precedent. Adding a real second curve is a **balance**
decision this seat may not make (CLAUDE.md §11B).

**The one question:** *does `levelCurve` stay as a knob — and if so, what is the second curve's formula — or is it
retired together with the "reserved for future curves" comment?*

### 3.2 `visibilityRule` — **ruling: which shape is canon, the string or the object?**
- Client, packaged twin: `CardCollectionItemPointer.VisibilityRule` is a **`string`** (`CardCollectionCatalog.cs:44`).
- Server: `visibility_rule` is **JSONB**, bounded to the keys `{requires_entitlement, min_level, unlock_key}`
  (`api/_lib/catalog-read.js:13` + `:109`; DDL at `api/migrations/20260828_0008_...sql:56`).
- The client **already parses the server's object** into `CardCollectionApiItem.Visibility` (a `JObject`,
  `:111`) — and **that** is unread too, though it is not on the exemption list because it is not a string field.
- Authored on **zero** rows: `grep -rn "visibilityRule" --include=*.json Assets/ api/` returned nothing, and
  `card-collections.json` carries none.

Giving the string a grammar would **invent a second contract** next to the server's. The filter itself is then
trivial — `ToPresentation` (`:255-261`) builds cards with no filtering at all and is the one place it belongs.

**The one question:** *should the packaged twin's `visibilityRule` become the same `{requires_entitlement,
min_level, unlock_key}` object the server emits (and `ToPresentation` apply it to both paths), or is the packaged
string retired?*

### 3.3 `expiry_behavior` — **ruling: the client is never told an item expired**
Server enum is `{hide, lock, fallback}` (`api/_lib/catalog-read.js:8`; DDL `...0008_...sql:17-18`), and it is
emitted per item (`:106`). The client parses it into `CardCollectionApiItem.ExpiryBehavior` (`:107`) and does
nothing with it. Authored only in the `CardCollectionFoundationRegression.cs:55` fixture (`"fallback"` ×2).

**Why it cannot simply be honoured:** the `/api/catalog/collection` envelope carries **no per-item expiry**
(collection-level `starts_at`/`ends_at` only). The entitlement path does carry expiry —
`Core/Entitlements/SkuEntitlementSnapshot.cs` reads `expires_at` per grant — but it **discards expired grants at
parse** (`ApplyPayload`, the `continue` on `expiresMs <= ServerNowMs`) and `IsEntitled` returns `false`
identically for *expired* and *never owned*. So the client structurally cannot distinguish the state whose
behaviour this field names. Honouring it needs the snapshot to **retain** expired grants, plus a rendering
decision for `lock`.

**The one question:** *is `expiry_behavior` about entitlement expiry — and if so, may `SkuEntitlementSnapshot`
retain expired grants so the client can tell "expired" from "never owned", and what does `lock` render as (a
greyed card, or the `fallback_sku` card)?* Otherwise: retire the field client-side and leave it server-only.

---

## 4. Files changed, and the exemption bookkeeping

| File | What changed |
|---|---|
| `Assets/_Modules/Cosmetics/CosmeticCatalog.cs` | `AchievementUnlock`/`BuyUnlock` consts + `IsAchievementUnlock` (2 overloads); `IsAchievement` now delegates |
| `Assets/_Modules/Cosmetics/CosmeticOwnershipService.cs` | `GrantAchievement` gates on `IsAchievementUnlock`, warns on refusal |
| `Assets/_Modules/Core/Quests/DailyQuests.cs` | `HeroRequirementMet` (public, pure) + `CurrentHeroClass`; `RollOne` reads `t.RequiresHero` |
| `Assets/Editor/Regression/AuthoredFieldReaderRegression.cs` | 2 `ParkedClaims` rows **and** their 2 `UnreadBaseline` rows deleted; both `MechanicalClaims` `why` strings corrected |
| `Assets/Editor/Regression/AuthoredFieldGateRegression.cs` | **NEW** — the behavioural pin (see §5) |

**Both lists had to be edited, and only one of them would have complained.** Each wired field appeared twice:
in `ParkedClaims` and in `UnreadBaseline`. `[parked-claim-now-read]` fires loudly on a stale `ParkedClaims` row —
but Case C `continue`s on a read field **before** consulting the baseline, so a stale baseline row is tolerated
**in silence**. Nothing would have reminded anyone. Both are deleted, with a comment where each row was.

**No JSON twin was touched.** No `View`, no `PanelDoorRegression`, no Unity, no git.

---

## 5. The new pin — `AuthoredFieldGateRegression`

`Assets/Editor/Regression/AuthoredFieldGateRegression.cs`, marker
`AUTHORED_FIELD_GATE_OK` / `AUTHORED_FIELD_GATE_FAIL <case>`. It exists because the reader oracle is blunt by
design: it matches `.Member` by name and goes green on any mention, so it cannot tell a gate from a log line.

| Case | Asks | RED recipe (written in the file, **not run by this seat**) |
|---|---|---|
| A `[cosmetic-corpus-present]` | anti-vacuity floor, ≥30 rows (37 measured) | point `StreamingRelativePath` at a missing file |
| B `[unlock-method-is-the-definition]` | predicate agrees with the authored string on every real row; ≥1 row is an achievement unlock; a 6-row decision table incl. `"buy"`/null/blank/near-miss; `IsAchievement` delegates | change `AchievementUnlock` to `"earned"`; or make `IsAchievementUnlock` `=> def != null` |
| C `[grant-achievement-consults-the-field]` | `GrantAchievement` still calls `IsAchievementUnlock` — **source text, and labelled as the weaker evidence it is** | delete the call from `GrantAchievement` |
| D `[requires-hero-decides-eligibility]` | 12-row decision table over `HeroRequirementMet`, incl. `("Paladin", Knight)→false` and `("2", Knight)→false` | delete the numeric guard; or `return true` on an unrecognised name |

**Case C is source text on purpose, and the reason is recorded in the file:** the behavioural route is
unavailable — `CosmeticOwnershipService` is a MonoBehaviour singleton whose success path writes PlayerPrefs, and
the refusal path is unreachable from real data because **no authored row is `"buy"`**. Without case C, deleting
the gate leaves **both** oracles green, since the reader oracle is satisfied by the read inside
`CosmeticCatalog.cs` itself.

**⛔ THE SUITE IS DORMANT UNTIL REGISTERED.** `DataRegression.cs` is outside this lane's ownership, so the
registration line is handed over rather than applied. Add to `DataRegression.RunAll`:

```csharp
DeNelle.Core.Diagnostics.Guard.Try("Regression", "authored-field-gate suite", () => { if (!DeNelle.Editor.AuthoredFieldGateRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[authored-field-gate] " + r); });
```

---

## 6. Unproven — what this seat did NOT establish (§11B)

1. **The new suite has never run.** Not registered, not executed; every RED recipe above is reasoned from source,
   not observed. The gate proves them, not this seat.
2. **Nothing compiled.** No Unity was fired. Brace + NUL passed on all five `.cs` (`CosmeticCatalog` 22/22,
   `CosmeticOwnershipService` 23/23, `DailyQuests` 105/105, `AuthoredFieldReaderRegression` 48/48,
   `AuthoredFieldGateRegression` 40/40; 0 NUL bytes) — that is a byte check, not a compile.
3. **Neither gate has a live effect on today's data** — 0 rows would be refused, 0 templates skipped. Both are
   correct-and-inert by design.
4. **The WO's line *"no achievement grants any of them"* is at least partly wrong and was not chased.**
   `Village/Progression/TierSystem.cs:198` grants `milestone.AchievementCosmeticId` through this very door. How
   many of the 37 rows are actually reachable that way was **not measured**.
5. **`requiresHero`'s meaning is an interpretation**, argued from the absence of a hero roster and one closed WO —
   not from a spec. §2.2 names the single function to change if the owner rules otherwise.
6. **`docs/reference/TUNABLE_LEVER_INVENTORY.md` has no rows for any of the five** (grepped 2026-09-09), so
   nothing there was stale and nothing was added; whether these belong in that inventory is a doc-owner call.
7. **Case D will emit three `[Flow:DailyQuest]` Warn lines** on every run (the `Paladin` / `"2"` / `None` rows
   deliberately take the loud path). Whether the harness tolerates warning output on a passing suite was **not
   verified from here** — if a gate treats `LogWarning` as a failure, those three rows need re-shaping.

## 7. Owed outside this lane (named, not fixed)

- **`AuthoredFieldGateRegression.cs.meta` does not exist yet.** Unity generates it on the first editor run; the
  committer must stage it **by path** alongside the `.cs`, or the next seat inherits a missing-meta warning and
  GUID churn.
- **`docs/MASTER_CATALOG/core.md:559-560` is owed a `STALE:` flag** (CLAUDE.md §15). It records
  `DailyQuests.cs` as 425L and calls the `requiresFeature` filter *"currently vacuous (dead gate)"* — already
  false before this lane (the `"raids"` branch reads `PostureSignals.RaidCapable`), and now missing
  `HeroRequirementMet` / `CurrentHeroClass` entirely. Out of this lane's file ownership.

---

# 2026-09-10 — lane FIELDS-DROP: fields 3-5 RETIRED per owner ruling

**Owner ruling (AskUserQuestion, 2026-09-10 morning), verbatim:** *"Drop them - remove the dead fields
from the catalog; re-add each when a system needs it."* — closing §3.1, §3.2 and §3.3 above by
RETIREMENT rather than by wiring. Those three sections are frozen point-in-time findings (CLAUDE.md
§15) and are **not** rewritten; this section supersedes their "one question" lines.

Lane constraints honoured: **EDIT ONLY** — no Unity run, no gate, no commit. Base `3da5e5360`
(fast-forwarded mid-lane from `b877e633c` to pick up the RULINGS-PM commit; the ruling block in the
WO `.md` was **not** touched by this lane, only the first `**Status:**` line).

## A. Per-field row counts — what lost what

| Field | Authored JSON rows removed | DTO members removed | Regression rows retired |
|---|---|---|---|
| `levelCurve` | **2** — one top-level key in each `echoes-balance.json` twin (`Assets/Resources/Data/Canonical/` + `Assets/StreamingAssets/Data/Canonical/`, both at the file's line 8) | **1** — `EchoBalanceData.LevelCurve` (`Assets/_Modules/Village/Harvest/EchoBalanceCatalog.cs`, was `:74`) | **3** — `ParkedClaims`, `MechanicalClaims`, `UnreadBaseline` |
| `visibilityRule` | **0** — authored on zero rows; `grep -c` over `card-collections.json` returned `0` | **1** — `CardCollectionItemPointer.VisibilityRule` (`Assets/_Modules/Core/Data/CardCollectionCatalog.cs`, was `:44`) | **3** — same three registries |
| `expiry_behavior` | **0** in `Assets/` canonical json | **1** — `CardCollectionApiItem.ExpiryBehavior` (same file, was `:107`) | **3** — same three registries |
| **totals** | **2 JSON rows** | **3 DTO members** | **9 registry rows** |

Note the shape: this was **not** a mass catalog edit. `levelCurve` was the only one of the three ever
authored in data at all, and it is a **top-level knob**, not a per-row field — so no catalog *row*
lost a field. The other two were declaration-only, which is precisely why they were dead.

## B. ⚠ FINDING — `expiry_behavior` HAS a reader, on the SERVER. Scope stated explicitly.

The lane brief said to STOP on any field that turns out to have a reader. Stating it plainly rather
than as a footnote:

- **Client reader: NONE.** `grep -rn "ExpiryBehavior" --include=*.cs Assets/` (this session) returned
  only the declaration and the three regression registry rows. Nothing under `Assets/` ever read it.
  It was parse-and-discard, exactly as §3.3 recorded before the ruling.
- **Server readers: LIVE, and deliberately untouched.** `api/schema.sql:1621-1622` constrains the
  column to `('hide','lock','fallback')`; `api/_lib/catalog-read.js:87` REJECTS a row whose value is
  not one of them and `:106` emits it in the payload; `api/admin/showcase-finalize.js:77,83` projects
  and filters on it. **`api/` was not edited by this lane and the server contract is unchanged.**
- **Consequence, and why it is safe:** live responses still carry `expiry_behavior`, which is now an
  *unknown member* on the client. `CardCollectionCatalog.cs:212` and `:272` call
  `JsonConvert.DeserializeObject<T>(json)` with **no `JsonSerializerSettings`**, so Newtonsoft's
  default `MissingMemberHandling.Ignore` applies and the key is discarded without error. Verified at
  source this session by grepping those two call sites for a settings object — there is none.
- **The `CardCollectionFoundationRegression.cs:55` fixture KEEPS the key on purpose.** It models a
  server response, the server still sends it, and the fixture is now the standing proof that the
  parser tolerates the extra member. Removing it would have deleted that proof.

The ruling's premise ("no reader") holds for the client catalog, which is what it scoped. Recorded
here so nobody later reads "expiry_behavior was dead" and deletes the server column.

## C. The pin was INVERTED, not deleted — `[retired-field-stays-retired]` (Case E)

`AuthoredFieldReaderRegression` previously asserted *"this authored field has a reader"* for all
three. That question is meaningless once the field is gone, and simply deleting the rows would have
left the ruling recorded **only in a WO** — the duplicated-state failure CLAUDE.md §2/§5/§16 each
describe. So the question was turned inside out:

- New `RetiredFields` registry + `CheckRetiredFieldsStayRetired`, wired as **Case E** inside
  `CheckAuthoredFieldsHaveReaders`.
- It REDS on **either** side of the seam: a `[JsonProperty("<key>")]` declaration anywhere under
  `Assets/_Modules`, **or** a `"<key>":` in **either** canonical json twin (both are scanned — the
  twins are kept byte-identical, and checking one would miss a half-revert).
- Matched on the **JSON key**, not the `Member|key|path` triple, so a re-add under a renamed member
  or in a different catalog file still fires.
- **Anti-vacuity floor, fail-not-skip:** the json corpus must reach 20 files, else the case FAILS
  rather than passing on an empty read.
- It does **not** forbid a re-add — the owner explicitly allowed one *"when a system needs it"*. It
  forbids a **silent** one: land the reader, delete the key from `RetiredFields` in the same change.
- Cases B/C/D could not have caught this. All three reason about fields that EXIST; a re-added field
  *with* a reader is indistinguishable from a healthy one.

`ParkedClaims` is now **empty and still declared**, with a tombstone. The `parked` branch in Case B
is the mechanism for the next field that needs an owner ruling; deleting it would mean rebuilding it.

## D. Evidence captured this session (EDIT-ONLY — no Unity available to this lane)

**RED-first, via a Python port of Case E's exact two regexes run over the same two corpora:**

```
[head] decl corpus=1364 .cs   json corpus=221 (floor 20)
  RED decl 'levelCurve'      -> Assets/_Modules/Village/Harvest/EchoBalanceCatalog.cs
  RED json 'levelCurve'      -> Assets/Resources/Data/Canonical/echoes-balance.json
  RED json 'levelCurve'      -> Assets/StreamingAssets/Data/Canonical/echoes-balance.json
  RED decl 'visibilityRule'  -> Assets/_Modules/Core/Data/CardCollectionCatalog.cs
  RED decl 'expiry_behavior' -> Assets/_Modules/Core/Data/CardCollectionCatalog.cs
[head] case-E failures = 5
-----
[tree] decl corpus=1364 .cs   json corpus=221 (floor 20)
[tree] case-E failures = 0
```

`head` reads every path through `git show HEAD:<path>`, so it is the pre-edit tree, not a memory.
**5 → 0.** The case is proven to fire on exactly the state this lane removed, and to be quiet on the
state it left. It has **not** been run inside Unity — that is the gating seat's step.

**Binary-safe JSON edit proof.** Both twins are CRLF, so "LF count unchanged" is impossible when a
line is removed; the invariants actually proven are CR==LF preserved, a byte delta of exactly the
removed line, `json.load` success, and the twins still byte-identical:

| | before | after |
|---|---|---|
| bytes (each twin) | 6142 | 6115 (−27 = `len('  "levelCurve": "linear",\r\n')`) |
| LF / CR | 26 / 26 | 25 / 25 (paired) |
| md5 (both twins) | `1afe0556f6e11c0a62cc2aa183b03bc1` | `128a51d1fd9160faf4d603039ee56e3f` |
| `json.load` | ok | ok, 12 top-level keys, `levelCurve` absent, `perLevelBonus` still `0.01` |

The edit was done in Python **bytes** with `assert data.count(needle) == 1` before a single
`replace(..., 1)` — never a text-mode rewrite (memory `canonical-json-edits-binary-only`).

**C# quality gate:** `python tools/gate_brace.py` on all three touched files →
`GATE_BRACE_SUMMARY bad=0 of 3`, exit 0. NUL scan → `NUL=0` on all three; raw braces balanced
56/56, 58/58, 42/42.

## E. Canon updated in the same breath (§15)

- `docs/MASTER_CATALOG/core.md` — the "Three authored fields remain STOPPED on owner rulings"
  sentence was **live and now wrong**; replaced with the retirement, the server-side caveat, and the
  pin that enforces it.
- `docs/reference/DATA_CLASS_MAP.md` — claimed `echoes-balance.json`'s *"only strings are
  `levelCurve: "linear"` and echo ids"*; corrected with a dated note.

## F. Owed outside this lane

- **The gate has not run.** `COMPILE_GATE_OK` + `REGRESSION_OK <n>/<n>` on a fresh log are the
  gating seat's, and the `AUTHORED_FIELD_READER_OK` line is the marker to look for in the suite log.
  This lane's Case E evidence is a Python port, not the C# case executing.
- **`api/` untouched by design** (§B). If the server ever stops sending `expiry_behavior`, nothing
  client-side needs to change — that is the point of the removal.

## G. Blast-radius checks run before hand-back (each grep executed this session)

Removing an authored key can red a suite that never *names* the key — a key-count, a golden hash or
a byte-length assertion. All four suites that touch `echoes-balance.json` were read with context:

- `EchoSpecializationRegression.cs` is the only one that asserts on the file's shape, and every
  assertion survives: `:244` `Version == 1`, `:245` `MaxLevel == 8`, `:316` the two twins are
  **byte-identical**, `:321` the file still AUTHORS `repairFractionPerHour`. **No key-count, no hash,
  no length assertion.** Verified after the edit: both twins md5 `128a51d1fd9160faf4d603039ee56e3f`
  (identical), and `grep -c repairFractionPerHour` returns `2` in each.
- `RemoteCatalogSeamRegression.cs:114` / `:354` only name the file as a **path** in the WO-1331
  remote allowlist — no field-level claim.
- `CrystalProductionRegression.cs:396` and `OfflineClaimFanOutRegression.cs:152` name it only in
  comment prose.
- `grep -rn "levelCurve\|visibilityRule\|expiry_behavior" tools/` → **no hits**.
- `grep -rln "echoes-balance" Assets/Data/` → **no hits** (no EditMode test reads it).

**The `CardCollectionFoundationRegression.cs:55` fixture claim is verified, not assumed.** The
fixture string is fed to `catalog.ResolveApiEnvelope(api, "build-defenses", ...)` at `:56` — the
**production** path, which is the `JsonConvert.DeserializeObject<CardCollectionApiEnvelope>` call at
`CardCollectionCatalog.cs:212` — and `:57-59` assert on the parsed result (`Cards.Count == 2`,
`Cards[0].StableId`, `Cards[0].Title`). So the fixture genuinely exercises the parser with the
now-unknown `expiry_behavior` key present, and a green there is real evidence the key is tolerated.

## H. Case E regex is deliberately LOOSER than the inventory's

`declRx` in Case E matches `[JsonProperty("<key>"` with **no `)]` tail and no `public string`
requirement — unlike the inventory `declRx`, which needs both. For an **absence** assertion,
over-matching is the safe direction: a re-add as
`[JsonProperty("levelCurve", Required = Required.Default)]`, or as a non-string member, must still
fire. The inventory regex is allowed false negatives (it under-reports on purpose, see the header);
this one is not. Both RED-first runs above were re-executed with the hardened pattern and still read
**5 → 0**.

## I. Also owed outside this lane (added at hand-back)

- **`docs/HANDOVER_2026-09-10_overnight.md`** was updated by the RULINGS-PM commit `3da5e5360` and
  records fields 3-5 as READY. It is a dated handover the lead owns; **not edited here**, flagged.
