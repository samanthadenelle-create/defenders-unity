# WO-1462: RaidDeployScreen has no backdrop - the town bleeds through the panel

**Status:** IMPLEMENTED - d6511b8e5 on HEAD, landed 2026-09-07 (was: uncommitted, awaiting gate); gated by the 2026-09-09 wave (REGRESSION 492/492 on Builds/wave1-reg3); owner felt-test closes (was: IN PROGRESS - lane handed back edits 2026-09-06 (uncommitted, awaiting the wave-two compile + regression gate); prior: READY TO IMPLEMENT)
**Silo:** `Assets/_Modules/Village/Hero/RaidDeployScreen.cs` + `RaidSelectionLayoutRegression`. Same class
WO-1442 fixed one screen earlier on RaidSelectionScreen.
**Source:** read-only audit fleet 2026-09-06 (CLI seat), minted from the banner
(`CLI_LANES_WO_NUMBERS.md`, main line 1462 -> 1463 in the same edit).

## 1. EVIDENCE

```
RaidDeployScreen.cs:144-145   passes withBackdrop: false to FrameCore
```

Device capture: `Logs/device/screens/seeker-357453-raid-deploy.png` - world geometry and world text are
legible behind the deploy panel.

WO-1442 fixed exactly this on `RaidSelectionScreen`, in the same modal family, five days ago. The sibling
screen was never checked.

## 2. FIX SHAPE

- Stop passing `withBackdrop: false`; take the FrameCore default the selection screen now uses.
- Pin it in `RaidSelectionLayoutRegression` as a SIBLING case, so the whole modal family is covered by one
  oracle rather than one screen at a time.

## 3. WHAT NOT TO DO
- Do not paint a bespoke opaque quad on this screen. FrameCore already owns the backdrop; a second one is a
  second authority.

## 4. ACCEPTANCE
- [ ] `withBackdrop: false` gone; file:line in the RESULT.
- [ ] Sibling case in `RaidSelectionLayoutRegression` covering every FrameCore modal in the raid family.
- [ ] A fresh capture of the deploy screen opened, with no world text visible through it.
- [ ] `REGRESSION_OK n/n` on a fresh log.
