# WORK ORDER 1697 - Skill-tree pip labels relax 30 -> 26 on the Seeker (render at 28 px, under the owner's floor)

**Status:** READY TO IMPLEMENT
**Minted:** 2026-09-10 15:35 by the CLI lead from the fit-guard allowlist oracle (main-line banner bumped 1697 -> 1698 in the same edit). NOT dispatched: the owner called a wrap-down at 15:30.
**Silo:** UI fit guard (FIT-GUARD family: WO-1495 allowlist shape, WO-1652 instrument, WO-1690 bands in px).
**Evidence (captured, not inferred):** `FitGuardRelaxAllowlistRegression` CASE D on `the session scratchpad (2026-09-10_1520_logcat.txt; moved out of Builds/device-frames so the allowlist oracle scans only leashed logs)` (device 2026-09-10 19:39:21Z, build 363866, owner session): eight NEW relaxations not on the allowlist, all of the shape
`'Viewport/GraphContent/Node_<id>/Pip/Label' floor 30 -> 26, renders at 28 px` for `Node_knight.s1n1`, `Node_knight.b1n1`, `Node_knight.t1n2`, `Node_shared.n9`, `Node_shared.n10`, `Node_shared.n11` (and repeats across two opens of the tree). Chain 48 log: `Builds/wave10g-reg1`.

## 1. What this is
The skill-tree node pips (the small count label on each node) are authored as a fraction of a pip that is too small on the Seeker's surface, so the fit guard relaxes the floor 30 -> 26 and the label renders at 28 px - 2 px under the owner's FontFloor. Same root shape as WO-1690 (a fraction that is right on a big host is wrong on a small one).

## 2. What to do (one lane, FIT-GUARD idiom)
- Find the pip label builder (grep `Pip/Label` / `GraphContent` under `Assets/_Modules/`), author the pip band in reference px through `ElarionUiKitObsidian.SeatHeaderBandInPixels` / `MinBandPxForFloor` (WO-1690 shape), never lower the floor.
- Until the seat lands, do NOT allowlist these keys - an allowlist entry hides a real 28 px label.
- Extend `TextFitGuardArmRegression` with a case that builds the tree node at the Seeker's reference extent and asserts the pip band >= `MinBandPxForFloor` (RED on HEAD).
- Device proof: a fresh Seeker logcat with zero `relaxKey=...Pip/Label` lines, plus a frame of the tree.

## 3. Do NOT touch
`FitGuardRelaxAllowlistRegression` entries (no leash), `ElarionUiKit.BuildConfirmModal` (WO-1690), any `.unity`.

## 4. Note for the lead
The chain-48 red on `[fitguard-relax-allowlist]` is THIS finding scanning the lead's ad-hoc logcat snapshot in `Builds/device-frames/`; the snapshot is moved to the session scratchpad after the arena lanes that grep it (WO-1694/1695/1696) hand back, and the finding survives here.
