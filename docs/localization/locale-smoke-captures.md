# Locale smoke captures

`LocaleSmokeCapture` produces the same three player-facing frames for every locale enabled in
the beta build: Settings at the top, Settings scrolled to the language controls, and the calm-town
adaptive HUD. Each surface is freshly
built and rendered at the Solana Seeker resolution, 2670 x 1200; screenshots are not rescaled.

## Overnight command

Close the Unity Editor, then run the complete localization lane from the repository root:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\localization\run-localization-overnight.ps1
```

This checks that the text manifest is current, runs the five focused localization suites, runs the
full data regression gate, and then writes and validates the screenshots. For a faster local visual
iteration, omit the full data gate with `-SkipFullRegression`.

To run only the screenshot stage:

```powershell
powershell -ExecutionPolicy Bypass -File .\run-unity-method.ps1 `
  -Method DeNelle.Editor.Localization.LocaleSmokeCapture.Run `
  -LogName localization-smoke.log `
  -ExpectMarker LOCALIZATION_SMOKE_OK
```

Do not pass `-nographics`. The wrapper intentionally starts Unity with `-batchmode -quit` and a
graphics device. The entry point remains synchronous until every image and the summary are on disk.

Outputs are overwritten deterministically under `Builds/localization-smoke/`:

- `<locale>_settings_2670x1200.png`
- `<locale>_settings_language_2670x1200.png`
- `<locale>_hud_2670x1200.png`
- `summary.json`

Expected locales are deliberately fixed in release order: `en`, `es`, `pt-BR`, `de`, `fr`, `ru`.
Adding an unrelated Unity Locale asset therefore cannot silently expand the release gate. Change
that list only as part of an intentional locale enablement.

## Forward-change rule

`LocalizationPolicy.json` is the region contract. `LocaleParityRegression` requires every locale
marked `required` there to have both canonical JSON copies, the exact English key set, matching
placeholder counts, and byte-equivalent Resource/StreamingAsset mirrors. Consequently, adding or
removing an English key makes the focused and full regression gates fail until **all required
regions** change in the same handback. A locale may remain `enabledInBuild: false` while font or RTL
support is incomplete, but it is still translation-required and cannot silently drift.

## What passes

The final log must contain:

```text
LOCALIZATION_SMOKE_OK locales=6 screenshots=18/18
```

`summary.json` must also say `passed: true`. For each locale the harness requires:

1. the Locale is registered in Unity Localization;
2. its `GameStrings` table and the English fallback table load successfully;
3. both Settings positions and HUD build and produce non-blank PNGs;
4. no visible TMP label contains `[[missing:...]]` or Unity's missing-translation message;
5. every visible character is present in the label's assigned TMP font or fallback chain; and
6. no visible key resolves through the English table in a non-English locale.

Failures are written to the summary before `BuildFailedException` makes the batch command fail.
Inspect the per-locale `errors`, `missingMarkers`, `missingGlyphs`, and `englishFallbackKeys` arrays.
Glyph findings use Unicode notation such as `U+0416`, making the missing font range actionable.

## Review process

The automated gate detects missing text, missing font glyphs, table load failures, blank renders,
and accidental English fallback. It cannot judge translation quality, natural phrasing, truncation,
bidirectional layout, or whether a glyph is artistically legible. After a green run:

1. Open all 18 PNGs, viewing each locale's two Settings frames and HUD side by side.
2. Check clipping, ellipses, overlap, line breaks, numeric order, and visual hierarchy.
3. Have a fluent reviewer approve wording; AI-first-draft status is not linguistic QA.
4. Record approval against the exact build commit. A dirty-tree capture is diagnostic evidence,
   not a release approval.

This is an edit-mode smoke harness. It uses the real Unity StringTables and the same real UI builders
as the established UI capture suite, but it does not boot a saved game or exercise input. A final
device pass remains required for platform font rendering and touch layout.
