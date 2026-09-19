# WO-1882 — Chat is a first-class HUD door (not buried in Settings / gear)

**Status:** READY TO IMPLEMENT — minted 2026-09-19 (banner bumped 1882 -> 1884 in the same edit). Felt: owner could not find chat except via Settings/gear.

**Owner, verbatim (2026-09-19):** "chat needs to come out of the settings and have its own home somewhere on the UI, where we can open and work with the modals but can minimize or chat game pauses while open or thy can choose in settings if game runs actively maybe?"

## What is true now (read 2026-09-19)
Chat is a **gear-dock row** (`HudKitController` `AddDockTab` `common.remnant_chat` → `OpenClanChat`), gated on `ClanFeatureGate.PlayerFacingEnabled`, sitting with Settings / Music / Realm / Leaderboard. That is "in Settings" as the player feels it: open the gear drawer, hunt for Remnant Chat.

WO-1873 (two rooms: Global + Circle) is a **different** ticket. This ticket is the **door + pause**. Do not implement 1873's room selector in this lane unless 1873 is already green. Do not rewrite Cherry.

## Rulings
1. **Own home on the HUD.** A dedicated face the player can tap without opening Settings or the gear drawer. Peaceful-dock face **or** a persistent chip. Not a seventh buried gear row. Do not add a second Chat door (retire the gear-dock Chat row when the new door exists). `ActionBarButtonId` ordinals stay; do not renumber. HudActionBarRegression `CheckMeasuredPeacefulDock` is the dock authority if you add a dock face.
2. **Minimize, do not only Close.** Chat can hide to a chip/badge and restore. Closing is still allowed.
3. **Default while open: the world pauses** (`timeScale` 0, same family as other full plates). Settings gets one toggle: world keeps running while chat is open. Default OFF (pause). Locale keys in all 10 catalogs, dual-copy.
4. Other HUD modals: chat may sit as a panel the player can minimize to keep using town UI. Do not freeze the entire HUD behind an unskippable exclusive if minimize exists.
5. Play builds: if ClanFeatureGate is off, no door (existing gate).

## Files (one HUD lane)
- `Assets/_Modules/HUD/Kit/HudKitController.cs` (retire gear Chat row; add the public door)
- `Assets/_Modules/HUD/ClanChatPanel.cs` (minimize + pause)
- Settings panel (the run-while-open toggle)
- `HudActionBarRegression` / dock oracle if a dock face is added
- Locale catalogs (10 locales, both dirs)

## Not in scope
Cherry embed rewrite, WO-1873 room split, voice, Circle SIGN IN, Jupiter.

## Acceptance
`COMPILE_GATE_OK` + `REGRESSION_OK n/n`. Felt: from castle hub, open Chat without touching Settings. Minimize. World is paused unless the Settings toggle is on. Gear drawer no longer has a Chat row.
