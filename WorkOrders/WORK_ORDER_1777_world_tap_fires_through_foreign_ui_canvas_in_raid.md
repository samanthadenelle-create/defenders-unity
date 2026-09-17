# WORK ORDER 1777 — In a raid, a press on the **ability row** is ALSO consumed as a world tap: the UI guard can only see the controller's own canvas

**Status:** IMPLEMENTED, NOT YET GATED

*(2026-09-16, lane A · raid input. Edit-only: no Unity, no compile gate, no regression run, no commit.
`gate_brace` exit 0 + zero NUL bytes on both touched `.cs`. Fix + regression pins:
`WorkOrders/WORK_ORDER_1777_world_tap_fires_through_foreign_ui_canvas_in_raid.RESULT.md`.
⚠ Acceptance 1-3 need a fresh Seeker capture, which this lane could not take; §4's open question —
WHICH ability face sits at x≈820 — is still unproven, and the fix is deliberately built so it does
not depend on the answer. §4 item 3, also calling `VirtualJoystick.IsInZone` here, is DEFERRED per
the lead's instruction to leave the joystick exclusion as it is; the RESULT records why it is a
no-op anyway.)*

**Minted:** 2026-09-16 by the raid-polish audit lane (number PRE-ASSIGNED from the block 1777-1790; this lane did NOT touch `CLI_LANES_WO_NUMBERS.md`)

**Silo:** raid world-tap input — `Assets/_Modules/Village/Troops/RaidDeployController.cs`. **No `.unity`, no bake, no AI, no camera, no wall geometry, no HUD code.**

**Build under test:** `2026.09.16.371701` (`ProjectSettings/ProjectSettings.asset:148`), built 2026-09-15 22:06 from `f6653501f`.

**Video path:** act 2 — the Mage casts constantly while troops are deployed and Breach is armed. P0 on the **Deploy** path (§3b).

---

## 1. SYMPTOM

Every press on the bottom ability row during a raid is consumed **twice**: once by the ability, and once by `RaidDeployController` as a tap on the world under the finger.

- With **Breach** armed the second consumption is harmless noise — the log says so: *"the standing order, if any, is UNCHANGED."*
- With **Deploy** armed the same press **deploys a troop at the ground 6-7 m in front of the hero**, because `HandleDeployTap` shares the identical guard (`:714`, `:721`).

## 2. EVIDENCE — re-run by this lane, not taken on trust

`Logs/device/pull-20260916-143101-bastion-owner-run/logcat_full.txt`, `grep -oE 'HandleBreachTap IN - screenPoint=\([0-9.]+, [0-9.]+\)' | sort | uniq -c` returns **41 distinct points**:

| taps | screenPoint | outcome |
|---|---|---|
| **40** | x **774-876**, y **78-130** | `not_wall_segment` ×40 |
| **1** | **(1333.00, 573.00)** | **`success`**, `wall='Wall_Keep1_SS_0'` |

Screen is **2670x1200**; Unity screen space has its origin **bottom-left**. So the 40 points sit in a ~100x50 px box in the **bottom 6.5-11%** of the screen, **29-33% across**. Every ray points steeply down (dir y ≈ −0.59 to −0.72) and dies on the ground 6.28-7.28 m out, and the unmasked 800 m `RaycastAll` printed **exactly one hit** — `[0] 'RaidGround' layer=Default isWallSegment=False`. Nothing was aimed at. **40 presses in a 100x50 px box is a finger on a fixed button.**

## 3. THE BUTTON, AND THE GUARD THAT CANNOT SEE IT

**3a. It is the ability row, named from layout data.** `RaidDeployController.cs:1923-1932` records the owner's 2026-09-06 ruling — *"the ABILITY ROW owns the thumb position at the bottom; this bar stacks ABOVE it"* — and measures the band at source: `kit actionBar (holds combatDock) Y 0.015-0.150`. At 1200 px that is **y = 18-180**, and the observed taps fall at **y 78-130 — inside it**. Corroborating: the Mage cast constantly in this run (`[Flow:Ranged] FireSpellOrb` ×54, `[Flow:VFXManager] PlayOneshot('Cast_MuzzleFlash')` ×54).

⛔ **The virtual joystick is RULED OUT by arithmetic, so do not chase it.** `VirtualJoystick.ComputeZone` (`Assets/_Modules/Village/Hero/VirtualJoystick.cs:152-158`) gives `radius = max(60, min(w,h)*0.16) = 192`, `center = (259.2, 259.2)`, and `IsInZone` (`:165-175`) accepts within `radius*1.7 = 326.4`. At y = 105 the zone reaches x ≈ 547; the taps start at x = 774. **Outside.** (Note that every *other* world-tap consumer does call this guard — `BuildModeController.cs:1035`, `ArenaDefenseSetupController.cs:237`, `CameraPanInput.cs` — and `RaidDeployController` calls it **nowhere**. Adding it is worth doing, but it is not what catches these 40.)

**3b. The guard, and why it is narrow.** `RaidDeployController.IsPointerOverUi:731-742` does a full `EventSystem.RaycastAll` and then throws most of it away:

```csharp
foreach (var h in hits)
    if (h.gameObject != null && _ui != null && h.gameObject.transform.IsChildOf(_ui.transform))
        return true;
```

Only hits under **this controller's own canvas** count. The ability row is built by `HudKitController.BuildAbilityRow` (`Assets/_Modules/HUD/Kit/HudKitController.cs:3139`, registered `:3205`) — a **different canvas, in `DeNelle.HUD`**.

⚠ **And that narrowing is not laziness — it is the assembly boundary.** The same file records it at `:1938-1941`: *"This class is in DeNelle.Village, which may not reference DeNelle.HUD (CLAUDE.md §5), so it cannot ask the kit for a free band."* It cannot name the HUD's canvas either. **So the fix must NOT add a reference to `DeNelle.HUD`.**

## 4. THE FIX

1. `IsPointerOverUi:731-742` — make the test **transform-agnostic**: accept any raycast hit that is a live interactable UI graphic, regardless of which canvas owns it, instead of testing `IsChildOf(_ui)`. Component-level identity (`Selectable` / `Graphic` with `raycastTarget`) crosses the assembly boundary; a type reference to `DeNelle.HUD` does not. ⛔ **Do not add a `.asmdef` reference** (CLAUDE.md §5).
2. ⚠ **Guard against over-rejection.** `RaidHudController`'s readout is deliberately tap-transparent; a rule that rejects *any* hit including non-interactable backing plates would swallow legitimate world taps under the HUD band. Test for **interactable** graphics only, and trace the rejection naming which canvas absorbed it.
3. Also call `VirtualJoystick.IsInZone(screenPoint)` here, matching every other world-tap consumer. Cheap, and closes the bottom-left corner this controller currently leaves open.
4. A breach tap that resolves nothing should say something to the player.

## 5. ACCEPTANCE

1. A fresh Seeker capture of a Bastion raid in which the player, with **Deploy** armed, presses each ability face once: **no troop is deployed** (screenshot) and `grep -c "HandleDeployTap IN"` counts zero taps with y < 200.
2. Same with **Breach** armed: `grep -c "HandleBreachTap IN"` counts zero taps with y < 200.
3. A genuine mid-screen wall tap still logs `outcome=success` + `source=order focus='Wall_…'`, and a mid-screen ground tap still deploys.
4. ⚠ **Still unproven and cheap to close:** *which* ability face sits at x≈820. The band is proven, the face is not — one in-game screencap at 2670x1200 names it. Not required to implement, required to close cleanly.
5. `python tools/gate_brace.py` exit 0, then `COMPILE_GATE_OK` + `REGRESSION_OK <n>/<n>` on fresh logs.

## 6. DO NOT TOUCH

The wall resolver (`RaycastGround` `:909`, `GetComponentInParent<WallSegment>` `:924`) — **correct; see WO-1790 §1 for the false collider-width theory this lane first wrote and retracted.** The collider sizing in `RaidBaseGenerator.cs` / `RaidBaseDresser.cs` (WO-1723 §6 already retired that reading). `HudKitController.cs` and anything in `DeNelle.HUD`. The diagnostic block at `:1034-1051` — **WO-1790 owns it.** Any `.unity` file.
