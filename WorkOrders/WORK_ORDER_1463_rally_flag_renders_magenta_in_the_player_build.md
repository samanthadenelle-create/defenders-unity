# WO-1463: the rally flag renders MAGENTA in the player build - a built-in-pipeline material under URP

**Status:** IMPLEMENTED - d6511b8e5 on HEAD, landed 2026-09-07 (was: uncommitted, awaiting gate); gated by the 2026-09-09 wave (REGRESSION 492/492 on Builds/wave1-reg3); owner felt-test closes (was: IN PROGRESS - lane handed back edits 2026-09-06 (uncommitted, awaiting the wave-two compile + regression gate); prior: READY TO IMPLEMENT)
**Silo:** `Assets/_Modules/Village/Troops/RaidDeployController.cs` (rally flag construction).
**Source:** read-only audit fleet 2026-09-06 (CLI seat), minted from the banner
(`CLI_LANES_WO_NUMBERS.md`, main line 1463 -> 1464 in the same edit).

## 1. EVIDENCE

```
RaidDeployController.cs:640-654   GameObject.CreatePrimitive(...)   // built-in default material
RaidDeployController.cs:671-675   TintRenderer sets material.color on that default material
```

`CreatePrimitive` assigns Unity's built-in `Default-Material`, which has no URP variant, so it renders as the
magenta error shader in a URP player build. Setting `.color` on it changes nothing.

Owner capture: `Logs/device/screens/owner-raid-ui-2026-09-06-143701.png` - the rally flag is a magenta block.

## 2. FIX SHAPE

- Assign a URP material from the kit to the flag immediately after construction (or replace the primitive with
  an authored kit prefab), then tint that.
- Regression: forbid `GameObject.CreatePrimitive` anywhere under `_Modules` without an explicit URP material
  assigned in the same method. That is the durable half; the flag is today's instance.

## 3. WHAT NOT TO DO
- Do not pick a hue. The owner is colourblind (memory `owner-colorblind-delegate-visual-creative`) - take the
  kit's existing rally/marker colour, do not invent one.

## 4. ACCEPTANCE
- [ ] Flag renders in the kit colour on a device capture, opened in the RESULT.
- [ ] The `CreatePrimitive` regression exists and goes red on a bare primitive.
- [ ] `REGRESSION_OK n/n` on a fresh log.
