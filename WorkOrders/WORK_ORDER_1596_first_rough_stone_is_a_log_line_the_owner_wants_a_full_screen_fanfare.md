# WORK ORDER 1596 - Earning the Rough Stone is a log line; the owner wants a full-screen fanfare that says THIS IS A BIG DEAL

**Status:** CLOSED 2026-09-08 - owner felt-test PASS (validated 2026-09-09T02:10:48, build 2026.09.08.361259). PRIOR STATUS: FIXED - implemented in the 2026-09-07 gate wave (COMPILE_GATE_OK Builds/cg-wave9.log 10:40, REGRESSION_OK 446/446 Builds/reg-wave9.log 11:02); reaches the Seeker with the next tester build; owner felt-test closes it. PRIOR STATUS: READY TO IMPLEMENT - minted 2026-09-07 (CLI) from the owner's words, live from her Seeker run
**Silo / Lane:** Dungeons exit payout -> presentation: `Assets/_Modules/Dungeons/DungeonController.cs` (the run payout, ~:560-600), `DungeonExitInteractable.cs` (the exit confirm and scene route), a NEW full-screen presentation panel (View + VM, MVVM; the payout stays the one producer), `Assets/_Modules/Core/Catalog/DungeonRunPayout.cs`
**Type:** EXISTING system, PRESENTATION GAP (owner ruling)
**Priority:** P1 - the Jeweler / Rings of Power loop is invisible without it

## Owner, verbatim (2026-09-07 09:46, after her first Sunken Vault clear)

> "ok so i got the crystal but that scren need to be a big moment fanfare full screen, the user needs
> to know that this is a BIG deal"

## Evidence (device log)

```
09:44:07.179 [Flow:DungeonExit] exit CONFIRM RESOLVED face=continue-to-exit
09:44:07.189 [Flow:JewelPolish] pending polish scores: pushed 2 (now 1 stone(s) waiting).
09:44:07.189 [Flow:JewelPolish] run payout (composed exit): 1x 'ing_rough_stone' granted (polish score 2; boss=True, encounters=0, chests=0, secrets=0). Take it to the Jeweler.
09:44:07.189 [Flow:DungeonExit] taking RETURN exit -> SceneRouter.Castle ('Main_Castle_Overworld')
```

The grant, the "Take it to the Jeweler" and the scene route all happen in the SAME 10 ms. Nothing is
shown: the town frame captured at 09:46 (`Logs/device/seeker-shots/screen-20260907-0946.png`) carries no
trace of it. The first Rough Stone is guaranteed exactly once per player (`DungeonController.cs`
`firstDungeonStone`), it is the door to the Jeweler and Rings of Power, and today the player learns it
from nothing. The treasure cache, by contrast, gets a modal (`DungeonTreasurePanel`).

## What to build

- A **full-screen** moment (safe-area, not a 60% plate; ruling 29 applies to every screen) that opens the
  instant the payout grants, BEFORE the scene route runs - the exit waits for the player's tap. Shape:
  the stone's art large and centred (`ing_rough_stone` sprite; if it has none, name it as an ART ASK and
  use the crystal glyph), a title in the kit's display face ("A ROUGH STONE"), one line that says what it
  is and why it matters ("Unpolished, unidentified. The Jeweler can polish it into a Ring of Power."),
  the polish score as stars (score 0..3), and ONE verb: "TAKE IT TO THE JEWELER" for the first stone /
  "TAKE" afterwards. Fanfare: the owner-tagged VFX/audio hooks only (memory `vfx-map-owner-tags-no-creative-pick`)
  - use the existing celebration/levelup SFX id and the marquee VFX hook if one is tagged; do not invent a
  new prefab. First-ever stone = the big version; later stones (15% roll) = the same panel, shorter copy.
- MVVM: a VM composes (stone id, name, score, first-ever, copy) from the payout; the View renders. The
  payout in `DungeonController` stays the ONE producer of the grant; the panel never touches inventory.
- `FlowTrace.Step("JewelPolish", "ROUGH STONE FANFARE shown first=... score=...")` and a Step on dismiss,
  so the device log proves the beat. Guard the panel: if it throws, the exit still proceeds (never a dead
  exit) and a `Fail` names it.
- Headless capture entry for the panel (both first and repeat), and a regression: the exit route waits
  for the dismiss; the panel opens for a grant and not for a missed roll; copy is ASCII; touch floor met.

## Not to touch
- The grant rule itself (first guaranteed, 15% after), `JewelPolishService`, the Jeweler panels.
- `ComposedLockedPort.cs` / door visuals (WO-1588 lane), `BreakableContainer.cs` (WO-1589 lane), `Enemy.cs` (WO-1590 lane).

## Acceptance
- Headless frame: full screen, stone art, title, one-line meaning, stars, one verb, kit chrome.
- Device: on the next first-stone exit the log shows FANFARE shown -> dismissed -> scene route, in that order.
- Regression green, REGRESSION_OK n/n on a fresh log. Owner felt-test closes.
