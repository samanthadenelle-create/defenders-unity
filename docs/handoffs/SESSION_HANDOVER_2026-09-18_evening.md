# Session handover — 2026-09-18 evening (CLI lead, Fable)

Written at 18:15 while the tester APK build runs with 16 GB of commit headroom on a machine that has
NOT rebooted since 09-15 (Fast Startup swallowed this morning's "reboot"; only Restart clears the
commit-charge leak). If the build dies, Restart first, then resume at step 1 below.

## Landed today (all on origin/dev, HEAD 23a57c529, zero ahead)
| Commit | What |
|---|---|
| 5bf6fee4c | WO-1869 Iron Bastion 3-star clear now captures the town (camp id from spawner/catalog) |
| 8723810e9 | WO-1857 raid batch: raid screens + victory/defeat copy localized (79 keys), SpoilRowVM.ConceptId |
| 6bc903ee6 | WO-1871 CHANGE ARMY door on the raid deploy screen; "troops" generic plural |
| d3e752e00 | WO-1872 captured town loads destroyed; clear rubble for salvage (`town.captureSalvagePct` = 50) |
| 35b901edf | WO-1870 the Circle screen (Remnant name, form/join, members, leaderboard, vault, ballots) |
| 90f7a49c9 | locale merge: 133 keys + group-sense Remnant -> Circle across 20 catalogs + 7 tables |
| 34d89a651 | F8 watcher RETIRED (owner ruling); daemons stopped, inbox + untracked Logs deleted |
| bc889e340 | WO-1868 + WO-1869 FIXED on Seeker build 2026.09.18.375545 (Firebase 3dve2e518t3b8) |

Gates for the evening tree: `Builds/cg1870c` COMPILE_GATE_OK 17:59, `Builds/reg1870b` REGRESSION_OK 584/584 18:03 (Android target). R2 parity 14:12 (Android/Win/WebGL, 207 objects).

## In flight
- Tester APK chain launched 18:05 (`overnight-apk-build.ps1 -Tester`); status file `Builds/overnight-apk-status.txt` read `SCHEMA_PARITY_OK` at 18:05:39; Unity pid 30644. The background waiter was killed by the harness for low system memory, not the build.

## Resume steps
1. `Get-Content Builds\overnight-apk-status.txt` — need `APK_OK` + `R2_PARITY_OK` + `APK_DONE`. If absent and no Unity process: Restart the machine (uptime must read under an hour), then re-run the chain.
2. `& .\install-apk-to-seeker.ps1 -Build:$false -Install:$true`; `adb shell dumpsys package com.denellestudios.echoesofelarion | findstr versionName`.
3. `.\distribute-android.ps1 -Notes "<build>: Bastion capture -> destroyed camp + rubble salvage; CHANGE ARMY on raid deploy; Circle screen (claim first name, form/join by code, members) - TWO-DEVICE Circle chat test; fog + tower arrows"`.
4. Flip WO-1870/1871/1872 Status from FIXED PENDING DEVICE BUILD to FIXED with the build number; board; commit; push.

## Owner rulings recorded today (memory + WOs carry them)
Remnant = the player ("Bob of RiverRun" / "Bob the Lonely"), Circle = the group; perks visual + narrative only; joining never gated on staking; "troops" is the generic plural; captured town loads destroyed and rubble clears for salvage; F8 watcher retired; Ollama/Haiku for mechanical work, Opus for logic; WebGL parked; prize = raid, SKR, loop.

## Open for the owner
- Delete lines 3-15 (the SessionStart F8 daemon hook) of `.claude/settings.json` — the seat may not edit it.
- WO-1874 Ceremony of Vigil: five tier words, Lonely-Remnant beat, ancestor figure in v1.
- WO-1872: re-raze an existing owned base on old saves, or leave it (default: leave).
- 75 s Bastion honor time stays unless tuned on the rail.

## Next READY tickets (prize path)
WO-1873 global + Circle chat rooms (dispatch now that WO-1870 lanes released ClanChatPanel.cs / site/clan-chat.html); WO-1874 after the owner's ruling. WO-1701 (WebGL) parked by ruling; WO-1857 remaining shards parked behind the prize.
