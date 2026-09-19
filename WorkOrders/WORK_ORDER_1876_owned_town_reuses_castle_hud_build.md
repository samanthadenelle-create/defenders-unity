# WO-1876 — Captured town uses the castle HUD/build stack (retire OwnedTownPanel as the rebuild door)

**Status:** READY TO IMPLEMENT — minted 2026-09-18 (banner bumped 1876 -> 1877 in the same edit). Prize-path (3-star Bastion → destroyed camp → player designs the layout). **No code until the owner says go.** Ollama `qwen2.5-coder:7b` ranked seams; the lead verified every path at source (claims that failed verification are recorded below, not acted on).

**Owner, verbatim (2026-09-18, this session):** "i want to start todays session by visiting that moment when a player beats the Iron Bastion and gets a player built Base. I envision that they get a destroyed base that they clear away and start designing their own base" then "the entire screen they use for rebuilding the town is asinine. We should be reusing the logic we have and simply passing if its castle or players base and using same structure"

**Owner, prior ruling that this ticket finishes (WO-1872 :13-14):** "Build mode in the owned town is free-form on the cleared plot (no change to build mode itself)." Last CLI bolted rubble-clear onto `OwnedTownPanel` instead.

## The moment (already in tree — do not rebuild the capture)

3-star Bastion → `RaidVictoryController.ReturnHome` (`:1316`) → `SceneRouter.GoOwnedTown()` → `OwnedTown_IronBastion`. WO-1872 already converts defensive bodies to rubble, Heart stays, salvage at `town.captureSalvagePct`. That half stands.

What does **not** match: after a 2s hold, `OwnedTownController.HoldThenReveal` (`:77-98`) `Show()`s **`OwnedTownPanel`** — a one-off Obsidian modal (`OwnedTownPanel.cs:42`) with Begin / Inspect / Repair / Next tower / Move west-east / Clear rubble / Next rubble / Build. That panel is "the only route onward" (`OwnedTownController.cs:45-47`).

## Where (least-needed model, then verified)

Asked local **Ollama `qwen2.5-coder:7b`** (not gpt-oss:20b, not Opus) to rank A_REUSE / B_STOP_CALLING / C_KEEP_ADAPTER / D_DO_NOT_TOUCH. Log: session terminal `call-14fd0ef4-23e4-4d50-a0c9-9be85b19928c-99.log`.

| Ollama said | Verified at source this session | Verdict |
|---|---|---|
| A_REUSE = `BuildModeController.cs` | `IsOwnedTown` already exists (`:43-44`). `SelectStructure` when owned (`:2603-2621`) **exits build mode** and calls `OwnedTownPanel.SelectStructure` instead of `EnsureSelectionUi` (the castle tap UI). `Enter()` already places via `OwnedTownConstructionService.TryBeginBuild` (`:2100-2110`). | **KEEP — this is the smallest cut.** |
| B_STOP_CALLING = `OwnedTownPanel.cs` | Modal overlay; Build button is the only door into the real builder (`:163-165`). | **KEEP** |
| B_STOP_CALLING = `OwnedTownController` Show() | `Start` adds the panel and `Show()`s after the hold (`:38-53`, `:97-98`). Reconstruction (`TryReconstruct`) must stay. | **Stop the Show(), keep reconstruct.** |
| B_STOP_CALLING = `HudContextEvaluator.cs:247` | `IsOwnedTown` returns true so HUD context is Town. That is the reuse we want. | **DROP — ollama inverted this.** |
| C_KEEP_ADAPTER = three copies of BuildModeController | Construction / Design / Repair services are the state adapters (`OwnedTownConstructionService`, `OwnedTownDesignService`, `OwnedTownRepairService`). BuildMode already calls them when `IsOwnedTown`. | **Keep those three services. Do not duplicate BuildMode in this list.** |
| D_DO_NOT_TOUCH = `HudKitController.cs` | Zero OwnedTown string-hits. `VillageHudBootstrap` is **not** menu-filtered for `OwnedTown_IronBastion` (`VillageHudBootstrap.cs:59-72`, `:88-104`), so the kit already spawns in that scene. `_heartGateIsHub = HubScenes.IsHub` (`HudKitController.cs:6037`) only hides hub-only widgets (heart/wave), not the dock. | **Do not rewrite HudKit first.** Prove with a frame that the peaceful dock is or is not visible under the panel. If the dock is present once the panel is gone, HudKit stays untouched. If it is absent, that is a second measured cut, not this ticket's opener. |
| TOP_A sentence | Agrees with the SelectStructure bounce. | **KEEP** |

`HudContextResolver.cs:68` already passes `inVillage` for owned town. The flag **castle | player-base already exists** as `BuildModeController.IsOwnedTown`. The defect is that the owned branch **leaves** that stack and opens a second UI.

## Fix (smallest cut — one presentation, two property kinds)

1. **Do not `Show()` `OwnedTownPanel` on capture/reentry.** First frame of the owned town is the town + the existing HudKit (WO-1783 already wanted this; the hold then the modal undid it).
2. **`BuildModeController.SelectStructure` when `IsOwnedTown`:** do not `Exit()` + `OwnedTownPanel.SelectStructure`. Stay on the castle selection UI (`EnsureSelectionUi`) and route pay/save/move/sell through the existing owned adapters (`TryBeginBuild` / `TryBeginMove` / `TrySell` / `TryQuoteClear`). Rubble (`condition01 <= 0`) is a clear-for-salvage verb on that same selection, not a "Next rubble" row on a modal. World tap already reaches this method (`:2620` comment).
3. **Peaceful dock Build face** is the build door (same as castle). Delete the panel's Build / Move west-east / Next tower chrome rather than restyle it.
4. **Reveal / inspect / repair-lesson milestones:** fold into first-entry copy or the structure tap. WO-1872 already said rubble is not damage (`OwnedTownPanel.cs:48-54`); do not revive a repair-all-ruins chooser. Open question below.

Do **not** greenfield a third town UI. Do **not** hand-edit `OwnedTown_IronBastion.unity`.

## Files (one lane)

- `Assets/_Modules/Village/BuildMode/BuildModeController.cs` — owned `SelectStructure` bounce (`:2603-2621`); keep `IsOwnedTown` + `TryBeginBuild` routing.
- `Assets/_Modules/Village/World/Camps/OwnedTownController.cs` — stop auto-`Show()` of the panel; keep `TryReconstruct`.
- `Assets/_Modules/Village/World/Camps/OwnedTownPanel.cs` — retire as the rebuild door (callers: this controller, BuildMode HideForBuild/Show around `:568`/`:711`, `UICaptureLaunch.RunOwnedTownPanelCapture`).
- Adapters stay: `OwnedTownConstructionService.cs` (incl. `TryClearRubble`), `OwnedTownDesignService.cs`, `OwnedTownRepairService.cs`.
- Regression: extend `[captured-town-bare]` or add `[owned-town-hud]` so a reconstructed owned town at rest has **no** `OwnedTownPanel` canvas, HUD context is Town, and `SelectStructure` on an owned identity does not `FindAnyObjectByType<OwnedTownPanel>()`.

## Open question (owner — do not invent)

Does the capture **repair lesson** (`OwnedBaseMilestones.EssentialRepairCompleted`) still exist, or did rubble-clear replace it? Default if unanswered: rubble-clear is the first act; do not require a funded repair of a standing body before the player may design.

## RED-first

A suite that fails if: (A) `OwnedTownController` still `Show()`s the panel after reconstruct; (B) owned `SelectStructure` still calls `OwnedTownPanel`; (C) a razed defensive record is offered as repairable damage (WO-1872 predicate must stay). Revert recipe: restore the `:2619-2620` bounce → B red.

## Acceptance

- `COMPILE_GATE_OK` + `REGRESSION_OK n/n` on a fresh log.
- Device/editor frame of the post-Bastion owned town: destroyed camp visible, **no** OwnedTownPanel modal, castle dock/build/tap usable, clearing one ruin pays salvage. Lead **opens the PNG** before flipping past IMPLEMENTED (memory `visual-ticket-never-fixed-without-an-opened-frame`; WO-1868 bounced for this).
- Owner felt-verify closes.

## Not in scope

- WO-1868 raid fog/storm (READY, bounced — separate lane).
- WO-1875 Circle sign-in.
- Rebaking raid or owned-town scenes.
- Rewriting HudKit until a frame proves the dock is missing.
- Migrating old owned-base saves (WO-1872 open question stands: default leave).
