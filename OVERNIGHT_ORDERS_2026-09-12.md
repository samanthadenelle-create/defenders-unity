# OVERNIGHT ORDERS - 2026-09-12 (owner going to bed after the skip-kit APK)

**Owner, 2026-09-11 evening:** after this tester build she sleeps. Next seats go **slow and systematic**. Do not freelance.

**This APK IS ON SEEKER.** Do not start a second APK.

| Field | Proven |
|---|---|
| package | `com.denellestudios.echoesofelarion` |
| versionName | `2026.09.12.365962` |
| versionCode | `365962` |
| lastUpdateTime | `2026-09-11 22:31:25` |
| APK mtime | `2026-09-11 22:27:12` `Builds\Android\DefendersOfTheRealm.apk` 444 MB |
| define | `TESTER_BUILD` (apk-build.log line 24 `-extraScriptingDefines` / line 820) |
| ship | `SCHEMA_PARITY_OK` 22:21 → `APK_OK` 22:27 → `R2_PARITY_OK` 22:28 catalog `2026.09.12.365962` objects=198 |
| install | `install-apk-to-seeker.ps1` streamed install **Success** 22:31 |
| NOT | `2026.09.12.365875` (superseded) |

Skip buttons compiled under `TESTER_BUILD` in `AdminOverlay`: **Set Level 15**, **MAX all buildings**, **MAX troop types**, **Grant Iron Bastion town**, **Raid: Iron Bastion**. Door: gear dock → **Settings → Help → Dev Tools**.

⚠ **Known open-gate (do not guess-fix unless she F8s it):** `AdminOverlay.SetOpen` still blocks when `!IsAuthorised() && !Debug.isDebugBuild`. `OwnerWalletAddress` is `""`, so `IsAuthorised()` is false. Tester is not a Development build. If she taps Dev Tools and the panel does not open, that is the named candidate — wait for her F8 / a log from **this** APK, then one-line the gate to also admit `TESTER_BUILD`. Do not rebuild unless she confirms it is dead.

Standing rules:
- Edit-only lanes, file-disjoint. ONE compile + ONE data regression on the combined tree. Markers, not exit codes.
- Commit local by explicit path. **NEVER push.**
- No scene hand-edits. Never strip FlowTrace.
- No owner rulings. If it needs a creative/balance call, park it.
- **Do not play her save. Do not launch the app. Do not wipe.**

## MUST (in order; STOP the night if a step is red)

1. **~~Finish and install this APK.~~ DONE** (table above). Do not start a second APK.

2. **Do not play her save.** Leave the device to her felt-check. After she sleeps, device idle.

3. **Headless only: owned-town proofs that already exist.** Run existing `OwnedTown*Proof` / `OwnedBaseStateRegression` / `RaidEscalationRegression` via `DataRegression.RunAll` **once** if Unity is idle (no editor, no other batchmode). Judge `REGRESSION_OK n/n suites` on a **fresh** log. If `unlockVictories != 0` on any flagship, FAIL — owner ruled **NONE** (both JSON twins already 0/0/0/0). If 2-star capture is accepted, FAIL. Do not author new suites unless one of those is hollow.

4. **Inventory the owned-town slice, do not expand it.** Morning commit fence = the list below. Do **not** `git add -A`. Do **not** fold Codex unstaged localization/worklog into it.

### Commit fence (MUST 4 — listed this session)

Runtime / capture:
- `Assets/_Modules/Village/World/Camps/DevSkipKit.cs`
- `Assets/_Modules/Village/World/Camps/RaidCaptureCensus.cs`
- `Assets/_Modules/Village/World/Camps/RaidVictoryController.cs` (census seam only)
- `Assets/_Modules/Village/World/Camps/OwnedTown*.cs` (controller, panel, navigation, construction, repair, design, layout snapshot, move, pose, identity, template, upgrade, importer, build op)
- `Assets/_Modules/Village/Arena/OwnedTownPracticeController.cs`
- `Assets/_Modules/Core/State/OwnedBaseState.cs`
- `Assets/_Modules/Core/State/OwnedBaseProgression.cs`
- `Assets/_Modules/Core/State/OwnedBaseConstruction.cs`
- `Assets/_Modules/Core/State/OwnedTownJobKey.cs`
- `Assets/_Modules/Core/State/OwnedTownPracticeSession.cs`
- `Assets/_Modules/HUD/AdminOverlay.cs` (TESTER_BUILD skip buttons + handlers only)
- `Assets/_Modules/HUD/HelpMenu.cs` / `HelpMenuVM.cs` (TESTER_BUILD Dev Tools door)

Scenes / template (do not hand-edit `.unity`):
- `Assets/Scenes/OwnedTown_IronBastion.unity`
- `Assets/Scenes/ArenaPractice_IronBastion.unity`
- `Assets/Resources/OwnedTown/` (IronBastion template)

Editor proofs / bakers (run, do not expand):
- `Assets/Editor/OwnedTown*.cs`
- `Assets/Editor/Regression/OwnedBase*.cs`
- `Assets/Editor/Regression/OwnedTown*.cs`

Skip-kit callers also live on F10 `DevPanelController` — include only if the diff is the skip buttons, not a drive-by.

## STOP / DO NOT

- Do **not** retune raid HP/DPS, garrison caps, or unlockVictories.
- Do **not** start the crash-settlement journal (`docs/RAID_SETTLEMENT_RECOVERY_MAP_2026-09-11.md`).
- Do **not** start PvP, wagers, or WebGL.
- Do **not** "fix" yellow harvest cubes or flooded courtyard without a **new** Seeker log from **this** APK (`[Flow:TripoMatFix]`, `[Flow:Harvest]`, water/moat). The 21:17 log is the old `365875` boot.
- Do **not** kill a live linker unless `Get-CimInstance Win32_Process` shows a stuck batchmode with **no log growth for 20 minutes**.
- Do **not** mark WO-1705 DONE. PO closes.
- Do **not** rebuild the APK unless she confirms Dev Tools is dead or the skip buttons are missing.

## STRETCH (only if MUST 1–4 are green and Unity is idle)

5. **Read-only RCA packet** (no edits) for leftover Default Town faults, using only a log pulled **after** this APK boot: white LightSkins gone or not; yellow cubes named; water named; lumbermill `NO ResourceCollector`. Write `logs/debug/default-town-after-skipkit.md` with quoted lines. No code.

6. If owned-town proofs are green, draft (do not commit) a commit message file for the owned-town fence only. Morning CLI commits.

7. If she F8s "Dev Tools tap does nothing", one-line `AdminOverlay.SetOpen` to admit `TESTER_BUILD` the same way HelpMenu `IsDevContext` already does. Then one compile gate. **Do not ship a new APK overnight** unless she asked.

## Morning program (do not start overnight)

Day loop she wants: **castle looks right → raids → 3-star Iron Bastion → owned town → arena.** Skip kit exists so she does not grind.

Order after her notes, one slice at a time:
1. Whatever her felt-check names on **365962** (white skins / cubes / flood / skip door). Instrument if no log.
2. Grant Iron Bastion town + Raid: Iron Bastion skip path, only if the door opened.
3. Honest 3-star Iron Bastion capture path (no fake victories).
4. Arena practice door. Park crash-settlement, PvP, mill T4 cost, WO-1426.

## Morning handback (fill this)

- APK `versionName=2026.09.12.365962` / `versionCode=365962` / `lastUpdateTime=2026-09-11 22:31:25` — **installed**
- `COMPILE_GATE_OK` log name + mtime
- `REGRESSION_OK n/n` log name + mtime, and whether raid-escalation still says 0/0/0/0
- Owned-town path list (the commit fence above)
- What was NOT done, on purpose
- Her felt-check notes if any (F8 inbox)
