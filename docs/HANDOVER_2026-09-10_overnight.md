# OVERNIGHT HANDOVER - 2026-09-09 night -> 2026-09-10 morning (CLI lead, Fable)

**Status:** LIVE for the morning of 2026-09-10; read this before `docs/HANDOVER.md`'s top notes. Closed out 03:20 with one
lane still running (GLYPH-ORACLE, WO-1630); its result lands as its own commit when it reports.
**Branch:** `dev` (read it off `git status -sb`; the count ahead of origin is measured, never copied - the
owner did NOT authorise a push tonight).
**Owner's standing orders tonight, verbatim:** *"gate and commit (explicitly with my permission and
continue to work through till all the tickets are in fixed"* / *"At the end of the tickets getting to
fixed status run a exe prod build a apk prod build install to seeker and run a webgl build that goes
to vercel. DO not place at the same location as our site, this is only the Pi branch so needs to be
somehow determined"* / *"seeker is unlocked and connected. I want images to verify anything that is
a viewable issue"* / *"im off to bed"*.

---

## 1. What landed (every commit gated on the same fresh logs)

Gate evidence for wave 2 (the rows from `276d9fb11` down): `Builds/wave2-compile2` -> COMPILE_GATE_OK (01:26), `Builds/wave2-reg2` -> `REGRESSION_OK 493/493 suites` (01:31); the first wave-2 run (`Builds/wave2-reg1`) was RED 491/493 on two lane-owned items, both sent back to their lanes and fixed by them.

Gate evidence for wave 1: `Builds/wave1-compile4` -> `COMPILE_GATE_OK :: scripts compiled clean`
(2026-09-09 23:44, zero non-advisory `error CS`); `Builds/wave1-reg2` ->
`REGRESSION_OK 492/492 suites -- 492 green, 0 red, 0 skipped` (23:48); after the raid re-bake
`Builds/wave1-reg3` -> `REGRESSION_OK 492/492` (23:57). UI capture `Builds/wave1-capture` ->
`UI_CAPTURE_OK 91` with `UI_GEOMETRY_FAIL x3`, all three the SAME pre-existing item (the
"Already built? Manage defenses" footer link under the touch floor on Build Collections; present in
the 17:05 capture before this wave - a ticket, not a regression).

| Commit | Lane | Tickets | What |
|---|---|---|---|
| `184c8ff06` | lead | - | 5-minute orchestration cadence hook + "the lane owns the board flip" rule (CLAUDE.md s11) |
| `eb879496f` | lead | - | SubagentStop cadence unwired (it re-prompted the lane, not the lead) |
| `cd7510e17` | rail | 1094/1095/1461/1594/1373 | ten new RemoteTunables knobs, four sources, manifest 52 knobs no drift |
| `e9517c474` | SHOP | 1096 | preview loader branch by the row's `loadVia`, never a `blink_` prefix |
| `0c9cd5538` | LOCOMOTION | 1094 | measured playable bound; 50 m fallback on the rail; teleport guard spans the warp |
| `e1a49f5fb` | HERO-GRIP + NPC-SHIELD | 1431, 1616 | staff grip DERIVED (0.75) on the live path; troop shield seats through the hero authority |
| `20374504f` | RAID + RAID-2 | 1095, 1594, 1607, 1593 | watchdog measures ENGAGED time; honor stars on the rail; both raid win paths ruled canon |
| `63aaa8a6c` | SPOILS | 1461 | spoils above cap retained in the raid cache; repeat clear pays the ruled 60% |
| `bbd83b0d6` | RAID-3 | 1373 | rough stone: top two raid tiers, 1/day, one payout authority (Core seam); village path reads the cycle |
| `cc0c90e39` | CACHE | 1092 | abandoned cache transaction repaired; chunk by unique bundle; WebGL-guarded |
| `965051ab9` | MANAGE-DOOR + STORE-RETURN | 2007, 1412 | build card asks the same ownership question as the tile (the red `MANAGE_BUILD_DOOR_FAIL`); store return pinned |
| `5354a7238` | HARVEST-COPY | 1099 | over-cap result is truthful and leads with the spend recovery |
| `181df7f7d` | PLACE | 1615 | PLACE chip + raycast owner instrumented; NO fix until the capture names the surface |
| `86df2e4e4` | LOADER | 1097 | Component-typed request for a GameObject address refused and traced |
| `027fbe270` | META | 1377 | Jupiter swap surface compiled out under GOOGLE_PLAY; residuals accepted with reasons |
| `4f2698b86` | FIELDS | 1430 | `unlockMethod` and `requiresHero` now have production readers |
| `29b6180ec` | BALLISTA | 1617 | one siege-machine decider; spire never stands on edge |
| `63394d81e` | PINS + Silo 0B | 1090, 1091, 1093 | first oracles for the checkpoint-landed fixes |
| `98f76752e` | Silo 0A/0B + RULINGS + MINT | many | flips proven on HEAD; owner rulings recorded; WO-1618..1620 minted, banner 1621 |
| `b479bb860` | lead | - | 18 suite registrations; WO-1614 oracle re-pointed; `tools/gate_brace.py`; catalog deltas; board |
| `8bd81df2a` | hygiene | - | 633 texture metas: the importer added a default iOS block during the full reimport (no content change) |
| `8281042e7` | bake | 1617 | four raid bases re-baked (spire art + navmesh 4/4) |
| `bb12c728e` | lead | - | build version stamp 2026.09.10.363195 (ProjectSettings on its own) |
| `3c47ebbb0` | MINT-3 + PORTRAITS | 1621-1624, 1574 | four tickets minted from tonight's findings (banner 1625); WO-1574 BLOCKED on the owner's art (all nine troop PNGs still medallions at HEAD) |
| `b302fb3c5` | BOARD-STALE | 57 tickets | every `IMPLEMENTED - 2026-09-06 uncommitted, awaiting gate` status proven on HEAD against git (54 landed 09-07 in d6511b8e5/c0c30f715/bea5c8240/cd57a1c1e/55d3a7c56; 3 proved at source); 0 flipped back to READY; ledger `docs/reference/BOARD_STALE_AUDIT_2026-09-10.md` |
| `8b1635ffe` | F8-DAEMON | 1624 | FIXED: the desktop daemon's FileShare string cast had thrown on every pass since 2026-07-09 (Editor/Player classifier never ran); typed enum + reads/bytes counters; daemon restarted pid 47444 passFails=0. Follow-up: f8-check-inbox says OK while carrying pass-failed (not ticketed yet) |
| `e225ca57b` | RUNNER | 1622 | FIXED: run-unity-method's LOG_SCAN binds 'error CS' to the Assets/ path token; package advisories are counted + set aside with a NOTE; re-judged wave1-compile4 PASS / wave1-compile2 FAIL with -JudgeExistingLog |
| `cd731ebfd` | MINT-4 | 1625-1627 | three follow-ups minted (banner 1628); the briefed bow-preview divergence was DISPROVED at source (`EquipmentController.cs:1440` vs `:5234` identical ternary), so 1627 is docs + a CORRECTION on the 1620 RESULT |
| `21783ea5e` | INBOX-VERDICT | 1625 | FIXED: f8-check-inbox prints F8_DAEMON_DEGRADED when passFails>0 or the detail says pass-failed; selftest 39/39 |
| `276d9fb11` | RAID-CLOCK | 1618 | INSTRUMENTED: sampled `[Flow:Raid] clock tick` + seven edge traces in RaidScoring; no behaviour change; the pin's raw-text scanner caught two COMMENTS quoting assignment shapes (fixed in prose) |
| `08c76e250` | SEAT-PREVIEW | 1620 | Seating Editor shield preview routes through `TryDeriveShieldMountRotation`; body-transform divergence fixed; new suite `[seating-preview-shield]` registered (493 suites now) |
| `4f7bdfac6` | FOOTER | 1623 | Build Collections footer link band = MinTouchPx in reference px; LayoutOracle prints the host rect; visual proof = the fresh UI capture below |
| `8ed4c14ab` | SPIRE | 1619 | step 1: `ScaleToHeight` reports rawHeight/wanted/applied/saturation per spire; suite case added; step 2 waits on the bake below |
| `c836a036f` | lead | 1623 | FIXED on the fresh capture: `Builds/wave2-capture` -> UI_GEOMETRY_OK 91 (was x3 FAIL on the footer link); PNG sent to the owner |
| `406dbda07` | ORIENT-DOC | 1627 | FIXED: five source-cited rows in WEAPON_ARMOR_ORIENT_LOGIC; CORRECTION banners retract the 1620 bow-divergence claim; the cross-seam convention difference stays open, not acted on |
| `3110903f8` | FOOTER-FONT | 1626 | footer caption goes through `ElarionUiKit.FitSingleLine(label, 20f, 24f)` (was raw fontSizeMin=16f under the 20f floor); gate `Builds/wave2-compile3` + `Builds/wave2-reg3` 493/493; capture proof below |
| `6600ea739` | lead | 1626 | FIXED on `Builds/wave2-capture2` (UI_GEOMETRY_OK 91; caption complete, no ellipsis) |
| `b3f6c9187` | MINT-5 | 1628 | minted: every Build Collections card subtitle renders "nothing affordable y" at 2670x1200 + 2340x1080 (1920x1080 wraps correctly); VM emits the full string; the capture oracle has NO glyph-survival assert (a future ticket) - instrument first, lane SUBTITLE dispatched (banner 1629) |
| `f31ff7cac` | SPIRE-2 | 1619 | FIXED: `SpireFitFactorMin/Max` tunables replace the 8x clamp; suite case pins the relation; gate `wave2-compile4` + `wave2-reg4` 493/493 |
| `600f75745` | bake | 1619 | raid bases re-baked with the full-height spire (`Builds/wave2-bake2`: all three `achieved=14.40m (100% of target)`), navmesh 4/4 (`Builds/wave2-navbake2`) |
| `aff7f8daf` | DEVICE-PROFILE | 1459 | device capture section: town idle 22-23 fps, 94% of the frame outside every Measure scope, SKIN-throttled; no fix; status unchanged (instrument first) |
| `446c8b992` | SUBTITLE | 1628 | step 1: post-layout subtitle fit probe (21 lines on `Builds/wave2-capture3`); step 2 dispatched to the same lane on that data |
| `3ce5eb832` | SUBTITLE | 1628 | FIXED: card subtitle band = 50 reference px; all 21 probes 2 lines / 22 chars / untruncated on `Builds/wave2-capture4`; PNG sent to the owner |
| `285ac82ec` | FRAME-SPLIT | 1459 | INSTRUMENTED: `[Flow:Perf] frame split:` per roll-up window via FrameTimingManager; the `enableFrameTimingStats` switch is yours (ProjectSettings never rides a lane commit) |
| `5c5419513` | MINT-6 | 1629, 1630 | minted: the headless capture shows SEVEN Build Collections cards, the player sees EIGHT (the capture calls the single-arg Show, `UICaptureLaunch.cs:8965`; the game passes the callback), so WO-1628's 50 px band is unproven on the real grid - WO-1629 instruments the eight-card frame first (lane PLACED-CARD running); WO-1630 = the oracle's missing glyph-survival assert, sequenced after (same file) |
| `064f60ed6` + (docs) | PLACED-CARD | 1629 | step 1: the capture builds the eight-card grid (managePlaced callback + counted-only PlacedStructure stub); 24 probes on `Builds/wave2-capture5`: WO-1628's 50 px HOLDS on the narrower cards; the Manage Placed caption truncates at every aspect (32/16/18 of 45 chars, preferredHeight 95.9/71.8/71.8 px vs bands 52.2/43.2/42.1); step 2 dispatched; PNG sent to the owner |

Board after the wave: `python tools/board_build.py` -> `BOARD_CHECK_OK 0 unlabeled, 0 missing status
lines, 0 status contradictions`.

## 1B. Morning wave (owner awake 03:20-03:45, then back to bed for an hour; orders: "exe apk webgl")

| Commit | Lane | Tickets | What |
|---|---|---|---|
| `08e336f28` | lead | 1184 | owner testimony recorded ("i do get a report post battle offline"); then CLOSED on her ruling in `9281cd7ea` |
| `9281cd7ea` | RULINGS-AM | 1373, 1412, 1461, 1184 | 1373 FIXED as shipped (code ladder top two, global day cap), 1461 FIXED (cap 1800 kept; the settle wiring HAD landed at RaidVictoryController.cs:281), 1412 item 2 READY (USD-only), 1184 CLOSED |
| `b877e633c` | PLACED-CARD | 1629 (+1088) | FIXED: "manage what you built" in its own 50 px band, pin tightened to zero; gate wave3-compile1/reg1 493/493, capture wave3-capture1 24/24 probes; WO-1088 CLOSED on her word |
| `3da5e5360` | RULINGS-PM | 1099, 1574, 1430, 1629 | 1099 CLOSED, 1574 CLOSED (09-06 medallions are final; crop stays), 1430 fields 3-5 DROP (field map in the WO), Manage Placed art strip = intended |
| (in the raid chain) | LANDSCAPE + lead | 1631 | landscape only: ProjectSettings portrait autorotate flags 1 -> 0 (lead, on the ruling), ScreenOrientationRegression registered; gated green on wave3-reg2 |
| (in the raid chain) | STORE-LABEL | 1412 | item 2: the busy label already read `Buy builder - $9.99` from PackDef.UsdReference; the deliverable is the pin + the ruling in code; green on wave3-reg2 |
| (in the raid chain) | GLYPH-ORACLE | 1630 | fourth LayoutOracle kind, `UI_GLYPH_*` marker from all 14 ReportTouchOracle sites; first live run: 68 cut labels over 21 panels (NightMarket 21, RumorBoard 12+9, RealmWorkspace 11, HeroSelect 5); baseline + umbrella WO-1636 being built |
| (in the raid chain) | ARENA-WALL | 1632 | exterior boundary ring at the plane edge, SQUARE (the staging diagonal fallback parks at 79 m; no circle fits), battle-arena rock vocabulary via a shared `ArenaBoundaryRing` helper, siege venue re-routed byte-identical; Case 6 `[arena-boundary]` reds until the bake |
| (in the raid chain) | RAID-POLISH | 1633-1635 | premise DISPROVED: `props` IS authored and read (27/25/27, bake missing=0); the real gaps: props stripped colliders (decor not cover), even polar singles not clusters, no clear lane - fixed as WO-1633 with `CoverRingPlacer` extracted from the arena; 1634 authored prop gaps + 1635 stale canon minted READY |
| (in the raid chain) | SPIRE-ART | 1619 | no ruined-watchtower model exists anywhere; owner chose the KayKit tower BASE; new catalog row `tower_ruined_watchtower`, raider_camp_small centralBuilding re-pointed; a dresser defect found and fixed (`MapCatalogArt` would have re-skinned the spire back to the arcane art); needs CopyKitToResources + MarkCatalogArt + bake + r2-ship (remote Structure_Art) |

LANDED 05:33 (chain 18 fully green: `Builds/wave3-compile9` COMPILE_GATE_OK, `wave3-reg9` REGRESSION_OK 494/494, `wave3-capture9`
UI_CAPTURE_OK 91 + UI_GEOMETRY_OK 91 + UI_GLYPH_OK 91/91, `wave3-navcapture5` UI_GLYPH_OK 15/15):
`29296e086` landscape only (1631) | `7429ddfb7` store label USD-only (1412 FIXED) | `ac15fb88e` fields 3-5 dropped (1430 FIXED) |
`9e44ad076` glyph oracle + baseline + WO-1636 (1630 FIXED, 1636 PARTIAL) | `9c7855902` RumorBoard 21 | `d5ccb24ed` deck cards 16 |
`28b0719f2` HeroSelect 5 + Manage ARMY partial | `3c2302534` arena boundary ring (1632 FIXED) | `b10cd6783` courtyard cover props (1633 FIXED,
1634 READY, 1635 PARTIAL) | `16e7da66b` Forsaken Camp tower base + raid-only catalog row + fallback regen | `2e66a552e` the re-bake (4 scenes + navmesh).
LANDED 05:45 (chain 19 green: `wave3-compile10`, `wave3-reg10` 494/494, `wave3-capture10` UI_GLYPH_OK 91/91 baselined=4,
`wave3-navcapture6`): `e71359283` NightMarket 21 (v3: reference-width reads; v2 had collapsed the CTA at 800x360) | `02fb42ba6`
BuildMenu two-line info band. WO-1636 = 64 of 68 cleared; the 4 left are Manage ARMY x2 (copy ruling: "BUILD BARRACKS" fits),
EndStateWaveClear x1 (stale capture fixture), Realm DeckCard_The Night Market x1. GlyphBaseline shrink to 4 in flight (lane).
BUILDS DONE (morning): exe `Builds/build.log` DesktopBuild SUCCEEDED 2010 MB 05:47 (release, 234 DLLs, no DevTools, sha of
DeNelle.Village.dll 7683c6da2d17e84f...); APK `Builds/overnight-apk-status.txt` APK_OK 444MB 05:54 + R2_PARITY_OK objects=278;
Seeker install Success, device versionName=2026.09.10.363529 (stamp committed on its own). WebGL building from 05:55.
REGRESSION CAUGHT 05:58: the build chain rewrote ProjectSettings' portrait autorotate flags back to 1 - the writer is
`Assets/Editor/AndroidBuild.cs:327-329` (`PlayerSettings.allowedAutorotateToPortrait = true` inside the Seeker build setup),
so the APK 363529 on the Seeker has portrait ON despite the ruling; my version-stamp commit `fbae77297` carried the flip and
`a96bfe332` reverts it. Lane ORIENT-SCRIPT fixes the writer + pins it in ScreenOrientationRegression; a NEW APK follows.
Lesson for memory: a ruling edit to ProjectSettings is not durable while a build script writes PlayerSettings.
WEBGL SHIPPED 06:15: `[webgl] SUCCESS` 182.2 MB (06:14), legal pages staged (WEB_STAGE_OK), `vercel deploy --yes` preview
https://defenders-of-the-realm-v2-ib8yl8uq4.vercel.app, `tools/r2-ship.ps1` -> R2_PARITY_OK objects=279 with
`WebGL/catalog_2026.09.10.363529` hosted (run AFTER the deploy - order slip, harmless because the client fetches the catalog
from R2 at runtime), alias moved: https://defenders-pi.vercel.app -> the new deploy (HTTP 200, serves the new loader
6a7459032dfda91b9c9bf1ec181c00f4.loader.js). DEVICE FRAMES of a full Forsaken Camp raid on APK 363529 (all landscape) are
under Builds/device-frames/2026-09-10_06*; two sent to the owner; WO-1618's blocked capture landed (64 clock ticks, one
scorer, clock starts at engagement 105 s after load, tracks 1:1 to 180 s - the RAID-CLOCK lane rules it); six raid-polish
tickets (1637-1642) minting from the frames.
CHAIN 21 GREEN 06:38 (`wave4-compile2` COMPILE_GATE_OK, `wave4-bake2` props 33/31/27 with courtyard 25/22/19 - the WO-1634
gaps placed: firepit, spit roaster, haybales, torches on Easy; racks, broken wall, catapult + ballista props on Hard; banners +
torches moved into Extreme's courtyard - `wave4-navbake2` 4/4, `wave4-reg2` REGRESSION_OK 494/494 incl. CaseSinglePropAuthority,
CaseAuthoredPropGaps, ScreenOrientation case 5 on the build script, the BUILD BARRACKS pin). Chain 20 before it had caught my
SECOND silent apply drop (the prop-gaps Resources twin) - the lane's twins copied in whole. Chain 22 (compile + both captures)
proves the Manage copy and the 4-entry baseline; then commits, then the APK rebuild + reinstall.
PUSHED 06:48 on the owner's word ("push dev and reinstall the apk when it's done"): dev -> origin/dev at d8672e1f0 (after
`git lfs push --all` for the new FBX). The held wave (1634, 1635 code, baseline shrink, 1631 build-script writer, 1618 edge-4 +
ruling, BUILD BARRACKS) commits after chain 22, then the APK rebuild + Seeker reinstall, then a second push.
BUILD CHAIN launched 05:45 (`Builds/wave3-build-chain.txt` step stamps; runners `wave3-exe` / `wave3-apk` / `wave3-install` /
`wave3-webgl`): Windows release exe -> production APK (overnight-apk-build, r2-ship inside) -> Seeker install -> WebGL. After it:
r2-ship for WebGL, `vercel deploy --yes` preview, re-alias `defenders-pi.vercel.app`, then device frames of a raid to the owner.
NOT pushed since c10e4f5d1 (her "push dev now" was for that moment; 22 commits since wait for her word).
Still gating (chain 19): the NightMarket CTA seat v3 (21 findings; v2 collapsed the CTA at the narrow aspects) and the BuildMenu two-line
info band (1 finding). Then the three builds.
Raid wave evidence (chain 16, 04:45-05:03): `Builds/wave3-compile7` COMPILE_GATE_OK; `wave3-mark5` STRUCTURE_MARK_OK 36;
`wave3-bake7`: Forsaken Camp spire = `art='Structures/building_tower_base_green'` rawHeight 1.500 m at factor 9.600 (100% of
14.40 m); `ring 'Arena'` 82 pieces/side, 328 boundary pieces per base, containment inner 66.350 vs clamp 66.000 and outer
70.850 vs limit 71.200 (0.35 m slack both, required 0.30), no assert; props 27/25/27 with cover; `wave3-navbake7` 4/4;
`wave3-capture7` UI_CAPTURE_OK 91 + UI_GLYPH_OK 91/91 (baselined 47 after the RumorBoard fix); `wave3-navcapture3`
UI_GLYPH_OK 15/15. Regression `wave3-reg7` 490/494: the four reds are all the new catalog row being swept as PLAYER content
(zero cost, zero stats, no build-card art, stale generated fallback) - back with the SPIRE-ART lane to use the repo's
non-player-row convention + regenerate CatalogFallbackData.g.cs. Three earlier bakes (chains 12-15) failed on the ring's
containment assert (boulder scale, then 66.0==66.0, then 0.30<0.30 float) and on three of the spire lane's hunks that my
3-way applies silently dropped (importer row, catalog row, generator path) - recovered by copying the lane's files.
Chain running: `Builds/wave3-compile3` -> `wave3-kitcopy` -> `wave3-mark` -> `wave3-bake3` -> `wave3-navbake3` -> `wave3-reg3`.
Owner-ruled this morning (all recorded in the WOs and memory): Pi = `https://defenders-pi.vercel.app`; LANDSCAPE ONLY; push done; 1373 as shipped;
1412 USD-only; 1461 keep 1800; 1184/1088/1099/1574 closed; 1430 fields dropped; Forsaken spire = KayKit tower base; Manage Placed
art strip intended; WO-1629 = shorten the copy; raid arena: "exterior walls around entire arena" + "similar strategy as we used in
battle arena" + "i have mentioned it in testing that it feels incomplete and not polished".
Number collision this morning: three worktree lanes each read next-free 1631; resolved 1631 LANDSCAPE / 1632 ARENA / 1633-1635 POLISH,
banner set to 1636 by the lead; rule recorded in memory (the lead pre-assigns number blocks to parallel minting lanes).

Second raid wave COMMITTED (06:50, all on wave4-compile2 COMPILE_GATE_OK / wave4-reg2 REGRESSION_OK 494/494 / wave4-bake2 + wave4-navbake2 4/4 / wave4-capture3 UI_CAPTURE_OK 91 + UI_GEOMETRY_OK 91):
`b4613489d` WO-1634 prop gaps + WO-1635 code half (FIXED); `e1558e6a0` WO-1631 build-script portrait writer fixed + Case 5 lint (FIXED; the
APK rebuilt after it is the first landscape-only APK); `969b57186` WO-1618 ruled on the device capture + edge-4 ramp trace + GlyphBaseline 68 -> 4;
`fdbbba52a` re-bake (props 33 / 31 / 27); `f33451b11` board + handover. HELD out of every commit: `ManageScreenPanel.cs` +
`ManageApprovedLauncherRegression.cs` + WO-1406 (BUILD BARRACKS still draws 12 of 13 glyphs at 2340x1080 / 2670x1200, `wave4-capture3`
UI_GLYPH_FAIL x2 NEW, back with the MANAGE-COPY lane) and the TMP fallback font asset. Lanes in flight: RAID-HUD 1639, RAID-STAGING 1640,
HEART-COPY 1641, TOWN-CHROME 1642, MANAGE-COPY. APK rebuild + Seeker reinstall chain launched 06:50 (`Builds/wave4-apk-chain.txt`,
`Builds/overnight-apk-status.txt`, `Builds/wave4-install.runner.txt`) on the owner's "push dev and reinstall the apk when it's done".

## 2. Builds (appended as each marker lands)

- **Windows release exe:** `Builds/build.log` -> `[DesktopBuild] SUCCEEDED - 2009 MB` (2026-09-10 00:13).
  `Builds/Windows/DefendersOfTheRealm.exe`. Release proof: 234 managed assemblies, `DeNelle.DevTools.dll`
  ABSENT. `DeNelle.Village.dll` sha256 `220C5612290D8D2267FA6B29C605988EC75224EA3402DB2280E4BD09222CEE60`.
  Not distributed anywhere; `Echoes of Elarion_BurstDebugInformation_DoNotShip/` sits beside it and must
  not ship.
- **Android production APK:** `Builds/overnight-apk-status.txt` -> `APK_START 00:15`, `SCHEMA_PARITY_OK`,
  `APK_OK 00:21 path=Builds/Android/DefendersOfTheRealm.apk size=444MB`,
  `R2_PARITY_OK targets=Android,StandaloneWindows64,WebGL objects=276`, `APK_DONE`. Version
  `2026.09.10.363195` (stamp committed on its own). `Builds/r2-parity.log` names
  `Android/catalog_2026.09.10.363195.bin/.hash` (the APK) and `StandaloneWindows64/catalog_2026.09.09.362725`
  (the exe) as present on the CDN - the CLAUDE.md s16 proof for both artifacts. Scripting define
  `DAPP_STORE`, no `TESTER_BUILD` (production shape).
- **Android production APK, second build (landscape-only):** `Builds/overnight-apk-status.txt` -> `APK_START 06:50`,
  `SCHEMA_PARITY_OK`, `APK_OK 06:56 size=444MB`, `R2_PARITY_OK targets=Android,StandaloneWindows64,WebGL objects=279`, `APK_DONE`.
  Version `2026.09.10.363591`, built AFTER `e1558e6a0` (the portrait writer fix); `ProjectSettings.asset` diff after the chain
  = the two version lines only, `allowedAutorotateToPortrait: 0` / `...UpsideDown: 0` held. Seeker install
  `Builds/wave4-install.runner.txt` -> `Performing Streamed Install / Success` (06:57); `dumpsys` on `SM02G4061955851` reads
  `versionName=2026.09.10.363591`. Device frames of a raid on this build are the WO-1631 felt-test closer.
  Landscape proof on 363591 (07:00): `settings put system user_rotation 0` (portrait) then 1 and 3 with the accelerometer off,
  screencap each -> `Builds/device-frames/2026-09-10_0700_363591_title_{portrait_lock,rot1,rot3}.png`, all three 2670x1200
  (PNG IHDR read). Sent to the owner. dev pushed at `94dfd6481` (LFS 5000 objects first). F8 seq 4988 = the benign
  StructureAssets INIT-handle line from that launch, acked.
- **Seeker install:** `install-apk-to-seeker.ps1 -Build:$false -Install:$true` -> `Performing Streamed
  Install / Success` on `SM02G4061955851` (00:23). Device frames under `Builds/device-frames/` and sent
  to the owner as they were taken.
- **WebGL build:** `Builds/wave1-webgl.runner.txt` -> `[webgl] SUCCESS -> Builds/WebGL/index.html (build dir ~182 MB)`
  (01:03). Legal pages staged by `tools/web-ship.ps1 -StageOnly` -> `WEB_STAGE_OK files=privacy.html,terms.html,styles.css`.
  The build wrote a NEW WebGL catalog (`ServerData/WebGL/catalog_2026.09.10.363195`), newer than the 00:24 parity
  log, so `tools/r2-ship.ps1` was run again -> `R2_PARITY_OK targets=Android,StandaloneWindows64,WebGL objects=276`
  (01:06, `Builds/r2-parity.log`), with the WebGL catalog named as hosted.
- **WebGL -> Vercel PREVIEW (the Pi-only surface, NOT the site, NOT production):**
  `vercel deploy --yes` from the repo root against the repo-linked project (`defenders-of-the-realm-v2`,
  `VERCEL_PROJECT_ID`/`VERCEL_ORG_ID` set explicitly), log `Builds/webgl-preview-deploy.log`, `readyState: READY`,
  `target: null` (a preview, never promoted). **URL: https://defenders-of-the-realm-v2-amfcywh6x.vercel.app**
  Proven reachable WITHOUT sign-in at 01:08 (curl from this machine, no redirect): `/` 200 40802 B
  (`<title>Echoes of Elarion</title>`), `/validation-key.txt` 200 128 B, `/privacy` 200 14912 B,
  `/api/assetlinks` 200 571 B, `/Build/40f0cff0972be8be534c29ec4487f2c3.wasm.unityweb` 200 with
  `Content-Encoding: br` and 17150491 B. Deployment protection read live from Vercel: this project has SSO
  protection OFF (`defenders-webgl` has it ON for all non-custom domains), which is why this preview is public and
  the older "previews are SSO-gated" note did not apply here. The production domain of this project was NOT touched
  (no `--prod`, no alias). The marketing site project was not touched. WHAT IS NOT PROVEN: that the game boots in
  Pi Browser on the Seeker (owner ruling: WO-1484/1314 need exactly that session); only HTTP reachability and the
  payload headers were measured.
- **Device frames taken on the new build (all sent to the owner as they were captured):**
  `Builds/device-frames/2026-09-10_0028_title_363195.png` (Title, the PLAY INT... face ellipsised),
  `2026-09-10_0031_after_continue_363195.png` (town: hero portrait caption truncated, ATTACK REPORT
  overprints HELD in the top strip), `2026-09-10_0033_manage_363195.png` (Manage: QUEUE button
  overprints the last letter of the MANAGE title; the UPGRADE face reads "UPGRADE ..."). All three
  frames are PORTRAIT, and portrait IS a supported orientation: `ProjectSettings/ProjectSettings.asset`
  `defaultScreenOrientation: 4` (AutoRotation) with all four `allowedAutorotateTo*` flags set to 1
  (read 00:34), and the UI kit's CanvasScaler reference is 1080x1920 (`ElarionUiKit.cs:95`,
  `CombatTextLayer.cs:109`). So these are real layout tickets, not a wrong-orientation artefact.

## 3. Rulings that are YOURS (each one word), gathered tonight

1. **Pi surface for WebGL.** The web-ship registry has three game-facing surfaces: the repo-linked
   production project (the domain the APK's api points at), the marketing site (excluded by your order),
   and `defenders-webgl` which is BLOCKED on a project id only you hold. Preview deployments are SSO-gated
   and cannot open in Pi Browser. Tonight's WebGL build is deployed as a PREVIEW on the repo-linked
   project (never the site, never `--prod`). To give Pi a reachable URL: supply the `defenders-webgl`
   project id, or say "promote".
   **UPDATE 01:08:** the preview on the repo-linked project turned out to be PUBLIC (SSO protection is off on that
   project; measured, section 2), so Pi CAN open https://defenders-of-the-realm-v2-amfcywh6x.vercel.app as-is.
   It is a hash URL that changes on every deploy; if you want a STABLE Pi address, say which: an alias on this
   project (e.g. `defenders-pi.vercel.app`, one `vercel alias` command, reversible) or a `--prod` on
   `defenders-webgl` (its id is in the web-ship registry now, but its default domain is SSO-gated, so that one
   would ALSO need the protection changed).
   **RULED 2026-09-10 morning:** aliased to https://defenders-pi.vercel.app - MEASURED this morning,
   `curl -sS -o /dev/null -w "%{http_code}" -L https://defenders-pi.vercel.app` -> **200**, no SSO wall.
   Push of `dev` is AUTHORISED **and DONE** - proven, not assumed: `git reflog show origin/dev --date=iso`
   reads `c10e4f5d1 refs/remotes/origin/dev@{2026-09-10 03:23:47 -0500}: update by push`, and after
   `git fetch origin dev`, `git merge-base --is-ancestor` returns YES for all three of `bbd83b0d6`,
   `965051ab9`, `63aaa8a6c` against `origin/dev`. (A first read at ~03:20 showed `dev...origin/dev` =
   95/0 with none of them ancestors - that read PREDATED the 03:23 push. Recorded so nobody re-derives it.)
2. **WO-1373 rough stone:** are "top two tiers" the code ladder (mage_enclave + iron_bastion, shipped)
   or the difficulty labels (would add fortified_garrison)? Day cap global (shipped) or per camp?
   Star-to-polish-grade mapping (shipped: settled stars, documented default).
   **RULED 2026-09-10:** *"As shipped - code ladder top two (mage_enclave + iron_bastion), one stone per
   day GLOBAL across raids"*. Recorded in `WorkOrders/WORK_ORDER_1373_raid_rewards_and_rough_stone_chain.md`;
   Status flipped to FIXED. ⚠ The **star-to-polish-grade mapping** was NOT part of this ruling - the
   shipped default stands and that clause stays open.
3. **WO-1099:** your one line on the 13:06 frame I sent (stuck open vs nonsense numbers) closes it. **RULED 2026-09-10:** *"Close it"* - RULED: close (not reproducible / not enough context on the 13:06 frame); the over-cap copy fix landed `5354a7238` and stays. Recorded in `WorkOrders/WORK_ORDER_1099_harvest_result_over_cap_reads_zero_and_reassures_falsely.md`; Status flipped to CLOSED.
4. **WO-1412 item 2:** the busy-only label cannot show an honest SKR amount from the Village assembly
   (the quote lives in Wallet, which Village may not reference). USD-only, or a Core DTO for the quote?
   **RULED 2026-09-10:** *"USD only - the busy label shows the USD price; SKR only where Wallet already
   renders it"*. No Core DTO, no new asmdef reference. Recorded in
   `WorkOrders/WORK_ORDER_1412_store_close_ejects_the_player_from_manage.md`; Status back to READY TO IMPLEMENT.
5. **WO-1430 fields 3-5:** `levelCurve` (a curve is a balance design), `visibilityRule` (client string vs
   server object contract), `expiry_behavior` (the client is never told an item expired).
6. **WO-1461 cache cap:** 1800 per resource is a stated derivation, not your number.
   **RULED 2026-09-10:** *"Keep 1800 per resource"* - the cap stays a TUNABLE, already remote-tunable at
   `Assets/_Modules/Core/Ops/RemoteTunables.cs:411` (`RaidCacheCapPerResourceDefault = 1800`, key
   `raid.cacheCapPerResource`), so no code change. Recorded in
   `WorkOrders/WORK_ORDER_1461_three_star_raid_clear_banks_25_of_1800_wood.md`; Status flipped to FIXED.
7. **WO-1619 Forsaken Camp spire art:** the lane substituted the default arcane spire; it proposes a
   ruined watchtower or wooden keep for an orc scavenger camp. Your creative call.
9. **WO-1574 troop portraits (lane PORTRAITS, BLOCKED):** all nine `Assets/Resources/RpgUi/troop/troop-*.png`
   at HEAD are still 1254x1254 gilt medallions on transparency (byte-decoded 2026-09-10; newest commit on
   that folder is `32659c0f6`, 2026-09-06, the day before the WO). The WO named a folder that does not exist
   (`Assets/Resources/Portraits/Troops/`) - corrected in the WO; the key is built at `ManageScreenVM.cs:4610`
   as `"RpgUi/troop/" + IconId`. Please drop the nine rectangular paintings under the EXISTING filenames in
   `Assets/Resources/RpgUi/troop/`; was the 09-06 drop meant to be that delivery (it re-exported the medallions)?
   **RULED 2026-09-10:** *"The 09-06 drop WAS the delivery - the medallions are final; the detail-card crop
   workaround stays and the ticket closes"* - RULED: medallions are final, closed. No art is owed; the crop
   (`ManageWorkspacePanel.cs:1559-1561` `artFrac` / `SquarePortrait`) and its pin `[detail-art-crops-the-ring]`
   (`ManageMockupConformanceRegression.cs:1184`, `:1193`) are the shipped shape. Recorded in
   `WorkOrders/WORK_ORDER_1574_troop_portraits_carry_baked_gilt_ring_detail_card_crops_by_zone_shape.md`;
   Status flipped to CLOSED.
10. **Portrait town HUD (WO-1621 + tonight's three device frames):** the Seeker autorotates to portrait (all four
    flags on) and every frame I sent tonight is portrait with truncations. Is the TOWN HUD expected to be playable
    in portrait? YES = the WO-1621 portrait row goes in and its Case 3/4/13 reds become real tickets; NO = the
    autorotate flags are the defect and the fix is landscape-only. Until you answer, the portrait row is held out
    of the suite so the gate stays green.
    **RULED: landscape only.** (Owner, 2026-09-10 morning, via AskUserQuestion; answer = NO.) The
    autorotate flags ARE the defect. Ticketed as **WO-1631** -
    `WorkOrders/WORK_ORDER_1631_player_build_autorotates_to_portrait_game_is_landscape_only.md`, which
    carries the evidence (all nine of tonight's device frames decode 1200x2670 PORTRAIT from their PNG
    IHDR), the enum proof (`defaultScreenOrientation: 4` = `UnityEditor.UIOrientation.AutoRotation`,
    reflected out of the 6000.4.8f1 editor install), the one-line fix (`ProjectSettings.asset:63-64`
    portrait flags 1 -> 0, `:11` stays 4 and `:65-66` stay 1 so BOTH landscape directions survive), and
    a new pin `Assets/Editor/Regression/ScreenOrientationRegression.cs` (LANDSCAPE_ONLY_OK/_FAIL, RED at
    HEAD by construction). The WO-1621 portrait Aspects row therefore stays OUT permanently, and its
    Case 3/4/13 reds do NOT become tickets. WO-1621 is reduced to one question: does PLAY INTRO fit in
    LANDSCAPE on the device? A landscape device frame is the proof, and there is none yet.
11. **Manage Placed card art:** on the eight-card Build Collections frame (`Builds/ui-capture/BuildCollections_2670x1200.png`, 03:07) the eighth card's art is a wide thin strip, unlike the seven category icons. Intended, or a placeholder that needs an icon? (Its caption truncation is WO-1629, in flight.)
    **RULED 2026-09-10:** *"Intended"* - RULED: intended. The wide thin art strip on the Manage Placed card is the
    shipped shape; no icon is owed. Recorded in
    `WorkOrders/WORK_ORDER_1629_manage_placed_card_caption_is_still_a_fraction_and_has_never_been_rendered_in_any_capture.md`
    (Status unchanged: FIXED).
12. **WO-1629 layout ruling (Manage Placed caption, 45 chars):** measured on the eight-card frame it needs 72-96 px
    of band; the card's `.21f` caption ceiling allows 55-69 px, so NO band constant can fit it and the lane stopped
    (correctly) rather than ship a still-cut caption. Which neighbour yields? (a) shorten the copy (e.g. "Move,
    upgrade or sell what you built" is still 32 chars - it needs to be ~22 to match the others), (b) let that card's
    art strip give up ~30 px (its art is already a thin strip, see item 11), (c) three-line band with the title moved
    up. My recommendation: (a) + (b) together - a card caption that needs three lines is copy, not layout.
13. RULED: BUILD BARRACKS (lane MANAGE-COPY). **Manage ARMY card copy (WO-1636, 2 findings left):** "BUILD A BARRACKS" cannot fit its face at 2340x1080 / 2670x1200 -
    the words are pinned by WO-1406's launcher regression and the cell by HubCardAspect. "BUILD BARRACKS" measures ~293 px and
    fits. Approve that copy, or name another.
14. RULED: keep the cook fire. **Easy camp cook fire (WO-1634):** the lane authored Synty's camp firepit + spit roaster on the hexagon-green Forsaken Camp
    (WO-1607 section 4 names Synty tents/spikes as Easy extras; WO-1609 named only KayKit fallbacks). Keep, or drop to the
    spec's haybale + torch only (a two-row data edit, no test change).

15. **FYI, not a question unless you disagree:** the portrait flags the build script forced on came from WO-1255 (2026-09-08,
    "Play Console large-screen / foldable readiness"). Your landscape-only ruling now overrides it in AndroidBuild.cs with that
    rationale kept as prose; GooglePlayPackagingRegression (which had pinned the portrait writer) now pins landscape-only.

8. **WO-1215 / WO-1459:** both need a PLAYED device session (a shield-equip capture; a raid profile
   with controlled thermals). WO-1484 / WO-1314: a Pi Browser session ON the Seeker, per your ruling.

## 3B. Lanes still in flight at the time of writing (patches held in the session scratchpad until the WebGL build ends)

- **WO-1618 RAID-CLOCK:** trace only, `RaidScoring.cs` (8 tags, `clock tick:` sampled every 5 unscaled s, per-instance
  throttle). Next raid capture must show `avgScaleWin` + `holds` before any behavioural edit. WO §3 line numbers were stale
  (Update is at :1306); candidate 2 (`_engaged=false`) is unreachable on this tree - only a second instance explains it.
- **WO-1620 SEAT-PREVIEW:** `EquipmentController.TryDeriveShieldMountRotation` (:2295) is now the one derivation both the
  attach authority and the Seating Editor preview call; a fourth divergence (preview passed `_animator.transform` as body,
  attach passes `transform`) fixed on the shield branch, the BOW preview still passes `_animator.transform` (flagged, not
  ticketed). New suite `SeatingPreviewShieldRegression` (`DeNelle.Editor.Regression`, tag `[seating-preview-shield]`).
  NOT proven: the shipped-rig delta (WO §5 step 1) - no Unity run; the fixture has no Animator. Owner: a previously
  dialled shield delta may now RENDER differently in the editor; the game path is untouched, nothing saved was migrated.
  `docs/WEAPON_ARMOR_ORIENT_LOGIC.md` needs the preview row (second flag on that table).
- **WO-1621 TITLE-CAPTION (patch HELD, not applied):** the trace is written (`TitleController.TraceActionRowFit`,
  two frames after build, one `[Flow:Onboarding] WO-1621` line per face/label) plus `HudLabelFitRegression`
  Case16/17 and a PORTRAIT row `1200x2670` in `Aspects`. The lane predicts that row REDs the existing town-HUD
  cases 3 (`manage-face`) and 4 (`wave-band`) at portrait, which would block every commit behind the gate. So the
  patch (`scratchpad wo1621.patch`, 610 lines, applies cleanly) is HELD for the owner's ruling in section 3
  item 10; the candidate cause it located is two resolvers fitting the same button (`MedievalUiSkin.ApplyButton`
  30..44 at FontRole.Title with spacing 2 vs `TitleController.cs:328` 24..34). Ruling 1 (asmdef) was declined by
  the lane - a typed read cannot reach method locals; the asmdef is untouched.
- **WO-1619 step 1 BAKED (`Builds/wave2-bake`, 01:35):** all three configs (`raider_camp_small`, `fortified_garrison`,
  `mage_enclave`) print `rawHeight=1.002m prefabScaleBefore=0.010 target=14.40m wantedFactor=14.366 appliedFactor=8.000
  saturatedAt=UPPER achieved=8.02m (56% of target) - SATURATED`. Cap axis confirmed by data; step 2 dispatched (lane
  SPIRE-2). The bake rewrote the three RaidBase scenes with no content change intended - reverted after the run, not
  committed; the post-fix re-bake is the one that ships.
- **WO-1619 step 2 (lane SPIRE-2) - LANDED, see section 1 (`f31ff7cac` + `600f75745`):** the clamp reads `SpireFitFactorMin` (0.2, unmoved)
  / `SpireFitFactorMax` (24; data needs >= 18, the suite pins `SpireFitFactorMax >= SpireMonumentMaxHeight`, not the
  number); `rawHeight` is a world-space AABB so the 0.010 pre-scale never enters the factor. Expected post-fix bake
  line: `appliedFactor=14.366 saturatedAt=none achieved=14.40m (100% of target)`. Felt-test note: the spire roughly
  doubles in height, and with it the fallback capsule hitbox and collapse sink (`RaidSpire.cs:200-213`, `:297`).
- **WO-1628 step 1 (lane SUBTITLE, applied, in the gate + capture chain):** the card-subtitle trace moved post-layout
  (`ReportSubtitleFit`, one `[Flow:Build] collection=... subtitle=... bandPx= cardPx= gridPx= fontSize= rendered=<lines>,
  <chars> sourceLen= truncated= preferredHeightPx=` line per card per aspect). Read it with
  `tr -d ' ' < Builds/wave2-capture3 | grep -a "Flow:Build" | grep -a subtitle` (must be 21 lines; 42 = the in-loop
  twin came back; `0x0` = measured before layout). The WO's `## INSTRUMENTED` section says which step-2 branch each
  number pattern licenses (`characterCount < sourceLen` + `truncated=True` is the cut).
- **WO-1459 DEVICE CAPTURE (committed `aff7f8daf`, read-only lane on the Seeker, build 363195):** Title 60 fps /
  16.6 ms; town idle with 0 enemies + 0 towers 22-23 fps / 43.9 ms (SurfaceFlinger agrees, 22.74). The perf table
  survived in full - `adb logcat -g` says 16 MiB per buffer on this device, so the 256 KiB ring assumption is wrong
  HERE (memory corrected). `HeroLocomotion.Update` is 64% of the INSTRUMENTED cost (37.3 ms/s), but all 20 scopes sum
  to 5.8% of wall clock: ~94% of every frame is outside every Measure scope, so the capture eliminates the
  instrumented sites rather than naming the cause. Thermal: SKIN 44 C at status 3 SEVERE the whole time (thresholds
  39/40/41/56/58/80), USB-powered; the absolute ms may be throttle-inflated, the ranking is not. Next MEASUREMENT
  dispatched (lane FRAME-SPLIT): CPU-main / render-thread / GPU split via FrameTimingManager folded into the
  `[Flow:Perf]` roll-up; it needs the NEXT APK to read. Six frames + full logcat under `Builds/device-frames/`.
  The raid case (13 enemies, the WO's own evidence) is still owed a capture.
- **WO-1459 FRAME-SPLIT - LANDED (section 1), gate `wave2-compile7` + `wave2-reg7` 493/493:** `PerfReporter` samples
  `FrameTimingManager` every frame and emits ONE `[Flow:Perf] frame split: cpuFrame/cpuMain/cpuRender/gpu/presentWait`
  line per 1 s roll-up window (unavailable state printed ONCE per session via FlowTrace.Once). Pinned in
  `FrameBudgetMeasureRegression` `[split]`. **Your one-line switch for the next APK:** `ProjectSettings.asset:157`
  `enableFrameTimingStats: 0` -> 1 (ProjectSettings never rides a lane commit; whether a RELEASE player needs it is
  unproven - the first capture settles it: numbers, or exactly one `unavailable` line). Routing rule is in the WO.
- **WO-1628 step 2 (lane SUBTITLE) - LANDED `3ce5eb832`, FIXED on the capture:** the data licensed branch 1 - the band
  seats one line at the two wide aspects (bandPx h 43.2 / 42.1 < preferredHeight 47.6; 52.2 at 1920x1080 passes).
  Band now `CaptionBandPx = 50` reference px hung from its own top edge (`CaptionTopFrac`), WO-1623's shape; pin in
  BuildCollectionPlayerRegression. Next capture must show 21 lines `truncated=False`, `rendered=2 lines, 22 chars`,
  `fontSize=21`; the lane warns the caption rect bottom sits 0.5 px above the bezel bar at 2670x1200 - the PNG judges.
- **WO-1629 step 1 (lane PLACED-CARD, applied, in the gate + capture chain `wave2-capture5`):** the capture now passes
  the managePlaced callback AND a counted-only `PlacedStructure` stub (the card also refuses when nothing is
  selectable, `BuildCollectionBrowser.cs:470-471`, a second gate the WO missed - lead accepted the stub, it is
  destroyed in the capture's finally). The Manage Placed caption is probed with the same fields. Expect 24 probe
  lines (8 cards x 3 aspects); the WO's INSTRUMENTED section says which numbers license step 2 and when WO-1628's
  50 px re-opens (any `fontSize=20` or `preferredHeightPx > 50` on the narrower eight-card grid).
  Both follow-ups are now WO-1629 / WO-1630 (`5c5419513`). ⚠ WO-1629's mint found the capture renders SEVEN cards
  where the player sees EIGHT, so the 1628 fix is proven only on the capture's grid; the eight-card probe is in flight.
- **WO-1622 / WO-1624 / WO-1623 / WO-1625 / WO-1626 / WO-1627 / WO-1619:** FIXED and committed (section 1).
- **BOARD-STALE audit:** every `IMPLEMENTED - 2026-09-06 uncommitted, awaiting gate` status is being proven against git
  (the tree is clean, so "uncommitted" is stale for all of them); never-landed ones flip back to READY; ledger at
  `docs/reference/BOARD_STALE_AUDIT_2026-09-10.md`.
- **Worktree lanes start on a STALE base** (`f5d39acd1`, the 09-07 release): every lane must `git merge --ff-only
  refs/heads/dev` before reading. Two lanes caught it themselves; the instruction is now in every lane brief.

## 4. Findings the next seat must not re-derive

- The compile gate's brace scanner has no interpolated-string model; `tools/gate_brace.py` is its port
  (CLAUDE.md s1 now says so). Two lanes hit it in one night.
- Unity gates run DETACHED (`Start-Process`); a harness background task hosting a 70-minute reimport
  was killed for "low memory" with 14.5 GB free. `-LogName foo` writes `Builds/foo` with no extension.
- `run-unity-method.ps1`'s `VERDICT=FAIL reason=LOG_SCAN` fires on the Solana package's WebGL
  advisories even when the gate marker is present and non-advisory errors are zero (runs 3 and 4
  tonight). Judge by the marker + `error CS` count, as canon says; ticket the runner.
- The UserPromptSubmit lane-check hook counted a CLOSED ticket as READY because it matched a status
  line quoted in the ticket body; it now reads the first `**Status:**` line per file.
- `WorkOrders/WO_1089_1094_WORKING_TREE_NOTE.md`'s "half-written" claim was DISPROVED by a full diff
  read of all eight files (Silo 0B); `OfflineContentService.cs` carried two `using` lines only.
- The runner's `REGRESSION MARKER` lint counts ANY file carrying a marker string as an emitter - a
  tool's docstring quoting `COMPILE_GATE_OK` went red. Never spell a marker in prose inside the repo.
- The "None" troop-gear sentinel (WO-1616 RESULT item 9) does not exist in `troops.json`; corrected in
  that RESULT, not minted.
- Six lanes could not name whether `PostToolUse` hooks fire inside subagents; measured evidence only
  covered `SubagentStop` (it does, and it re-prompts the lane). Not proven either way for `PostToolUse`.

## 5. Not on the device / not proven

- Nothing in this wave is felt-tested. Every FIXED row is gated, not judged.
- WO-1615 is INSTRUMENTED, not fixed: the next tower-move capture names the surface that eats the tap.
- WO-1618 (frozen raid clock) is minted with the discriminating trace; nothing changed in the clock.
- The raid-base felt-test (1607-1611 + 1617) needs the APK below; the Forsaken Camp now carries the
  default spire art at 8.0 m (WO-1619 says why 8.0).

## 6. Resume steps for the next seat

1. `git status -sb`; the tree should show only `ProjectSettings.asset` (build stamp) and
   `Assets/Resources/Localization/Fonts/ElarionLocaleFallback.asset` (a TMP material ratio Unity
   rewrote before this session; left alone, not mine).
2. Read section 2 for the build markers and section 3 for the rulings; ask the owner the eight
   questions in one AskUserQuestion pass.
3. Next wave: WO-1619 (spire height) and WO-1620 (Seating Editor shield preview) are READY and were
   held only because the builds were running; WO-1618 is READY (instrument first).
4. Every visual ticket gets a device frame sent to the owner (`adb screencap`), not a headless PNG.
