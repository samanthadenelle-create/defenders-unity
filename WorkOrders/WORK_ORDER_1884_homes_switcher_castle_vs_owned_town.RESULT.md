# WO-1884 RESULT — CLAIM (pending lead gate)

**Status:** IMPLEMENTED PENDING LEAD GATE  
**Date:** 2026-09-19  
**Seat:** grok-4.5 agent (shared workspace `D:\eoa`)  
**Commit:** none (agent ordered not to commit)  
**Unity compile gate:** not run (lead gates)

## Claim

First-class **Homes** switcher is wired so the owner can move between Elarion (castle hub) and the captured village without hunting the Realm deck or restoring `OwnedTownPanel`.

## What shipped

1. **`PanelId.Homes = 30`** (append-only) in `Assets/_Modules/Core/UI/PanelRouter.cs`.
2. **`HomesSwitcherPanel`** + **`HomesSwitcherPanelBootstrap`** (`DeNelle.HUD`):
   - Bootstrap is the PanelDoorRegression **D2** root (`[RuntimeInitializeOnLoadMethod]` + `AddComponent<HomesSwitcherPanel>`).
   - Spawns on hub **or** owned-town scenes; skips raids / enemy-owned.
   - Plate title `ownedTown.homes`; rows use `ownedTown.castle` (Elarion) and reused `ownedTown.enter` (Your town).
   - Current scene row: gray, not tappable, ASCII `(here)` marker. Other row: gold face.
   - Switch calls `SceneRouter.GoCastle` / `SceneRouter.GoOwnedTown` under `Guard.Try` + `FlowTrace`.
   - Open refuses when `OwnedBaseProgression.Validate` is false.
3. **HudKit Homes chip** (gear-family, own root canvas like the safety-net Settings door):
   - Built always; `TickHomesChip` shows it only when Validate is true on hub/owned-town.
   - Tap → `PanelRouter.Open(PanelId.Homes)`.
4. **Realm deck visit button RETIRED** in `PlayerDeckWorkspace` — one public Homes door (the chip).  
   PanelDoorRegression does **not** need that constructor; Bootstrap is the D2 root.
5. **Locale:** `ownedTown.homes` added in all 10 catalogs × dual-copy (`Resources` + `StreamingAssets`); byte-identical verified. Reused `ownedTown.enter` / existing `ownedTown.castle`.
6. **Regression** `HomesSwitcherRegression` `[homes-switcher]` cases A–D registered in `DataRegression.cs` next to `[owned-town-hud]`.

## Explicit non-goals honored

- Did **not** restore `OwnedTownPanel` as a door.
- Did **not** implement WO-1882 chat gear row.
- HUD assembly only — no `using DeNelle.Village` in HUD files (`SceneRouter` + `OwnedBaseProgression` are Core).

## Hygiene

- `python tools/gate_brace.py` on edited `.cs`: **GATE_BRACE_SUMMARY bad=0**.
- NUL scan on edited `.cs`: clean.
- No `BOARD.html`, no APK, no git commit.

## Lead still owes

- Unity `COMPILE_GATE_OK`
- Fresh `REGRESSION_OK n/n` including `[homes-switcher]`
- Felt: castle → Homes → Your town; owned town → Homes → Elarion
