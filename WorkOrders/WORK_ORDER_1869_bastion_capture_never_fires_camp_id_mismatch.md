# WO-1869 — Iron Bastion 3-star clear never captures the town (camp-id mismatch)

**Status:** IMPLEMENTED PENDING LEAD GATE — 2026-09-18: `ResolveConfigId` now reads the spawner's stored id, then the `SceneConfigCatalog` row for the scene, and only then the legacy strip (warned); new `RaidConfigIdResolveRegression` ([raid-config-id], `RAID_CONFIG_ID_OK`/`_FAIL`) proves catalog parity over the real rows. Lead registers the suite in `DataRegression.cs`, gates and commits. Original minting note: minted 2026-09-18 12:20 from the owner's live Seeker run; banner bumped 1869 -> 1870 in the same edit. P0: loop defect (owner ruling 2026-09-18: "all about the polished items we give them, the raid the skr integration and the loop").

**Owner, verbatim (2026-09-18):** "beat bastion in 1 min shouldnt that unlock?" / "my point is 3 stars should have triggered to player base right?"

## The measured defect (device log `Logs/device/logcat-bastion-victory-20260918.txt`, build 2026.09.18.374427)

The owner 3-starred `RaidBase_IronBastion` in 65 s (`:202363` `stars settled: 3 ... elapsed=65s/180s underTime=True`), with `OwnedBase == null` (`:178382` `[Flow:OwnedBase] REFUSED: Owned base is missing.`). The designed payoff of a 3-star Bastion clear is the TOWN CAPTURE (WO-1778, DONE, `b76e1d28d`; WO-1783 shortfall sentence). It did not fire:

- `:202350` `[Flow:Raid] OBJECTIVE COMPLETE - RaidSpire 'RaidSpire' (config 'iron_bastion') RAZED.` <- the spawner's id
- `:202352` `[Flow:Raid] VICTORY — raid 'IronBastion' won (SPIRE RAZED).` <- the controller's derived id
- `:202395` `[Flow:EndState] RAID VICTORY composed: baseClaimed=False captureStarsRequired=0 stars=3 lead=ordinary-clear.`
- `grep -c 'CAPTURE ELIGIBLE'` = 0 and `grep -c 'highest raid settled'` = 0 in the whole 208k-line log, and 0 in the 2026-09-16 owner run (`Logs/device/pull-20260916-143101-bastion-owner-run/logcat_full.txt`). One of those two lines MUST print whenever the id equality holds (`RaidVictoryController.cs:387-397`), so the equality itself failed.
- Same id kills the rough-stone faucet: 3x `ROUGH STONE withheld: camp 'IronBastion' is not on the I..IV ladder`.

## Root cause (read at source 2026-09-18)

`RaidVictoryController.ResolveConfigId` (`Assets/_Modules/Village/World/Camps/RaidVictoryController.cs:978-987`) derives the camp id by stripping `"RaidBase_"` off the scene name. The Bastion scene is `Assets/Scenes/RaidBase_IronBastion.unity` (PascalCase) while its three siblings are `RaidBase_raider_camp_small` / `RaidBase_fortified_garrison` / `RaidBase_mage_enclave`, so it yields `"IronBastion"`. The capture gate compares ordinally against `OwnedBaseProgression.FinalRaidId = "iron_bastion"` (`Assets/_Modules/Core/State/OwnedBaseProgression.cs:11`) at `RaidVictoryController.cs:387`, and `_captureRaidShortOfStars` (`:408`) uses the same compare. The catalog already carries the right mapping: `scene-configs.json:304` `"id": "iron_bastion"` with `:310` `"sceneName": "RaidBase_IronBastion"`.

`ResolveConfigId` is called at `:311` and `:385`; the comment at `:976` says "prefer the spawner's stored id" but the body never reads the spawner.

## Fix (architecture ruling)

1. `ResolveConfigId` resolves the id from the catalog, not from string surgery: prefer the spawner's stored config id (the spawner already logs `config 'iron_bastion'` at `:202350`, so it has it); else look up `SceneConfigCatalog.All` by `sceneName == gameObject.scene.name` (`:632` already iterates `SceneConfigCatalog.All`); fall back to the current strip ONLY as a last resort, and `FlowTrace.Warn` when the fallback is taken.
2. Do NOT rename the scene: literal `"RaidBase_IronBastion"` checks live at `RaidVictoryController.cs:191` and `RaidSelectionScreen.cs:452`, and the `.unity` is baked by name.
3. Keep every existing FlowTrace line; add one `FlowTrace.Step("Raid", "config id resolved: '<id>' via <spawner|catalog|strip> for scene '<scene>'")` at the resolve seam so the next log proves which path ran.
4. No balance change, no tunable change, no scene edit.

## RED-first regression (new suite, registered in `DataRegression.cs` by the lead)

`Assets/Editor/Regression/RaidConfigIdResolveRegression.cs`, tag `[raid-config-id]`, markers `RAID_CONFIG_ID_OK` / `RAID_CONFIG_ID_FAIL`:
- Case A (catalog parity): for every `scene-configs.json` raid row, resolving the row's `sceneName` through the SAME resolver the controller uses returns exactly the row's `id`. Must be RED on HEAD for `RaidBase_IronBastion` -> prove by running it before the fix and pasting the FAIL line in the RESULT.
- Case B (capture gate reachability): with a resolver result of `iron_bastion`, `OwnedBase == null` and stars = `CaptureStarsRequired`, `_captureRequired` is true; with stars one below, `_captureRaidShortOfStars` is true. Existing `CaptureStrandExitRegression` sets `_captureRequired` directly and can never catch this; do not weaken it.
- Revert recipe per case: restore the strip-only `ResolveConfigId` and Case A must go red again.

## Acceptance

- `COMPILE_GATE_OK` + `REGRESSION_OK <n>/<n>` on fresh logs, the new suite green, and the RED run's FAIL line pasted in the RESULT.
- Next Seeker build: a 3-star Bastion clear logs `CAPTURE ELIGIBLE` and routes to the owned town; the rough-stone line no longer says `camp 'IronBastion'`. Owner felt-verify closes.

## Not in scope

Per-camp honor times (WO-1763), the 65% vs 70% razed readout mismatch on the victory modal, the raid HUD showing through the modal (separate tickets to mint on the owner's word).
