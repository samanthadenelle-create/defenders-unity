# WO-1464: the in-raid troop tray is unreadable and three raid bands overlap the HUD beneath them

**Status:** IMPLEMENTED - c0c30f715 on HEAD, landed 2026-09-07 (was: uncommitted, awaiting gate); gated by the 2026-09-09 wave (REGRESSION 492/492 on Builds/wave1-reg3); owner felt-test closes
**Silo:** `Assets/_Modules/Village/Troops/RaidDeployController.cs` (in-raid tray + top band) and
`RaidDeployScreen`. Pairs with WO-1462 (same screen, different defect).
**Source:** read-only audit fleet 2026-09-06 (CLI seat), minted from the banner
(`CLI_LANES_WO_NUMBERS.md`, main line 1464 -> 1465 in the same edit).

## 1. EVIDENCE

Tray, at source:

```
RaidDeployController.cs:1141-1153   count badge drawn in ElarionUi.Ink on a dark plaque
```

Dark ink on a dark plate, no counts legible, and the raw slab covers the joystick.

`Logs/device/screens/owner-raid-ui-2026-09-06-143701.png`:

```
"2:23"        painted over  "Th... Lv 7"      (hero nameplate)
"0/3"         painted over  the compass
"Troops 10/10" painted over the progress bar
```

`Logs/device/screens/seeker-357453-raid-deploy.png`:

```
"Army: 8 / 10 slots"  painted over the Grom/Sylas row
"where i..."          the Echo quote truncated
```

## 2. FIX SHAPE

- Tray labels and count badges take kit foreground colours against the plaque, not `Ink`; run every label
  through `FitSingleLine`.
- Derive the raid top band's rect from `HudLayoutBands` so it cannot sit on top of the nameplate, compass or
  troop bar - one band authority, as the peaceful HUD already uses.
- Move the tray slab off the joystick rect.
- Deploy screen: army-slot line gets its own row; the Echo quote wraps or fits rather than truncating.

## 3. WHAT NOT TO DO
- Do not nudge coordinates by hand. A hardcoded offset re-breaks at the next aspect ratio; derive from the
  band model.

## 4. ACCEPTANCE
- [ ] A MEASURED overlap case covering the raid top band vs nameplate/compass/troop bar and the tray vs joystick.
- [ ] Fresh captures of the in-raid HUD and the deploy screen opened; no truncation, no overlap.
- [ ] `REGRESSION_OK n/n` on a fresh log.

---

## APPENDED 2026-09-06 - the DEPLOY-SCREEN half landed under WO-1519; the IN-RAID half is still READY

Status deliberately NOT flipped. This ticket covers two surfaces and only one of them moved.

**LANDED (in the WO-1519 lane, uncommitted, awaiting the gate) - the deploy screen:**
- *"`Army: 8 / 10 slots` painted over the Grom/Sylas row"* - fixed at the cause, not by a nudge. The
  army readout now owns its OWN band (`RaidDeployScreen` body 0.548-0.648, its own plate) instead of being
  a bare label sharing the party row's airspace, and the separation is MEASURED, not eyeballed:
  `RaidDeployLayoutRegression` case `[bands]` builds both stacks on a live canvas at four surfaces and
  reds on any overlap within a column. The word itself comes from the VM (`RaidDeployVM.ArmyBandText`,
  "ARMY 10 / 10 - FULL", WO-1517's grammar).
- *"`where i...` - the Echo quote truncated"* - the quote is GONE, not re-fitted. The whole ECHO GUIDE
  block left the deploy screen on the owner's 20:24 ruling (WO-1519 section 2B). A truncated string with
  no surface cannot truncate.
- A defect this ticket did NOT name but which the same lane found and fixed: the WO-1385/1403 band budget
  claimed every single-line row was ">= 36 px so the 30 px FontFloor seats". `NeedPx(30)` is 38.6, so
  those rows were one measurement from rendering BLANK (TMP Ellipsis culls the whole line, and the runtime
  relax guard does not run in the headless capture the acceptance PNG comes from). Every band is now >= 39
  ref px on the 411 ref px body, and case `[seat]` measures it.

**STILL READY - everything in `RaidDeployController.cs`:**
- the tray count badges drawn in `ElarionUi.Ink` on a dark plaque (`:1141-1153`);
- the raid TOP BAND overlapping the nameplate / compass / troop bar, and the fix shape's
  "derive the rect from `HudLayoutBands`";
- the tray slab sitting on the joystick rect;
- the MEASURED overlap case for those three.

Not touched here on purpose: `RaidDeployController.cs` is carrying another lane's uncommitted staging-area
work (WO-1520), and the WO-1519 lane was fenced off it. The in-raid half needs its own pass against a
fresh in-raid capture.
