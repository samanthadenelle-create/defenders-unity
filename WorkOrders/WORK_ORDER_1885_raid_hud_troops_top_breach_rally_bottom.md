# WO-1885 — Raid HUD: troops at the top; Breach + Rally bottom-middle

**Status:** IMPLEMENTED — gated 2026-09-19 `COMPILE_GATE_OK` (`Builds/cg-1885.log`) + `REGRESSION_OK` (`Builds/r-1885.log`). FIXED after tester APK.

**Owner, verbatim (2026-09-19):** "could we move the troops to the top of screen and out of way with a select and deploy/deploy all out of the main area and breach rally in bottom middle?"

Frame: raid combat view with troop chips + DEPLOY ALL + Breach + Rally as one bottom strip across the hero.

## Ruling
1. Troop tiles + Deploy All sit in **HudLayoutBands.RaidTroopTrayBand** — top of the screen, left of Retreat/readout. Out of the combat view.
2. **Breach** and **Rally** sit in the existing thumb-stack floor (**DeployBarBand** / command band), **bottom-middle** only (not full width). Ability row still owns the thumb (WO-1436 stays).
3. Retreat stays **RaidRetreatBand**.
4. Re-point RaidHudThumbBandRegression: command bar still above the ability row; troop tray is the top band and must not intersect Retreat/readout.

## Files
- `HudLayoutBands.cs` — `RaidTroopTrayBand` (authority, not a Village literal)
- `RaidDeployController.cs` — BuildHud split
- `RaidHudThumbBandRegression.cs` — re-point
