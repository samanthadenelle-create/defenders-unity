# WORK ORDER 1661 — The ARMY tile's state chip paints "UPGRADE A..." on device; the headless fixture never composes that word

**Status:** IMPLEMENTED - awaiting gate + owner word
**Silo:** Manage / view-model (`Assets/_Modules/Village/UI/Manage/ManageScreenVM.cs`, `Assets/_Modules/Core/Manage/ManageWorkspacePanel.cs`, `Assets/Editor/UICaptureLaunch.cs` fixture)
**Origin:** DEVICE-FRAMES-3 lane, WO-1658 device measurement, 2026-09-10
**Device under test:** Seeker SM02G4061955851, APK **2026.09.10.363786**, 2670x1200 landscape, PID 8062.

---

## 1. THE DEFECT, AS THE PLAYER SEES IT

Frame: **`Builds/device-frames/2026-09-10_1018_363786_manage_army.png`** (MANAGE – ARMY, 9 troop tiles). The two unlocked tiles — **Footman** and **Archer** — each paint a state chip reading **`UPGRADE A...`**. The other seven read `LOCKED` and are clean.

An ellipsis on a state word is the same defect class the repo already ruled on. `ManageWorkspacePanel.cs:843`, verbatim:

> `// "UPGRADE AVAILABLE" and "UPGRADING" both truncate to "UPGRADI...".`

That comment was written about the BUILD grid. The ARMY grid is now shipping the same failure with a *different* cut point, so the earlier fix did not cover it.

### Measured (PIL, on the frame above, full-resolution 2670x1200)

Footman tile, state chip:

| Quantity | Measured |
|---|---|
| Chip plate rect | x **289..591**, y **236..299** → **303 x 64 px** |
| Painted glyph ink ("UPGRADE A...") | x **303..550**, y **252..278** → **248 x 27 px** |
| Cap height of the painted word | **27 px** |
| Ink right edge to plate right edge | **41 px of unused plate** |
| Source string length vs painted | 17 printable chars authored, **10 + ellipsis** painted |

⚠ **The chip ellipsised while 41 px of its own plate stayed empty.** That is the number the lane must explain before touching a font size — the label's painted rect is narrower than the plate it sits on, so a font-size change alone will move the cut without curing it.

Second unlocked tile (Archer) shows the same string with the same cut; the seven `LOCKED` tiles show no ellipsis.

---

## 2. THE PRODUCER — a missing short face, not a layout constant

**The string source.** `Assets/_Modules/Village/UI/Manage/ManageScreenVM.cs:4895-4896`:

```csharp
else if (string.Equals(c.UpgradeWord, "UPGRADE AVAILABLE", StringComparison.Ordinal))
{ item.Badge = ManageTileBadge.UpgradeAffordable; item.BadgeText = "UPGRADE AVAILABLE"; }
```

`c.UpgradeWord` is composed at `ManageScreenVM.cs:2607` (`choice.UpgradeWord = "UPGRADE AVAILABLE";`) and the vocabulary is declared at `:309` (`"UPGRADE AVAILABLE" | "MAX" | "UPGRADING" | "NEEDS <blocker>"`).

**The projection.** `Assets/_Modules/Core/Manage/ManageVmProjection.cs:223`:

```csharp
StateWord = string.IsNullOrEmpty(item.BadgeWord) ? item.BadgeText : item.BadgeWord,
```

**The gap.** `BadgeWord` — the SHORT face this exact fallback exists to prefer — is assigned in **exactly one place in the whole tree**: `ManageScreenVM.cs:4708` (`item.BadgeWord = ready ? "READY" : "SHORT";`). The troop-upgrade branch at `:4895-4896` sets **only** `BadgeText`. So the projection falls through to the 17-character long face and the grid cell paints it.

That is the same shape as the defect WO-1518 already fixed on a neighbouring branch — the design intent is documented at `ManageWorkspacePanel.cs` `TileStateWord`'s summary: *"tile.StateWord, falling back to tile.StateText when the composer authored no shorter face (WO-1567 panel row 2) … the amounts fit the research row's state column and the detail card but NOT a grid cell."* **A composer forgot to author the grid face for `UpgradeAffordable`.**

**The view's sizing path** (context, not the cause): `ManageWorkspacePanel.cs:855-862` picks `widestState` across the grid's own tiles and calls `ResolveStateWordFont(widestState, cellW)` (`:1113-1170`), which probes with TMP `GetPreferredValues` against `availablePx = ((TileStateX1 - 0.01f) - (TileStateX0 + 0.01f)) * cellW`, with `TileStateX0 = 0.03f, TileStateX1 = 0.77f` (`:206`).

⚠ **AND HERE IS THE MEASURED CONTRADICTION THE LANE MUST OPEN WITH.** `ResolveStateWordFont` logs `FlowTrace.Step("Manage", "state word type resolved to …")` on the scale-down path (`:1157`) and `FlowTrace.Warn` on the hard-floor path (`:1146`). **Neither appears anywhere in the device log** — `grep -i "state word"` over `2026-09-10_1019_363786_logcat.txt` (PID 8062) returns **zero lines**, while `[Flow:Manage]` is demonstrably live in the same window (`10:18:37.088 [Flow:Manage] troop state id=troop-footman word=Upgradable upgrading=False hasNext=True`, and 30+ sibling lines). So the resolver took one of its **four silent early-return paths** (`:1116` empty/cellW, `:1121` availablePx, `:1136` wantPx, `:1137` "already fits, nothing to do") — i.e. **it believed the word fitted** — and the device then ellipsised it. The probe's model of the band and the label's real painted rect disagree, and today nothing says so.

---

## 3. WHY `UI_GLYPH_OK 20/20` PASSES ON `ManageFlow_ARMY_gridtop` — the fixture never composes the word

Opened at source: **`Builds/ui-capture/ManageFlow_ARMY_gridtop_2670x1200.png`** (1,289,276 bytes, mtime 2026-09-10 10:05 — the current gated capture).

Its nine tiles read: **`MAX`** (Footman), **`QUEUE FULL`** x4 (Archer, Spearman, Field Cleric, Shieldguard), **`LOCKED`** x4 (Outrider, Siege Catapult, Battlemage, Echo Legionnaire). Every one paints in full, with no ellipsis.

**`UPGRADE AVAILABLE` is not on that grid at all.** The longest word the fixture composes is `QUEUE FULL` — 10 characters against the device's 17. `ResolveStateWordFont` sizes for the widest word *on this grid* (`ManageWorkspacePanel.cs:855-862`, and the comment at `:844-846` says so explicitly: *"The longest word is taken from THE MODEL'S OWN TILES, never from a vocabulary list copied into this View"*), so the headless grid is sized for a string 7 characters shorter than the shipped one.

So the answers to the three questions asked:
- **Is the tile chip built in the headless fixture state?** Yes — the chips render and are measured.
- **Different string?** **Yes, and that is the whole hole.** The fixture's troop state is MAX / QUEUE FULL / LOCKED; the device's is UPGRADE AVAILABLE / LOCKED. The device's own log proves the live state (`10:18:37.088 troop state id=troop-footman word=Upgradable`, `id=troop-archer word=Upgradable`, `troops browse: 9 troop def(s) -> 2 Train row(s), 2 Upgrade row(s), 7 still locked`).
- **Different aspect?** No — both frames are 2670x1200.

`UI_GLYPH_OK 20/20` is therefore an **honest pass over the strings it was given** and structurally blind to the one the player gets. `LayoutOracle` ASSERT C (`LayoutOracle.cs:244-260`) compares visible glyphs against printable source characters and *would* red on `UPGRADE A...` — it is never handed that label.

---

## 4. FIX SHAPE

**A. INSTRUMENT FIRST (CLAUDE.md §12 — this is the opening move, not a fallback).** Before any layout or string edit, give `ResolveStateWordFont`'s four silent early-returns a `FlowTrace.Step` each, carrying `widest`, `cellW`, `availablePx`, `wantPx` and the returned size. Then re-open the ARMY tab on device and read which branch fires and with what numbers. The 41 px of unused plate (§1) means one of those inputs is wrong; **do not guess which.** A `FlowTrace.Step` on a screen-open path is not a hot loop, so the 3-arg form is correct here.

**B. Author the short face.** At `ManageScreenVM.cs:4895-4896`, set a grid-sized `BadgeWord` alongside the existing `BadgeText`, exactly as `:4708` already does for READY/SHORT. The long face stays on `BadgeText` for the detail card and the research row, which is where it fits. This is a **composer** edit — the View must not truncate, split or re-word (canon 9; `ManageWorkspacePanel.cs` `TileStateWord` summary; `ManageDumbViewRegression` scans for exactly that).

⚠ **The word itself is the owner's call, not the lane's.** `UPGRADE` is the obvious candidate (it matches the button vocabulary at `ManageStateModel.cs:183` / `ManageViewContract.cs:124`), but the badge must stay distinguishable from `UPGRADING` (`:4891`), which is a different state on the same grid. **Put the two candidate words to the owner before shipping one.**

**C. Cover the state in the fixture.** The ARMY capture fixture composes MAX / QUEUE FULL / LOCKED and no upgradable troop. Add an `UpgradeAffordable` tile to the ARMY grid fixture (or a dedicated `ManageFlow_ARMY_upgradable` frame) so the glyph oracle is handed the longest word the shipped game can produce. **Without C, B is unpinned and the next long badge re-opens this ticket.**

---

## 5. RED-FIRST PIN (PROD-008 / WO-1138)

Home: `Assets/Editor/Regression/ManageTroopsTrainDoorRegression.cs` — it already knows this vocabulary (`:666`, `:672`, `:682` assert on the literal `"UPGRADE AVAILABLE"`).

1. **RED case:** project a troop VM in the `UpgradeAffordable` state and assert `StateWord` is **non-empty and distinct from `StateText`** — i.e. a short grid face exists. **Run against HEAD and confirm it FAILS** (today `BadgeWord` is empty and `StateWord` falls back to the 17-char face).
2. **Glyph case:** build the ARMY grid with that tile present and assert the state label's rendered glyph count equals its printable source characters (the `LayoutOracle` ASSERT C shape, `LayoutOracle.cs:244-260`). This is the case that would have caught the device frame.
3. Assert the *rule*, not the word: read the face off the model. Never hardcode the chosen short word in the test as a second copy — that is the duplicated state CLAUDE.md §2/§5/§16 each describe.

---

## 6. WHAT NOT TO TOUCH

- ⛔ **Do not truncate, abbreviate, uppercase or re-word in the View.** `ManageWorkspacePanel.TileStateWord`'s summary states it: *"It does not truncate, split, uppercase or re-word either of them — doing any of that would be the View deriving a label, which canon 9 bans and `ManageDumbViewRegression` scans for."*
- ⛔ **Do not change `ResolveStateWordFont` to ellipsise, and do not lower its floor.** `ManageWorkspacePanel.cs:1140-1150` rules it: below `ElarionUiKit.FontHardFloor` TMP culls the line outright, so *"the honest outcome is the floor plus a named shortfall … nothing here will ellipsise a state word to hide that."* Adding instrumentation (§4A) is not a behaviour change; changing the return values is.
- ⛔ **Do not touch `TileStateX0 = 0.03f, TileStateX1 = 0.77f`** (`:206`) or `MinTileHeightPx` (`:155`) / `MaxTileAspect` (`:171`) until §4A's trace says the band is the problem. Those constants carry their own measured history in the comments above them.
- ⛔ **Do not reorder the tile layer stack.** `ManageWorkspacePanel.cs:1075-1099` records the WO-1443 §2 measurement (frame-tile centre alpha 253/255) and rules the frames painted **under** the portrait. A state-chip fix must not disturb it.
- ⛔ **Do not "fix" the fixture by making the device state match it.** The device state (2 upgradable troops) is correct game state; the fixture is the thing missing a case.
- ⛔ Out of scope: the `LOCKED` tiles, the QUEUE badge, and the BUILD/RESEARCH grids. If the same missing-`BadgeWord` shape exists on those branches, raise it as a sibling ticket rather than widening this one.

---

## 7. ACCEPTANCE

- [ ] §4A's traces landed and a **fresh device logcat** names which branch of `ResolveStateWordFont` fires on the ARMY grid, with its numbers. Quote the line.
- [ ] The RED-first pin was seen **failing** on HEAD, and passes after the fix.
- [ ] Owner ruled on the short word (§4B).
- [ ] Fixture covers the `UpgradeAffordable` state; `UI_GLYPH_OK <n>/<n>` on a fresh log with the new frame included, and the frame **opened by a human**.
- [ ] `COMPILE_GATE_OK` + `REGRESSION_OK <n>/<n>` on fresh logs (markers, not exit codes).
- [ ] A fresh device frame of MANAGE – ARMY shows both unlocked tiles painting a **complete** state word, measured with PIL the same way §1 was — glyph ink inside the plate, **no ellipsis**.

---

## 8. EVIDENCE INDEX

| Claim | Source, opened 2026-09-10 |
|---|---|
| `UPGRADE A...` on two device tiles | `Builds/device-frames/2026-09-10_1018_363786_manage_army.png` (PIL: plate 303x64, ink 248x27 at x 303..550) |
| Live troop state was Upgradable x2 | `Builds/device-frames/2026-09-10_1019_363786_logcat.txt` PID 8062, 10:18:37.088 (`troop state id=troop-footman word=Upgradable`, same for `troop-archer`, `troops browse: … 2 Upgrade row(s), 7 still locked`) |
| No `state word` resolver line anywhere in the session | same log, `grep -i "state word"` → 0 hits, while 30+ `[Flow:Manage]` lines print in the same window |
| The long string is composed here | `Assets/_Modules/Village/UI/Manage/ManageScreenVM.cs:2607`, `:4895-4896`, vocabulary at `:309` |
| `BadgeWord` assigned in exactly one place, not this branch | `Assets/_Modules/Village/UI/Manage/ManageScreenVM.cs:4708` (sole `BadgeWord =` in `Assets/_Modules`) |
| Projection prefers the short face, falls back to the long one | `Assets/_Modules/Core/Manage/ManageVmProjection.cs:223` |
| The known-defect comment naming this exact string | `Assets/_Modules/Core/Manage/ManageWorkspacePanel.cs:843` |
| Per-grid sizing from the model's own tiles | `Assets/_Modules/Core/Manage/ManageWorkspacePanel.cs:844-846`, `:855-862` |
| Resolver, its bands, and its silent early returns | `Assets/_Modules/Core/Manage/ManageWorkspacePanel.cs:1113-1170`, `TileStateX0/X1` at `:206` |
| Fixture paints MAX / QUEUE FULL / LOCKED only | `Builds/ui-capture/ManageFlow_ARMY_gridtop_2670x1200.png` (mtime 2026-09-10 10:05), opened |
| Glyph oracle emit site + rule | `Assets/Editor/UICaptureLaunch.cs:6363`; `Assets/_Modules/Core/UI/LayoutOracle.cs:244-260` |
| Existing suite that knows this vocabulary | `Assets/Editor/Regression/ManageTroopsTrainDoorRegression.cs:666`, `:672`, `:682` |

## 9. UNPROVEN

- **Why the label ellipsised with 41 px of plate to spare is NOT established.** I measured the contradiction; I did not find the cause. Candidates I did not test: the label rect is narrower than the plate rect, `cellW` handed to the probe differs from the resolved cell (`BuildTile`'s own param doc warns *"PASSED, NOT READ BACK — the cell rect is 0 on the frame the tile is built"*), or bold metrics differ between probe and painted label. §4A exists precisely so the lane does not guess between them.
- I did **not** confirm which of the four silent early-returns fired — only that no logged branch did.
- I did **not** check whether the BUILD or RESEARCH grids carry the same missing-`BadgeWord` shape.
- Only 2670x1200 was measured, on device and in the capture.

## Device evidence (lead, 2026-09-10 11:37, APK 2026.09.10.363866)

`Builds/device-frames/2026-09-10_1137_363866_manage_army.png` + `_army_badge_crop.png`: Footman and Archer tiles read "UPGRADE" whole (363786 read "UPGRADE A..."). Instrument: `[Flow:Manage] state word font: early return [already-fits] - widest=UPGRADE wants 20px at 26px type and the plate offers 407px (cellW=565.87)` x2; the other three returns 0. Anomaly recorded: seven glyphs at 26 pt wanting 20 px is implausible - the probe may measure an unlaid rect (WO-1665). The WORD is still the owner ruling (15).

## OWNER RULING (2026-09-10 12:07, AskUserQuestion)

Badge word = UPGRADE (keep). UpgradeAffordableGridWordProvisional is now the ruled word - rename the const to drop "Provisional" in the next touch of the file; the collision with UPGRADING is accepted.
