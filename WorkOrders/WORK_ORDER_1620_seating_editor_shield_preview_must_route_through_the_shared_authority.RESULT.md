# WO-1620 RESULT — the Seating Editor's shield preview routes through the shared authority

**Status:** IMPLEMENTED - awaiting gate (lane SEAT-PREVIEW 2026-09-10)
**Lane:** SEAT-PREVIEW, edit-only. No Unity run, no gate, no commit — the lead holds all three.
**Base:** the lane worktree was stale (`f5d39acd1`, 2026-09-07) and did not carry WO-1431's or
WO-1616's work at all. It was fast-forwarded to the `dev` tip `bb12c728e` before a line was edited,
because §8 forbids starting from a tree that predates those lanes. Both are COMMITTED on `dev` now,
so "start from the working tree, never HEAD" resolved to this tip. Every line number below was read
in that tree on 2026-09-10.

---

## 1. What changed, per file

### `Assets/_Modules/Village/Hero/EquipmentController.cs` — the only code file

**(a) `SeatShieldMountRotation` (`:2190`) became a thin write-wrapper.** Its three derivation steps —
`GearSeat.GetShieldAxes` → `WeaponOrientHelper.TryComputeShieldMountRotation` →
`GearSeat.EnsureShieldOuterFaces` — were **moved, not re-authored**, into a new compute-only entry
point. The method keeps its whole job (measure the frame, gate on `mayDerive`, WRITE
`gripRoot.localRotation`, fill the `ShieldSeat`); it just no longer owns a private copy of the
derivation. New body at `:2234-2246`.

**(b) NEW `public static bool TryDeriveShieldMountRotation(...)` (`:2295`).** Signature:

```
WeaponOrientHelper.ShieldFrame frame, Transform hand, Animator animator, Transform body,
string subject, out Quaternion mountLocal   ->  bool
```

The three steps verbatim, plus the same `Guard.Try` wrapper with a **byte-identical** message string
(`$"derived shield seat for '{subject}' (WO-1123)"`). Returns false — caller keeps the arrived pose —
when the mount is null, the frame is invalid, or the rotation math declines. Doc block at `:2251-2294`
records why it exists, that the split is write-vs-compute and not a fork, the frame-ownership rule,
and where precedence lives.

**(c) The preview's `shieldPreview` branch (`:5268-5297`) now CALLS it.** The whole prior body —
`WeaponOrientHelper.ComputeShieldMountRotation(_previewShieldFrame, grt.parent, body.forward,
body.up)` — is gone, replaced by `TryDeriveShieldMountRotation(_previewShieldFrame, grt.parent,
_animator, transform, "<key> [seating-preview]", out previewShieldRot)` at `:5275`.

---

## 2. Acceptance criteria, answered

### Exactly ONE code path derives a shield's mount rotation
`EquipmentController.TryDeriveShieldMountRotation`, **`Assets/_Modules/Village/Hero/EquipmentController.cs:2295`**.
Callers, all of them:
1. `EquipmentController.SeatShieldMountRotation` (`:2240`) — the write-wrapper, itself called by the
   hero attach path (`:2918`) and by `TroopGearApplier.SeatShieldOnHand`
   (`Assets/_Modules/Village/Troops/TroopGearApplier.cs:240`, with its plate seat at `:244` and its
   `authority=EquipmentController.SeatShieldMountRotation` device line at `:255` — all opened and
   confirmed unchanged 2026-09-10; that file was NOT edited).
2. `EquipmentController.ApplySeatingPreview` (`:5275`) — the Seating-Editor preview. **New.**

Grep of the whole file for `ComputeShieldMountRotation` / `GetShieldAxes` / `EnsureShieldOuterFaces`
now returns, in live code: `:2306`, `:2311`, `:2317` (inside the new entry point), `:2344`
(`SeatShieldPlateOnSocket`'s own `GetShieldAxes` for the off-bone push — a different concern,
untouched) and `:4610` (the **SHEATHED** hip/back pose, a deliberately different anchor and rule,
explicitly out of this ticket's scope). **Zero inside `ApplySeatingPreview`.**

### Join shape (§3a): option **(a)**, a compute-only entry point
Chosen for the reason §3a names: it is WO-1616's own shape (that lane lifted the hero's steps into
`public static` entry points and made both callers execute them), and it is a **move** rather than a
rewrite. Option (b) — write then read `gripRoot.localRotation` back — was rejected because the preview
would have to write a throwaway rotation onto the live grip root every drag frame, and `:5308-5313`
already documents a second-writer hazard on that transform (`_offHandCompState` invalidation).

### Frame ownership (§3b): unchanged, and now explicit
The frame is an **input** to the entry point and is never measured inside it. The attach path hands in
the frame it measured at attach; the **preview keeps its own re-measure at `:5205-5208`** (WO-1123: the
cached attach frame was measured on the runtime seat, `SeatNative` for a native shield, while the
preview re-seats through `NormalizeInto`). WO-1123's reasoning survives intact — one frame, one owner,
per caller. Nothing was collapsed.

### `mayDerive` / owner precedence (§3c): the preview's gate is POSITIONAL, and it is the same expression
`TryDeriveShieldMountRotation` takes **no `mayDerive`** — it is pure math; precedence belongs to the
caller that knows whose row it is. `SeatShieldMountRotation` short-circuits on `mayDerive` at `:2213-2217`
before reaching it. The preview never enters the branch unless `_currentOffHandDerivable` is true
(`:5268`), and that field is assigned at **`:3013-3014`** from
`!fullOverride && vis.kind == WeaponClass.Shield && WeaponOrientHelper.MayDerive(hasOffset, _currentOffHandManual)`
— the **identical expression** the attach path builds `shieldMayDerive` from at **`:2916-2917`**
(`!fullOverride && WeaponOrientHelper.MayDerive(hasOffset, _currentOffHandManual)`, inside a
`vis.kind == WeaponClass.Shield` block). So the preview effectively passes `mayDerive: true` and an
owner-dialled seat still wins, one branch earlier. Pinned by `Case5_OwnerPrecedenceStillWins`.

---

## 3. A FOURTH divergence, found while joining — and fixed

The WO listed three. There is a fourth, and it is the kind that only shows up on a real rig:

| | attach path | old preview |
|---|---|---|
| `body` argument | `transform` (`:2918-2919`) | `_animator.transform` (old `:5186`) |

`_animator` is resolved with `GetComponentInChildren<Animator>()` (**`:5673`**, and `:920`), so on
any rig whose Animator sits on a **child** of the EquipmentController's GameObject those are different
transforms with different `right` / `up` / `forward` — and both `GearSeat.GetShieldAxes` and
`GearSeat.EnsureShieldOuterFaces` read them. The preview now passes **`transform`** (`:5276`), exactly
what attach passes. Reasoning written in-code at `:5261-5267`.

**The bow branch above (`:5230-5238`) still passes `_animator.transform` and was deliberately NOT
touched** — it is a different derivation with its own history and the WO pins it. Whether it has the
same divergence is **not investigated and not claimed**; it is flagged in §7 below.

> **CORRECTION 2026-09-10 (WO-1627, re-measured at source) - see the full banner at the end of §7.**
> The bow branch does NOT "still" pass a body different from its own attach path. Bow attach
> (`EquipmentController.cs:1440`) and bow preview (`:5234`) pass the identical expression
> `_animator != null ? _animator.transform : transform`. There is no bow preview-vs-attach
> divergence. What remains is a cross-seam convention difference (shield seam vs bow seam), not
> investigated and not claimed.

---

## 4. One behaviour change beyond the join, named

When the shared derivation **declines**, the preview now sets `shieldPreview = false` (`:5289`) and
falls through to `ApplyGlobalWeaponYaw(baseRot * Euler(delta))`, logging a `FlowTrace.Warn`
(`:5290-5295`). That matches the attach path exactly, which applies the yaw when
`!offHandDerivedSeat` (`:2971`). Previously the branch had no failure path at all.

**In practice this is unreachable**: the branch already requires `_previewShieldFrame.Valid &&
grt.parent != null` (`:5268-5269`), which are the only two conditions
`TryComputeShieldMountRotation` fails on — that overload's ONLY `return false` is the
`mount == null || !frame.Valid` block (`WeaponOrientHelper.cs:654-663`); everything after it runs to
`return true` at `:708`, read to the method's end this session. It is written because a silent second path is what §12
forbids, not because it was observed firing. **Not observed firing — asserted from the two conditions,
read at `WeaponOrientHelper.cs:654-660`.**

---

## 5. THE BEHAVIOUR CHANGE — plain words for the owner (WO §4)

**This changes what you see in the Seating Editor when you dial a shield. It does not change what the
game ships.**

Until now the Seating Editor computed the shield's pose a *different way* from the game — it ignored
where your hero's forearm actually was. So every shield nudge you have saved was dialled against a
pose the game never renders. The tool now shows you the same pose the game uses.

**Consequence:** a shield offset you already dialled and saved may now render differently **in the
editor**. The game is untouched. If a saved delta looks wrong after this, that is a finding for you to
rule on and it gets its own ticket. **Nothing saved was migrated, rewritten or deleted** — §4 forbids
it and this lane did not do it.

---

## 6. ⛔ WHAT IS **NOT** PROVEN (CLAUDE.md §11B)

1. **THE §5 STEP-1 MEASUREMENT WAS NOT TAKEN BY THIS LANE.** The WO asks for the angle between the two
   poses on the **shipped shield, on the shipped rig**, before the join. This lane is edit-only and
   ran no Unity, so **no such number exists and none is claimed anywhere.** What was built instead:
   `Case2_ControlCannotPassForFree` types the retired preview arithmetic into the suite and prints
   `Quaternion.Angle(derived, legacy)` plus the seated face-off-outward for both. **That number lands
   on the lead's gate run, and it is the FIXTURE's number, not the rig's.**
   *How to close it cheaply:* a headless equip capture with both rotations logged for
   `knight_shield_starter` on the live hero.
2. **The fixture has no Animator**, which is the documented no-rig branch of `GetShieldAxes`
   (`GearSeat.cs:209`: `outward = -body.right`). So **divergence 1's real teeth — the projection
   onto the plane perpendicular to the forearm — are not exercised by the suite at all.** The fixture
   delta is dominated by the outward-axis choice (`body.forward` vs `-body.right`, 90° apart by
   construction) and is independent of the hand's pose. This is stated in the suite header and in the
   Case 2 comment, not hidden.
3. **RED-at-HEAD for `CaseOneOwner` is asserted by construction, not observed.** Nothing was run.
   `Case1` as written is green-by-construction today (the wrapper calls the entry point); its job is
   to go red when someone re-forks the authority. The genuinely discriminating case is Case 2.
4. **The real `ApplySeatingPreview` is NOT driven by any case.** `_currentOffHandProp` is assigned in
   exactly one place — the attach path, `:3004` — and `DeNelle.Village` publishes **no
   `InternalsVisibleTo`** (grepped 2026-09-10, zero hits). Driving the live preview from an EditMode
   suite would need catalog + Addressables + a humanoid rig. **A seeding seam on `EquipmentController`
   is a design call for the lead — this lane did not invent one quietly.** Coverage is therefore
   (a) the shared entry point vs the authority, (b) the measured control, (c) a source lint. Case 3 is
   **supplementary, not the coverage** (CLAUDE.md §8's own warning).
5. **Nothing was compiled.** No brace-gate proxy is a compiler; `python tools/gate_brace.py` reported
   `GATE_BRACE_SUMMARY bad=0 of 2` and both files carry zero NUL bytes, which proves neither
   compilation nor behaviour.
6. ⚠ **THE NAIVE §1 BRACE ONE-LINER WILL READ 49/48 ON THE NEW SUITE — THAT IS NOT A DEFECT.**
   `PreviewMethodBody` carries the char literals `'{'` twice (`IndexOf('{', start)` and the depth
   walk) against `'}'` once. `python tools/gate_brace.py` — the port of the gate's OWN rule, which
   strips comments and literals — reports `GATE_BRACE_SUMMARY bad=0 of 2` on both files, and both
   carry zero NUL bytes. Judge by that, not by the raw count (CLAUDE.md §1's own warning).
7. **The `.meta` for the new suite does not exist yet** — Unity generates it on first import at the
   lead's gate run.
8. **Only the owner can close this ticket** (WO §11, last line): open the Seating Editor on a shield
   and confirm the preview matches the game.

---

## 7. Handed back to the lead

**Registration line** (`DataRegression.cs` is lead-owned; this lane did not touch it):

- class: `SeatingPreviewShieldRegression`
- namespace: `DeNelle.Editor.Regression`
- tag: `[seating-preview-shield]`
- entry: `public static bool Run(out string reason)` (plus `RunAll()` for standalone)
- suite markers: `SEATING_PREVIEW_SHIELD_OK` / `SEATING_PREVIEW_SHIELD_FAIL`

**Doc row to WRITE (flagged, not written — WO §9):** `docs/WEAPON_ARMOR_ORIENT_LOGIC.md`'s "what is
wired live" table needs the preview's **shield** row stating that it routes through
`EquipmentController.TryDeriveShieldMountRotation`, so *"the Seating Editor preview shares the same
method so the two can never disagree"* becomes true for the shield as well as the bow. **This is the
second flag on that doc** — WO-1616 RESULT §6 item 8 flagged a different missing row. Two flags on one
table is a signal, not a licence.

**New finding for the lead to triage (no ticket minted — the numbering banner is lead-owned):** the
**bow** preview branch (`:5230-5238`) passes `_animator.transform` as its body while this lane proved
attach passes `transform`. Whether `ComputeBowHeldRotation` is sensitive to that difference was **not
investigated and is not claimed**. It is the same shape as divergence 4 and deserves one read.

> **CORRECTION 2026-09-10 (WO-1627, re-measured at source): the clause "while this lane proved attach
> passes `transform`" does not hold for the BOW.** That was proven of the SHIELD attach
> (`EquipmentController.cs:2918-2919`). The bow's own attach passes
> `_animator != null ? _animator.transform : transform` (`:1440`) - the identical expression the bow
> preview passes (`:5234`), and the same expression again at the sheathed preview (`:5390`) and in
> `TraceBowSeatMeasured` (`:1611`). **There is no bow preview-vs-attach divergence. Do not open a
> ticket to fix one.** What remains is a cross-seam convention difference - the shield seam
> standardises on bare `transform`, the bow seam on the ternary - not investigated and not claimed.
> The body of this RESULT is otherwise correct and is left unrewritten (CLAUDE.md sec.15: a dated
> RESULT is frozen; a wrong statement inside one gets a banner, never a rewrite). Recorded in
> `docs/WEAPON_ARMOR_ORIENT_LOGIC.md` under the live table.

**Pins verified untouched:** `SeatShieldPlateOnSocket` (`:2329`) and its `extraTraceToken` `(arm=…)`
thread; the `rule=` tokens `ShieldRuleDerived` / `ShieldRuleNotDerived` / `ShieldRulePrecedence`
(`:2162-2164`, byte-identical); `TroopGearApplier.cs` (not opened for edit); the bow and
`nativeMeleePreview` branches; the WO-1123 frame re-measure and its comment; the
`ParentScaleCompensation` composition; `_seatEditMode` reset semantics; `WeaponOrientHelper.cs` and
`GearSeat.cs` (read-only, unedited); `TroopGearApplier.cs` (read, unedited — see §2);
`SeatingEditorOverlay.cs` (unopened for edit);
`DataRegression.cs` (unedited).

## 8. Files changed

| file | change |
|---|---|
| `Assets/_Modules/Village/Hero/EquipmentController.cs` | authority split write/compute (`:2234-2246`, new `:2251-2320`); preview joined (`:5246-5297`) |
| `Assets/Editor/Regression/SeatingPreviewShieldRegression.cs` | **NEW** — 5 cases |
| `WorkOrders/WORK_ORDER_1620_...md` | `**Status:**` flipped (line 3) |
| `WorkOrders/WORK_ORDER_1620_....RESULT.md` | **NEW** — this file |

---

## 9. INCREMENT 1 — hollow-pass ratchet RED on the first gate run, resolved (2026-09-10)

**The finding (not mine, and it was right):** `Builds/wave2-reg1`, fresh, 491/493 —
`REGRESSION MARKER FAIL (1): hollow pass: SeatingPreviewShieldRegression.cs:613
[A-missing-dependency] guard 'body == null' - returns out of a null/empty/missing-dependency guard
having asserted nothing.` `RegressionMarkerRegression` strips comments and scans every `.cs` under
`Assets/Editor` (`:463-478`), and its RULE-4 ratchet is exactly right about my code: three case
methods read `if (body == null) return;`, and the caller's only channel is the `bool`, so a missing
source file would have landed those three pins in the GREEN column.

**Which of the three ways, and why: FIXTURE-ABSENT → FAIL naming the missing path.**
The rule text (`RegressionMarkerRegression.cs:227-232`) offers fixture-absent → FAIL,
harness-capability-absent → `RegressionOutcome.PartialSkip`, content/art-absent → assert through the
fallback. `PartialSkip` is **wrong here**, and the neighbour shows why: `RaidWatchdogHonorRegression.cs:454-473`
uses it for a **machine** condition — a live `ff.tun.*` PlayerPrefs override that legitimately makes
the pin unrunnable *on this box*. My guard's subject is `Assets/_Modules/Village/Hero/EquipmentController.cs`,
a **tracked source file at a fixed path, present in every clone**. Its absence is never a property of
the harness; it means the subject of the pin left the repo. Declaring that a "partial skip" would
mislabel a real defect as an environment quirk.

**The incremental change (one file, `Assets/Editor/Regression/SeatingPreviewShieldRegression.cs`):**

| before | after |
|---|---|
| `:430` `private static string PreviewMethodBody(Action<string> Fail)` — banked its own four failures and returned null | `:444` `private static string PreviewMethodBody(out string why)` — reports **nothing**; hands the reason back so no path out of it can be silent. Doc block `:430-443` records the gate line that caught it. |
| — | `:494` NEW `PreviewSourceMissing(string caseTag, string why)` — the one FAIL text, naming the missing path and stating in-line why this is a FAIL and not a skip |
| `:472-474` (Case 3) `Action<string> Fail = …; string body = PreviewMethodBody(Fail); if (body == null) return;` | `:503-507` `if (body == null) { failures.Add(PreviewSourceMissing("no-second-derivation-in-the-preview", why)); return; }` |
| `:540-542` (Case 4) same shape | `:574-578` same fix, tag `dialled-delta-still-composes` |
| `:611-613` (Case 5 — **the line the gate named**) same shape | `:648-652` same fix, tag `owner-precedence-still-wins` |

Every guard arm now banks an **unconditional** `failures.Add(...)` before returning, so there is no
path on which the guard returns having asserted nothing. **All three sites were fixed, not only the
one the gate named** — the ratchet dedups by (file, arm, guard) and the other two carried the
identical shape.

⚠ The new doc block at `:436` **quotes** the retired `if (body == null) return;` text. That is safe:
the ratchet scans comment-stripped source (`RegressionMarkerRegression.cs:473`), so the quotation
cannot re-trigger the row. Read at source 2026-09-10.

**Re-checked:** `python tools/gate_brace.py Assets/Editor/Regression/SeatingPreviewShieldRegression.cs`
→ `GATE_BRACE_SUMMARY bad=0 of 1`; zero NUL bytes. **Still not compiled and still not re-gated by
this lane** — the increment is a claim until the lead's next fresh run.
