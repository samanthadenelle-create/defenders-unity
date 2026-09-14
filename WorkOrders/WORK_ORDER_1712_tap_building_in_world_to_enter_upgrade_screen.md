# WORK ORDER 1712 - Tap a building in the world to jump directly into its upgrade screen

**Status:** READY TO IMPLEMENT - owner nice-to-have, NOT blocking, low priority
**Minted:** 2026-09-14 by the CLI lead (Fable seat), from the owner's felt-test of the 2026-09-14
tester build (release 2026.09.14.369302)

## 1. Owner request, verbatim

> "The last thing that I would like to see, but it's not required. It would be nice to be able to
> click on the building directly from the city or the view and be able to enter into the upgrade so
> if you're standing there at a tower, you're like wow I didn't upgrade this one click on it and have
> the ability to upgrade it directly from that screen, that would be nice, but it's not really
> necessary because they could always go through the build screen or the managed screen, but I just
> think it might be a nice option just to somehow have the ability to at least enter into the upgrade
> screen from there"

## 2. Scope

Tapping a structure in the world (town/overworld view) should offer a path directly into that
structure's upgrade UI - either the existing per-structure upgrade panel or a route into the Manage
screen pre-focused on that structure. The owner explicitly said this can piggyback on EXISTING UI
rather than invent a new one: "they could always go through the build screen or the managed screen" -
the ask is a shortcut INTO one of those, not a third screen.

- Find the existing world-tap handling for structures (whatever currently drives repair taps,
  per WO-1708's `WallRepairController`, and whatever drives structure inspection/selection elsewhere)
  and determine the cleanest existing entry point into the upgrade UI to route a tap to.
- Respect WO-1708's pointer-over-UI guard pattern - a world tap that opens UI must still not fire
  when the tap is over an existing UI element.
- Do not remove or change the existing Build screen / Manage screen paths to the same upgrade UI -
  this is an ADDITIONAL entry point, not a replacement.

## 3. Priority

Explicitly low priority - the owner said "not required" and "not really necessary" twice. Do not pull
this ahead of WO-1707 (white town) or any bug-shaped ticket; implement when the board has room.

## 4. Acceptance criteria

- [ ] Tapping a placed, upgradeable structure in the world opens its upgrade UI (or Manage screen
      pre-focused on it) without going through the Build menu first.
- [ ] A tap over existing UI (HUD, an open panel) does not trigger this.
- [ ] Existing Build-screen and Manage-screen upgrade paths are unchanged.
- [ ] Headless or capture-based proof that the tap routes correctly for at least one structure type.

## 5. Related context from the same felt-test (NOT part of this ticket, recorded for continuity)

- **Iron Mine duplicate-offer bug** (already fixed, WO-1710, commit `593823f7d`, in the APK currently
  building): the owner's live report confirms she saw two mines offered even though both existed. On
  reflection she is not certain whether the second was meant to be an "iron mine" or a future "stone
  mine" concept - see the bucket-list note below. No action beyond WO-1710's existing fix.
- **Crystal Mine stays buildable - CONFIRMED CORRECT, not a bug.** Owner, verbatim: "the crystal mine
  makes sense that we can leave it there... because we can add the crystal mines." WO-1710's RCA found
  nothing in the tree seeds a crystal mine at castle-builder time, so it was already correctly left
  offered. Do not add it to any "already built" registry.
- **Storage pallets (lumberyard/foundry/silo) - no action.** Owner confirms they look good and
  acknowledges the build menu still offers an additional pallet even though singletons exist, and
  reasons through the CoC-style case for allowing a second collector later, but lands on "ideally we
  don't really need them for this at this level" - i.e. no change requested now. Worth a note for
  whoever eventually designs a "second storage collector" feature, not an action item today.
- **Barracks not listed on the Defense screen despite being on the map** - this is the same bug as
  WO-1710 symptom 1 (already fixed, in the current build). No new information.
- **Bucket-list, not a ticket:** the owner wants to source a quarry/stone-mine art asset later for a
  possible stone-producing structure; she has no image for it yet. Do not mint a ticket for this -
  she said she will bring it when she has the art.
- **Engageability not yet checked** - owner has not verified every structure has an NPC/interaction
  point, but explicitly said reachability through the Manage screen alone is acceptable ("it's not
  even so much the NPC as long as they can get away into it through the manage screen I'm fine with
  that"). No ticket needed unless a specific structure is later found unreachable both ways.
