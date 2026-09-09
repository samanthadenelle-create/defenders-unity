# WORK ORDER 1597 - Manage hub: the HEART chip has no reason to be there unless an upgrade is due, a ghost CLOSE sits under the cards, and the three cards fill half the screen with empty wells

**Status:** CLOSED 2026-09-08 - owner felt-test PASS (validated 2026-09-09T02:10:48, build 2026.09.08.361259). PRIOR STATUS: FIXED - implemented in the 2026-09-07 gate wave (COMPILE_GATE_OK Builds/cg-wave9.log 10:40, REGRESSION_OK 446/446 Builds/reg-wave9.log 11:02); reaches the Seeker with the next tester build; owner felt-test closes it. PRIOR STATUS: READY TO IMPLEMENT - minted 2026-09-07 (CLI) from the owner's device and words
**Silo / Lane:** Village/UI Manage hub - `Assets/_Modules/Village/UI/Manage/ManageScreenPanel.cs` (BuildLauncher / hub cards / `BuildHubHeartDoor` ~:436-480, ~:2020-2045), `ManageScreenVM.cs` (`OpenHeartRequested`), `Assets/_Modules/Core/Manage/ManageArt.cs` (hub art keys :132-134), suite `ManageMockupConformanceRegression`
**Type:** EXISTING system, owner ruling on the last unmatched screen
**Priority:** P1 - "its the only one wrong" (the other eight Manage screens closed on her Pass this morning)

## Owner, verbatim (2026-09-07 10:2x, Seeker, build 2026.09.07.359405)

> "can you look at manage main screen? Its on phone now" / "its the only one wrong" /
> "and there is no reason to have heart on this set of manage screens unless for an upgrade"

## Evidence

Frame `Logs/device/seeker-shots/screen-20260907-1021-manage-hub.png` (2670x1200), read off it:

1. **HEART L3 chip** top-left of the body, a permanent face. Ruling: it belongs on this hub ONLY when a
   Heart upgrade is available, and then it IS the upgrade door ("UPGRADE HEART" with the cost), not a
   level badge. Otherwise it is absent and the body starts at the top.
2. **A ghost CLOSE** (dimmed, non-interactive plate) sits under the cards at ~y 0.85. The constant EXIT
   (X, top right, WO-1583 / 55d3a7c56) is the one way out; the dead plate reads as a broken button.
   Remove it.
3. **Three cards at ~0.55 of the well** with EMPTY art wells (dark plates) and the title under each.
   Mockup panel 1 shows three tall painted cards filling the band. The hub paintings
   (`hub-build`/`hub-army`/`hub-research`, `ManageArt.cs:132-134`) do not exist (art ask, WO-1567 s5).
   Until they land: the cards fill the band edge to edge (derive the card height from the band, not from
   a 145/160 aspect capped by width), and each well shows a STAND-IN from art that exists - the
   building portraits the BUILD grid already draws (`ManageArt.BuildingPortraitKey`): e.g. the lumber
   mill for BUILD, the barracks for ARMY, the library/research school for RESEARCH - chosen by the lane
   from what resolves, and named in the RESULT so the owner can swap them. Never a blank well.
4. QUEUE + X top right are correct. Title MANAGE is correct.

## What to do

- Instrument: `FlowTrace.Step("Manage", "hub layout: band=..., card=WxH, heartChip=shown/hidden why=..., closePlate=...")`.
- HEART chip: shown only when the Heart's upgrade is affordable-or-queueable (read the same predicate
  the Heart's own detail uses; one producer), labelled as the upgrade verb; hidden otherwise. The
  `OpenHeartRequested` door stays wired for that case.
- Delete the ghost CLOSE plate on the hub (the X is the exit, ruling WO-1583); keep the geometry the
  chrome row already reserves.
- Cards: fill the band; art well = stand-in portrait until the painting key resolves (the painting wins
  the moment it exists - key order: hub painting, then stand-in).
- `ManageMockupConformanceRegression`: hub case asserts no CLOSE plate, HEART chip absent without an
  upgrade and present with one, cards >= 0.9 of the band height, every well has a sprite.
- Headless capture of the hub (both chip states) - `RunCaptureHeadless` / the Manage flow map entry.

## Not to touch
- The other eight Manage screens (closed on her Pass 2026-09-07); the queue overlay; `HudKitController`.

## Acceptance
- Device frame beside mockup panel 1: no HEART chip (no upgrade due), no ghost CLOSE, three tall cards
  with art filling the band, titles and copy under them, QUEUE + X top right. Owner's match closes it.
