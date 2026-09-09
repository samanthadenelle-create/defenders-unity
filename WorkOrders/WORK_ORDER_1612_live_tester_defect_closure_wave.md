# WO-1612 - Live tester defect closure wave

**Status:** FIXED — implemented and regression-gated for owner verification in the next Windows/APK build

## Owner captures (2026-09-09)

This card records the live tester findings that are already corrected in the build set. They belong in
Fixed—not Ready—because code/data and regression evidence now exist for each:

- move-placement keeps PLACE/ROTATE/CANCEL available and feedback never steals the click; feedback success copy does not overlap;
- default Echo Hollow, crafting station, and Store resolve as built from raw BaseLayout identity;
- completed research no longer advertises an available upgrade, and Harvest owns one Close button;
- Echo returns do not spawn on the hero; passive returns alert once and then settle silently;
- Welcome Back requires at least 30 minutes offline; shorter absences use normal stored-resource behavior;
- raid portals cannot leak around the town; raid troop deployment uses round portraits and Deploy All;
- army capacity is ten troop bodies rather than weighted power points;
- Syphon Essence has authored hot-swap art;
- Ballista levels 2 and 3 retain the correct upright/facing orientation;
- death settling is one-way toward the sampled ground, preventing airborne corpse ratcheting;
- maxed Healing Caravan has no upgrade bubble and its yellow filled plate is hidden;
- storage pallets sit outside the building footprint rather than stacking beneath the structure;
- Mage defense is Arcane Shell with its authored protection aura instead of a physical shield action.

## Evidence

`COMPILE_GATE_OK`; full `REGRESSION_OK`. Dedicated markers cover feedback, civic build identity,
research completion, harvest shape/offline behavior, portal presence, raid deploy UI, troop roster,
concept icons, Ballista orientation, enemy death grounding, caravan surface, storage placement, and
Mage protection dock.

The reported “stuck in battle mode” capture was not a stale latch: the player log proved Wave 23 was
Active with living `wave23-*` enemies. F8 intentionally pauses `Time.timeScale`; closing F8 resumes it.
