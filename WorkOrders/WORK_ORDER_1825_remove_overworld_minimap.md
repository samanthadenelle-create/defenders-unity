# WO-1825 — Remove the overworld/town corner minimap (widget, flag, and its reserved band)

**Status:** IMPLEMENTED

**Silo:** `Assets/_Modules/HUD/Kit/HudMinimapWidget.cs` (delete) ·
`Assets/_Modules/HUD/Kit/HudKitController.cs` (build/wire site only) ·
`Assets/_Modules/Core/FeatureFlags.cs` (the `Minimap` flag) ·
`Assets/_Modules/Core/UI/HudLayoutBands.cs` (`MinimapMount` + the plate/status-line pixel consts) ·
`Assets/_Modules/HUD/Kit/HudAreasHost.cs` (the one `Add(...)` line + its prose) ·
`Assets/Resources/Data/Canonical/hud-areas.json` + `Assets/StreamingAssets/Data/Canonical/hud-areas.json` ·
`Assets/Editor/Regression/FrameBudgetMeasureRegression.cs` ·
`Assets/Editor/Regression/NightMarketUiRegression.cs`

**Do NOT touch:** `HeartMount`, the Heart plate build code, `HudLabelFitRegression.cs` Case 10 —
WO-1824 owns those and is editing `HudKitController.cs` concurrently (its own note is already in the
tree at `HudKitController.cs:2565-2566`).

---

## 1. Owner ruling (verbatim, binding)

> "Let's remove it, landscape is too small"
> "And only help could be in dungeons maybe"

So the corner minimap leaves the overworld/town HUD outright — not "left flagged off".
**A dungeon map is an explicit "maybe" and is PARKED:** this WO builds no dungeon minimap and
scopes none. If it is ever wanted it needs its own spec and its own owner ruling.

## 2. What was read at source this session (evidence, no inference)

- `Assets/_Modules/Core/FeatureFlags.cs:933` — `public static bool Minimap => Get("minimap", defaultOn: false);`
  ⚠ **Its own doc comment at `:920` says "Default ON".** The prose and the code disagree; the code is
  the authority. (The flag is deleted here, so the contradiction goes with it — recorded because the
  dispatch brief repeated the doc's claim.)
- `Assets/_Modules/Core/FeatureFlags.cs` has **no `"minimap"` entry in `s_urlActivatableFlags`**
  (`:1441`); `Assets/Editor/Regression/FeatureFlagSnapshot.cs` and
  `FeatureFlagSnapshotRegression.cs` contain **no** minimap reference — so nothing pins the flag's
  existence.
- **`FeatureFlags.Minimap` has ZERO call sites.** Grep over `Assets/_Modules`, `Assets/Editor`,
  `Assets/Data` returns only the declaration at `:933`. The flag was already inert.
- `Assets/_Modules/HUD/Kit/HudKitController.cs:1037-1045` — the build site is **already a comment
  block that constructs nothing**: *"Locked adaptive-HUD ruling: no minimap is constructed on the
  player HUD."* `_minimap` (`:275`) is assigned nowhere; `WireMinimapProviders` (`:1648-1655`) has
  no caller.
- `Assets/_Modules/Core/UI/HudLayoutBands.cs:140-143` states the same at source: *"the Minimap
  mount … is EMPTY at runtime: HudKitController constructs no HudMinimapWidget … so
  MinimapPlatePx / StatusLinePx above currently describe a plate and a status line that nothing
  draws."*
- `Assets/Resources/Data/Canonical/hud-areas.json:17-20` (`calm(town)`) and `:99-103`
  (`calm(explore)`) carry the `"minimap"` area rows; `StreamingAssets` copy is byte-identical at
  the same lines.
- `Assets/_Modules/HUD/Kit/HudKitController.cs:1259-1264` — the Night Market card anchors to its
  mount's **top-left at a FIXED pixel size** (`anchorMin = anchorMax = (0,1)`,
  `sizeDelta = NightMarketCardWidthPx x NightMarketCardHeightPx`), never as a fraction of the mount.
  **Therefore the mount rect may be renamed but its four values must not change by a digit.**

## 3. ⚠ THE HONEST FINDING: THIS FREES A RESERVATION, NOT SCREEN PIXELS

The minimap has not drawn for some time (evidence above). What WO-1825 actually removes is the
**phantom occupancy** the layout oracle was budgeting around: the `minimapPlate` + `statusLine`
bands that `HudLayoutBands.ResolveLeftColumn` (`:542-548`) returned for a widget nothing builds.

Measured at the owner's device (2670x1200; `CanvasReferenceSize` → **2147.7 x 965.3** reference
units, computed from `HudLayoutBands.CanvasReferenceSize`'s own formula at `:519-527`):

| Rect | Screen fractions | Reference units |
|---|---|---|
| The whole former `MinimapMount` | x 0.011..0.240, y 0.420..0.645 | 491.8 x 217.2 |
| Night Market card (still occupies it) | x 0.011..0.1600, y 0.4834..0.645 | 320 x 156 |
| Pocket RIGHT of the card | x 0.1600..0.240, y 0.4834..0.645 | 171.8 x 156 |
| Strip BELOW the card | x 0.011..0.240, y 0.420..0.4834 | 491.8 x 61.2 |

⛔ **The strip below the card is NOT all free.** `HudLayoutBands.DockMount` (`:96`) is
`0.000..0.230 x, 0.360..0.470 y` and overlaps it. Net of the Dock the genuinely unclaimed vertical
is **y 0.470..0.4834 = 0.0134 of screen = 12.9 reference units.** That is consistent with the
WO-1824 lane's own independently-derived figure in the tree (`HudKitController.cs:2564-2566`:
*"the whole left column can free ~0.0184 even with the minimap retired"*).

**Consequence for WO-1824, flagged not buried:** the Heart plate cannot grow below y 0.655 on the
strength of this WO. Reaching the Night Market card's top edge (0.645) buys ~0.010 of screen; going
further **moves the store card**, which is an owner ruling nobody has made.

## 4. Changes

1. **DELETE** `Assets/_Modules/HUD/Kit/HudMinimapWidget.cs` (674 lines) + its `.meta`.
2. **`FeatureFlags.cs`** — delete the `Minimap` property and its doc block, replaced by a one-line
   RETIRED marker in the style of the MapTab retirement at `:843-844` (so the next seat does not
   re-add it).
3. **`HudKitController.cs`** — delete the `_minimap` field, the dead build comment block, and
   `WireMinimapProviders`. ⛔ **Keep `MakeHeroProvider` / `MakeSeamObjectiveProvider` /
   `MakeEnemyProvider`** — `WireCompassProviders` uses all three.
4. **`HudLayoutBands.cs`** —
   - **`MinimapMount` → `NightMarketMount`, same four values verbatim.** The band's only occupant
     is the store card, so it is named for it. This is the "documented replacement" the anchors are
     repointed to: `ResolveNightMarketCard` (`:181-185`) keeps hanging the card off `xMin` / `yMax`,
     which is why nothing moves on screen.
   - **DELETE** `MinimapPlatePx`, `StatusLinePx`, `StatusLineGapPx` (`:107-112`) — sole consumers
     were `HudMinimapWidget` and the two deleted `ResolveLeftColumn` bands.
   - `ResolveLeftColumn` / `LeftColumnNames` drop `minimapPlate` + `statusLine` → **4 entries**
     (hero plate, Heart objective, gear, Night Market card). Both callers find their band **by
     name** (`NightMarketUiRegression.cs:1071-1075`, `HudLabelFitRegression.cs:2108-2113`), so the
     shorter array is index-safe.
   - Rewrite the header and the WO-1335 block's "the card takes the plate's seat" conflict note —
     the conflict no longer exists.
5. **`HudAreasHost.cs:192`** — point the `Add` at `NightMarketMount`; correct the stale prose at
   `:52-54` / `:189-191`.
6. **`hud-areas.json` (both copies, kept byte-identical)** — drop the `"minimap"` **widget** id from
   `calm(town)`; in `calm(explore)` it was the row's only widget, so the row becomes
   `"widgets": []`, the precedent already used by the `system` rows in `calm(town)` and `build`.
   The **area key stays `"minimap"`** — see §5.
7. **`FrameBudgetMeasureRegression.cs:126-127`** — remove the tuple pinning
   `HudMinimapWidget.cs` / `LateUpdate`; it asserts a file path that no longer exists.
8. **`NightMarketUiRegression.cs:1076-1080`** — remove the two dead `"minimap plate"` /
   `"status line"` name-skips and their comment; the bands they skipped are gone.

## 5. Deliberately NOT done, and why (named, not silent)

- **`HudArea.Minimap` (`HudAreasHost.cs:54`), the `"minimap"` area key in both `hud-areas.json`
  copies, and `HudAreasConfig.cs:123` are LEFT AS-IS.** The area mount is **live** — it is what
  hosts the Night Market card. Renaming the key is a schema rename across two shipped canonical
  JSON copies plus `HudUiRegression` check 9c (`:1999-2046`, which resolves
  `host.Mount(HudArea.Minimap)` to prove paint order against the Dock), i.e. a structural refactor
  smuggled into a player-facing removal (`docs/ARCHITECTURE_PRINCIPLES.md`). **Follow-up ticket
  owed:** rename the area key to `nightMarket` in one dedicated change.
- **Still reference the minimap, all OUTSIDE this silo, all left untouched** (each is prose or an
  unrelated live seam):
  - `Assets/_Modules/HUD/VillageHudController.cs:230-268` — `IVillageHud.SetMinimapPoi` /
    `ClearMinimapPois`. **Still live and still needed:** they publish into `RealmPinBoard`, which
    the parchment Realm Map reads. Interface members; not this WO's to remove.
  - `Assets/_Modules/Core/HudModel/HudModels.cs:475-485`, `Core/World/RealmPins.cs`,
    `Core/UI/RealmAtmosphereStyle.cs`, `Core/UI/DefenseMapPlate.cs:6-8/486`,
    `Village/Waves/StructureVitalsWatch.cs:26/56`, `Village/World/RealmPinProducers.cs`,
    `Village/Hero/RealmMapPanel.cs`, `Village/HUD/TownHudBridge.cs` — comments citing the widget.
  - `Assets/_Modules/HUD/Kit/HudCompassWidget.cs:593-595` — a member is `internal` "so
    HudMinimapWidget draws its THREAT pip"; the widening is harmless, the reason is now historical.
  - `Assets/Editor/Regression/HudLabelFitRegression.cs:1657-1659` — a **comment** naming
    `MinimapMount.yMax`. WO-1824's file; the symbol name there goes stale on this rename and that
    lane should absorb it.
  - `Assets/Blink/.../Prefabs_Obsidian/Minimap.prefab` + the `Minimap` rows in
    `widget-params.json` / `BlinkPrefabMirror.cs` — **unrelated**: a third-party Obsidian UI pack
    asset, never the game's minimap.
- **`HudLayoutBands.cs:26` claims "HudUiRegression check 8" reads `ResolveLeftColumn`.** Grep finds
  no `HudUiRegression` caller of it — the only callers are `NightMarketUiRegression` and
  `HudLabelFitRegression`. Recorded as a stale-header finding; not fixed here.

## 6. Canon (CLAUDE.md §15)

- `docs/MASTER_CATALOG/core.md:176` asserts the minimap is a LIVE widget → one-line `STALE:` banner.
- `docs/MASTER_CATALOG/hud.md:326` already reads *"No minimap exists"* — accurate, left alone.

## 7. Acceptance

- [ ] `HudMinimapWidget.cs` + `.meta` gone; no `HudMinimapWidget` identifier remains in any `.cs`
      that is compiled (comments outside the silo may still name it — §5).
- [ ] `FeatureFlags.Minimap` gone; a RETIRED marker in its place.
- [ ] `ResolveLeftColumn` returns 4 bands and `LeftColumnNames` has 4 entries, same order.
- [ ] The Night Market card's resolved rect is **unchanged** at every aspect (same `xMin`/`yMax`
      source values, same fixed pixel size).
- [ ] Both `hud-areas.json` copies stay byte-identical (the CanonicalJson dual-copy law
      `HudActionBarRegression.cs:835` enforces).
- [ ] `tools/gate_brace.py` exit 0 + no NUL bytes on every `.cs` touched.
- [ ] **Unity not run by this lane** — `COMPILE_GATE_OK` / `REGRESSION_OK` are the lead's to obtain.
