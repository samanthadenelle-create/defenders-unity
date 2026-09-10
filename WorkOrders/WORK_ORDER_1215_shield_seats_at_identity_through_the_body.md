# WORK ORDER 1215 - A dropped shield attaches at IDENTITY and sits through the hero's body

**Status:** CLOSED 2026-09-10 - owner validated on device ("1215 i already validated and should be closed"). PRIOR STATUS: READY TO IMPLEMENT - cause NOT proven 2026-09-06 (the WO-1215 seat path cannot be the live starter shield, fullOverride=True short-circuits); permanent MEASURED facets added to EquipmentController + registryProbe, gated; needs one device capture of a hero holding a shield + screenshot
**Silo:** Gear seating / attachment orientation
**Origin:** Owner felt-test, Seeker build `2026.08.26.341419`, 2026-08-26.
Owner verbatim: *"and shield sits through the body not seated correctly"*.

## PROOF (captured, not inferred)

- Device screenshot `tmp/shield-seat-101829.png` - the shield renders as a flat, face-on slab
  intersecting the hero's chest and upper arm. Not gripped, not angled, not offset outward.
- `Assets/OffsetForge/offsets.json`, read at source this session: **26 rows total, only TWO mention
  a shield** - `shield_A` (hand-dialled: rot -160/-180/-84, pos 0.12/-0.01/0, scale 1.04) and
  `ShieldWithItemLogic` (**rot 0,0,0** - identity, scale 1.733).
- `Assets/Resources/Data/Canonical/weapons.json`: **19 shield entries exist** - `tripo_shield_a`
  plus `blink_shield1h_01..25`. **Eighteen have no offset row at all**, so they attach at identity -
  exactly the pose in the screenshot.

## Why this is a principle violation, not merely a bug

`docs/ARCHITECTURE_PRINCIPLES.md` §4, verbatim:

> *"Orientation, grip, seat, and scale of any asset are DERIVED - from the mesh BOUNDS + the asset
> NAME - not guessed as a hand-typed Euler and **not left at identity**. Prior sessions ignored this
> and attached weapons at identity (blades laid flat / gripped by the blade) - that is a principle
> violation even though it compiles."*

⛔ **Read `docs/WEAPON_ARMOR_ORIENT_LOGIC.md` before touching any attach / placement / orient code.**
It is binding canon and carries the algorithm.

## The fix is the DERIVER, not eighteen hand-typed Eulers

Hand-authoring 18 rows is the exact failure this repo keeps paying for: **a value authored BY HAND
instead of DERIVED from the thing it describes.** The 2026-08-06 sweep found four of these in one
day (`IsLoop` 53 of 122 picks wrong, the self-contained VFX flag, `HeroTalentNodeDef.Hidden`, the
capture resolution that was a label rather than a layout).

**Required:** extend the existing derivation so a shield with no authored row gets a correct seat
from its own geometry - bounds give the axes (a shield's broadest face is its plane; the grip sits
on the inward normal, offset outward from the forearm), the name gives the archetype.

⛔ **`manual = true` rows are CANON and are NEVER overwritten by the auto pass** (§4). `shield_A`'s
hand-dialled values stay exactly as they are. The deriver fills the GAP - it does not re-derive what
a human already perfected.

⚠ **A shipped prop's mesh may have Read/Write OFF.** The 2026-08-21 sheathe work records that
vertex-based approaches are **silently inert ON DEVICE** while looking correct in the editor. Derive
from `mesh.bounds`, which is available regardless - never from vertex data.

## Acceptance

1. `COMPILE_GATE_OK` + `REGRESSION_OK <n>/<n> suites` on fresh logs, counts read off the marker.
2. ⭐ **A DEVICE SCREENSHOT of the hero holding a shield, opened and looked at.**
   ⛔ Headless gates CANNOT see orientation. This is stated canon and `bb6dc010` proved it by laying
   the entire town on its side **with every marker green**. A green gate is NOT acceptance here.
3. Spot-check **at least four different `blink_shield1h_*` ids**, not one. One correct shield proves
   one shield.
4. `shield_A` is byte-identical after the change - prove it, do not assert it.
5. Owner felt-verifies and CLOSES.

## What NOT to touch

- ⛔ The eight structure `-90` pitch rows. Different lane, and they are CORRECT (their FBX metas
  read `bakeAxisConversion: 0`). A "tidy up the remaining -90s" pass breaks all eight including
  `collector_lumbermill`, the FTUE's first building.
- ⛔ The global `_sheatheLongAxisSign` - WO-1136 records that flipping it only moves the defect onto
  the other heroes.
- The equip LOGIC. That a Mage can hold a shield at all is **WO-1214**, a separate ticket.

---
## RCA re-verified 2026-09-04 (QA read-only pass)
**Verdict:** UNPROVABLE
**Evidence:**
- Owner bounce (`695a5c92b` 2026-09-03): "Fail" with no note, no shield id, no hero, no capture. `logs/f8-inbox/device/SM02G4061955851/` holds nothing dated 09-03/09-04 about a shield.
- The fix is in the tree: `Assets/_Modules/Core/Geometry/WeaponOrientHelper.cs:319` `ManualSeatIsSubstantiated(bool manual, bool rowIsGenerated, bool hasAuthoredSeat)`, `:329-331` the 3-arg `MayDerive` overload; `:288` comment "All 19 shield rows: 18 have NO row in offsets.json". Pinned by `AttachmentOffsetRegression.cs` `Case6_ShieldSeatSubstantiation`. Landed `3eb499b88` 2026-08-26, flipped FIXED in `c7268971d`.
- Data drift: `offsets.json:4` `shield_A` (rot -160/-180/-84, scale 1.04, fullOverride) unchanged; `offsets.json:24` `ShieldWithItemLogic` is now rot 1.915/-48.302/-127.941, pos -0.103/0.164/-0.238, scale 0.71 - this WO's "rot 0,0,0 scale 1.733" is stale (`74d9e6546` 2026-08-30). `weapons.json` has 20 shield ids, not 19 (`knight_shield_starter` at `:70`).
- PROD-019 s0b (REOPENED 2026-08-30): a device trace shows the locked row applying byte-exact on `Socket_Shield` and says "THE SEAT IS NOT THE DEFECT. Stop dialling it"; the device reports `ShieldHandleSide ... AMBIGUOUS margin=0.072 < 0.1`. WO-1226 and WO-1214 are CLOSED; neither supersedes this ticket.
- New since: `GearSeat.cs` (`EnsureShieldSocket:138`, `OrientShieldSocket:180`); mesh Read/Write fixed under WO-1284.
**What changed since the RCA:** the identity gap is closed at the gate (18 shields no longer derive from a missing row); the `ShieldWithItemLogic` row was re-dialled; whether the DERIVED pose reads right on device is unmeasured.
**Ready for a lane?** no - the failing case must be named first; PROD-019 already shows the knight's row applying correctly. Files a lane would touch: `WeaponOrientHelper.cs` (`TryResolveShieldFrame:514`, `TryComputeShieldMountRotation:627/647`, `TryResolveShieldHandleSide:727`), `GearSeat.cs`.
**Pins/rulings needed:** a device screenshot plus logcat `AttachOffHandProp MEASURED` / `ShieldHandleSide` lines for the exact shield id and hero she saw (`blink_shield1h_*` vs `ShieldWithItemLogic`).

---
## 2026-09-06 — implementation pass: CAUSE STILL NOT PROVEN; the missing FACET is now instrumented

**Verdict: the WO-1215 code path cannot be the defect for the LIVE starter shield.** Proving line,
`logs/device/pull-20260830/boot-logcat.txt` (device, build after the 08-26 fix):

> `[Flow:Equip] off-hand seat NOT derived for 'knight_shield_starter' key='ShieldWithItemLogic':`
> `source=AuthoredOffset (authoredRow=True manual=False native=True fullOverride=True) — keeping the`
> `preset euler (-58.00, 16.00, -90.00).`
> `AttachOffHandProp MEASURED after hold: id='knight_shield_starter' parent='Socket_Shield'`
> `state=SHEATHED fullOverride=True comp=False GRIP 'EquipmentProp_OffHand' parent='Socket_Shield'`
> `lPos=(-0.103, 0.164, -0.238) lEuler=(1.9, 311.7, 232.1)`

`fullOverride=True` short-circuits before `ManualSeatIsSubstantiated` ever runs, and the measured GRIP
is `offsets.json:24` applied byte-exact. `AttachmentOffsetRegression` pins the gate truth table and a
source lint; it asserts **nothing** about the on-device grip TRS, so its green was always orthogonal
to what she saw. Matches PROD-019 s0b: *"THE SEAT IS NOT THE DEFECT."*

**Why it stays unprovable.** No capture in the repo records a shield attach after the bounce:
- `logs/device/freeze-20260904-095249.log` and `full-buffer-094110.log` (build `2026.09.04.354315`,
  i.e. AFTER 09-03, so not the build under test) contain **0** occurrences each of
  `AttachOffHandProp`, `Socket_Shield`, `EquipmentProp_OffHand`, `knight_shield_starter`,
  `blink_shield`. The session (pid 22805) is a KNIGHT — `[Flow:HudModel] ability bar bound: bar=qwer
  class='knight' source=GameState(persisted) hero='Grom'` — on a body GameObject named
  `Hero (Blaise)`, yet it attaches `tripo_staff_a` and logs `seat-proof … state=SHEATHED
  offHand='<null>' prop=False`. So the loadout carried **no off-hand at all**: nothing to seat, hence
  nothing to measure. (A knight holding a staff with an empty off-hand is a separate, PROD-019-shaped
  data point — recorded here, not acted on in this silo.) Zero `RemoteProviderException`/404, so this
  is not a §16 bundle miss.
- `logs/f8-inbox/QUEUE.jsonl` holds nothing on a shield for 09-03..09-06.
- `proof/owner-validations.json:106` records `"verdict": "Fail"` with `"note": ""` (ingested
  `695a5c92b`, 2026-09-03 18:15).
- Seat frame has NOT drifted since the row was dialled: the only two commits touching
  `EquipmentController.cs` after `74d9e6546` are `dcd25e9fe` (null-guard on a destroyed hand) and
  `2f167c8d6` (the D-SEAT oracle, "writes no transform"). `GearSeat.cs` and both `offsets.json`
  untouched; `Assets/OffsetForge/offsets.json` and `Assets/Resources/OffsetForge/offsets.json` are
  byte-identical.
- The Resources offset path IS live on Android: `[Flow:Offset] WO-994 registryProbe path=START
  rows=26 shield_A[…]` appears in the 09-04 device log.

**Instrumented instead of guessed (§12).** Two facts the RCA needed and no log has ever carried:
1. `AttachOffHandProp MEASURED` now names `key=`, `class=` (the wearer's class via
   `GearLoadout.WearerClass`), `body=`, `hand=`, `authoredRow=` and `derived=` alongside the pose.
2. The `registryProbe` START/SCENELOAD lines now print `ShieldWithItemLogic[…]` and
   `ShieldWithItemLogic@sheathed[…]` — the row the starter shield actually uses. They previously
   printed only `shield_A`, the **tripo** shield the default hero never wears.
Both pinned by new `Require` tokens in `AttachmentOffsetRegression.Case2_TripwireWiringLint`.

**Build she tested: UNPROVABLE from the repo.** No APK, `logs/apk-build*.log` or version stamp dates
to 2026-09-03 (newest build artefacts: `logs/apk-build.log` 08-08, `Builds/r2-parity.log` 09-06); the
oldest post-bounce device log is 09-04 build `2026.09.04.354315`. Stated, not inferred.

**THE CAPTURE THAT CLOSES THIS (name it when asking her):** with a hero holding a shield, on a build
whose version stamp is in the same log —
`adb logcat` filtered to `Flow:Equip|Flow:Offset`, keeping `registryProbe path=START`,
`AttachOffHandProp MEASURED after hold`, `off-hand seat (DERIVED|NOT derived)`, `ShieldFrame`,
`ShieldHandleSide` — **plus a device screenshot of that same hero**. One line each and the ticket is
decidable; without them any further seat edit is a guess.

**Files touched:** `Assets/_Modules/Village/Hero/EquipmentController.cs`,
`Assets/Editor/Regression/AttachmentOffsetRegression.cs`. Braces 904/904 and 71/71, 0 NUL bytes.
Needs `COMPILE_GATE_OK` + `REGRESSION_OK` from the CLI seat (this pass ran no Unity).
