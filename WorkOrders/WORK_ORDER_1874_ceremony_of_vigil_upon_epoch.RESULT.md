# WO-1874 RESULT — Ceremony of Vigil (Unity client) — CLAIM

**Status:** IMPLEMENTED
**Seat:** CLI lead gated 2026-09-19

Lead verified. `COMPILE_GATE_OK` on `Builds/c1875d.log`. `REGRESSION_OK 585/585 suites` on `Builds/r1875c.log`. `[vigil-ceremony] VIGIL_CEREMONY_OK` 7/7. Device felt-verify remains the owner's close.

## What shipped

Unity client half only. Five locked words Ember / Flame / Beacon / Pyre / Dawn. Dawn line is **"The canopy shifts. The Circle has been heard."** (no "ancestors stir"). v1: no Lonely Remnant ceremony, no ancestor figure. Perks visual+narrative only. No save-schema bump. Last-seen epoch is PlayerPrefs `vigil.ceremony.lastEpoch`. HUD never references Village; Village never references HUD. Joining still not stake-gated. HeartState crystal colour and HeartAuraController HP tell untouched.

## Auto-play vs replay vs skip

- **Hub Consider** (`VigilCeremonyPanelBootstrap` on `HubScenes.IsHub` / `IsOverworld`): `FetchBallot`, apply dressing immediately from `ceremony.word` / `vigil_weight`, then auto-open the plate **iff** in a Circle, `ceremony.passed`, `epoch_index` > last-seen, and the scene is not a raid (`HubScenes.IsRaid` / `SuppressTownHud`). Raid: do nothing.
- **Auto-play:** `RequestSequence` (roots → trunk → canopy, ~3.2s or `SequenceCompleted`), then the plate. **CONTINUE and Skip both `RememberEpoch`.**
- **Replay:** Ballots tab `circle.ceremony.replay` → `PanelRouter.Open(PanelId.CeremonyOfVigil, "replay")`. Plays even if already seen. **CONTINUE / Skip do not bump last-seen.**
- **Skip:** armed after the first frame (full-canvas catcher). Jumps to dressing-on (already applied) and closes. Never opens in a raid.
- **Lonely Remnant:** `CLAN_NOT_IN_CLAN` / not in a Circle → no plate, no auto-play.

## Brace / NUL

`python tools/gate_brace.py` on all 14 edited `.cs` files: **`GATE_BRACE_SUMMARY bad=0 of 14`**. NUL scan: **0 of 14**.

## Tests run

**Not run.** EditMode (`VigilCeremonyTests`) and DataRegression need the Unity batch the lead owns. Suite is registered next to circle-screen:

```
DeNelle.Core.Diagnostics.Guard.Try("Regression", "vigil-ceremony suite", () => { if (!DeNelle.Editor.Regression.VigilCeremonyRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[vigil-ceremony] " + r); });
```

## Files changed

- `Assets/_Modules/Core/Circle/VigilCeremonyWords.cs` (new)
- `Assets/_Modules/Core/Circle/VigilCeremonyLedger.cs` (new)
- `Assets/_Modules/Core/UI/PanelRouter.cs` — `PanelId.CeremonyOfVigil = 29` appended
- `Assets/_Modules/HUD/Circle/CircleWire.cs` — `CeremonyDto`; `PerkDto.title` / `.description` (still no `effect` / `perk_id`)
- `Assets/_Modules/HUD/Circle/VigilCeremonyVM.cs` (new)
- `Assets/_Modules/HUD/Circle/VigilCeremonyPanel.cs` (new)
- `Assets/_Modules/HUD/Circle/VigilCeremonyPanelBootstrap.cs` (new)
- `Assets/_Modules/HUD/Circle/CircleScreenVM.cs` — `CanReplayVigil` + replay command
- `Assets/_Modules/HUD/Circle/CircleScreenPanel.cs` — D1 door names `VigilCeremonyPanel` / `PanelId.CeremonyOfVigil`
- `Assets/_Modules/Village/Heart/HeartVigilDressingController.cs` (new) — FireFlies / `TreeofLifeAura_Aura` density+motion, no hue, no second pool
- `Assets/Tests/EditMode/VigilCeremonyTests.cs` (new)
- `Assets/Editor/Regression/VigilCeremonyRegression.cs` (new)
- `Assets/Editor/Regression/CircleScreenRegression.cs` — FrozenKeys
- `Assets/Editor/Regression/DataRegression.cs` — registration line
- `Assets/Resources/Data/Canonical/{en,es,pt-BR,de,fr,ru,ar,ja,ko,zh-Hans}.json`
- `Assets/StreamingAssets/Data/Canonical/` same 10, written from the same dump (`tools/merge_wo1874_ceremony_keys.py`)
- `WorkOrders/WORK_ORDER_1874_ceremony_of_vigil_upon_epoch.md` — Status flipped

BOARD.html not regenerated (lead does). No commit.
