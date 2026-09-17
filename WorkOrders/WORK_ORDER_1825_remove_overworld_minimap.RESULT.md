# WO-1825 RESULT — Overworld/town corner minimap removed

**Date:** 2026-09-17 · **Lane:** SME (no Unity run — `COMPILE_GATE_OK` / `REGRESSION_OK` owed to the lead)

## Landed

| File | Change |
|---|---|
| `Assets/_Modules/HUD/Kit/HudMinimapWidget.cs` (+ `.meta`) | **DELETED** (674 lines) |
| `Assets/_Modules/Core/FeatureFlags.cs:917-927` | `Minimap` property + its 17-line doc block replaced by a 10-line RETIRED marker |
| `Assets/_Modules/HUD/Kit/HudKitController.cs` | `_minimap` field deleted (was `:275`); dead build comment → a 7-line "no minimap may be added" marker (`~:1036-1042`); `WireMinimapProviders` deleted → 3-line note (`~:1639-1641`) |
| `Assets/_Modules/Core/UI/HudLayoutBands.cs` | `MinimapMount` → **`NightMarketMount`**, same four values; `MinimapPlatePx`/`StatusLinePx`/`StatusLineGapPx` deleted (`~:130`); `ResolveLeftColumn` + `LeftColumnNames` 6 → **4** entries; header (`:11-26`, `:33-36`) and the WO-1335 "takes the plate's seat" block rewritten |
| `Assets/_Modules/HUD/Kit/HudAreasHost.cs` | enum doc (`~:52-58`) + the `Add(HudArea.Minimap, HudLayoutBands.NightMarketMount)` line (`~:200`) |
| `Assets/Resources/Data/Canonical/hud-areas.json` + `Assets/StreamingAssets/...` | `"minimap"` widget id dropped from `calm(town)`; `calm(explore)` row → `"widgets": []`; 275 → 272 lines |
| `Assets/Editor/Regression/FrameBudgetMeasureRegression.cs:126-127` | the `HudMinimapWidget.cs`/`LateUpdate` tuple removed |
| `Assets/Editor/Regression/NightMarketUiRegression.cs:1076-1080` | the two dead name-skips removed |
| `docs/MASTER_CATALOG/core.md:2-7` | one-line `STALE 2026-09-17` banner (CLAUDE.md §15) |

## Anchor repoint — stated, per the brief

`ResolveNightMarketCard` hung the card off `MinimapMount.xMin` / `.yMax`. **Chosen replacement: rename
the constant, keep the values verbatim** (`NightMarketMount = Rect.MinMaxRect(0.011f, 0.420f, 0.240f,
0.645f)`). Why this over a screen edge or `HeartMount - gap`:

1. The card anchors **top-left at a fixed pixel size** (`HudKitController.cs:1259-1264`), so the band's
   `xMin`/`yMax` are the only load-bearing values — renaming moves nothing on screen, and a re-derived
   anchor would.
2. `HeartMount` was **off-limits and being edited by WO-1824 concurrently**; coupling the store card to
   it would have made a sibling lane's ruling silently move the card.
3. The band now has exactly one occupant, so naming it for that occupant removes the reservation the
   owner objected to without smuggling a geometry change into a removal.

## Verification done (no Unity)

- `python tools/gate_brace.py` on all 6 edited `.cs` → `GATE_BRACE_SUMMARY bad=0 of 6`, exit 0.
- NUL-byte scan on the same 6 → all clean, exit 0.
- Both `hud-areas.json` copies: `md5 fe1dd5f43add80c7df328a3c521f5f7d` **identical**, 0 CR bytes,
  272 LF lines, `json.load` parses.
- `grep -rn` over `Assets --include=*.cs` for `HudMinimapWidget`, `MinimapMount`, `MinimapPlatePx`,
  `StatusLinePx`, `StatusLineGapPx`, `FeatureFlags.Minimap`, `WireMinimapProviders`, `_minimap`:
  **zero code hits.** Four comment-only hits remain and are named in the WO §5.

## Suites checked for a hidden pin on the deleted flag / file (all clear)

- `FeatureFlagSnapshot.cs:45` — `FlagsSourceRelative = "_Modules/Core/FeatureFlags.cs"`: the snapshot
  **parses the live source at runtime**, there is no committed baseline file and
  `FeatureFlagSnapshotRegression.cs` asserts only `keys.Count == 0` (`:33`), never a specific flag or
  a flag count. Deleting `Minimap` cannot trip it.
- `FrameBudgetMeasureRegression.Run` — iterates `foreach (var site in Sites)` (`:368`) and uses
  `Sites.Length` only in the PASS message (`:440`). **No count assertion**, so the table is one
  shorter with no off-by-one.
- `docs/localization/manifest.json` contains the string `"minimap plate"`. **Out of silo, untouched** —
  named here as a follow-up in case an orphan-key lint ever runs over it.

## Not verifiable from here

`COMPILE_GATE_OK`, `REGRESSION_OK <n>/<n>` and a `UI_CAPTURE_OK` screenshot proving the Night Market
card sits exactly where it did. This lane ran **no Unity** (per instruction; `Get-Process Unity` was
not needed because no launch was attempted). Suites most likely to speak: `NightMarketUiRegression`,
`HudLabelFitRegression`, `HudUiRegression` check 9, `FrameBudgetMeasureRegression`.

⚠ **The deleted `.meta`'s GUID could not be swept** — this lane may not run git. A serialized scene or
prefab reference would be by GUID, not by name, so the name greps above cannot prove absence. One
command for the lead: `git show HEAD:Assets/_Modules/HUD/Kit/HudMinimapWidget.cs.meta`, then grep that
guid across `Assets/**/*.unity` and `Assets/**/*.prefab`. Risk is low (the HUD is code-built and the
class was only ever instantiated by `HudMinimapWidget.Create()`), but it is unproven, not proven.

⚠ **`HudKitController.cs` is under concurrent edit by the WO-1824 lane** (its line numbers shifted +15
between this lane's first read and its edits; WO-1824's own note was already in the tree at `:2565`).
All three hunks here were applied with unique anchors and succeeded, but a full-file `Write` from a
stale copy in that lane would silently drop them. **Before gating, grep `WO-1825` in
`Assets/_Modules/HUD/Kit/HudKitController.cs` — **two** hits expected (counted at source: the build-site
marker and the WireMinimapProviders note; the `_minimap` field deletion left no marker, by design).**

## Follow-ups owed (not done here, deliberately)

1. Rename the `HudArea.Minimap` enum key / `"area": "minimap"` JSON key / `HudAreasConfig.cs:123`
   parser case to `nightMarket`, in one dedicated change (WO §5).
2. `HudLabelFitRegression.cs:1659` and `HudKitController.cs:168` are WO-1824 comments naming
   `MinimapMount`; that lane should absorb the new symbol name.
3. `HudLayoutBands.cs:26` credits "HudUiRegression check 8" with reading `ResolveLeftColumn`; no such
   caller exists. Stale header, recorded not fixed.
