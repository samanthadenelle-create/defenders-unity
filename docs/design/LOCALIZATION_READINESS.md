# Localization / i18n Readiness Audit

> **Implementation update - 2026-09-09 (WO-1605, through `50cba5715`):** The runtime gap described by
> this June audit is now being closed. `LocalText` is the single Core facade;
> `DeNelle.Localization.UnityLocalizationProvider` is its sole package-backed adapter;
> selected and English `GameStrings` tables preload asynchronously; explicit locale
> preferences and System Default are supported; missing keys fail visibly. Six build-enabled
> tables now reconcile exactly to all 414 canonical entries (2,484 entries total). English, Spanish, Brazilian
> Portuguese, German, French, and Russian are exposed, with every non-English choice labeled
> Beta. Arabic, Japanese, Korean, and Simplified Chinese remain required for exact regional
> parity but hidden until RTL/CJK font and layout gates pass. Settings is migrated and
> refreshes labels in place; HUD/Raid compatibility catalogs now forward to `LocalText`.
> Feedback and the Jeweler unlock/polish/reveal path use key-only wrappers with typed
> arguments. Seven focused suites cover authority, literal leakage, locale parity,
> Smart arguments (named and positional), Settings wiring, glyph coverage, and Flee state.
> The broad inventory remains report-only pending human classification. Store has 39 keys
> staged in all required locales, but its runtime still uses legacy `StoreStrings`/canon;
> the remaining Store cutover and many visible literals remain open. Current canon lives in
> `docs/localization/architecture.md`.

**Date:** 2026-06-28
**Scope:** How `Echoes of Elarion / Defenders of the Realm` handles user-facing text, and how ready it is to ship a translated build.
**Verdict:** **PARTIAL — good intent, shallow reach.** A clean externalized-string architecture exists for the *narrative spine* (intro, tutorials, heart-voice, tooltips, buildings, hero/pet select), and the Unity Localization package is installed and seeded. But the **runtime never actually goes through the Localization package**, the **majority of HUD/combat/shop/inventory UI hardcodes English inline**, **only `en` exists**, and the **font + layout stack is not glyph- or RTL-safe**. This is a translation-*aware* codebase, not a translation-*ready* one.

---

## 1. How text is handled today (the facts)

### 1a. The externalized layer (the good part)
- **Two canonical JSON files** under `Assets/StreamingAssets/Data/Canonical/` (mirrored in `Assets/Resources/Data/Canonical/`):
  - `canon-strings.json` — **proper nouns** (Elarion, the Heart, Alduin, hero names, building display-name keys). Deliberately *not* meant for free translation; flagged verbatim-from-narrative-bible.
  - `en.json` — **localizable copy**: cold-open, tutorials, heart-voice variants, wave warnings, victory/defeat, pet/keeper ambient, hero & pet select, element/resource blurbs, building descriptions, tooltips, ability tooltips, movement hints, shopkeeper barks, the Jupiter swap panel. ~200 flat dotted keys (`intro.coldOpen.line1`, `tooltip.ability.mage.q.body`, …). Good key hygiene; `_comment`/`_sources` metadata keys are `_`-prefixed and skipped by loaders.
- **Two runtime resolvers** read those files directly:
  - `Assets/_Modules/Onboarding/CanonStrings.cs` (`DeNelle.Onboarding`)
  - `Assets/_Modules/Village/VillageStrings.cs` (`DeNelle.Village`) — a deliberate twin (asmdef isolation: Village must not depend on Onboarding).
  - Both load via `DeNelle.Core.CanonicalJson.Read(...)` (Resources-first, StreamingAssets-fallback, WebGL-safe), parse a flat `string→string` map, and return a visible `[[missing:key]]` marker on a miss (good — no silent blanks).
- **Unity Localization package present:** `com.unity.localization@1.5.11` in `Packages/manifest.json`; `Assets/Localization/LocalizationSettings.asset` + Addressables `Localization-Locales` group exist.
- **Editor seeding exists:** `Assets/Editor/LocalizationBuilder.cs` (`Defenders/Week 1/Build Localization`) creates `LocalizationSettings`, an `en` `Locale`, and a `GameStrings` `StringTableCollection`, then flattens `en.json` into it. Idempotent.

### 1b. The gap layer (the problem)
- **The runtime does NOT use the Localization package.** `LocalizationBuilder` is the *only* file that touches `UnityEngine.Localization` / `StringTableCollection`. Nothing at runtime calls `LocalizedString` / `LocalizationSettings.StringDatabase.GetLocalizedString(...)`. So `GameStrings` is built but **never read** — the live text path is the hand-rolled JSON loaders, which are **hardwired to `en.json`** (`LocaleRelativePath = "Data/Canonical/en.json"`). There is no locale selection, no fallback chain, no runtime language switch.
- **Only ~11 runtime files consume `CanonStrings`/`VillageStrings`** — almost entirely Onboarding + a handful of Village building files (`Building.cs`, `BuildingSign.cs`, `BuildMenu.cs`, `WaveData.cs`).
- **Hardcoded inline English is widespread.** ~95 `.text = "…"` literal assignments across ~37 runtime UI files, including the screens players spend the most time in: `BattleHud9Zone.cs` (16), `PartyShopPanelMvvm.cs` (7), `CosmeticShopPanel.cs`/`ShopPanel.cs`/`VillageHudController.cs`/`JupiterSwapPanelController.cs` (4 each), plus inventory/equipment/settings/crafting/troop/raid panels, button captions, game-over, dialogue, clan chat, camp prompts, tower menus. Numeric/format strings (`$"{n}"`, `"Lv {x}"`, `"x{count}"`) are likewise inline. These never reach `en.json`.
- **Only one locale exists.** No `fr.json` / `de.json` / `ja.json` etc.; no per-locale folders; the `Locale` set is `en` only.
- **Two duplicate loaders** maintain two parsed copies of the same files — fine functionally, but doubles the surface that must be migrated to the package later.

---

## 2. Font / glyph coverage — the star-glyph (tofu) risk

**Lesson restated:** a special glyph the player must see (e.g. a `★` rating star) renders as **tofu** (a missing-glyph box) the instant it is asked for from a TMP **SDF atlas that was baked without that glyph** — exactly the failure mode that bites translated text the moment it leaves ASCII.

- **Fonts in use are static SDF atlases:**
  - Default: `LiberationSans SDF` (`TMP_Settings.defaultFontAsset`, with a hard fallback `Resources.Load("Fonts & Materials/LiberationSans SDF")` in `ElarionUiKit.cs`).
  - Themed UI: Obsidian set — `Acme-Regular SDF`, `Alata-Regular`, `TitilliumWeb-Regular SDF` (`Assets/Blink/Art/UI/Obsidian_UI/Fonts_Obsidian/…`).
- **Risk A — special symbols:** any `★`/`☆`/arrow/check/currency glyph dropped into a label (the "Victory star row" / crown-tier reward display is a live example, task #41) will tofu unless that exact codepoint is in the atlas. Prefer **sprite icons** for stars/crowns/currency, or bake the symbol range explicitly.
- **Risk B — translated text:** the Latin-1 atlases will **not** cover accented Latin-Extended (č, ş, ğ, ł), and have **zero** coverage for Cyrillic / Greek / CJK / Arabic / Hebrew / Thai / Devanagari. A `de`/`fr` build would tofu on accents; any CJK/RTL build would be almost entirely tofu.
- **No dynamic-fallback font chain is configured.** TMP supports a fallback list (and Dynamic atlas mode that rasterizes missing glyphs on demand). None is set up, so there is no safety net — a missing glyph is a box, not a graceful substitution.

---

## 3. Layout flexibility

- UI is overwhelmingly **code-built uGUI/TMP** (per project UI canon), much of it with **fixed pixel sizes, fixed-width pills/buttons, and single-line labels**. German/Finnish/Russian commonly run **+30–40 %** longer than English; long compound words will clip or overflow fixed widths.
- No evidence of **pseudo-localization** (accent+expand+bracket) ever being run to surface truncation/clipping early.
- Tooltip bodies in `en.json` are full sentences; their containers must be verified to **grow/wrap** rather than clip.
- **No RTL support anywhere:** no bidi/Arabic-shaping pass (TMP needs an Arabic/RTL text plugin or `RTLTMPro`), no mirrored layouts, no `TextAlignmentOptions` flip. RTL is effectively unsupported.

---

## 4. Readiness checklist

| # | Item | State | Notes |
|---|------|-------|-------|
| 1 | Localizable strings externalized to data | 🟡 Partial | Narrative/tutorial/tooltip spine in `en.json`; HUD/combat/shop/inventory mostly inline `.text="…"` (~95 across 37 files). |
| 2 | Proper nouns separated from translatable copy | 🟢 Yes | `canon-strings.json` vs `en.json` split is clean and documented. |
| 3 | Key-based lookup with missing-key markers | 🟢 Yes | `[[missing:key]]` surfaces typos on screen; no silent blanks. |
| 4 | Localization package installed | 🟢 Yes | `com.unity.localization@1.5.11`, settings asset + Addressables group present. |
| 5 | Runtime actually reads through the package | 🔴 No | Only the editor builder touches it; runtime is hand-rolled JSON, hardwired to `en.json`. `GameStrings` table is built but unused. |
| 6 | Single resolver / no duplication | 🟡 Partial | Two near-identical loaders (`CanonStrings`, `VillageStrings`) by asmdef necessity; both bypass the package. |
| 7 | More than one locale | 🔴 No | `en` only; no other locale assets or JSON. |
| 8 | Runtime language selection + fallback | 🔴 No | No locale selector, no fallback locale, no in-game language switch. |
| 9 | Number/date/plural formatting culture-aware | 🔴 No | Inline `$"{n}"` / `"x{count}"` string-building; no `ICU`/Smart-String plural handling. |
| 10 | Font atlas covers target scripts | 🔴 No | Static Latin-1 SDF atlases; no Latin-Extended/Cyrillic/CJK. |
| 11 | Dynamic font fallback chain configured | 🔴 No | No TMP fallback list / dynamic atlas → missing glyph = tofu. |
| 12 | Special symbols are sprites, not font glyphs | 🟡 Risk | Star/crown/currency glyphs in labels will tofu; use sprite assets. |
| 13 | Layout tolerates +35 % text expansion | 🔴 Unverified/No | Fixed-width code-built UI; no pseudo-loc pass run. |
| 14 | RTL (Arabic/Hebrew) support | 🔴 No | No bidi shaping, no mirrored layout. |
| 15 | Strings free of concatenation / interpolation traps | 🔴 No | Sentence fragments assembled in code in places; blocks clean translation. |

Legend: 🟢 ready · 🟡 partial/risk · 🔴 not ready.

---

## 5. Gaps & prioritized recommendations

**P0 — decide the single source of truth.** Today there are *two* string systems half-built: the hand-rolled JSON loaders (used) and the Unity Localization `GameStrings` table (built, unused). Pick one. Recommended: **route the runtime through the Localization package** (`LocalizedString` / `StringDatabase`) so locale selection, fallback, Smart-Strings (plurals/gender), and Addressables locale-loading come for free — and retire the bespoke loaders to thin shims over it. If the package is rejected, then at minimum parameterize the loaders' locale path (`{locale}.json`) and add a locale selector + `en` fallback.

**P1 — close the inline-text leak.** Sweep the ~37 files with inline `.text = "…"` and move player-facing literals into `en.json` (or the string table) behind keys. Add a lightweight **lint/regression** (extend `DataRegression` / CompileGate) that flags new inline UI literals so the leak doesn't reopen. Combat/shop/inventory HUD is the highest-traffic and least-covered — start there.

**P2 — make fonts glyph-safe.** Configure a **TMP fallback chain** (or Dynamic atlas) on the default + Obsidian fonts so out-of-atlas glyphs substitute instead of tofu-ing. Replace any `★`/crown/currency *text glyphs* in labels with **sprite assets** (kills the tofu risk and keeps art consistent). When a target language is chosen, bake/import an SDF atlas that covers its script.

**P3 — prove layout under expansion.** Run a **pseudo-localization** locale (accent + ~40 % expand + brackets) through every screen; fix clipping by switching fixed widths to content-size-fit / wrapping. This is the cheapest way to find truncation before a real translator does.

**P4 — formatting & plurals.** Replace inline numeric/count concatenation with the package's **Smart Strings** (or `string.Format` with culture) so plurals, gender, and number/date formatting are translator-controllable.

**P5 — RTL (only if targeted).** If Arabic/Hebrew is in scope, add an RTL/bidi shaping pass (e.g. `RTLTMPro`) and a layout-mirroring strategy. Defer unless a target market needs it.

---

## 6. Key files

- `Assets/StreamingAssets/Data/Canonical/en.json` — localizable copy (and Resources mirror).
- `Assets/StreamingAssets/Data/Canonical/canon-strings.json` — proper nouns (do-not-translate).
- `Assets/_Modules/Onboarding/CanonStrings.cs` — onboarding runtime resolver (hardwired to `en.json`).
- `Assets/_Modules/Village/VillageStrings.cs` — village runtime resolver (twin).
- `Assets/Editor/LocalizationBuilder.cs` — editor seeding of the (currently unused-at-runtime) `GameStrings` table.
- `Assets/Localization/LocalizationSettings.asset` — package settings (en only).
- `Assets/_Modules/Core/UI/ElarionUiKit.cs` — default TMP font resolution / fallback to `LiberationSans SDF`.
- `Assets/Blink/Art/UI/Obsidian_UI/Fonts_Obsidian/…` — Obsidian SDF fonts (Acme/Alata/Titillium).
- High inline-text offenders to migrate first: `Assets/_Modules/Village/Arena/BattleHud9Zone.cs`, `Assets/_Modules/Village/Hero/PartyShopPanelMvvm.cs`, `Assets/_Modules/HUD/CosmeticShopPanel.cs`, `Assets/_Modules/HUD/VillageHudController.cs`, `Assets/_Modules/Village/Hero/ShopPanel.cs`.
