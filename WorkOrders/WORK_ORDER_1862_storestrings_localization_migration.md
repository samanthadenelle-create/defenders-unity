# WORK ORDER 1862 — StoreStrings / canon-strings.json localization migration

**Status:** READY FOR LEAD MERGE — code + all 20 locale JSON files landed in the tree; the Unity
String Table merge (55 keys x shared + 6 enabled tables) is the lead's serialized step and
`LOCALE PARITY` is RED until it lands. No gate fired, nothing committed, nothing pushed by this lane.

**Minted:** 2026-09-18 from the `CLI_LANES_WO_NUMBERS.md` banner (main line was 1862; the banner row
was bumped 1862 -> 1863 in the same edit).
**Origin:** item 0 of `docs/handoffs/OVERNIGHT_LOCALIZATION_2026-09-17.md`'s "Next steps" —
*"NEW, separate from Village: `StoreStrings`/`canon-strings.json` is entirely unlocalized."*
**Owner ruling that scopes it:** fix it completely, because *"why would the player play if all in
their native language to only switch over to the wallet when they can't understand what's written."*

---

## 1. The defect, proven at source

`Assets/_Modules/Wallet/StoreStrings.cs` was the entire Wallet/Store module's text authority. It read
`Data/Canonical/canon-strings.json` through its own private `Dictionary` cache via
`CanonicalJson.Read`. There is exactly **one** `canon-strings.json` per mirror and **no
`canon-strings.<locale>.json` sibling anywhere on disk**;
`Assets/_Modules/Core/Data/LocalJsonCatalogSource.cs` takes the literal path with no `{locale}`
substitution of any kind.

**Consequence: every player, in every language setting, read this module in English** — buy-gate
refusals, the wallet-balance mirror, the commerce lifecycle, the Night Market band heads and trust
strip, the whole Season Track and the whole Monthly Ledger. It was invisible to WO-1857's sweep
(scoped to `en.json` + its 9 siblings) and to WO-1861's pseudoloc harness (which wraps `LocalText`,
never `StoreStrings`).

## 2. Three premises in the brief were falsified before any edit — record them

1. **"5 call sites."** There are **5 runtime FILES and 70 call sites**
   (`PackStore.cs` 20, `SeasonTrackPanel.cs` 24, `MonthlyLedgerPanel.cs` 22, `StoreLegalFooter.cs` 3,
   `NightMarketSharedCardSession.cs` 1), plus 7 editor regressions that reference the class.
2. **"Delete `canon-strings.json` if nothing reads it."** Not possible, and not desirable. Of its 324
   string rows only 86 are Store rows; the rest are read by `BiomeRoadsRegression` (region/tunnel
   names), `BuildingCatalogTest` (`displayName` keys), `BreakableContainerChestRegression`,
   `CanonicalJsonIntegrityTest`, `DungeonStatusDevMenu` and `GooglePlayContentExclusion`.
   **Do not delete the file.** The 86 Store rows are now unread by code — see §7 follow-up 2.
3. **"Retire `StoreStrings.cs`."** Not possible. `NightMarketNoWalletRegression:98,143` requires the
   file to exist on disk; `:336-340,464` read its two inline consts; `StoreNameSingleSourceRegression:181,198`
   compares and resolves `StoreStrings.KeyWordmark`; `BattleMonthlyRegression:759` calls `Reload()`
   and `:761-817,977` iterate its key arrays. The class survives as a key catalog.

## 3. The fix — the repo's own twice-proven pattern, not a new one

`StoreStrings.Get`/`Format` now forward to **`LocalText`**, and the private canonical reader, its
cache and its path constant are gone. This is verbatim what this repo already did twice:

| precedent | shape | pinned by |
|---|---|---|
| `Assets/_Modules/Core/UI/HudStrings.cs:171-184` | key catalog; `Get`/`Format` forward to `LocalText`; `Reload` a no-op | `SmartArgumentRegression:27-29` asserts the live `HudStrings.Format -> LocalText.Format` positional path resolves exactly |
| `PromoStrings` | identical | `PromoRedeemEntryRegression:292-300` **FAILS** if a private canonical reader, a Newtonsoft deserialize or a canon path constant ever returns to it |

`HudStrings`' own header states the sanctioned end state: *"a compatibility key catalog while its
call sites move to typed `LocalizedText` wrappers. It is not a second table reader."*

**Why this and not 70 rewritten call sites into new typed shims** — stated as a deliberate deviation
from the brief's step 4:

- It fixes the defect for **all 86 keys at once**, with **zero** call-site edits in money-adjacent
  code (brief §6: change only the text-resolution mechanism).
- All three existing shim classes have their `AllKeys` array **pinned**, two of them to an exact set:
  `StorePresentationLocalizationRegression:34-37` does `SetEquals` + a `Length` check on exactly
  seven keys, and `BuyGateAndPriceLadderRegression:696-708` pins `StoreBuyText` to exactly seven.
  **Extending either class reds the gate**; new siblings plus 70 rewrites would instead need 7
  regression needle re-points in files this lane was told not to touch.
- `docs/ARCHITECTURE_PRINCIPLES.md`: never smuggle a structural refactor into player-facing work.

**⛔ A `canon-strings` fallback was deliberately NOT added under `LocalText`.** It would make the
class a second table reader again and keep 86 duplicate rows load-bearing forever — the
duplicated-state failure CLAUDE.md §2/§5/§8/§16 each describe. A missing key must go loud
(`FlowTrace.Fail` + `[[missing:]]`) so `BattleMonthlyRegression`'s copy case and
`LocaleParityRegression` red at the gate rather than quietly shipping English.

## 4. Key naming — forced, not chosen

The en.json key **must equal the `StoreStrings` constant**, because the constant is what 70 call
sites and 7 regressions name. So: existing camelCase, verbatim, no `store.` prefix. This also matches
the 62 canon keys already reused verbatim in `en.json` and the pinned identity
`HudStrings.KeyStoreWordmark == StoreStrings.KeyWordmark` (`StoreNameSingleSourceRegression:181`).
The newer snake_case guidance in `docs/localization/key-naming.md` is correct for *new* keys and is
deliberately not applied to this cohort — renaming here would break the pins.

## 5. What landed

**`Assets/_Modules/Wallet/StoreStrings.cs`** — header rewritten; `using` set reduced to
`DeNelle.Core.Diagnostics` + `DeNelle.Core.UI`; `CanonRelativePath`, `_canon` and `EnsureLoaded`
deleted; `Get`/`Format` forward to `LocalText.TryGet`; `Reload()` a no-op with no `[Obsolete]`
(a warning there buys nothing and risks the gate). **All 86 `Key*` constants and all 5 key arrays
are byte-for-byte intact**, as are both inline sentences and `PiWalletRequired()`.
Braces 39/39, `tools/gate_brace.py` exit 0, 0 NUL bytes, 0 CR bytes.

**55 new keys x 10 locales into all 20 canonical files** (both mirrors), appended by surgical
byte-level insertion before each file's final `\n}` so no existing byte was re-serialised.
Real key count 834 -> **889 in every one of the 20 files**; all 10 mirror pairs `cmp`-identical.

**`Assets/Editor/Localization/GooglePlayLocalizationVariantPolicy.json`** — the 13 new
`store*`-prefixed keys added to the existing `Wallet` stripGroup (39 -> 52 keys).
**This was mechanically forced, not a judgment call:**
`GooglePlayLocalizationVariantPolicyRegression:136-150` computes `expectedStore` as *every* `en.json`
key starting with `store` except `storeWordmark`, requires the strip set to be a superset, and
requires `stripKeys.Count == expectedStore + expectedSwap + SettingsWalletKeys`. Verified after the
edit: **71 == 71**, all three subsets satisfied, `storeWordmark` not stripped, 0 missing disposition
rows across 10 locales, 0 forbidden-closure violations.

**No `replacementRows` were added and `GooglePlayLocalizationVariantPolicyRegression.cs` was not
touched.** Only one new key carries a forbidden token (`storeBalanceAfter`, " skr" + "wallet") and it
is a `store*` key, so a replacementRow would be a duplicate disposition *and* break the count law.
The 42 non-`store*` keys carry no forbidden token in any locale.

**`Assets/Editor/Localization/LocalizationPolicy.json`** — the
`legacyAuthorities` entry for `Assets/_Modules/Wallet/StoreStrings.cs`
(`"kind": "direct-table-reader"`, `"removeBy": "phase-2"`) **removed. This ticket IS that phase-2
removal, and leaving the entry would have RED the gate.**
`LocalizationAuthorityRegression.CheckAuthorities` (`:52-80`) is symmetrical: it flags an undeclared
reader *and* — at `:79-80` — a **declared entry that is no longer a reader**
(`"stale localization authority policy entry: <path>"`). Its hit test requires `CanonicalJson.Read`,
which this ticket deleted, so the entry became stale the moment the forwarder landed.
Verified by re-implementing that exact hit detection over `scanRoots` (`Assets/_Modules`): declared ==
hits, **0 unapproved, 0 stale**. Legacy readers remaining: 5 (was 6).

**`docs/localization/manifest.json` needs NO regeneration for this ticket** — read at source, not
assumed: `SmartArgumentRegression.CheckManifestDeclarations:143` returns early unless the manifest
carries `reviewed: true`, and the live manifest has no `reviewed` key at all (`None`), so its 7394
generated entries are report-only and bind nothing. Regenerating it is still worthwhile hygiene at
some point, but it is not a gate dependency here.

**`Assets/_Modules/Wallet/README.md` needed no edit** — grepped, it mentions neither `StoreStrings`
nor `canon-strings`, so §15 carries no debt there. (`MonthlyLedgerPanel.cs:39`'s comment *"all from
canon-strings.json via StoreStrings"* IS now stale, and is deliberately left: it is a comment-only
change inside a money file that `BattleMonthlyRegression` scans, which is not worth the risk in this
pass. Listed as follow-up §7.6.)

**Sidecar:** `Logs/debug/scratch/sweep-sidecar-storestrings-migration.json` — 55 rows, each with
`key`, `sourceRef` (+ `allSourceRefs`), `constant`, `args`, all 10 `translations`,
`regressionNeedles` (**empty for every row — see §6**), `playDisposition`, `unityTableWork`.

## 6. Regression needles: ZERO re-points needed — and one substantive consequence

Because no call site changed, every source-text pin stays green untouched:
`StorePresentationLocalizationRegression:64-68`, `StoreNameSingleSourceRegression:171`,
`StorePiSkinCurrencyRegression:205`, `BuyGateAndPriceLadderRegression:745`.

> ### ⚠ FLAG FOR THE OWNER — a deliberate hold is LIFTED in substance, not in mechanism.
> `StorePresentationLocalizationRegression:63-68` holds five rows — `storeBandFree`,
> `storeBandPatronage`, `storeBandBasketSub`, `storeCardAnchor`, `storeBalanceAfter` — as
> *"Product-sensitive rows remain deliberately outside this safe cutover"* (WO-1605). Their **source
> pin is untouched and still green**, but they now render **translated**, because resolution moved
> underneath them. That is the direct consequence of the owner's "fix it completely" ruling and is
> surfaced here rather than discovered later. If any of those five must stay English, say so and they
> can be pinned back individually.

## 7. Explicitly NOT done, with reasons

1. **Two inline English sentences stay English: `WalletlessBrowsingBanner` and
   `PiWalletRequiredSentence`.** This is brief §6's stop rule, not an omission.
   `PackStore.cs:1653` **probes the rendered label text**
   (`_balanceLabel.text.IndexOf(WalletlessBrowsingBannerProbe)`) to decide header state. Localizing
   the label makes that probe miss in nine languages — a **behaviour** change in the module where
   money changes hands. The localized row `storeWalletlessBrowsingBanner` already exists in all ten
   tables; the real fix is to compare STATE instead of text, then point the label at that row.
   **Follow-up ticket.**
2. **The 86 Store rows in `canon-strings.json` are now read by no code, and were left in place.**
   Zero risk this pass, and the mirrors stay trivially identical. Deleting them is a clean follow-up
   with a precedent to copy (`BreakableContainerChestRegression:443` pins the *absence* of rows a
   prior migration deleted). Note their `PlayNeutralStringReplacements` entries in
   `GooglePlayContentExclusion.cs:284-296` are now dead config — harmless, since that sweep still
   requires every offending canon value be listed, but they should go in the same follow-up.
3. **Typed `LocalizedText` wrappers per cohort** (a `StoreNightMarketText`, `SeasonTrackText`,
   `MonthlyLedgerText` in the `StoreBuyText`/`StorePiText`/`StorePresentationText` shape) — the
   documented follow-up, needing 7 needle re-points and a review of the WO-1605 hold. **Follow-up ticket.**
4. **`Assets/Localization/Tables/*.asset`** — untouched by design (the lead's serialized merge step).
5. **`storePiWalletGate` has a hole-count difference between its old canon value (two `{0}`) and its
   en.json value (none).** Zero runtime risk: it has **no live caller** (verified — only its own
   declaration, the `PiSkinKeys` array and a comment), and `StorePiSkinCurrencyRegression:293`
   actively FAILS if `PackStore` ever formats it again. Recorded, not changed.
6. **`MonthlyLedgerPanel.cs:39`'s header comment still reads "all from canon-strings.json via
   StoreStrings"** — now half wrong (the keys are still `StoreStrings`', the words are not
   canon-strings'). Comment-only fix, deliberately deferred out of a money file that
   `BattleMonthlyRegression` source-scans. **Follow-up.**

## 8. FOUR English strings change on screen — and `en.json` being the newer authority is PROVEN

Of the 31 keys already present in `en.json`, 25 were byte-identical to canon and 6 differed. Of those
6, two have **no live `StoreStrings` caller** and therefore change nothing on screen
(`storeSpotlightEmpty` already renders through `StorePresentationText.SpotlightEmpty`;
`storePiWalletGate` is dead, §7.5). **Four actually change**, all in `PackStore.cs`:

| key | call site | was (canon) | now (en.json) |
|---|---|---|---|
| `storeBalanceUnavailable` | `PackStore.cs:4107` | SKR: unavailable in this build | SKR balance unavailable right now |
| `storeCommerceFulfilled` | `PackStore.cs:5297` | [DONE] Added to your account | [DONE] In your account |
| `storeCommerceFailed` | `PackStore.cs:5299` | [FAILED] Purchase interrupted - payment status is unknown | [FAILED] Purchase interrupted |
| `storeCommerceDelayed` | `PackStore.cs:5300` | [PENDING] Confirmation delayed - do not pay again | [PENDING] Purchase pending - do not pay again |

**Provenance, measured rather than asserted** (an earlier draft of this section cited the WO-1819
ruling, which was about the walletless banner's SKR/USD tail and says nothing about these rows — that
was hearsay and is retracted):

```
git log -1 -S"payment status is unknown" -- Assets/Resources/Data/Canonical/canon-strings.json
  58b9d8fe6  2026-08-22  feat(store): harden SKR recovery and rebuild Night Market UI
git log -1 -S'"[FAILED] Purchase interrupted"' -- Assets/Resources/Data/Canonical/en.json
  5e85b1781  2026-09-09  content(localization): stage Store commerce states
```

The `en.json` wording is **18 days newer**, and its commit was authored expressly to stage these
Store commerce states for localization. So the forwarder adopts the intended newer copy — that is the
point of the change, not a side effect.

**No regression asserts the old wording.** Grepped all of `Assets/Editor/` and `Assets/_Modules/` for
each retired sentence: `"Pick a card"`, `"Added to your account"`, `"payment status is unknown"` and
`"Confirmation delayed"` have **zero** hits outside the canonical JSON; `"SKR: unavailable"` appears
only inside explanatory **comments** in `GooglePlayContentExclusion.cs:589` and
`GooglePlayPackagingGate.cs:299`, which describe the still-untouched canon row and assert nothing.
**Zero needle re-points, confirmed by measurement and not by inference.**

> ### ⚠ SECOND OWNER-EYES ITEM — one of the four drops a chargeback-relevant clause.
> `storeCommerceFailed` goes from *"[FAILED] Purchase interrupted - payment status is unknown"* to
> *"[FAILED] Purchase interrupted"*. The dropped half is the only thing that told the player their
> money's fate is **unknown** — in the one state where they might otherwise pay twice. Its sibling
> `storeCommerceDelayed` still says "do not pay again"; this one no longer says anything equivalent.
> The `en.json` row is the newer authored copy and this lane did not change a single word of it, but
> a double-charge is a real cost and this is the owner's call, not a translator's. Flagged, not fixed.

## 9. ⚠ One real new fragility, flagged not fixed

`BattleMonthlyRegression:763-775` asserts every Season/Monthly value resolved through
`StoreStrings.Get` is **ASCII-only**. Before this ticket that read locale-invariant canon and was
therefore machine-independent; it now reads whatever locale `LocalText.LanguageCode` resolves, which
in the editor is `CodeFor(Application.systemLanguage)`. **On this English machine it reads `en.json`
and passes** — proven, not assumed: `SmartArgumentRegression:26-28` already asserts an exact English
string through the same path and is green at 578/578. But on a non-English build machine that suite
would red on accents. Cheap fix if it ever matters: have that case read `en.json` explicitly.

## 10. Counts

| | |
|---|---|
| `StoreStrings` `Key*` constants | **86** |
| keys now resolving in all 10 locales | **86 / 86** (0 gaps across 860 key-locale pairs, read back from disk) |
| already in `en.json`, fixed by the forwarder alone | **31** (0 new translation work; 6 change English, §8) |
| newly minted + translated into 10 locales | **55** |
| of those, with a live render site | **52** (3 dormant but array-listed: `storeCovenant`, `monthlyLedgerWeekSelected`, `monthlyLedgerWeekClaimable` — the latter two are still iterated by `BattleMonthlyRegression:977`, so they must resolve) |
| call sites rewritten | **0, by design** (§3) |
| regression needle re-points required | **0** (§6) |
| locale JSON files written | **20** (10 locales x 2 mirrors), 834 -> 889 real keys each, all pairs `cmp`-identical |
| Play stripGroup | 39 -> **52** `store*` keys; count law 71 == 71 |
| `replacementRows` added | **0** (§5) |
| left English by design | **2** inline consts (§7.1) |
| glyph-atlas fixes | **1** (`monthlyLedgerMilestone` pt-BR: `BÔNUS` -> `BONUS`; U+00D4 absent from the 201-scalar atlas) |
| `StoreStrings.cs` retired | **No** — survives as a key catalog (§2.3) |
| `canon-strings.json` retired | **No** — 238 non-Store rows still read by 6 other systems (§2.2) |

## 11. A free side-effect worth naming: the pseudoloc harness now covers the Store

The WO-1857 handoff recorded the Store module as invisible to **both** the sweep and WO-1861's
pseudoloc harness, *"which only wraps `LocalText`, never `StoreStrings`"*. Because every
`StoreStrings.Get`/`Format` now goes through `LocalText.TryGet`, and `LocalText`'s `Pseudo(...)` hook
is applied to all three table-resolved returns (`LocalText.cs:184,192,198`), the **next
`RunPseudolocCaptureHeadless` will exercise this module for the first time** — no harness change
needed. The Store panels should therefore appear as *fixed* in that report rather than as new leaks;
if any Store line still comes back untransformed, that names a leak this ticket did not reach (the two
inline consts of §7.1 being the known pair).

## 12. ⛔ FOUR FILES IN `git status` ARE NOT THIS LANE'S — exclude them from the commit

`Assets/Editor/Regression/DataRegression.cs`, `HarvestOverCapCopyRegression.cs`,
`HarvestResultShapeRegression.cs` (all modified) and `HarvestMaxedContainerDoorRegression.cs` (+ its
`.meta`, untracked) are another session's in-flight work in the shared tree. **Proven, not assumed:**
grepped all four for `1862` and `StoreStrings` — zero hits. Per CLAUDE.md §11 multi-session
reconciliation, the lead commits **by explicit path** and these four are not on this ticket's path
list (§13.3).

## 13. What the lead must do next

1. Merge the 55 keys into `Assets/Localization/Tables/GameStrings.asset` (shared) + all 6
   `GameStrings_<locale>.asset`. **Mandatory, not deferrable:** `LocaleParityRegression:152-167` does
   `DictionariesEqual(canonical, generated)` per enabled locale, so `LOCALE PARITY` is **RED** until
   this lands. The sidecar lists every key needing an `m_Id`.
2. Gate once on the combined tree (`COMPILE_GATE_OK` + `REGRESSION_OK` on a fresh log), watching
   `LOCALE PARITY OK`, `SMART ARGUMENT OK`, `GLYPH COVERAGE OK`,
   `PLAY_LOCALIZATION_VARIANT_POLICY_OK`, `BATTLE_MONTHLY`, `STORE_PRESENTATION_LOCALIZATION_OK`,
   `NIGHT_MARKET`, `STORE_PI_SKIN`.
3. Commit by explicit path — **exactly these, and nothing else** (§12):
   `Assets/_Modules/Wallet/StoreStrings.cs`,
   `Assets/Resources/Data/Canonical/{en,es,pt-BR,de,fr,ru,ar,ja,ko,zh-Hans}.json`,
   `Assets/StreamingAssets/Data/Canonical/{same ten}.json`,
   `Assets/Editor/Localization/GooglePlayLocalizationVariantPolicy.json`,
   `Assets/Editor/Localization/LocalizationPolicy.json`,
   `Assets/Localization/Tables/` (after step 1),
   `WorkOrders/WORK_ORDER_1862_storestrings_localization_migration.md`,
   `CLI_LANES_WO_NUMBERS.md`, `BOARD.html` (after regen),
   `Logs/debug/scratch/sweep-sidecar-storestrings-migration.json`.
   One commit — the JSON and the Unity tables cannot split without breaking parity mid-commit, and
   `LocalText.Get` on an unresolved key calls `Debug.LogError`, so a split would put fresh errors in
   any gate or F8 capture taken in the window between them (the handoff's own load-bearing finding).
4. Take the owner's ruling on **two** flagged items: the §6 lifted WO-1605 hold (five product-sensitive
   rows now render translated) and the §8 `storeCommerceFailed` chargeback clause.
5. Open the four §7 follow-up tickets (banner state-probe, canon row deletion, typed cohort wrappers,
   `MonthlyLedgerPanel.cs:39` comment).
