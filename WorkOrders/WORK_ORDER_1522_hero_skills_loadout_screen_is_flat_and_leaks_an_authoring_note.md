# WO-1522: the hero SKILLS / LOADOUT screen is flat, its icons are missing, and a developer note reaches the player

**Status:** IMPLEMENTED - d6511b8e5 on HEAD, landed 2026-09-07 (was: uncommitted, awaiting gate); gated by the 2026-09-09 wave (REGRESSION 492/492 on Builds/wave1-reg3); owner felt-test closes (prior: READY TO IMPLEMENT - owner ask, 2026-09-06 20:23)
**Silo:** hero skill tree + loadout - the talent tree panel, the learn dialog, the assigned-skills strip.
WO-1310 / 1342 / 1401 are prior art on this surface.
**Source:** read-only audit fleet 2026-09-06 (CLI seat), minted from the banner
(`CLI_LANES_WO_NUMBERS.md`, main line 1522 -> 1523 in the same edit).

## 1. EVIDENCE

Owner words: *"loadout screen feels very flat"*.

Device frame `Logs/device/screens/owner-screen-20260906-202355.png` (build 358574, 20:23):

```
tree      THREE nodes on a mostly empty panel
icons     TWO nodes draw a MAGENTA CHECKERBOARD where the icon should be
          (the "SYPHON ESSENCE ACTIVE" node and the clock node) - missing sprite
labels    "N..." and "ARCANE B..."   truncated
dialog    Arcane Shield learn dialog COVERS a node and reads:
          "Arcane Shield / Arcane Shell blocks 40% more damage and holds 2s longer."
          then, verbatim, a raw authoring note:
          "NO EFFECT YET - not implemented yet (data note: 'v2'). C"     <- cut mid-sentence
side      "WISDOM 1 - next point at Level 8"
strip     "Assigned skills - change them in LOADOUT."   1 ARCANE B... / 2 MEND / 3 EMPTY
```

The authoring note is the sharp one: a data-entry comment is being rendered to the player, and truncated
mid-word at that.

### CORRECTION 2026-09-06 (edit lane) - `icons` above is WRONG, and the wrong seam

The frame does not show a missing sprite. A node with no sprite draws a TWO-LETTER MONO LABEL
(`HeroSkillTreePanelMvvm.BuildGraphNode`, the `LoadIcon(...) == null` branch) and can never draw a
texture; a missing SHADER draws flat magenta, not tiles. What the frame shows is a multi-coloured tile
grid filling a SQUARE around exactly two plates - the focused one and the single owned one - and the
focused one's square is visibly the larger. That is `BuildVfxPatch`: peek **0.35** for the pointer rig,
**0.25** for the aura rig. The seam is `TalentNodeVfxRig`'s RenderTexture, bound to a `RawImage`.

PROVING LINES (`Logs/device/freeze-20260904-095249.log`, same code path on device):
```
07:48:40.676 [Flow:TalentPointer] attach: rig live - pointer loop presents on the focus node
07:48:45.911 [Flow:TalentAura]    attach: rig live - aura presents on owned nodes
```
Both rigs go live and bind patches. **NOT PROVEN:** why the render leaves the texture undefined on
device - no capture in the tree names a failing call, and no `Camera.Render` / URP warning appears in
any `Logs/device/*.log`. The fix therefore closes the CONSEQUENCE, not the cause: the RT is cleared to
the well ink on create and re-created + re-cleared on device context loss, so an undefined surface can
never be sampled. A `FlowTrace.Step` on the first `RenderTick` was added so the next device log says
whether the draw executes at all.

Consequently **acceptance item 4's `KnownGaps` clause does not apply** - `MageAbilityIconRegression` is
an uncommitted lane and this was never an icon defect. Left untouched.

## 2. FIX SHAPE

1. **Never paint an authoring note.** The learn dialog shows player copy ONLY. An unimplemented perk either
   does not appear in the tree, or carries the word `COMING` composed by the VM. Add a SOURCE case that fails
   on any `data note` / `not implemented` literal reaching a player-facing string.
2. **Every node has a sprite**, or the kit's authored fallback medallion - never the magenta error texture.
   `MageAbilityIconRegression`'s `KnownGaps` shrinks accordingly (it is one of WO-1495's undated allowlists).
3. **Node labels** use `FitSingleLine` or two lines. Never an ellipsis.
4. **Layout**: the tree FILLS the panel at the kit's node spacing, with branches drawn between nodes and the
   class portrait as the trunk. The learn dialog anchors BESIDE the node, never over it.
5. **The assigned-skills strip becomes the loadout rail**: large medallions, drag or tap-to-assign, and the
   class's locked Q named as locked (canon: Q is the locked basic, W/E/R are swappable).

The owner is red/green colourblind: state every node's status by WORD and ICON, never by hue.

## 3. WHAT NOT TO DO
- Do not invent perks to fill the tree. Sparse is a content question; this ticket is presentation.
- Do not silence the magenta by tinting it. A missing sprite must resolve to the authored fallback.

## 4. ACCEPTANCE
- [ ] Headless `SkillTree_*` and `Loadout_*` PNGs captured and OPENED.
- [ ] Measured no-overlap case: the learn dialog never covers a node; no label truncated.
- [ ] The source lint on authoring notes exists; RED proof stated with the current string.
- [ ] Zero magenta nodes in the capture; `KnownGaps` shrinks, with the removed ids listed.
- [ ] `REGRESSION_OK n/n` on a fresh log.
