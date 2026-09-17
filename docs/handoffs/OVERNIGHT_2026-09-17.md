# Overnight report - 2026-09-16 evening to 2026-09-17 morning

**Owner orders (verbatim, 2026-09-16 ~21:00-21:25):** "can you work overnight to polish this. I want you to run it
autonomously and just refine items so the graphics do not break. In the raids I have seen, its always in combat graphics
that is causing the broken color squares" / "im going to bed, but we are now working only on polish" / "we want spells to
launch and land well. the thunder looks amazing" / "use least accessible model so llm and haiku where you can using an
fable advisor" / "change the store to SKR only and set to flat amounts" / "and test with sales promos" / "so you can see
screenshots of discounts". Rulings before bed: no loot on a failed raid; overnight APK installs to the Seeker allowed
when the game is not in the foreground.

## 1. What landed (16 local commits on `dev`, ff9537970 .. 5a600f5bc, NOT pushed)

Gate on the combined tree: `COMPILE_GATE_OK` 22:21, `REGRESSION_OK 564/564` 22:27 (fresh logs `Builds/compile-gate.log`,
`Builds/regression.log`). One lane per commit, explicit paths (`docs/handoffs/overnight-2026-09-16/lanes/*.txt|.msg`).

| WO | Commit | What the owner will feel |
|---|---|---|
| 1806 | ff9537970 | Archer/projectile flat grey quads: the class is fixed (opaque Lit placeholder on particle slots, 4,197 slots / 969 prefabs); the exact quad in her flag is NOT attributed - a new `DRAWING BILLBOARD CENSUS` line names survivors on the next build |
| 1807 | 1b0b7f0f1 | "corners upside down / towers inverted" on the second base: a wooden tower floated 2.48 m under the stone clad (never a rotation). All four raid bases rebaked, `RAID_POST_AUDIT_OK 59` (was 49 defects) |
| 1808 | b00be1e54 | Enemy towers no longer shoot through a standing wall (107 shots, zero wall checks in her log; the enemy path never ran the check) |
| 1809 | d2e2925db | The invisible CastleBarracks collider is out of the baked hub (`STRUCTURE_HUSK_CLEANUP_OK 1`, `NAVMESH_BAKE_OK`, `HUB_HUSK_FREE_OK`) |
| 1810 | 74d5f1e64 | A raid loss costs troops: killed = dead; fail loses 100% of survivors; retreat loses 60%; a fail pays no loot; result screens say how many were lost (victory too). Tunables `raid.lossPctFail=100`, `raid.lossPctRetreat=60` |
| 1811 | 5130e79b7 | Army screen rebuilt: "Army 7 of 10 / 3 recovering / Room for 3 more", TRAIN one per tap, MORE = dismiss for gold / move to Reserve / recall, presets behind Loadouts. SaveSchema v42 (reserve). Capture `UI_GEOMETRY_OK / UI_TOUCH_OK / UI_GLYPH_OK 6/6` |
| 1812 | e0528ab56 | Raid wall audit oracle: every wall on Structure, muzzle margins printed per base (garrison 0.50 m) |
| 1813/1814 | 86ce98b28 | Screen-filling "LEVEL UP!" text fixed with the maths (1.478 m em at 3.5 m = 439 px; now distance-compensated, 9% of screen). White square on the hero stays READY: candidates named, renderer not provable from the capture |
| 1815/1819 | baeba6b5b | Store shelf SKR-only at flat amounts (100/200/300/500/1000/9999; 1000 rung proposed), gold 30% OFF plates measured 11.57:1 ink/fill and 7.39:1 fill/surface, banner "priced in SKR", "Raise the Barracks" fits. PNGs `Builds/ui-capture/Store_SkrFlat_{NoSale,Sale30}_2670x1200.png` |
| 1818 | 7d0d05469 | api quotes flat SKR (ceil of flat x (1 - sale)), verify reads the persisted row; 90/90 store tests, 771 overall green after the knob rename. **Needs the owner-run production deploy** |
| 1816 | ca567185c | Synty castle clad materials converted to URP (75 converted; pink/yellow towers). `Assets/Synty` is gitignored: the TOOL is the deliverable, re-run `Defenders/Art/Fix Synty Castle URP Materials` on a fresh clone |
| stone | 9df1d5f2c | Stone stockpile stacks rough stone chunks, not flour sacks |
| regs | f0855d011 | Registrations, banner 1807..1819 -> next free 1820, board |
| 1813 sweep | 937f934e5 | **The white square is the level-up column.** All eleven renderer slots of the Lana Studio Level_up prefab have no texture, so the column is a hard-edged additive rectangle 2.29 m wide that composes to white over a sunlit town (frames in `Builds/vfx-whitequad/`). Also fixed: the flesh-hit mist drew a solid white square on every hit (opaque URP/Lit with an all-white base map), and five Hovl combat effects still carried placeholder slabs in their trail slot because the Hovl path ran no proof pass. 32 played prefabs / 198 slots classified. WO-1806 positive control run, red, reverted |
| 1813 remedy | 4fe270ed3 | The level-up column's drawer named by isolation (child `area`, a Mesh-mode slot on an additive material with no texture; no pack texture exists to rebind, and a soft-edge texture was tried and refuted by measurement). Remedy = the tree's own 09-09 ruling for the same pack applied by condition: a Mesh slot whose every material has no albedo is disabled on both spawn paths. After frame: arrows, rings and floor glow still read, no hard edges. WO-1813 IMPLEMENTED |
| 1817 + 1820 | 5a600f5bc | **Watchtowers were sub-metre in three bases** (0.05 m in the Iron Bastion) because the clad was never fitted; now they render at the authored turret cadence and the garrison muzzle margin is 2.99 m. **The raid spire, the win target, rendered 14 cm tall in three of four bases** (clad prefab scale applied twice; the small camp was right by coincidence) with a 14.4 m hit box around it. Now 14.40 m in all four, seated on the keep platform. All four bases rebaked through the sanctioned chain (OWNED_TOWN_CHAIN_OK, RAID_POST_AUDIT_OK 63). Frames `Builds/raid-post-audit/RaidBase_*_RaidSpire.png` |

## 2. Build and install

- Tester APK **2026.09.17.373164** built 22:29 (`Builds/overnight-apk-status.txt`: SCHEMA_PARITY_OK, APK_OK 446 MB,
  R2_PARITY_OK objects=204, APK_DONE 22:30; `Builds/r2-push.log` R2_PUSH_OK 20 uploaded 17.2 MB).
- Installed on the Seeker 22:32 via `install-apk-to-seeker.ps1 -Build:$false -Install:$true` with the launcher in the
  foreground (game not running, not launched, not uninstalled): `dumpsys package com.denellestudios.echoesofelarion`
  reads versionCode=373164 versionName=2026.09.17.373164.
- **Second tester APK 2026.09.17.373245** (with the VFX sweep commits 937f934e5 + 4fe270ed3): APK_OK 23:49,
  R2_PUSH_OK 2 uploaded, R2_PARITY_OK objects=204, APK_DONE 23:50; installed on the Seeker 23:51 (launcher in front,
  game not launched): `dumpsys package` reads versionCode=373245.
- **Third tester APK 2026.09.17.373306** (with the watchtower + spire commit 5a600f5bc): APK_OK 00:51, R2_PUSH_OK 2
  uploaded, R2_PARITY_OK objects=204, APK_DONE 00:51; installed on the Seeker 00:52 (launcher in front, game not
  launched): `dumpsys package` reads versionCode=373306. **This is the build on your Seeker in the morning.**
- `ProjectSettings/ProjectSettings.asset` diff after the builds is only the bundleVersion / versionCode stamp
  (371701 -> 373245); left uncommitted as before.
- History note: the WO-1815 store capture entry in `Assets/Editor/UICaptureLaunch.cs` travelled in the WO-1811 commit
  (5130e79b7) because both lanes edited that file.

## 3. Awaiting the owner's felt-test

- Fresh raid on the second base: corner towers upright, no shots through walls, casualties on loss, no loot on a fail.
- Level-up in town on a BRIGHT day: the label is small now, and the white square should be gone (the column slab no
  longer draws; the arrows and rings stay). If anything flat still shows, `adb logcat | grep "DRAWING BILLBOARD CENSUS\|UNTEXTURED MESH SLAB\|OPAQUE BILLBOARD REPAIRED"` names it.
- Archer fire in a raid: `adb logcat | grep "PARTICLE SLAB after repair"` - zero lines = the shipped set is clean.
- Army screen: is "trained vs deployed" now obvious; Reserve/dismiss verbs; dismiss refund is `army.dismissReturnPercent` = 50 (PROPOSED, training charges nothing).
- Store: shelf at flat SKR; the sale needs `node tools/client-tunables.mjs set store.saleBps 3000` AND the production deploy of tonight's api (`! npx vercel deploy --target production --skip-domain --yes`, then I verify + promote).

## 4. Open questions for the owner

- Tier -> SKR ladder is a proposal (1000 rung added for the $19.99 band; 9999 SKR ~ $175 vs its $49.99 anchor).
- Hero death in a raid now counts as a FAIL (no loot, warband lost) - reverses the old unruled "death pays like retreat".
- Raid spire height 14.40 m in every base is the bake's authored value (clamp of the repo height x 1.6); say if the
  garrison's spire should read taller than its 5 m wall by more.
- Garrison watchtowers now sit at their 3.00 m catapult cadence, BELOW the 5 m parapet (one coherent tower, but short);
  the one-line alternative is cadence = wall top + margin. Not implemented, your call.
- Named, not fixed (next polish round): the dresser clads catapult hosts with the kit's tower and leaves green
  fallback pills under the siege art (visible in `Builds/raid-post-audit/RaidBase_IronBastion_RaidSpire.png`, WO-1617
  class); CornerPost_* still render clad-native (1.11 / 7.52 / 17.96 m); the Synty backdrop/decal/FX shader families
  are out of the URP conversion scope; CompileGate's Packages/ classifier can be defeated by an interleaved log line.

## 5. Still local

- `git push origin dev` is the owner's (harness refuses it here). 29 commits from earlier tonight + 16 overnight
  (ff9537970 .. 5a600f5bc, plus the docs commit that carries this report).
- Production deploy of tonight's api (flat SKR quotes) is the owner's:
  `! npx vercel deploy --target production --skip-domain --yes`, then I verify the candidate and promote.
- `.claude/settings.json` and `ProjectSettings/ProjectSettings.asset` left uncommitted on purpose.
