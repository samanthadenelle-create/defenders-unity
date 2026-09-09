# Localization architecture and supported languages

**Canon status:** Active architecture for WO-1605 through commit `e51c51358` (2026-09-09). This document describes the
implemented authority and regional contract. It does not claim that every player-facing surface has
already migrated.

## One global resolution path

```text
Feature UI or content
  -> domain key catalog (LocalizedText / LocalizedText<TArguments>)
  -> DeNelle.Core.UI.LocalText
  -> ILocalTextProvider
  -> DeNelle.Localization.UnityLocalizationProvider
  -> Unity GameStrings table for the selected locale
  -> English GameStrings fallback
```

`LocalText` is the only feature-facing resolver. Feature areas remain understandable and separately
owned through semantic namespaces such as `hud.*`, `battle.*`, `interaction.*`, `shop.*`, `lore.*`,
`settings.*`, `feedback.*`, and `jeweler.*`; they do not create separate language systems. English
wording lives in the table, while C# wrappers contain keys and typed format arguments only.

The explicit player choice persists and wins over device language. Device Language removes that
override. Tables load asynchronously—runtime code must not use `WaitForCompletion`, particularly on
WebGL. Missing selected-locale data falls back to English; development builds expose unknown keys.

## Regional contract

`Assets/Editor/Localization/LocalizationPolicy.json` is authoritative for language membership and
release exposure. Every policy locale marked `required` must change whenever the English key set or
format arguments change, whether or not that locale is currently exposed in Settings.

| Locale | Player name | Required | Build-enabled | Current quality gate |
|---|---|---:|---:|---|
| `en` | English | Yes | Yes | Source language |
| `es` | español | Yes | Yes | Beta, AI first draft |
| `pt-BR` | português (Brasil) | Yes | Yes | Beta, AI first draft |
| `de` | Deutsch | Yes | Yes | Beta, AI first draft |
| `fr` | français | Yes | Yes | Beta, AI first draft |
| `ru` | русский | Yes | Yes | Beta, AI first draft; tracked Cyrillic glyph coverage |
| `ar` | العربية | Yes | No | Internal/limited; RTL shaping, layout, and fonts required |
| `ja` | 日本語 | Yes | No | Internal/limited; CJK font coverage required |
| `ko` | 한국어 | Yes | No | Internal/limited; CJK font coverage required |
| `zh-Hans` | 简体中文 | Yes | No | Internal/limited; CJK font coverage required |

The six exposed locales are selectable without reinstalling. Non-English choices are explicitly
labeled Beta. The four internal locales stay translation-current but cannot be enabled merely because
their JSON exists; their script, shaping, font, layout, and device gates must pass first.

## Authoring and generation

- Canonical transitional inputs are mirrored at `Assets/Resources/Data/Canonical/<locale>.json` and
  `Assets/StreamingAssets/Data/Canonical/<locale>.json`.
- Both copies contain exactly 416 player keys today. `LocaleParityRegression` requires exact keys,
  nonempty values, semantically identical mirrors, and format-argument multisets for all 10 required locales.
- `LocalizationBuilder` reads policy, registers only build-enabled locales, reconciles shared keys,
  generates the six `GameStrings` tables and Addressables groups, preserves Smart metadata, assigns
  deterministic locale order, and records English fallback metadata.
- The six build-enabled Unity tables contain 2,496 entries in total (416 entries per locale).
- `LocaleParityRegression` compares every build-enabled Unity table byte-for-value with its canonical
  locale source, so updating all JSON regions without rebuilding the shipped tables remains a hard failure.
- A new English key without the other nine regional values is a failing change, not an English-only
  fallback release. Removing or renaming a key has the same all-region requirement.
- Any future player-facing copy addition or wording/argument change must update every required locale
  catalog and its mirror, rebuild every enabled Unity table, and pass the localization tests. A translation
  may be labeled limited or provisional pending human review, but it may not be omitted from parity.
- Player-authored names, wallet addresses, and free-form feedback are data and are never translated.
- Text baked into images should be removed in favor of text-free art plus localized TMP text. When text
  is inseparable from approved art, use a locale-specific Asset Table and keep accessible localized text.

## Font authority

The default runtime font is the committed static atlas at
`Assets/Resources/Localization/Fonts/ElarionLocaleFallback.asset`, generated from the tracked
Liberation Sans source and OFL license under `Assets/Localization/Fonts/Source`. Title, body, and
stamp role fonts explicitly reference this asset as their fallback. `GlyphCoverageRegression` reads
every value in every build-enabled locale and requires each used Unicode character to exist in this
tracked static atlas; it deliberately ignores machine-local TMP settings and dynamic font assets.

Regenerate it after an enabled locale introduces a new character:

```text
-executeMethod DeNelle.Editor.Localization.LocaleFontBuilder.Build
```

Then run `tools/localization/check-localization-font-assets.ps1` after staging to prove the source,
license, atlas, and their Unity metadata are all tracked.

## Verification and current boundary

Run `tools/localization/run-localization-overnight.ps1` with Unity closed. It verifies the manifest,
runs the eight focused localization suites, runs the complete data regression, and captures Settings
top, Settings language controls, and HUD at 2670 x 1200 for every build-enabled locale. The Store
data cohorts pass 7/7 focused suites, and the 2026-09-09 visual run is 18/18 screenshots with no
missing keys, English fallback, missing glyphs, or blank frames. The current complete registered gate is
`REGRESSION_OK 465/465 suites`. `HudLabelFitRegression` now checks player copy against the two `en.json`
localization catalogs rather than the protected-name `canon-strings.json` twins; its runtime measurements
still resolve through `HudStrings` and `LocalText`. The Flee and Google Play variant policy oracles are
registered in `DataRegression`, and the HUD clearance source oracle recognizes the localized Collectors label.

The checkpoint inventory contains 6,034 rows: 416 `keyedEntry`, 85 `keyedCall`, 5,517
`literalCandidate`, and 16 `imageTextCandidate` rows. Literal candidates remain report-only and unarmed while the
baseline is human-classified; this count is discovery debt, not 5,517 confirmed defects.

The architecture, Settings, Feedback/Jeweler slice, shared Close treatment, calm navigation, Heart HUD,
combat Flee control, and HUD/Raid forwarding shims are in place. Village and Canon compatibility readers now forward to the
global facade. Dungeon chests resolve `interaction.chest.open` and `interaction.chest.blockedByEnemies`
through `ChestInteractionText`; `BreakableContainer` uses the localized open prompt and the same localized
combat refusal for both prompt and toast. The obsolete `chestOpenPrompt`, `chestCombatRefusal`, and their
dedicated canon note are removed from both canon twins. Store has 39 translated keys across its purchase-gate, Pi, wallet-mirror, commerce-state,
and safe presentation cohorts. The seven existing BUY GATE keys are live through `StoreBuyText` /
`LocalText` and `PurchaseGate`; `storeBuyWalletRequired` carries the named `{Threshold}` argument
exactly twice in every required locale. The other 32 Store keys remain data staging behind legacy
`StoreStrings`/canon readers, so Store is not fully migrated. Other Store cohorts and many direct
visible HUD/runtime literals remain migration work. The literal
inventory is deliberately report-only until its baseline is human-classified; do not describe WO-1605
as complete or arm the debt ratchet early.

The safe presentation cohort is exactly `storeBandGap`, `storeBandGapSub`, `storeBandBasket`,
`storeSpotlightEmpty`, `storeLedgerHeading`, `storeCardOwned`, and `storeCardGap`. The remaining
presentation copy is held where it asserts time-limited offers or patronage, performs value/balance
comparisons, or uses ambiguous locked-state wording; it needs product and formatting review before staging.
The four legacy trust-strip rows are also intentionally held out. Their fee, treasury, power, and covenant
claims are not uniformly accurate across Solana, Pi, and Google Play, and the covenant is still baked into
English raster art. Provider-specific approved wording and a text-free/localized carrier are required before
that cohort may stage or cut over.

The chest checkpoint passed compile/import at 2,496 entries, `CHEST_OK`,
`LOCALIZATION_REGRESSION_OK 7/7 suites`, and `LOCALE_FONT_BUILD_OK`.
The BUY GATE runtime checkpoint preserved the 2,496-entry import and passed `BUY_GATE_OK`,
`STORE_PI_SKIN_OK`, `LOCALIZATION_REGRESSION_OK 7/7 suites`, and the
`GooglePlayPackagingRegression` source gate.

### Google Play localization variant

Google Play builds prepare a transient localization variant before the explicit Addressables build. The
authority is `Assets/Editor/Localization/GooglePlayLocalizationVariantPolicy.json`, not token wildcards:
57 exact rows belonging to unavailable Wallet Store, Web3 swap, Settings-wallet, and Jeweler-stake branches
are stripped, while five still-visible rows receive locale-specific Play-neutral replacements across all
ten required locales. `storeWordmark` remains because the Google Play storefront still renders it.

`GooglePlayLocalizationVariant` transacts over both copies of all ten canonical locale files, the shared
`GameStrings` table, and all six enabled locale tables (27 assets). `AndroidBuild` prepares this state before
`AddressablesContentBuild.EnsureBuilt`; the later content-exclusion hook asserts it, composes its legacy
canon/packs/UXML quarantine, and every success or failure path restores source bytes. The focused policy
gate is now 8/8, and the combined transaction regression proves byte-identical restoration with no surviving
ledger or quarantine. This closes the source and localized-Addressables ordering defect. It does not replace
the required forbidden-token scan of the final physical AAB, which remains the artifact-level release gate.
