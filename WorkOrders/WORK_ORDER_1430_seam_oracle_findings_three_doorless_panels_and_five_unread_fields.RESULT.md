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
