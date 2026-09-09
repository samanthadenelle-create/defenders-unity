# WO-1605 - Localize every player-readable text surface

**Status:** IN PROGRESS - current checkpoint covers localization authority, regional parity, six-locale beta, calm/Heart/Flee HUD copy, and 21 Store buy/Pi/wallet keys staged; Store runtime cutover and remaining visible-text batches are still open

**Owner decision:** Every written or player-readable phrase must be switchable by language. Nothing is
spoken, so localized voice/audio is explicitly out of scope.

## 1. Outcome

Move player-facing copy into one localization system without destabilizing gameplay. The finished game
must support changing language without reinstalling, fall back safely to English, expose missing strings
during development, and prevent new hard-coded copy from entering the project.

This is a staged migration, not a blind replacement of every quoted string. Save keys, object names,
analytics events, URLs, resource paths, protocol values, developer logs, and other internal identifiers
must remain stable.

## 2. Measured baseline (2026-09-08)

- Unity Localization `1.5.11` is installed and an English `GameStrings` table exists.
- Runtime UI does not consistently use Unity's String Database. Several hand-written readers are active:
  `LocalText`, `CanonStrings`, `VillageStrings`, `HudStrings`, `RaidStrings`, and `StoreStrings`.
- `LocalText.Get` is used in only 3 runtime files (37 calls). The other legacy readers account for at
  least 130 additional calls.
- The mirrored English locale files contain 262 localizable entries. The mirrored canon files contain
  347 entries that need classification between translatable copy and protected names.
- A broad literal scan identifies approximately 1,254 candidate code lines. This intentionally includes
  false positives and must become a reviewed manifest before it is treated as migration scope.
- The largest candidate areas are Village (~706), Core (~176), HUD (~140), Onboarding (~61), Wallet
  (~57), Settings (~34), and Dungeons (~30).

The baseline counts are planning numbers. Phase 0 produces the authoritative count.

## 3. Architecture decision

Use Unity Localization as the single authoring and runtime authority. Keep a package-independent facade
in Core so feature assemblies do not each acquire their own localization implementation.

```text
Feature UI / authored content
            |
            v
     LocalText facade (Core)
            |
            v
 ILocalTextProvider contract
            |
            v
 UnityLocalizationProvider adapter
            |
            v
 Unity String + Asset Tables
```

### Provider boundary

- `ILocalTextProvider` and `LocalText` live in Core and do not reference Unity Localization.
- A small `DeNelle.Localization` assembly references Core, Unity Localization, and Addressables, then
  registers `UnityLocalizationProvider` during startup.
- The provider preloads the selected locale asynchronously. Do not use `WaitForCompletion`; it is unsafe
  for Addressables on WebGL and can stall startup elsewhere.
- Until the selected locale is ready, the facade returns the English value. Development builds display
  `[[missing:key]]` for unknown keys and report them once.
- Existing readers become deprecated forwarding shims, then are removed after their consumers migrate.

### Authoring authority

- `GameStrings` is the default String Table Collection for shared and UI copy.
- Additional collections may be created by content domain when scale or ownership warrants it, such as
  `Dialogue`, `Items`, `Quests`, and `Legal`. Do not create a collection per screen.
- English is stored once in the English table. It must not also be duplicated as a fallback argument at
  every call site.
- `canon-strings.json` remains a source for protected lore names only after every entry is classified.
  Player instructions, descriptions, and labels found there move to localization tables.
- Locale data should have one generated runtime representation. Existing JSON mirrors remain transitional
  inputs only and are validated for parity until retired.

### Target project structure

```text
Assets/_Modules/Core/UI/
  LocalText.cs

Assets/_Modules/Localization/
  DeNelle.Localization.asmdef
  UnityLocalizationProvider.cs
  LocalizationBootstrap.cs
  SmartArguments.cs

Assets/Localization/
  Settings/
  Locales/
  StringTables/
    GameStrings/
    Dialogue/        # when needed
    Items/           # when needed
  AssetTables/       # approved locale-specific art only
  Fonts/
  PseudoLocales/

Assets/Editor/Localization/
  LocalizationManifestBuilder.cs
  PlayerTextLiteralScanner.cs
  LocaleParityValidator.cs
  GlyphCoverageValidator.cs
  LocalizedCaptureRunner.cs

docs/localization/
  manifest.json
  key-naming.md
  translator-guide.md
  approved-image-text.json
```

## 4. Key and string rules

Use stable semantic keys, not English sentences, as identifiers. C# constants may define keys; they must
not become a second library of English copy.

```csharp
// Transitional call while the English table is being populated.
LocalText.Get(UiKeys.Jeweler.Unlocked, "Jeweler Unlocked");

// Required end state: English lives only in the table.
LocalText.Get(UiKeys.Jeweler.Unlocked);
```

Recommended key form is `<domain>.<surface>.<meaning>`, for example:

- `jeweler.ftue.title`
- `jeweler.polish.action`
- `jeweler.polish.duration`
- `raid.queue.position`
- `settings.language.system_default`
- `feedback.rate_us.dismiss`

Rules:

- A key describes meaning, not control hierarchy or current English wording.
- Reuse a key only when both meaning and translation context are identical.
- Never concatenate translated fragments. Use one Smart String with named arguments.
- Use Smart Strings for plurals, select/gender forms, dates, numbers, currency, and resource quantities.
- Arguments have stable, documented names; automated tests verify every locale uses compatible arguments.
- Preserve intentional line breaks sparingly. Layout, not the string, normally controls wrapping.
- Proper names are explicitly marked `translatable: false` with a reason; they are not silently omitted.
- Accessibility labels and content descriptions use the same tables as visual copy.
- User-authored text, wallet addresses, player names, and free-form feedback are data, not locale strings.
  Do not machine-translate them without a separate privacy and moderation decision.

## 5. Coverage manifest

Phase 0 generates a machine-readable row for every candidate and gives it exactly one disposition:
`migrated`, `nonTranslatable`, `approvedAssetVariant`, or `blocked` with an owner.

Each row records:

- stable key and table collection;
- English source value;
- owning module and source file or asset;
- screen/surface and translator context;
- named arguments and formatting rules;
- whether it is translatable and, if not, why;
- maximum expected expansion and layout constraints;
- reference screenshot where useful;
- migration state, owner, reviewer, and last verification date.

Example:

```json
{
  "key": "jeweler.polish.duration",
  "collection": "GameStrings",
  "english": "Polishing takes {minutes} minutes.",
  "owner": "Village",
  "surface": "Jeweler polish confirmation",
  "arguments": { "minutes": "integer" },
  "translatable": true,
  "maxExpansion": 1.4,
  "state": "planned"
}
```

The scanner starts as report-only so the baseline can be classified. Once the baseline is committed, CI
fails only on new unclassified player-facing literals. Existing debt is then burned down by module.

## 6. Text embedded in images

An image is not a substitute for a translated instruction. Use this order of preference:

1. text-free artwork with live TextMeshPro text layered over it;
2. a universally understood object/icon plus localized accessible text;
3. a locale-specific Asset Table variant only when text is inseparable from approved branded art.

For example, the rough-stone art can represent the item and reduce repeated labeling, but actions such as
`Polish`, timing, requirements, failures, and reveal results remain localized strings. Every approved image
containing words is listed in `approved-image-text.json`, with source art and one asset per shipped locale.

## 7. Migration plan

### Phase 0 - Inventory and freeze new debt

- Generate the manifest from C#, prefabs, scenes, ScriptableObjects, JSON, and supported art metadata/OCR.
- Review false positives and classify canon/proper names.
- Import the current 262 English entries as the English baseline and validate JSON mirror parity.
- Add a literal-leak scanner in warning mode, approve the baseline, then fail CI on new debt.

**Exit:** Every discovered candidate has an owner and disposition; new player-copy literals are gated.

### Phase 1 - Establish the runtime authority

- Add the Core provider contract, Unity adapter, asynchronous initialization, English fallback, and missing
  key diagnostics.
- Add `System Default` plus explicit locale selection. Explicit player choice wins over device language and
  persists across sessions.
- Convert legacy resolvers into forwarding shims and mark them obsolete.
- Provide typed key wrappers or generated constants for compile-time discovery without duplicating English.

**Exit:** One resolver controls all migrated copy and language can change without reinstalling.

### Phase 2 - Prove one complete vertical slice

Migrate Settings language controls, feedback/rate-us UI, and the Jeweler unlock/polish/reveal FTUE. This
slice deliberately crosses Core, Settings, and Village and exercises modals, buttons, toasts, accessibility,
formatted duration/quantity text, persistence, and screenshots.

**Exit:** The slice passes English, pseudo-localized, missing-key, locale-switch, and device layout tests.

### Phase 3 - Migrate runtime UI by bounded batch

1. HUD, navigation, decks, Manage, Queue, and enemy labels.
2. Combat, raids, results, rewards, failures, and reconnection states.
3. Inventory, equipment, skills, crafting, Jeweler, Apothecary, shops, and purchase flows.
4. Onboarding, hero/pet tutorials, settings, help, feedback, and platform prompts.
5. Buildings, research, troops, quests, seasons, social, and remaining Village surfaces.
6. Offline/error states, wallet/platform states, legal copy, notifications, and accessibility-only text.

Each batch includes table entries, call-site migration, screenshots, manifest updates, and regression tests.
Do not mix unrelated UI redesign into a localization batch unless required to support text expansion.

### Phase 4 - Migrate authored content

- Convert dialogue, tutorial, quest, item, building, skill, shop, and narrative descriptions to stable keys.
- Keep gameplay values as structured arguments rather than embedding them in translated sentences.
- Give translators speaker, tone, gameplay context, variables, and screenshots where meaning is ambiguous.

**Exit:** No player-readable authored content depends on an English-only JSON field.

### Phase 5 - Fonts, art, and layout hardening

- Build target-script font fallback chains and validate every character used by every locale.
- Add an accented pseudo-locale, approximately 40% expansion, missing-glyph stress, and a separate RTL
  readiness pass. Shipping RTL is gated independently from simple string translation.
- Capture affected screens at phone, Seeker landscape, desktop, and portrait targets.
- Remove baked-in words or provide approved localized Asset Table variants.

**Exit:** No truncation, overlap, ellipsis that hides required meaning, tofu glyphs, or unusable touch areas.

### Phase 6 - Translation workflow and first language pilot

- Export/import XLIFF or CSV with keys, context, arguments, screenshots, and character constraints.
- Require linguistic review in game, not spreadsheet-only approval.
- Track translator, reviewer, source revision, locale revision, and verification build.
- Ship the pseudo-locale first; Product selects the first human language from actual audience/grant needs.

**Exit:** The pilot locale passes linguistic, functional, device, and fallback verification.

## 8. Regression and release gates

Add or extend these automated checks:

- `LocalizationAuthorityRegression`: migrated screens resolve through the single provider.
- `PlayerTextLiteralLeakRegression`: no new unclassified player-facing literals.
- `LocaleParityRegression`: shipped locales contain the required keys and no broken table references.
- `SmartArgumentRegression`: locale entries use valid, matching named arguments.
- `GlyphCoverageRegression`: every locale character resolves through the configured font chain.
- `LocalizedLayoutRegression`: pseudo-localized captures show no clipping, overlap, unintended ellipsis, or
  undersized interactive controls at supported display classes.
- `EmbeddedImageTextRegression`: word-bearing art is either removed or present in the approved registry.
- Locale switching persists, System Default follows the device, explicit selection wins, and missing locale
  content falls back to English without blocking startup.
- English reference captures remain semantically unchanged unless an approved copy change is recorded.

Release requires 100% of manifest rows to be migrated or explicitly dispositioned. A blocked row needs a
named owner and cannot be silently excluded from the score.

## 9. Work package and review structure

Every migration pull request or work order should contain one bounded module/surface set and include:

- keys and English table entries;
- migrated call sites/assets;
- manifest disposition changes;
- automated test changes;
- English and pseudo-localized evidence captures;
- an owner review for wording and a UI review for layout.

Track progress by manifest rows and surfaces, not by raw key count. A single complex formatted dialogue and
twenty repeated button labels are not equivalent work.

## 10. Implementation checkpoint - 2026-09-08

The first foundation increment is implemented and integrated:

- one package-independent `LocalText` facade and one package-backed runtime provider;
- reusable `LocalizedText` and `LocalizedText<TArguments>` wrappers, with feature files
  acting only as key catalogs;
- one global selected locale, persisted explicit choice, System Default mode, asynchronous
  table loading, and English fallback;
- exact reconciliation of six build-enabled Unity tables to 396 canonical keys each, including
  stale-key removal, Smart String metadata, deterministic ordering, and English fallback metadata;
- ten required regional catalogs with exact English-key and placeholder parity; English, Spanish,
  Brazilian Portuguese, German, French, and Russian are build-enabled beta locales, while Arabic,
  Japanese, Korean, and Simplified Chinese remain internal until font/RTL readiness is complete;
- Feedback, Jeweler FTUE/polish/reveal, and Settings converted to key-only calls;
- HUD and Raid compatibility catalogs forward through `LocalText`; Village and Canon compatibility
  readers now do too, while Store remains an explicitly tracked coherent follow-up;
- calm navigation and the complete Heart HUD cluster resolve through domain wrappers, including typed
  named arguments for troop counts and Heartfire timers;
- the hostile two-tap Flee control uses separate action/confirmation keys and preserves its armed
  semantic state when the locale changes;
- Store purchase-gate copy is staged as one seven-key semantic cohort in all ten required locales and
  all six enabled tables. This is not a runtime cutover: `StoreStrings` keeps its legacy reader until all
  Store cohorts are present and can move to `LocalText` atomically;
- the six-key Pi cohort replaces its stale guest-price threshold with the truthful wallet-at-every-price
  policy, and the eight-key wallet cohort includes the real walletless banner plus a neutral transient
  balance-unavailable state; both remain data staging pending that same atomic Store cutover;
- positional placeholders replaced with typed named arguments (`Minimum`, `Resource`,
  `AmountOver`, `Duration`, and `Gem`);
- domain conventions established for `hud`, `battle`, `interaction`, `shop`, `lore`,
  `settings`, and feature-specific namespaces;
- authority, literal-leak, locale-parity, Smart-argument, and Settings regressions registered in
  the full data gate.

Evidence: the latest canonical import reports 2,376 localized entries across six enabled tables;
the post-cohort focused suite reports `LOCALIZATION_REGRESSION_OK 7/7 suites`. The latest clean full registered
suite reports `REGRESSION_OK 462/462 suites`; a subsequent mixed-tree run is honestly red at 460/463 on
three unrelated HUD/source-registration assertions. The 2026-09-09 locale smoke harness reports
`LOCALIZATION_SMOKE_OK locales=6 screenshots=18/18` at the Seeker landscape reference size,
with Settings top/language-row and HUD proof frames per enabled locale. CompileGate's project
compile passed; its wrapper remains externally red only because this workstation lacks the
optional WebGL built-in module required by the Solana package's WebGL input source.

The clean staged-tree manifest contains 6,013 report rows (396 keyed entries, 83 keyed wrapper
calls, 5,518 candidates, and 16 raster reviews). Its exact 2,994-fingerprint literal-debt
block is intentionally `reviewed:false`: classification and domain-by-domain burn-down
remain active goal work, and the ratchet must not be armed before that review.

## 10. Explicit non-goals

- Spoken audio localization, because the game has no spoken dialogue.
- Automatic translation of player-authored or free-form feedback text.
- Replacing stable internal identifiers merely because they are English-like.
- Shipping RTL before layout, shaping, input, and font support pass their own gate.
- Using icons alone where their meaning is culturally ambiguous or inaccessible.

## 11. First implementation increment

The safest first buildable increment is Phases 0 and 1 plus the Phase 2 vertical slice. It creates the
authority and enforcement before bulk migration, then proves the structure on real screens already known
to need copy and layout work. Only after that evidence passes should the high-volume Village and HUD
batches begin.

This work order expands and supersedes the recommendations in
`docs/design/LOCALIZATION_READINESS.md` while retaining it as historical audit evidence.
