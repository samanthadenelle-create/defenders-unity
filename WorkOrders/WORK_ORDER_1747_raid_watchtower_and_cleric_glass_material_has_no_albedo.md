# WORK ORDER 1747 — Raid watchtower / KayKit Cleric: `glass` material ships with NO albedo and NO tint (pink/grey patch on device)

**Status:** READY TO IMPLEMENT
**Minted:** 2026-09-15 by the lead, from the owner's felt-test on the Seeker (tester build `2026.09.15.371127`, scene `RaidBase_IronBastion`).
**Silo:** content / Addressables dependency closure. Files: whichever KayKit material named `glass` is referenced by the `NPCs/KayKit/Cleric` address (and, to be PROVEN not assumed, by the `Watchtower_Archer_*` prefab). Do NOT touch `RaidAssaultAi.cs` / `TroopController.cs` (WO-1746 silo) or any `.unity`.

## Evidence (captured, not theorised)
- F8 device capture **seq 5257** (`logs/f8-inbox/capture-device-20260915-134044-seq5257.md`), device error at `2026-09-15T18:40:44Z`:
  `[Flow:StructureAssets] dep MISS on 'NPCs/KayKit/Cleric': material 'glass' has NO albedo and NO tint — renders as an untextured grey blob. shader='Universal Render Pipeline/Lit' albedo slots scanned: _BaseMap=EMPTY, _MainTex=EMPTY`
  Logged by `Assets/_Modules/Core/Addressables/DependencyClosureTrace.cs:129`.
- Owner screenshot one second later (`logs/f8-inbox/device/SM02G4061955851/flag_20260915-183736_00.png`): the archer watchtower beside the hero shows a flat pink/white window patch mid-tower. The reticle log names it `Watchtower_Archer_3` (`DefenseTower`, faction Hostile).

## What is NOT proven
Whether the watchtower's pink patch IS the same `glass` material the Cleric trace names, or a second instance of the same class. The trace fires per address; the tower prefab was not traced in this capture. The lane proves the mapping first (open the tower prefab's materials; run the closure trace on its address) before fixing either.

## Acceptance
1. `DependencyClosureTrace` reports zero `dep MISS … NO albedo` for `NPCs/KayKit/Cleric` and for the `Watchtower_Archer` address on a fresh headless run.
2. The tower renders with no untextured patch in a captured PNG (headless capture or the owner's device screenshot).
3. Content re-pushed through `tools/r2-ship.ps1` (bundle hashes change); `R2_PARITY_OK` on a fresh log.
4. Lane flips this Status line and writes the `.RESULT.md`.
