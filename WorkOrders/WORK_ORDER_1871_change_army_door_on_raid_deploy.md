# WO-1871 — Change Army door on the raid deploy screen (+ the "troops" plural ruling)

**Status:** FIXED — tester APK `2026.09.18.375785` built 18:11 (APK_OK 447MB, R2_PARITY_OK 207), Firebase release 5jrcip5ivq10o; Seeker install pending (phone not on USB at 18:20); owner felt-verify closes. PRIOR: FIXED PENDING DEVICE BUILD — lead gated 2026-09-18 (`Builds/cg1870c` COMPILE_GATE_OK 17:59, `Builds/reg1870b` REGRESSION_OK 584/584 18:03); rides the next tester APK, owner felt-verify closes. PRIOR: IMPLEMENTED PENDING LEAD GATE — 2026-09-18. CHANGE ARMY face added to the raid deploy footer (door -> `ArmyMusterPanel.Show()`, return re-read on a new `ArmyMusterPanel.Closed(handedOff)` seam), the starter-squad toast is now the generic-plural locale key, and `RaidDeployChangeArmyDoorRegression` (3 cases) is RED-first on HEAD. ⚠ LEAD: two follow-ups belong to files outside this lane's silo — register the suite in `DataRegression.cs`, merge the 2 sidecar keys, and RE-POINT `StarterArmyGrantRegression.cs:281-292` (it pins the retired per-unit plural). See the `.RESULT.md`.

*(Minted 2026-09-18 14:50; banner bumped 1871 -> 1872 in the same edit. Prize-path, raid loop polish.)*

**Owner, verbatim (2026-09-18):** "also there is no screen that you can access that allows you to change configuration of troops you want to use in raid" — ruling (AskUserQuestion, same day): **"Add a Change Army door on the raid deploy screen"** (chosen over a per-raid troop picker and over a discoverability-only fix).

**Second ruling, same message thread:** *"that correct footman/footmen (or make generic troops) can be used across the board for plural troops"* — **the generic word "troops" is the plural across the board.** Never author per-unit plural forms (Footman/Footmen, Archer/Archers) in code or keys; count + generic noun (`{0} troops`) is the convention. This unblocks the WO-1857 skip in `StarterArmyGrant.cs:173`.

## The measured gap (read at source 2026-09-18)

- The army surface EXISTS: `ArmyMusterPanel` (WO-1811 RESULT `:60-72`): per-troop **Manage** face -> Move to Reserve / Return to army / Dismiss; presets behind one **Loadouts** door (`ArmyMusterPanel.cs:73`, `:354`, `:383`, `:460` `surface=LOADOUTS-DRAWER|PRIMARY`); its GO face opens `RaidSelectionScreen.Open()`.
- The raid flow has NO door back: neither `Assets/_Modules/Village/Hero/RaidDeployScreen.cs` nor `RaidSelectionScreen.cs` references `ArmyMusterPanel` (grep 2026-09-18: callers are only ObsidianQueueHud, TroopTrainingPanel, ArmyComposition, ArmyMusterService/VM). So a player standing at the raid deploy screen cannot change who goes.

## Fix

1. On the raid deploy screen (and the raid selection screen if it shows the army summary), add ONE face, **Change Army**, that opens the existing `ArmyMusterPanel` (PRIMARY surface) and, on close, returns to the raid screen it came from with the army summary re-read (deployable count / reserve count / readiness). Reuse the panel's existing `Open` and the readiness seam `ArmyReadiness`; no new data model, no gate change (the raid gate stays full-army per the 2026-09-16 ruling).
2. Both directions keep their FlowTrace: `"RaidDeploy"` on the door tap, `"Muster"` already traces open/close; add a `Step` on the return re-read naming the counts.
3. All new strings are locale keys (`village.troops.raid_deploy_screen.change_army` etc.) — sidecar per the WO-1857 lane shape, all 10 catalogs + 7 tables merged by the lead.
4. `StarterArmyGrant.cs:173`: replace the Footman/Footmen branching with `LocalText.Format("village.troops.starter_army.first_squad_ready_fmt", count)` where the English is `"{0} troops ready"`-style generic copy (exact English from the current literal, made generic per the ruling).
5. Touch floor per `HudDockSlotLayout`; the face is dimmed, never hidden, while a raid is staging.

## RED-first regression
`Assets/Editor/Regression/RaidDeployChangeArmyDoorRegression.cs`, tag `[raid-deploy-change-army]`, markers `RAID_DEPLOY_CHANGE_ARMY_OK/_FAIL`: (A) the deploy screen source wires a face whose handler calls `ArmyMusterPanel.Open` (comment-stripped source pin); (B) the muster panel's close path raises the event the raid screen re-reads from (pin the seam name); (C) no per-unit plural literal (`Footmen`, `Archers`) remains in `StarterArmyGrant.cs`. Revert recipe: remove the face -> A red; restore the plural branch -> C red.

## Acceptance
`COMPILE_GATE_OK` + `REGRESSION_OK n/n` fresh; a headless or device capture of the deploy screen showing the face; owner felt-verify: from the raid deploy screen, change the army, come back, the summary reflects it.

## Not in scope
A per-raid troop picker; loosening the raid gate; the Manage screen layout.
