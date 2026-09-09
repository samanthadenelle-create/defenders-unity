# Localization architecture and supported languages

**Canon status:** Active architecture for WO-1605 as of 2026-09-08. This document describes the
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
| `ru` | русский | Yes | Yes | Beta, AI first draft |
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
- Both copies contain exactly 358 player keys today. `LocaleParityRegression` requires exact keys,
  nonempty values, semantically identical mirrors, and format-argument multisets for all 10 required locales.
- `LocalizationBuilder` reads policy, registers only build-enabled locales, reconciles shared keys,
  generates the six `GameStrings` tables and Addressables groups, preserves Smart metadata, assigns
  deterministic locale order, and records English fallback metadata.
- A new English key without the other nine regional values is a failing change, not an English-only
  fallback release. Removing or renaming a key has the same all-region requirement.
- Player-authored names, wallet addresses, and free-form feedback are data and are never translated.
- Text baked into images should be removed in favor of text-free art plus localized TMP text. When text
  is inseparable from approved art, use a locale-specific Asset Table and keep accessible localized text.

## Verification and current boundary

Run `tools/localization/run-localization-overnight.ps1` with Unity closed. It verifies the manifest,
runs the five focused localization suites, runs the complete data regression, and captures Settings
top, Settings language controls, and HUD at 2670 x 1200 for every build-enabled locale. The current
checkpoint is 5/5 focused suites, 462/462 full suites, and 18/18 screenshots with no missing keys,
English fallback, missing glyphs, or blank frames.

The architecture, Settings, Feedback/Jeweler slice, shared Close treatment, and HUD/Raid forwarding
shims are in place. Store, Village, Canon, and many direct visible HUD/runtime literals remain migration
work. The literal inventory is deliberately report-only until its baseline is human-classified; do not
describe WO-1605 as complete or arm the debt ratchet early.
