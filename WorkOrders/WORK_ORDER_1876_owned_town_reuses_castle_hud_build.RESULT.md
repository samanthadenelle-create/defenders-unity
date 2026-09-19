# WO-1876 RESULT — CLAIM (pending lead gate)

**Status claimed:** `IMPLEMENTED PENDING LEAD GATE`  
**Seat:** grok-4.5 subagent  
**Date:** 2026-09-19  
**Commit:** none (explicit: do not commit)  
**Unity compile / DataRegression / APK / BOARD.html:** not run (lead owns combined-tree gate)

## Claim

Captured owned town no longer auto-opens `OwnedTownPanel` after reconstruct. Selection stays on the castle `BuildSelectionUI` stack when `BuildModeController.IsOwnedTown`; rubble clear / sell / move / upgrade route through the existing owned adapters. Peaceful dock Build remains the intended door (HudKit untouched — no frame proved the dock absent).

## What changed

| File | Change |
|---|---|
| `Assets/_Modules/Village/World/Camps/OwnedTownController.cs` | Removed `AddComponent`/`HoldThenReveal`/`panel.Show`. After reconstruct, folds pristine capture milestones (`OwnershipRevealed` + `TryInspectPristineTown`) so `CanEdit` unlocks without the modal Begin/Inspect; rubble-clear stays the first act. |
| `Assets/_Modules/Village/BuildMode/BuildModeController.cs` | Owned `SelectStructure` no longer `Exit()` + `FindAnyObjectByType<OwnedTownPanel>()`; stays on `EnsureSelectionUi`. Enter/Exit no longer HideForBuild/Show the panel. Sell→`TryClearRubble`/`TrySell`; CommitMove→`OwnedTownDesignService.TryBeginMove`; Upgrade→`OwnedTownJobKey` panel. |
| `Assets/_Modules/Village/BuildMode/BuildSelectionUI.cs` | Optional `sellVerb` so rubble shows `Clear (N)` on the same strip. |
| `Assets/_Modules/Village/World/Camps/OwnedTownPanel.cs` | Documented as harness/legacy only — not the rebuild door. `Show()` kept for `UICaptureLaunch` / play-proofs. |
| `Assets/Editor/Regression/OwnedTownHudReuseRegression.cs` | New `[owned-town-hud]` RED-first oracle (A–E). |
| `Assets/Editor/Regression/DataRegression.cs` | One-line registration for owned-town-hud. |

## Adapters kept (untouched logic)

- `OwnedTownConstructionService` (`TryBeginBuild` / `TrySell` / `TryQuoteClear` / `TryClearRubble`)
- `OwnedTownDesignService` (`TryBeginMove`)
- `OwnedTownRepairService` (not revived as a repair-all chooser)

## Regression claim (not yet lead-gated)

Suite: `OwnedTownHudReuseRegression` → marker `OWNED_TOWN_HUD_OK` / tag `[owned-town-hud]`

- **A** controller must not `HoldThenReveal` / `panel.Show` / `AddComponent<OwnedTownPanel>`; must fold milestones + emit `OWNED_TOWN_READY_NO_PANEL`
- **B** owned `SelectStructure` must not find `OwnedTownPanel`; must allow `IsClearableRubble` + `EnsureSelectionUi`; clear/move adapters present
- **C** razed inherited rubble is clearable and **not** repairable damage (WO-1872 predicate)
- **D** `HudContextResolver.ResolveForSceneAtRest(OwnedTown_IronBastion)` → `Town`
- **E** Enter/Exit must not HideForBuild/Show the panel

Revert recipe (B red): restore the `:2619-2620` bounce to `OwnedTownPanel.SelectStructure`.

## Local checks run by this seat

- `python tools/gate_brace.py` on all edited `.cs` → `GATE_BRACE_SUMMARY bad=0 of 6`
- NUL scan on the same paths → clean

## Not proven here (lead / owner)

- `COMPILE_GATE_OK` on a fresh log
- `REGRESSION_OK n/n` including `[owned-town-hud]`
- Device/editor frame: destroyed camp, **no** OwnedTownPanel modal, dock/build/tap usable, one ruin clear pays salvage (WO acceptance: lead opens PNG)
- Owner felt-verify

## Open question (unchanged — default applied)

Repair lesson vs rubble-clear: default applied — fold pristine inspect so design unlocks; do **not** require a funded repair of a standing body. If repairable damage remains on a save, `CanEdit` stays gated and a Warn is traced (out of default path / old-save leave).

## Not in scope (honored)

- WO-1868 fog, WO-1875 Circle, scene rebakes, HudKit rewrite, third town UI, git commit, APK, `BOARD.html`
