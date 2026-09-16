# OVERNIGHT EXECUTION — owner in charge of this seat (2026-09-11 ~22:41)

Owner, verbatim: goodnight; in charge overnight; all executive decisions; express permission; do not wake to no resolution; iterate until castle-hub injector buildings are upright with image proof and the raid→owned-town flip is image-proven.

## Proven this evening (do not re-litigate)

- Seeker package `com.denellestudios.echoesofelarion` **versionName=2026.09.12.365962** lastUpdate 22:31:25.
- Tester define ON: FLAG chip on title (`proof/seeker-365962-title-flag.png`).
- Dev Tools bounce: logcat 22:37:37 and 22:38:07
  `DevPanel toggle/click reached (HelpMenu 'Dev tools' -> AdminOverlay)`
  then `DevPanel open BLOCKED — not authorised (release owner gate)`.
  Help closes first, so she lands on the HUD. **Fix in tree:** `AdminOverlay.SetOpen` skips the wallet gate under `#if !TESTER_BUILD`.
- White LightSkins: **gone** on 365962 vs 21:17 PNGs (textured mill/farm).
- Remaining player-felt: **inner courtyard standing water**, sunk/tilted frames, farm+lumbermill `NO ResourceCollector` every 10s in 365962 logcat.
- Phone at 22:40 was on the Android home screen (`proof/seeker-365962-now-20260911-224056.png`). Do not launch her save. Next APK install is allowed.

## Landed on Seeker (this night)

| Field | Proven |
|---|---|
| versionName | `2026.09.12.365996` |
| APK_OK | 23:01:33 444MB |
| R2 | catalog `2026.09.12.365996` `R2_PARITY_OK` objects=198 |
| install | streamed Success, dumpsys lastUpdate after 23:01 |
| `COMPILE_GATE_OK` | `Builds/compile-gate-overnight.log` 22:55 |

What 365996 contains vs 365962:
- Dev Tools: `AdminOverlay.SetOpen` no longer wallet-blocks `TESTER_BUILD` (logcat 22:37:37 was the bounce).
- Courtyard: runtime `Courtyard_PlazaFallback` 80×80 m grass at y=0.05 (merged scene had no `CourtyardFloor_*` tiles; flood was ExteriorTerrain).
- Hub farm/lumbermill: `AttachHubCollector` + grant-time `RetryAllAfterStateReady`.
- Hub skins: `SetBottomToGround` after seat.

**Morning check (do not Continue the flooded 365962 session):** force-stop the app, launch 365996, **Start New → Default Town**. Gear → Settings → Help → **Dev Tools** must stay open (365962 bounced). Skip kit: Lv15, MAX buildings, MAX troop types, Grant Iron Bastion town, Raid: Iron Bastion.

DataRegression overnight2: **511/513**. Remaining red: `raid-selection-spoils` catalog-empty in batchmode (not this night's files). `help-menu-entry tester-open` was a comment-string collision; fixed on disk after the APK (the `#if !TESTER_BUILD` gate IS in 365996). First-raid writer fail on overnight1 is gone (DevSkipKit no longer stamps `EverCompletedRaid`). `COMPILE_GATE_OK` 22:55.

F8 proofs of the old flood stay in `proof/seeker-365962-*`. New upright shots go beside them as `proof/seeker-365996-*`.

## Overnight done-shape (morning)

1. Tester APK newer than 365962 installed (or ready if phone gone), Dev Tools opens skip kit.
2. Hub courtyard grass, buildings upright — PNGs in `proof/`.
3. Capture flip (3-star Iron Bastion → OwnedTown FTUE) image proof, editor/UICapture acceptable if Seeker is unplugged.
4. `COMPILE_GATE_OK` + `REGRESSION_OK n/n` on a fresh log.
5. No push.

Do not retune raid HP/DPS. Do not mark WO-1705 DONE.
