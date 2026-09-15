# WORK ORDER 1748 — A troop spawns with an EMPTY id and no Animator under its root (frozen body on device)

**Status:** READY TO IMPLEMENT
**Minted:** 2026-09-15 by the lead, from the owner's felt-test on the Seeker (tester build `2026.09.15.371127`, scene `RaidBase_IronBastion`).
**Silo:** `Assets/_Modules/Village/Troops/TroopFactory.cs` (spawn/bind order) and the troop art address it resolved. ⛔ `TroopController.cs` and `RaidAssaultAi.cs` are WO-1746's silo — read them, do not edit them unless the fix is provably there, and then say so.

## Evidence (captured, not theorised)
- F8 device capture **seq 5258** (`logs/f8-inbox/capture-device-20260915-134045-seq5258.md`), device error at `2026-09-15T18:40:45Z`:
  `[Flow:TroopVisual] id=: NO Animator anywhere under the troop root - the body cannot animate at all (model missing -> tinted-capsule fallback, or a rig-less prop was skinned).`
  Logged by `Assets/_Modules/Village/Troops/TroopController.cs:514`. Note **`id=` is EMPTY** — `_troopId` was not set when the animator check ran, which itself narrows the spawn path: `TroopFactory.cs:189` is the only `AddComponent<TroopController>()` site, and `TroopController.cs:490,522` already record that `ApplyTroopAnimator` must bind BEFORE that AddComponent.
- Every troop in the owner's warband logged its `[Flow:RaidAI]` line with a real id at `13:40:59` (`troop-footman`, `troop-archer`, `troop-spearman`, `troop-outrider`, `troop-catapult`, `troop-battlemage`, `troop-echo-legionnaire`, `troop-shieldguard`) — so the empty-id spawn is NOT one of the deployed ten. Candidates the lane must prove or eliminate: a raid guard spawned through the troop factory, a rally/reinforcement spawn, or a prop skinned as a troop.

## Ask
1. Instrument first (§12): make the `TroopVisual` fail line name the prefab/address and the spawn caller (one `FlowTrace.Once` at `TroopFactory.cs:189` with the resolved address + caller) — the empty id is the symptom that hid the caller.
2. Reproduce headless in `RaidBase_IronBastion`; read the trace; fix THAT spawn path (missing model -> fallback capsule, or bind order).
3. Regression case in the existing troop-visual suite pinning "every troop root carries an Animator and a non-empty id after spawn".

## Acceptance
- Fresh headless run of the IronBastion raid: zero `TroopVisual … NO Animator` lines; zero `id=:` (empty) troop ids in any `[Flow:*]` line.
- Lane flips this Status line and writes the `.RESULT.md`.
