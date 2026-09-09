# WORK ORDER 1588 - The locked dungeon port is an untextured white slab with a glowing keyhole, and the "Locked" prompt on screen is not the string the port's code owns

**Status:** CLOSED 2026-09-08 - owner felt-test PASS (validated 2026-09-09T02:08:53, build 2026.09.08.361259). PRIOR STATUS: FIXED - implemented in the 2026-09-07 gate wave (COMPILE_GATE_OK Builds/cg-wave9.log 10:40, REGRESSION_OK 446/446 Builds/reg-wave9.log 11:02); reaches the Seeker with the next tester build; owner felt-test closes it. PRIOR STATUS: READY TO IMPLEMENT (instrument first) - minted 2026-09-07 (CLI) from the owner's F8 flag seq 4699
**Silo / Lane:** Dungeons - `Assets/_Modules/Dungeons/ComposedLockedPort.cs`, the door visual seam from WO-1568 (`CommonDungeonDoor.cs` / `BuildDoorVisual`, `Assets/Resources/Dungeon/Door/*`), the prompt presenter
**Type:** EXISTING system, VISUAL + a duplicated-producer smell
**Priority:** P2

## Evidence

F8 flag seq 4699, device `SM02G4061955851`, 2026-09-07 14:34:31Z, scene `dg_sunken_vault`, message
`[dg_sunken_vault] on-screen FLAG button (mobile)`; frame
`logs/f8-inbox/device/SM02G4061955851/flag_20260907-143255_00.png`. Read off the frame:

1. The hero stands before a large plain WHITE slab (no texture, no frame, no lintel, a flat lit box) with a
   yellow glowing blob at hand height - the locked port. This is the "moving wall" the owner asked to
   retire on 2026-09-06 ("make the working doors look like doors instead of moving walls"); WO-1568 gave
   `CommonDungeonDoor` a KayKit leaf + frame + lintel, but the LOCKED port still draws the slab.
2. The prompt reads **"Locked — need key"** with an EM DASH. `ComposedLockedPort.cs:47` owns
   `promptLocked = "Locked - need key"` with a HYPHEN. So the text on screen is produced somewhere else
   (a second prompt string, a legacy port, or a formatter that rewrites the dash). WO-1333 retired em
   dashes from player copy; a second producer is how one comes back.
3. The owner's flag is not annotated; the chest-toast remark she sent a minute later is WO-1589, not this.
   If the trace shows the door is already the WO-1568 visual and only this port type differs, that is the
   finding.

## What to do

- **Instrument first:** `FlowTrace.Step("DungeonDoor", ...)` when a locked port builds its visual (which
  builder, which prefab/leaf, which prompt string and from which class), and when the prompt is shown
  (the exact string and its producer). Run the dungeon door headless capture (WO-1568 added
  `CaptureDoor` / `DUNGEON_DOOR_CAPTURE_OK`) extended to a LOCKED port and read the frame + trace.
- Route the locked port through the ONE door visual seam (`BuildDoorVisual`), locked state = same door,
  closed, with a lock/keyhole prop on the leaf; the glow stays as the affordance. No second door builder.
- ONE prompt producer: the port's own `promptLocked` string (ASCII hyphen) reaches the screen; delete or
  re-point the other. Extend the copy-hygiene oracle (WO-1333 / WO-1413 family) to the dungeon prompt
  strings.

## Not to touch
- Dungeon composition/layout data (`DungeonComposeLayout`), key/unlock logic, the Dungeon scene files
  (never hand-edit `.unity`; re-bake only in an isolated worktree - memory `dungeon-scene-shared-tree-corruption`).

## Acceptance
- Headless capture of a locked port reads as a closed door with a lock, not a slab; the prompt is
  `Locked - need key`; the trace names one builder and one prompt producer.
- Oracle green, REGRESSION_OK n/n on a fresh log. Owner felt-test closes.
