# Localization first-draft review (4-model pass)

**Generated:** 2026-09-08
**Purpose:** Cross-check AI first-draft locale files before CLI wires them into Unity Localization.

## Locales in this draft

| Code | Language | Files (dual-copy) |
|---|---|---|
| `en` | English (source) | already shipped |
| `es` | Spanish | `…/Canonical/es.json` |
| `pt-BR` | Portuguese (Brazil) | `…/Canonical/pt-BR.json` |
| `de` | German | `…/Canonical/de.json` |
| `fr` | French | `…/Canonical/fr.json` |
| `ru` | Russian | `…/Canonical/ru.json` |
| `ja` | Japanese | `…/Canonical/ja.json` |
| `ko` | Korean | `…/Canonical/ko.json` |
| `zh-Hans` | Chinese (Simplified) | `…/Canonical/zh-Hans.json` |
| `ar` | Arabic (MSA) | `…/Canonical/ar.json` |

Each non-English locale must exist in **both**:

- `Assets/StreamingAssets/Data/Canonical/<code>.json`
- `Assets/Resources/Data/Canonical/<code>.json`

(byte-identical within a locale)

## How to run the 4-model review

1. Pick a sample of **30–50 keys** spanning: title, tutorial, HUD (`hud*`, `settings.*`), combat warnings, victory, errors/toasts.
2. For each model (e.g. GPT, Claude, Gemini, Grok), ask:
   - Is the translation natural for a fantasy tower-defense UI?
   - Were `{placeholders}` preserved exactly?
   - Were lore names (Elarion, Keeper, Heart, Spire, …) left intact?
   - Any meaning errors vs English?
3. Mark each sampled key: **OK** / **Revise** / **Block**.
4. Only keys marked OK by ≥3/4 models (or unanimously for legal/settings) ship without human translator review.
5. CJK + Arabic also need a **font/glyph** check on device (TMP tofu), separate from wording.

## Explicit non-claims

- These files are **AI first drafts**, not linguistic QA.
- Arabic needs RTL layout work before player-facing ship.
- CLI still must: register locales, run `LocalizationBuilder`, keep `LocaleParityRegression` green.

## Suggested CLI paste (after you approve drafts)

```text
Register locales es, pt-BR, de, fr, ru, ja, ko, zh-Hans, ar from Canonical dual-copy JSON.
Import via LocalizationBuilder into GameStrings_{locale}. Do not invent a second string system.
Font fallback for CJK/AR is a separate ticket if glyphs tofu.
```
