# READY silos - dispatch table, 2026-09-09 (21:04 CDT)

Read-only dispatcher pass per `docs/TICKET_PIPELINE.md` + `.claude/hooks/ORCHESTRATION_CADENCE.md`,
run by an Opus lane and written to disk by the lead. Every fact below was read from a WO file, the
tree, a git command, or a fresh log this session. No Status line was changed by the pass itself, no
code was edited, no gate was run. Aged rule: this file is a point-in-time reading; verify any row older
than 7 days at source.

---

## 0. Header - the facts that change the table

- **HEAD at the time of the pass = `184c8ff06`** (the cadence-hook commit); the order named `e9ac5279a` = HEAD~1.
- **Board Ready count = 42** (`python tools/board_build.py` -> `BOARD_CHECK_OK 0 unlabeled, 0 missing status lines, 0 status contradictions`; 42 `data-bucket="Ready"` rows; ids match `docs/READY_RCA_2026-09-09.md` exactly). The UserPromptSubmit hook's smaller count greps `WORK_ORDER_*.md` only and over-matches "READY" inside CLOSED lines; the board is the authority.
- **Gate state: RED.** `Builds/ready-rca-checkpoint-regression.log` (2026-09-09 14:26:33): `REGRESSION_FAIL: 2 failure(s) (472/474 registered suites green, 0 skipped)` - `[hero-element-cast]` and `MANAGE_BUILD_DOOR_FAIL` (WO-2007). The log postdates `6a5c7a36d` (14:17:30) by 9 minutes, so 472/474 is a verdict on the checkpoint tree.

### 0a. `[hero-element-cast]` is a STALE ORACLE LITERAL, not a runtime defect
`HeroElementCastVfxRegression.cs:68` searched the literal `PlayCastVfxKey(def, origin, castVariant);`; HEAD reads `animVariant` at `HeroAbilities.cs:1127` (renamed by `f4e4630e3`, WO-1614). Runtime order intact: `SpellVfxFactory.PlayCast :1126` -> `PlayCastVfxKey :1127` -> `ResolveEffect :1138`. Re-pointed by the lead in the working tree under a WO-1614 comment (sanctioned oracle re-point WITH a ruling). No lane.

### 0b. `MANAGE_BUILD_DOOR_FAIL` is WO-2007 and its lane landed
`ManageScreenVM.cs` ~:5296: the detail card now asks the same ownership question as the tile (`IsPlacedThisTown`) - deliberately NOT `UpgradeFamilyResolver`, which answers "which ladder", not "is it placed". Lead concurs.

### 0c. The "8 UI-seat files" constraint has MOVED - all eight are on HEAD
`git log -1 -- <each>` returns `6a5c7a36d chore: checkpoint complete workspace and rebuild board` for all eight files in `WorkOrders/WO_1089_1094_WORKING_TREE_NOTE.md`. That is an ungated bundle commit (memory `other-seats-commit-ungated`). Mitigating evidence, not absolution: the 14:26 gate postdates it at 472/474 and neither failure is in those files. Silo 0B read all five diffs in full: none was half-written. `OfflineContentService.cs` carried only two dead `using` lines (a killed-mid-edit remnant) - the CACHE lane reconciles it.

### 0d. Flipped in the working tree during this session
1090, 1091, 1093, 1097 (item 4 unmet - follow-up), 1098, 1367, 1607, 1599, 1593, 1481, 1450 (Silo 0A/0B); 1094, 1096, 2007 (lanes). 1412 held PARTIAL -> lane STORE-RETURN. 1414 must flip in the same batch (its C/D mechanism IS the 1090 implementation, on HEAD via `6a5c7a36d`).

---

## SILO 0 - IMPLEMENTED-UNFLIPPED (done by Silo 0A/0B this session; kept as the record)

| WO | Proving sha | Verification at HEAD | Note |
|---|---|---|---|
| 1607 | `6a5c7a36d` | `DataRegression.cs:741` registers `raid-base-layout`; green in the 14:26 log | acceptance 6 DEVIATED: `RaidBaseLayoutRegression.CaseGarrisonWipeWins` pins garrison wipe as a win path vs 1607 s8 "raze RaidSpire" - **owner ruling needed** |
| 1593 | `6a5c7a36d` | superseded by the 1608-1611 spine | do not re-bake |
| 1599 | `b10038556` | `api/admin/console.js:1040-1050`, `:1552`; `node --test test/admin.sku.dropdown.test.js` 9/9 | deployment not proven |
| 1481 | `d6511b8e5` | CLAUDE.md s8 is a pointer table | residual: `CLAUDE.md:369` still carries two live numbers |
| 1450 | `d6511b8e5` | `Enemy.cs:262,:281,:287,:1893,:1918` | device cadence read closes |
| 1412 | `3c677027e` | `PackStore.cs:1303-1310` | PARTIAL: no `StoreReturnToManageRegression`; busy label lacks SKR + $ -> lane STORE-RETURN |
| 1090/1091/1093 | `6a5c7a36d` | Silo 0B full-diff read | no regression pins existed -> lane PINS |
| 1097/1098 | `f4e4630e3` | `[apex-dragon-spawn]` green; modal hold is `WorldHold.cs:469-472` scale 0 | 1098 evidence must NOT close WO-1297 |
| 1367 | `c417d7997` | texture pass on HEAD | size figures are 09-04; re-measure before Play upload |

---

## SILO 1 - RAID LOOP ECONOMY (highest owner-felt leverage; `KEY_FACTS.md:468,:474`)

### Lane 1A - WO-1461 (running as lane SPOILS)
`RaidClaimService.cs:78` `RepeatClearLootMultiplier = 0.25f` vs the owner's "60% repeat clear"; `grep -rni "raidcache|RaidCacheService"` = 0 hits. Evidence: `troop-ai-blind-2026-09-06.log 14:37:40.333` `BANK FULL [Grant] Wood: requested 450, banked 25, LOST 425`. Files: `RaidClaimService.cs`, `RaidLootTunables.cs`, `RaidSelectionVM.cs`, `RaidSelectionScreen.cs`, a new RaidCache store, a new suite. Pins: never raise the bank cap; the claim door is a spec + PanelId hand-back (PanelDoorRegression is shared with 3B).

### Lane 1B - WO-1594 + WO-1095 as ONE lane (running as lane RAID)
`git merge-base --is-ancestor 5c3c82de2 HEAD` -> exit 1; `git branch -a --contains 5c3c82de2` -> `grok/raid-1593-1595` only; only a comment at `RaidScoring.cs:528` mentions `ComputeHonorStars`. WO-1594 Q1 has a stated default (T3=90s / T2=150s / D2=50%) - the lane runs on it and the RESULT flags it. WO-1095: `RaidDeployController.cs:342,:355,:397,:400` measures scene age while `RaidScoring.Update` excludes staging; ledger addendum delta 224.977 s vs the 180+45 bound with 2:46 on the clock. Files: `RaidScoring.cs`, `RaidHudController.cs`, `RaidDeployController.cs`, `TroopController.cs`, `RaidGarrisonSpawner.cs`, `RaidVictoryController.cs`, `RaidScoringRegression.cs`.

## SILO 2 - CONTENT DELIVERY
### Lane 2A - WO-1092 (running as lane CACHE)
Abandoned cache transaction on `enemy_models_assets_enemyfam-orc_2220522384eb58b0db363f6c6e1b47ab.bundle` (19,398,472 bytes, CRC 4262033540; current-hash dir holds only a zero-byte `__lock`; `19,151,184 + 3 x 19,398,472 = 77,346,600`). Files: `OfflineContentService.cs`, `AssetBundleProvider.cs`. Never touch `tools/r2-ship.ps1`.

## SILO 3 - HYGIENE
### Lane 3A - WO-1431 (running as lane HERO-GRIP)
`TryDeriveStaffGripY` computes 0.75 (gripY 0.7204) but only via `TraceMeasuredSeat`; the live melee path calls `SeatHiltLowerHalf` (0.18). Files: `WeaponOrientHelper.cs`, `EquipmentController.cs`.
### Lane 3B - WO-1430 Group B (SEQUENCED AFTER 1A; shares `PanelDoorRegression.cs`)
`AuthoredFieldReaderRegression.cs:117-121` still exempts `unlockMethod`, `levelCurve`, `requiresHero`, `visibilityRule`, `expiry_behavior`. Files: that suite + `CosmeticCatalog.cs`, `EchoBalanceCatalog.cs`, `DailyQuests.cs`, `CardCollectionCatalog.cs`.

### Disjointness
Pairwise-disjoint as listed. `VillageSceneBuilder.cs`: no lane. `DataRegression.cs`: lead-owned; every lane hands back its suite and the lead registers it.

---

## PARKED

### (C) BLOCKED-ON-OWNER - one word each
| WO | The one word |
|---|---|
| 1373 | "A" / "B" / "C" on rough-stone exclusivity (highest-leverage parked item) |
| 1377 | "type-name OK" / "move it"; plus a physical AAB scan |
| 1099 | what "here" meant (seq 4974): stuck open vs numbers nonsense - presentation half runs as lane HARVEST-COPY |
| 1244 | her missing-surface list from the 09-03 Fail (ledger's "ADMIN_OPS_KEY unset" is STALE: set + proven live 09-06, `GET_WELL_PLAN` s7.2) |
| 1446 | one command: `tools/run-schema-repair.mjs` (do not deploy `api/` before) |
| 1607 | garrison-wipe-as-win vs raze-the-spire (see SILO 0) |
| 1088 | external linguistic review; never a code lane |
| 1566 | a SPEC by its own Status; never dispatch |
| 1567 | the CLI's own gate/capture/ship step; runs after the gate is green |
| 1484 | RETRACTED by a controlled 16-min run (flat 431 MB); PO closes as not-reproduced |
| 1314 | root retracted in its own Status; Pi parked; PO closes |
| 1348 | hold is STALE (1343-1347 landed); still new architecture (`realm.vfx` path absent), P3 |

### (D) PARTIAL - Manage device-frame tickets (owner judges the frame, memory `manage-screens-pass-rule-owner-device-frames`)
2016, 2015, 2014 (five queue rows required, four fit), 2012, 2010, 2009, 1574 (named directory gone), 1291 (3 of 33 addresses unmapped; never bulk-purge). One capture session on a green-gate build unparks all of them (`GET_WELL_PLAN` s2).

### (E) UNPROVEN
| WO | Why | Proof needed |
|---|---|---|
| 1215 | original fix `3eb499b88` IS on HEAD (pinned at `AttachmentOffsetRegression.cs:654-661`) but the owner bounced Fail 09-03 with no hero/item/image/log | one device capture: hero + shield id + `AttachOffHandProp MEASURED` + `registryProbe path=START` + screenshot; never re-dial `shield_A` by guess |
| 1459 | CONFOUNDED: 4,798 logcat lines in the 3 s window, a 2,038.4 ms hitch, SEVERE thermal | a release-like raid profile with controlled thermals on a build carrying WO-1450; no edit before the 4-arg `FlowTrace.Measure` names the cost |

---

## Footnote - three proven RCAs with NO ticket (minted from the main-line banner by the lead's lane)
1. Build -> Manage Upgrades -> tower -> Move -> PLACE does not seat: `MOVE BEGUN` then world taps then `MOVE CANCELLED`, never `PlaceConfirm: UI PLACE button latch consumed`. Discriminating proof first: log the top uGUI raycast target + a first line inside the OkChip callback.
2. Raid NPC shield offset: `TroopGearApplier.ApplyDefaultGrip` hard-codes `(0.05,0.05,0.02)` / Euler `(0,90,0)`; hero path (`ShieldFrame` + `GearSeat.ShieldPlateOffBone`) not inherited. Collides with lane 3A files - sequence behind it.
3. Raid ballista on its edge: `RaidBaseGenerator.PlaceSpire` applies `EnsureUpright` + `clamp(targetHeight*1.6, 8, 18)` to a low, wide siege machine; `PlaceTowerProp` already exempts siege, `PlaceSpire` does not; `RaidBaseDresser.MapCatalogArt` maps `siege` ids to `Ballista`. Touches WO-1607's proving files.

## Dispatch order for the lead
1. Land the in-flight wave (oracle re-point, 2007, 1094, 1096, 1431, 1092, flips) with the new suites registered; the gate cannot go green before the first two land.
2. Silo 1A + 1B parallel; 2A parallel; 3B after 1A; 3A after 1431 commits.
3. Top up from (D) the moment a green-gate build exists for a device-frame session.

## Not provable from this seat
Prod Neon `auth_sessions.signed_at` (1446); the deployed console dropdown (1599); any (D) device frame; the soundness of `6a5c7a36d`'s scene/controller half (four raid scenes, three hero controllers) - 472/474 is a source/data gate, not a visual one. Open risk, flagged not certified.
