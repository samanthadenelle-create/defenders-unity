# WO-1476: a VFX rises over the town along Y and must be removed or turned off

**Status:** IMPLEMENTED - d6511b8e5 on HEAD, landed 2026-09-07 (was: uncommitted, awaiting gate); gated by the 2026-09-09 wave (REGRESSION 492/492 on Builds/wave1-reg3); owner felt-test closes (was: IN PROGRESS - lane handed back edits 2026-09-06 (uncommitted, awaiting the wave-two compile + regression gate); prior: READY TO IMPLEMENT (identify from a capture FIRST))
**Silo:** VFX + the town scene's effect manifest.
**Source:** read-only audit fleet 2026-09-06 (CLI seat), minted from the banner
(`CLI_LANES_WO_NUMBERS.md`, main line 1476 -> 1477 in the same edit).

## 1. EVIDENCE

Owner validation note on UI-001, 2026-09-07T00:50Z, verbatim:

```
there is a VFX exiting about town along Y and it needs removed or turned off
```

**TWO CANDIDATES, from the VFX audit later the same session** (candidates, not a conclusion):

```
TreeofLifeAura_Aura   -> FireFlies.prefab     upward motes on the Heart   [Flow:Vfx] STUCK LOOP x2
atfootprintoftree_Aura -> Aura_Nature.prefab                              [Flow:Vfx] STUCK LOOP x2
```

Both come from the `VfxManualPicks.json` mapping. `Main_Castle_Overworld.unity` contains ZERO baked
ParticleSystems, so whatever it is, the fix is in the RUNTIME picks, not the scene. **WO-1002 (hub tree aura)
is the likely prior** - read it before editing.

Note both candidates are also stuck loops, so they overlap WO-1473's release-policy work.

## 2. FIX SHAPE

- Take a device capture of the town and locate the emitter from the frame. For a visual defect the screenshot
  IS the data (memory `screenshots-are-primary-evidence-for-visual-defects`); do not guess the emitter from
  the VFX registry.
- Disable it AT SOURCE - the scene object or the manifest row that spawns it - not by a runtime suppression.
- Add the emitter to the scene's VFX manifest regression so a re-add is caught.

## 3. WHAT NOT TO DO
- Do not delete a VFX prefab that other scenes use; turn off the town instance.
- Do not pick a replacement effect. If the owner wants something in its place she will say so.

## 4. ACCEPTANCE
- [ ] The emitter is NAMED, with the capture that identified it attached.
- [ ] A fresh town capture shows it gone, opened in the RESULT.
- [ ] Scene VFX manifest case added.
- [ ] `REGRESSION_OK n/n` on a fresh log.
