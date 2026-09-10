# WO-1616 RESULT — the raid NPC now reads the hero's shield seat, because there is only one

**Status:** IMPLEMENTED - awaiting gate (2026-09-09 lane NPC-SHIELD); owner felt-test on a raid closes
**Lane:** NPC-SHIELD (edit-only). Branch `dev`, started from the **WORKING TREE** (which carries lane
HERO-GRIP / WO-1431's uncommitted `EquipmentController.cs` + `WeaponOrientHelper.cs` edits), never from HEAD.
**Silo:** Hero + troop equipment seating (Hygiene silo, after lane 3A)
**Gate state:** ⛔ **NOT GATED.** No Unity run was fired by this lane (edit-only; the Unity lock belongs
to one seat). `COMPILE_GATE_OK` and `REGRESSION_OK <n>/<n> suites` are **OWED**. Judge them by the
marker on a FRESH log (Unity logs are UTF-16 — read with PowerShell `Select-String`), never the exit code.

---

## 1. What the defect was, and what actually changed

`TroopGearApplier.ApplyDefaultGrip` seated **every** off-hand on `LeftHand` with one hard-coded
position/rotation/scale triple, under a header conceding its own ceiling (*"Coarse grips for ~1.8 m
Supercyan / Tripo humanoids"*). No per-rig, per-mesh or per-shield term existed anywhere in it. The
hero has never had that bug because the hero **measures**. The correct seat was never missing — the
NPC path simply could not reach it.

**The fix is the join, not a number.** The hero's own steps were **lifted** (moved, not re-authored)
into two `public static` entry points on `EquipmentController`, and both callers now execute the same
instructions. The troop constant is **deleted**, not re-dialled.

## 2. Files changed

| File | What changed |
|---|---|
| `Assets/_Modules/Village/Hero/EquipmentController.cs` | New `public struct ShieldSeat` + `public static SeatShieldMountRotation(prop, gripRoot, hand, animator, body, mayDerive, subject)` (measure-first, then the precedence-gated derive: `GetShieldAxes` → `TryComputeShieldMountRotation` → `EnsureShieldOuterFaces` → write `gripRoot.localRotation`) and `public static SeatShieldPlateOnSocket(...)` (handle-dummy snap, else centre + `GearSeat.ShieldPlateOffBone`). The hero's inline blocks now CALL them; the hero keeps its own precedence inputs and its own trace strings (they name `vis`/`offsetKey`). `ApplyOffHandCentreOnSocket` became a thin wrapper over a new `public static CentreGripOnSocket(...)` core so the NPC does not need a second centring implementation. **The hero's `(arm=…)` trace token is threaded through** — `SeatShieldPlateOnSocket` takes an optional `extraTraceToken` and the hero's DRAWN call passes `$"(arm={_sheatheSocketOffIsArm})"`, so the centring line the F8 captures grep for stays byte-identical on both hero paths (caught in review: without it the DRAWN line would have silently lost the token while the SHEATHED one kept it). Three rule strings are consts: `ShieldRuleDerived` / `ShieldRuleNotDerived` / `ShieldRulePrecedence`. |
| `Assets/_Modules/Village/Troops/TroopGearApplier.cs` | The `else if (shield)` branch of `ApplyDefaultGrip` is **DELETED**. `Attach` routes a shield to new `SeatShield` → `public static SeatShieldOnHand(...)`, which inserts an identity `TroopGear_ShieldGrip` root under the bone, reparents the prop under it, calls the shared authority, and emits the device line. `SeatShield` also **pose-settles** (`anim.Update(0f)`, guarded) before seating, and `DescribeOffHandArm` prints the forearm-vs-body angle in the seat line — see §6 item 2. File header + the deleted branch's slot both carry a "do not re-add a shield case here" note. |
| `Assets/Editor/Regression/TroopShieldSeatRegression.cs` *(new)* + `.cs.meta` (guid `31a180c6be494b8781bca9ba1feb876d`) | 6-case suite, markers `TROOP_SHIELD_SEAT_OK` / `TROOP_SHIELD_SEAT_FAIL`. |
| `WorkOrders/WORK_ORDER_1616_*.md` | Status flipped; sequencing note kept. |

**Never touched:** `WeaponOrientHelper.cs`, `GearSeat.cs`, `RaidGarrisonSpawner.cs`, `DataRegression.cs`,
any `.unity`, any `.json`, git, Unity.

### Registration line for `DataRegression.cs` (lead-owned — this lane did NOT add it)

Place it beside the `staff-grip-seat` line at `Assets/Editor/Regression/DataRegression.cs:1940`:

```csharp
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "troop-shield-seat suite", () => { if (!DeNelle.Editor.Regression.TroopShieldSeatRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[troop-shield-seat] " + r); });
```

Standalone: `run-unity-method DeNelle.Editor.Regression.TroopShieldSeatRegression.RunAll`.

## 3. The single authority — file:line, as §5 asks

- **Rotation:** `Assets/_Modules/Village/Hero/EquipmentController.cs:2190` — `SeatShieldMountRotation`.
- **Plate-off-bone:** same file, `:2265` — `SeatShieldPlateOnSocket`.
- Hero call sites: `:2851` (rotation, inside `AttachOffHandProp`) and `:2923` (plate).
- NPC call site: `Assets/_Modules/Village/Troops/TroopGearApplier.cs` — `SeatShieldOnHand`.

*(Line numbers read off the working tree 2026-09-09 and will shift the moment anything above them
changes — CLAUDE.md §11B: they are a pointer, re-read them at source rather than trusting this row.)*

⚠ **ONE HONEST QUALIFIER on "exactly one call site".** The **runtime** seat is now one. The in-game
**Seating Editor preview** (`EquipmentController`, the `shieldPreview` branch) still derives its own
shield rotation via `WeaponOrientHelper.ComputeShieldMountRotation(_previewShieldFrame, grt.parent,
body.forward, body.up)` — and it was **already divergent from the attach path before this ticket**: it
uses raw `body.forward/up` rather than `GearSeat.GetShieldAxes`, and it never calls
`EnsureShieldOuterFaces`. Routing it through the authority would CHANGE what the owner sees while
dialling, which this lane was told not to do. **Named, not fixed — it deserves its own ticket.**

## 4. RED-first — what is proven, and what is NOT

⚠ **HONEST STATEMENT (CLAUDE.md §11B): the suite has NOT been executed this session.** Edit-only lane;
no Unity ran. Calling this "RED-first proven" would be a guess dressed as a fact.

**The mutation the suite catches, stated so it can be re-run by hand:** re-insert an
`else if (shield) { t.localPosition = <the triple>; t.localRotation = Quaternion.Euler(0,90,0); … }`
branch in `TroopGearApplier.ApplyDefaultGrip` and route the off-hand back to it. Case 1's derived
assertions and Case 3's literal-seat assertion both go red.

What *is* proven, and how:

1. **The oracle cannot pass for the wrong reason, and it proves that AT RUN TIME.** On a hand at
   identity the deleted triple's flat 90° yaw lands the plate's thickness on the **same world line the
   derivation aims for** — an oracle written on a clean rig would go GREEN against the old code. Every
   fixture therefore seats on a hand carrying `Euler(37,101,-23)`, and Case 1 runs a **CONTROL** with
   the legacy triple typed in the suite and **FAILS IF THE CONTROL IS LESS THAN 30° OFF**. That is the
   WO-1138 "a probe that cannot fail" guard, applied to the thing that would actually have hidden here.
2. **Case 2 is the one-owner proof, and it is mechanical.** The same plate is seated twice — once
   through `TroopGearApplier.SeatShieldOnHand`, once by calling the shared authority directly the way
   the hero does — and the two grip roots must agree within 0.5° / 1 mm / 1 mm of scale. If the troop
   path ever re-acquires a nudge, a scale write or an offset of its own, they diverge and it reddens.
   A source-text lint could not do this (CLAUDE.md §8's own warning about source-lints as coverage).
3. **The assertions measure the SEATED transform**, not a returned number: face-off-outward and
   long-axis-off-up are recomputed from `gripRoot.rotation * frame.ThicknessAxis` after the shipped
   seat ran. `docs/WEAPON_ARMOR_ORIENT_LOGIC.md` (WO-1226 note 1) records why: a trace can print a
   perfect `0deg` about a prop that is hanging sideways.
4. **The hero is guarded, not assumed.** Case 5 asserts `mayDerive:false` moves nothing **and still
   measures the frame** — the owner's 2026-08-20 "measure first, decide after" ruling, whose absence
   left a whole device capture with not one `ShieldFrame` line.
5. **Case 6 reads the shipped roster** and asserts every live off-hand is a shield, by COUNT, never by
   id (the WO-1431 Case 5 convention — the owner may edit the roster and that must not redden a gate).
   Measured at source 2026-09-09: `troop-shieldguard` and `troop-echo-legionnaire` both carry
   `TroopGear/Shield`; every other row's `offhand` is the string `"None"`.

## 5. THE DEVICE CAPTURE LINE THE OWNER SHOULD LOOK FOR

Deploy troops in a raid and grab the log (`adb logcat`, or F8). One line per shield attach:

```
[Flow:TroopGear] SHIELD SEAT APPLIED troop-echo-legionnaire 'TroopGear/Shield': rule=SHIELD-DERIVED-THICKNESS-OUTWARD authority=EquipmentController.SeatShieldMountRotation frameValid=True derived=True lPos=(…) lEuler=(…) lScale=(…) — WO-1616: this used to be the hard-coded …
```

### ⛔ READ THE `rule=`, AND DO NOT READ THE OLD ATTACH LINES AS PROOF

- **`rule=SHIELD-DERIVED-THICKNESS-OUTWARD`** = the shared authority seated it. This is the fix, live.
- **`rule=SHIELD-NOT-DERIVED`** = the plate could not be measured; the `[Flow:Equip] ShieldFrame …`
  **Warn immediately above says WHICH clause failed** (null, no measurable renderer bounds, or not
  plate-shaped) and the prop was left exactly as it arrived.
- **`rule=SHIELD-SEAT-OWNED-BY-PRECEDENCE`** cannot appear on the troop path (no authored channel
  exists there); on the hero it means an owner-dialled seat owns the row, which is correct precedence.
- ⚠ **The five `id=troop-echo-legionnaire: attached 'TroopGear/Shield' on LeftHand` lines the RCA
  quotes still print, and they are NOT the proof of anything** — they printed identically while the
  constant was seating the shield wrong. That is exactly why the new line exists.

⭐ **But the log is not the acceptance.** This is a visual defect: **the raid screenshot is the
evidence** (memory `screenshots-are-primary-evidence-for-visual-defects`, WO §5). The felt-test
question is: *does the deployed legionnaire's shield sit on the forearm, plate outward — not floating,
not edge-on?*

## 6. UNPROVEN / NOT CLOSED — named, not papered over

1. **Nothing here was executed.** No compile, no suite run, no device. §11B: an unproven thing named
   as unproven is useful; stated as fact it costs someone a day. The gate + the owner's raid capture
   are the proof.
2. **⭐ THE MOST LIKELY WAY THE FELT-TEST BOUNCES: the NPC seat is derived ONCE, at the BIND POSE,
   and nothing re-asserts it.** `TroopFactory.Build:137` calls `TroopGearApplier.Apply` **synchronously**,
   one line after `ApplyTroopAnimator` — the Animator is BOUND but has never EVALUATED, so the rig is
   in its bind/T pose. `GearSeat.GetShieldAxes` derives `outward` by projecting the body's left onto
   the plane perpendicular to the forearm; in a T-pose the forearm **is** the body's left, both
   `ProjectOnPlane` calls collapse, and the seat is taken against the documented FALLBACK axes.
   The resulting local rotation is then rigid on the bone, so it travels into every animated frame.
   (`GearSeat.SnapHandleToSocket`'s own doc names the same degenerate case; the hero is immune only
   because it equips while already posed, and `WeaponOrientHelper.cs:491` records that the hero
   re-asserts its pose per frame.)
   **Mitigation added, and it is UNPROVEN:** `SeatShield` calls `anim.Update(0f)` (guarded, and a
   no-op when no controller is bound) to write the entry state onto the bones before measuring.
   **The discriminator is in the seat line** — `armVsBodyRight=<n>deg`. Near 0 or 180 prints
   `⚠ DEGENERATE` and means the settle did not take; a well-conditioned angle means the seat was
   derived against a real pose. **Read that token before re-theorising a wrong-looking NPC shield.**
   The suite cannot see any of this: every fixture passes `animator: null`, which takes
   `GetShieldAxes`' documented no-rig branch and never touches the projection chain.
3. **BEHAVIOUR CHANGE, DELIBERATE: an unmeasurable troop shield no longer gets a constant.** Before
   WO-1616 it got the triple; now it stays at its parent's frame with a Warn. `docs/WEAPON_ARMOR_ORIENT
   _LOGIC.md` says hand-typed constants are kept as the documented fallback, and WO-1616 §4/§5 says
   delete this one and the grep must return zero — the WO is the specific, newer ruling for THIS
   constant, so it wins, and the tension is recorded here rather than resolved silently. **Practical
   risk is low but unmeasured:** the shipped troop shield is Supercyan `Fantasy_Shield`, whose FBX meta
   reads `isReadable: 1` (read at source 2026-09-09), and it is plate-shaped.
4. **SCALE IS NO LONGER FORCED TO 1 on a troop shield.** The deleted branch overwrote the prefab
   root's scale; the shared authority derives ROTATION only and leaves a native prop's pivot/scale
   alone, as the hero does. `TroopGear/Shield.prefab` carries no scale override (its prefab
   modifications are name/position/rotation/material only — read at source), so this is **expected to
   be a no-op, and that expectation is not proven.** If the NPC shield reads a different SIZE on
   device, this line is why — do not re-add a scale write; say so and it gets its own ticket.
5. **A grip root now exists under the bone** (`TroopGear_ShieldGrip`). It is required, not cosmetic:
   the measured frame is expressed in the PARENT's frame and the seat rotates that parent, so the prop
   must not be the transform being rotated. It carries the `TroopGear_` prefix, so the existing
   re-skin cleanup in `Attach` still destroys it (and the prop beneath). **Not proven at runtime** —
   a re-skin/reconfigure in a live raid is the check.
6. **Trace-text changes on the hero path — TWO of them, both additive.** (a) When a shield is
   derivable but its frame is unmeasurable, `SeatShieldMountRotation` emits a new `Warn` naming the
   consequence (the helper's own Warn, saying which clause failed, was already printed above it).
   (b) The hero's "off-hand seat NOT derived …" Step now also fires in that same case (previously
   only the helper's Warn appeared) and gained a `rule=` token. **No transform changed by either.**
7. **The `x5` attach count and the `troop-echo-legionnaire` id come from the RCA ledger's reading of
   the live log**, not from a raw-log grep in this lane — carried forward from WO §7 unchanged.
8. **`docs/WEAPON_ARMOR_ORIENT_LOGIC.md` should gain a row** (flagged, NOT edited — outside this
   lane's file list). Its "what is wired live" table describes the shield rows for the hero only.
   Suggested addition for whoever owns the doc: *"**shield, raid NPC** — DERIVED through the same
   authority as the hero (`EquipmentController.SeatShieldMountRotation` /
   `SeatShieldPlateOnSocket`); `TroopGearApplier` holds no shield constant (WO-1616, 2026-09-09)."*
   **CLOSED 2026-09-10 by WO-1627 (lane ORIENT-DOC): the "shield, raid NPC" row is WRITTEN into
   `docs/WEAPON_ARMOR_ORIENT_LOGIC.md`'s live table.** This item needs no third flag. One precision
   added while writing it, re-measured at source: "holds no shield constant" is exact for a shield
   ROTATION constant (`TroopGearApplier.cs:274-280` records the deleted triple), but the file does
   still carry shield-shaped SCALE/COLOUR literals in the missing-prefab primitive fallback
   (`:348-352`), so the doc row says "no shield ROTATION constant" and names that exception.
9. **CORRECTION 2026-09-09 (lead, re-measured at source): THIS ITEM IS WRONG - NOT MINTED.**
   `troops.json` carries ZERO `"None"` values in `weapon` or `offhand`; the only `"None"` strings
   are `"element": "None"` (8 of 9 rows). Rows without gear simply OMIT the key (2 lack `weapon`,
   7 lack `offhand`), `TroopDef` declares no default, and `TroopGearApplier.Apply` already filters
   with `IsNullOrEmpty` - so no placeholder slab is built for a missing off-hand and the
   "moved grey slab" warning below does not apply. The likely misread was a python `.get()` walk
   printing Python's `None` for a missing key. Kept unrewritten below as the record.
   ~~**Out of scope, found while reading — worth its own ticket.**~~ `TroopDef.Weapon`/`Offhand` use the
   **string `"None"`** as the "no gear" sentinel, and nothing filters it: `Apply` only tests
   `IsNullOrEmpty`, so `Resources.Load("None")` misses and `BuildPrimitiveFallback` builds a
   **placeholder cube** on those rows. Read at source 2026-09-09: 6 of 9 roster rows carry
   `"weapon": "None"` or `"offhand": "None"`. **Not touched** (WO §3 says keep the fallback branch;
   changing it would remove visuals on rows nobody has captured), and **not proven on device.**
   ⚠ **CONSEQUENCE THE OWNER SHOULD EXPECT:** an `offhand: "None"` row builds a plate-shaped grey
   slab (0.45 x 0.55 x 0.08), which IS measurable — so those placeholder slabs now go through the
   derivation too and will sit **differently** from the last build. A moved grey slab on a footman
   is this line, not a new bug.

## 7. Gate checklist for the orchestrator

- [ ] Add the `troop-shield-seat` registration line (§2) to `DataRegression.cs`.
- [ ] `COMPILE_GATE_OK` on a fresh log (also clears the NUL scan, WO-434).
- [ ] `REGRESSION_OK <n>/<n> suites` — judge the `^REGRESSION_(OK|FAIL)` summary line, PowerShell-read.
- [ ] Confirm `[attachment-offset]`, `[sheathe-pose]` and `[staff-grip-seat]` stayed green — this
      ticket must not become a hero-visual change (§5 of the WO).
- [ ] Brace + NUL check re-run: `{`/`}` balanced on all three `.cs` (**956/956**, **48/48**, **32/32**)
      and **ZERO NUL bytes** — measured in this lane before hand-back.
- [ ] `grep -n "0.05f, 0.05f, 0.02f" Assets/_Modules/Village/Troops/TroopGearApplier.cs` → **zero hits**
      (verified in-lane; the value is not repeated in a comment there either).
- [ ] Device build → owner felt-test on a raid → **PO closes** (§5). CLI never closes a visual ticket
      on a log line.
