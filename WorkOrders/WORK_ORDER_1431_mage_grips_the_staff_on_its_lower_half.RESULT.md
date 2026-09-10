# WO-1431 RESULT — the staff derivation that was MEASURED now APPLIES

**Status:** IMPLEMENTED - awaiting gate (2026-09-09 lane HERO-GRIP); owner felt-test on device closes
**Lane:** HERO-GRIP (edit-only). Branch `dev`, from HEAD `184c8ff06`.
**Silo:** Hero visuals — weapon attachment / grip seat
**Gate state:** ⛔ **NOT GATED.** No Unity run was fired by this lane (edit-only, and the Unity lock
belongs to one seat). `COMPILE_GATE_OK` and `REGRESSION_OK <n>/<n>` are OWED. Judge them by the marker
on a fresh log, never the exit code.

---

## 1. What the defect actually was

Owner, 2026-09-06, verbatim: *"the staff needs reversed, right now they grasp it 75% on the lower half
instead of on the upper half of the staff"*.

The RCA is in `docs/READY_RCA_2026-09-09.md` § "WO-1431" and it is source- **and** device-proven. In
one breath: `WeaponOrientHelper.TryDeriveStaffGripY` already computed the owner-ruled grip — the
retained device trace measures `tripo_staff_a` on `Hero (Blaise)` at **1.2639 m** and derives
**`fraction=0.75 -> gripY=0.7204`** — and the live attach path **threw the number away**. The
derivation was reachable only from `TraceMeasuredSeat`, a read-only prediction that changes nothing,
while every non-native melee prop was seated by `EquipmentController.SeatHiltLowerHalf`: 18% up from
the foot, with a crossguard refinement that terminates at the midpoint (`if (by > mid) break;`) and is
therefore **structurally incapable of returning anything above 0.50**. Two programs, one prop, and
only the wrong one moved it.

**The fix is the join, not a number.** One dispatcher now decides the grip by archetype, so a future
reader cannot re-open the split by editing "the other" path.

**⛔ It is deliberately NOT a JSON offset** — the old WO §3 said "Data, not code"; the ledger rules
that out and the WO now carries a superseded-box saying why. Verified at source 2026-09-09 — **both
copies read, not just the Resources one**: `Assets/Resources/OffsetForge/offsets.json` and its twin
`Assets/OffsetForge/offsets.json` each hold **26** rows with an identical id list, and **none is a
staff**. Authoring one
would need re-dialling per staff mesh, and — worse — an authored row moves that staff **out of the
derived tier**, permanently unreaching the sanctioned rule.

---

## 2. Files changed

| File | What changed |
|---|---|
| `Assets/_Modules/Village/Hero/EquipmentController.cs` | New `SeatMeleeGripPoint` dispatcher + `MeleeGripSeat` struct (both `public static`, so the suite asserts the SHIPPED dispatch). `SeatHiltLowerHalf` now reports its `gripY`/fraction and returns `bool` — **its rule is byte-for-byte unchanged**, only its signature and who routes into it. The melee attach branch resolves ONE archetype and ONE derivability, feeds both to the seat and to `TraceMeasuredSeat`, and traces the applied rule. The **Seating Editor preview** call site is routed through the same dispatcher. Two new fields, `_currentWeaponArchetype` / `_currentWeaponDerivable`, cleared on every attach. |
| `Assets/_Modules/Core/Geometry/WeaponOrientHelper.cs` | A 6-arg `TryDeriveStaffGripY` overload that additionally reports the `yMin`/`length` it derived from; the 4-arg form delegates to it. The stale *"the staff rule is measurement-only today"* paragraph is retired **in place** (the KNOWN-GAP paragraph about min-Y-as-the-foot is kept — that gap is real and unchanged). |
| `Assets/Editor/Regression/StaffGripSeatRegression.cs` *(new)* + `.cs.meta` | 5-case suite, markers `STAFF_GRIP_SEAT_OK` / `STAFF_GRIP_SEAT_FAIL`. |
| `WorkOrders/WORK_ORDER_1431_mage_grips_the_staff_on_its_lower_half.md` | Status flipped; §2/§3 superseded-box added. |

**Never touched:** `HeroLocomotion.cs`, `DataRegression.cs`, any `.unity`, any `.json`, git.

### Registration line for `DataRegression.cs` (for the orchestrator to add — this lane did not)
Place it beside the `attachment-offset` line at `Assets/Editor/Regression/DataRegression.cs:1357`:

```csharp
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "staff-grip-seat suite", () => { if (!DeNelle.Editor.Regression.StaffGripSeatRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[staff-grip-seat] " + r); });
```

Standalone: `run-unity-method DeNelle.Editor.Regression.StaffGripSeatRegression.RunAll`.

---

## 3. Precedence — unchanged, and re-asserted at the new call site

The WO-1123 ladder (`WeaponOrientHelper.ResolveSource`) is untouched: **authored offset row → manual →
derived → archetype default**, with `manual` fed through WO-1215's
`ManualSeatIsSubstantiated(manual, rowIsGenerated, hasAuthoredSeat)`. The derived branch runs **only**
when `MayDerive` is true; anything an owner dialled takes the byte-for-byte pre-WO-1431 path
(`SeatHiltLowerHalf` + the authored nudge on top).

**Proof the fix will actually reach the shipped staves** (read at source 2026-09-09, not from a doc):
`Assets/Resources/Data/Canonical/weapons.json` carries `tripo_staff_a/b/c/d` as `category: "staff"`,
`manual: true`, `generated: true`, with `prefabPath` `Heroes/Props/Weapons/staff_A..D` — and **no**
`offsets.json` row exists for any of them. `manual && (hasAuthoredSeat || !generated)` is therefore
`true && (false || false)` = **false** → the flag is DEMOTED → `MayDerive` = **true** → the derivation
fires. (`aegis_aetherstaff` declares neither flag, so it is derivable outright.) Case 5 of the new
suite re-measures this against the live catalog at gate time, asserting a **count**, never a row id —
the owner may dial a staff in the Seating Editor at any moment and that must not redden a gate.

---

## 4. RED-first — what was proven, and what was NOT

⚠ **HONEST STATEMENT, per CLAUDE.md §11B: the suite has NOT been executed this session.** This is an
edit-only lane; no Unity ran. Calling this "RED-first proven" would be a guess dressed as a fact.

What *is* proven, and how:

1. **The oracle is red against the pre-fix behaviour, and the suite proves it AT RUN TIME rather than
   in prose.** `Case1_StaffSeatsOnTheUpperShaft` runs the **same fixture twice through the same
   shipped dispatcher** — once with `mayDerive: true` (the fix) and once with `mayDerive: false`,
   which is byte-for-byte the old path, since `SeatHiltLowerHalf` is the only thing the live melee
   seat ever called. It then **fails** if the legacy run does not land below 0.50, and **fails** if
   the two runs land within 0.2 of each other. So the case cannot pass while the fix is reverted, and
   it cannot pass for the wrong reason either (the WO-1138 "a probe that cannot fail" lesson).
2. **The pre-fix value is arithmetic, not opinion.** `SeatHiltLowerHalf` sets
   `gripY = yMin + length * 0.18f` and its refinement loop `break`s once `by > mid`, so its output is
   bounded **below 0.50 by construction** — read at
   `Assets/_Modules/Village/Hero/EquipmentController.cs` in the `SeatHiltLowerHalf` body this session.
   Against a 0.75 assertion it can only be red.
3. **The oracle measures the SEATED MESH, not the returned number.** Every case re-reads the prop's
   bounds after the shipped seat ran and asks where the grip root's origin (the hand bone) landed
   along the shaft. Case 1 additionally **fails if the returned struct and the re-measured prop
   disagree by more than 0.02** — because the device line the owner is asked to read must describe
   what happened to the mesh. Six prior staff fixes asserted a deriver and shipped a visibly wrong
   staff (`docs/WEAPON_ARMOR_ORIENT_LOGIC.md`, WO-1226 note 1).
4. **The bladed families are guarded, not assumed.** `Case2_SwordKeepsTheLowerHilt` fails if a sword
   ever seats at or above 0.50 — "a staff repair must not rotate every melee family to fix one".

Fixture arithmetic (so a reviewer can check the expected numbers without running anything): the prop
is a unit cube scaled `(0.06, 1.2639, 0.06)` at the grip root's origin, so parent-local
`yMin = -0.63195`, `length = 1.2639`, and `gripY = yMin + 0.75 * length = +0.31598`. The seat shifts
the prop down by `gripY`, leaving the hand at 0.75 of the shaft. The legacy run gives
`gripY = yMin + 0.18 * length = -0.40446` → the hand at 0.18.

---

## 5. THE DEVICE CAPTURE LINE THE OWNER SHOULD LOOK FOR

Equip the mage's staff and grab the log (`adb logcat`, or F8). The proving line is new and is emitted
once per staff attach:

```
[Flow:Equip] MELEE GRIP APPLIED 'tripo_staff_a': rule=STAFF-DERIVED-UPPER-SHAFT archetype=Staff gripY=0.3160 fraction=0.750 up the long axis (ySpan=1.2639m) shiftedY=-0.3160 ...
```

### ⛔ READ THE `fraction`, NOT the `gripY` — and do NOT look for `0.7204`

**`fraction=0.750` and `rule=STAFF-DERIVED-UPPER-SHAFT` are the discriminators.** The absolute
`gripY` on a fixed build is **≈ +0.32**, *not* the `0.7204` the RCA quotes, and an owner told to look
for 0.7204 would read a correct build as a failure. The arithmetic, so nobody re-derives it wrong:
`gripY 0.7204` on a `1.2639 m` shaft implies `yMin = -0.2275 = -0.18·L` — a frame whose foot had
**already been shifted 18% by `SeatHiltLowerHalf`**, because the old `TraceMeasuredSeat` ran *after*
the legacy seat. **0.7204 was a residual of the defect, never a target.** The new dispatcher runs on
the `NormalizeInto`'d, bounds-centred frame (`yMin = -L/2`), so the same 0.75 rule prints
`gripY = 0.25·L ≈ +0.316`. Same grip, different origin.

- **`rule=STAFF-DERIVED-UPPER-SHAFT` + `fraction=0.750`** = the fix is live on this prop.
- **`rule=HILT-LOWER-HALF` on a staff** = the derivation did not fire. The same line then says why
  (`mayDerive=false` → an authored/substantiated-manual seat owns the row, which is *correct*
  precedence; or a bounds `Warn` immediately above → the mesh could not be measured).
- The preceding **`melee seat source '<id>'`** line names the resolved tier, the archetype and every
  input (`authoredRow` / `manual` / `rawManual` / `generated` / `derivable`) — so a bounce is
  diagnosable from the log alone, without a second capture.

### The `OrientMeasure` cross-check — the agreement condition is a RESIDUAL OF ZERO

`TraceMeasuredSeat` still runs **after** the seat, so post-fix it re-derives 0.75 on the
**already-shifted** frame (`yMin ≈ -0.948` on the device staff) and prints:

```
[Flow:Equip] OrientMeasure 'tripo_staff_a' archetype=Staff: ... | PREDICTION: staff grip would sit at localY=0.0000 (0.75 up)
```

**That `localY≈0.0000` IS the proof.** The grip root's origin is the hand bone, so "the 0.75 point is
already at zero" means the hand is sitting exactly where the rule says it should — a stronger oracle
than two matching numbers, and it is independent of the dispatcher's own arithmetic. A prediction that
comes back **materially non-zero** on a staff means the seat and the measurement have split apart
again; **that** is the bug, and both lines should be quoted in any bounce.

⚠ **Expect the helper's own `StaffGrip '<prop>': ySpan=… -> gripY=…` Step to appear TWICE per staff
attach**, with different `gripY` values (once from the dispatcher on the pre-shift frame, once from
the trace on the post-shift frame). That is the two callers doing their jobs, not a double-seat — do
not triage it as one.

⭐ **But the log is not the acceptance.** This is a visual defect, so **the screenshot is the
evidence** (memory `screenshots-are-primary-evidence-for-visual-defects`; the WO's own §5). The
felt-test question is simply: *does the mage's hand sit on the upper shaft, near the head?*

---

## 6. UNPROVEN / NOT CLOSED — named, not papered over

1. **WHICH END IS THE HEAD is still underived.** `TryDeriveStaffGripY` takes **min-Y as the foot** —
   i.e. whatever `WeaponBoundsOrient.EnsureHandleAtShortYEnd` already resolved — and does not
   independently find the finial. On a staff that imports head-down, this grip lands 0.75 from the
   **head**, which would read to the owner as the defect *inverted*, not fixed. The disambiguator
   that would settle it is named in `docs/WEAPON_MESH_ARCHETYPES.md` §3, and that doc also notes a
   plain quarterstaff has **no head at all**, so for some meshes the ends are genuinely
   interchangeable. **Only the device screenshot resolves this**, per staff. If the owner reports the
   grip is now at the wrong end (rather than in the wrong place), that is this gap and it needs its
   own ticket, not a re-dial of the 0.75 constant.
2. **The SHEATHED staff pose will visibly change.** `ResolveSheathedTipSign` runs *after* the seat and
   measures the frame the sheathe pose actually uses, so it stays internally coherent — but the prop
   now pivots about a point 0.75 up the shaft instead of 0.18, so the hip/back carry will hang
   differently. Not a defect by itself; **name it in the felt-test** so a changed carry is not read as
   a new bug.
3. **The four un-staffed staves are covered; the wider ratio is not.** WO-1431 §6 asked for the ratio
   of weapon ids to the 26 authored offsets. This lane counted the **staff** family only (5 rows
   classify as Staff, all derivable). The general question — how many weapon ids have no authored
   seat, and which of those families have no archetype rule at all (axe / hammer / mace / wand /
   crossbow all classify `Unknown` and derive **nothing** by the owner's 2026-08-19 spec) — is a real
   finding and belongs in its own ticket. **Do not guess a rule for those families.**
4. **`docs/WEAPON_ARMOR_ORIENT_LOGIC.md` now has one stale row (§15 — flagged, NOT edited: outside
   this lane's file list).** Its "what is wired live" table says **`melee (sword/staff/…)` → *"seat
   unchanged. The archetype rules run as a read-only `[Flow:Equip] OrientMeasure` prediction"***. That
   is false for the staff as of this change. Suggested replacement row, for whoever owns the doc:
   *"**staff, drawn + sheathed** — grip point **DERIVED** (0.75 up the long axis) via
   `EquipmentController.SeatMeleeGripPoint`, precedence-gated; sword/dagger and every Unknown family
   keep the hilt-lower-half seat and the read-only prediction."* The two struck lines in that doc's
   STAFF bullet stay as they are — they are already correct.
5. **`AttachmentOffsetRegression` was not touched and its staff cases are a DIFFERENT axis.**
   `Case5_StaffNeutralDefault` and `Case7_DrawnSeatVerticality` pin the drawn **rotation**
   (`StaffDrawnGripNudgeDefault = (90,0,0)`, owner-ruled 2026-08-26). This change touches the **grip
   point** and not one rotation. They should stay green; if either reddens at the gate, that is a
   real interaction and **not** something to silence.

---

## 7. Gate checklist for the orchestrator

- [ ] Add the `staff-grip-seat` registration line (§2) to `DataRegression.cs`.
- [ ] `COMPILE_GATE_OK` on a fresh log (also clears the NUL scan, WO-434).
- [ ] `REGRESSION_OK <n>/<n> suites` — judge the `^REGRESSION_(OK|FAIL)` summary line, and read the
      Unity log with PowerShell (they are UTF-16).
- [ ] Confirm `[attachment-offset]` and `[sheathe-pose]` stayed green (§6 item 5).
- [ ] Brace + NUL check re-run: **`{`/`}` balanced on all three `.cs` (943/943, 305/305, 30/30) and
      ZERO NUL bytes**, measured in this lane before hand-back.
- [ ] Device build → owner felt-test on the mage's staff → **PO closes** (§5). CLI never closes a
      visual ticket on a log line.
