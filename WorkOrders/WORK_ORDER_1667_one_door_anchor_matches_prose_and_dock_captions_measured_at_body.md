# WORK ORDER 1667 — a pin its own prose satisfies, and a live-bar oracle missing a weight term

**Status:** IMPLEMENTED (b re-fixed after chain 38) — awaiting re-gate
> ⚠ **Chain 38 (`Builds/wave8-reg1`) RED on (b), and it was RIGHT to.** Two causes, both now
> closed: **(1) my offline font was WRONG** — the live measurer resolves `FontRole.Body` to
> **`ElarionLocaleFallback`**, not `font_body`, because the numeral gate REJECTS `font_body`
> (`[Flow:UI] role font 'font_body' REJECTED`, same log). Real `JOURNEY` raw is **95.6**, not the
> 86.63 I reported. **(2) the row that red was `1080x1920` PORTRAIT**, and the game is
> **LANDSCAPE ONLY** (owner ruling 2026-09-10, WO-1631). That portrait row is REMOVED from
> `DockSurfaces` with the ruling cited at source; the weight term is KEPT. Re-measured against the
> live figures, the tightest LANDSCAPE surface (2048x1536, box 120.1) seats the charged `JOURNEY`
> 105.1 at **87.5%, 15.0 px of headroom** — no red, no widened slack.
> See the RESULT's **§C** for the correction and the re-run table.
> (a) Case7 re-anchored on the full retirement text (option i; the full line is unique, counted 1 vs
> 2 for the short form). **RED-FIRST PROVEN: the `:811`-replaced-with-a-live-call mutation goes
> GREEN → RED.** `HudKitController.cs:1933`'s false claim corrected in the same change.
> (b) Weight term added as ONE shared `internal const HudLabelFitRegression.BoldOnlyWidthSlack = 1.10f`
> (referencing the runtime `RumorBoardPanel` constant was rejected with reasons); role stays Body;
> per-surface solved `boxW` logged before/after. **No red, as the ticket predicted and required** —
> the portrait surface's worst caption moves from a reported 87.9% to an honest 96.7% of its box.
> b5 (es/de captions over the floor-width box) recorded as a separate ticket subject, NOT implemented.
> See `WORK_ORDER_1667_one_door_anchor_matches_prose_and_dock_captions_measured_at_body.RESULT.md`.
**Silo:** oracles — `Assets/Editor/Regression/SessionShapeRegression.cs` (a) and
`Assets/Editor/Regression/HudActionBarRegression.cs` (b). One doc-fix comment in
`Assets/_Modules/HUD/Kit/HudKitController.cs` belongs with (a).
**Origin:** WO-1666 §7 and §1 — both found while implementing that ticket, both deliberately left
alone because they sit outside its silo.
**Number:** PRE-ASSIGNED by the lead. **The `CLI_LANES_WO_NUMBERS.md` banner was NOT edited by this lane.**
**Date:** 2026-09-10
**Two halves, one ticket:** each is a *measurement that does not measure what it claims*. They share
no file, so they are independently landable — land (a) alone if (b) needs more time.

---

# PART (a) — `Case7_OneDoor`'s retirement anchor is satisfied by the PROSE that documents it

## a1. THE DEFECT

`SessionShapeRegression.Case7_OneDoor` guards the owner's 2026-08-07 retirement of the Builders
chip. Read at source 2026-09-10, `SessionShapeRegression.cs:314`:

```csharp
if (view.IndexOf("// BuildQueueStatusChip(pool);", StringComparison.Ordinal) < 0)
    failures.Add("[one-queues-door] the Builders chip's retirement line is gone from " +
                 "HudKitController - the chip was retired by the owner on 2026-08-07 and " +
                 "the bar's Manage face is the single Queues entry");
```

**That substring occurs TWICE in `HudKitController.cs`.** Both quoted verbatim:

1. **The real retirement**, `HudKitController.cs:811` (inside `Build()`):
   ```
               // BuildQueueStatusChip(pool);   // retired 2026-08-07 (owner)
   ```
2. **A PROSE MENTION of it**, `HudKitController.cs:1932`, inside `BuildQueueStatusChip`'s own doc
   comment:
   ```
               // `// BuildQueueStatusChip(pool);` in Build(), so nothing here ever runs, and
   ```

So **the comment that documents the pin satisfies the pin.** Delete the real retirement at `:811`,
put a live call in its place, and Case7 still passes. The guard survives its own subject.

## a2. THE MUTATION TABLE — measured, not argued

The 15d state machine (`HudLabelFitRegression.cs`, added by WO-1666 §5) was ported and replayed
against the real `HudKitController.cs` and three mutations. Case7's assertion was evaluated on the
same inputs:

| input | 15d | **Case7_OneDoor** |
|---|---|---|
| HEAD, unmodified | `NOTE(dormant - pre-emptive pin)` | GREEN |
| `:811` retirement line replaced with a **live** call | **FAIL** | **GREEN** ⛔ |
| a **live** call added elsewhere, comment left intact | **FAIL** | **GREEN** ⛔ |
| every mention of the token deleted | `NOTE(removed outright)` | **RED** |

**Case7 catches only total erasure — the one thing nobody would do by accident.** It misses both
shapes of an actual un-retirement. ⚠ The reason the second row still reads GREEN even though the
`:811` line was replaced is precisely the `:1932` prose: it alone satisfies the `IndexOf`.

## a3. FIX SHAPE — pick ONE, and say which

- **(i) ANCHOR ON THE FULL RETIREMENT TEXT** (simplest, recommended). Assert the byte-exact
  `// BuildQueueStatusChip(pool);   // retired 2026-08-07 (owner)`, which appears once and cannot
  be satisfied by prose about it. ⚠ Note the **three spaces** before the second `//` — copy the
  line, do not retype it.
- **(ii) REQUIRE EXACTLY ONE MATCH, and locate it.** Count occurrences and assert the one that
  lives inside `Build()`. More faithful, more code; only take it if (i) proves too brittle.

⛔ **Do NOT solve this by deleting the `:1932` prose.** That comment is load-bearing documentation —
it is how a reader of `BuildQueueStatusChip` learns the method never runs. An oracle that forces
useful comments to be deleted is a worse oracle.

**Land with it (same change):** `HudKitController.cs:1933` currently asserts *"SessionShapeRegression
Case7_OneDoor FAILS the build if that byte-exact retirement line disappears."* **The mutation table
above disproves that sentence** — it is true only after this ticket lands. Fix the comment in the
same commit (CLAUDE.md §15: canon updates travel with the change), or the next seat inherits a
comment that lies about a guard.

## a4. RED-FIRST (mandatory — this half CAN red, so it must be seen to)

1. In a scratch copy, replace `:811` with a live `BuildQueueStatusChip(pool);` call.
2. **Before** the fix: Case7 GREEN (the defect, reproduced).
3. **After** the fix: Case7 **RED**, naming the retirement line.
4. Restore, confirm green on HEAD.
5. Also replay row 3 (live call added elsewhere, comment intact) — (i) does **not** catch that one
   and is not required to; **`HudLabelFitRegression` 15d already owns it**, and the two are
   deliberately disjoint. Say so in the RESULT rather than widening Case7 to duplicate 15d.

---

# PART (b) — `CheckMeasuredPeacefulDock` measures the LIVE bar's captions with no weight term

## b1. THE CLASSIFICATION IS ALREADY DONE — READ IT, THEN RE-READ IT AT SOURCE

WO-1666 asked whether the dock caption is a skinned obsidian face. **It is NOT.** Traced
2026-09-10, and the answer is more interesting than either branch the WO anticipated:

| step | source | what it does |
|---|---|---|
| `BuildAdaptivePeacefulDock` | `HudKitController.cs:2594` | five `BuildPeacefulDockSlot(...)` calls, `:2621-2636` |
| `BuildPeacefulDockSlot` | `HudKitController.cs:2681` | `StyleAsRoundMedallion`, `SetIcon`, `SetCaption(caption)` (`:2717`), then `FitSingleLine(slot.caption, FontHardFloor, FontMicro)` (`:2726`) |
| `ActionSlotHandle.SetCaption` | `ElarionUiKitObsidian.cs:1095` | builds the caption with `Label(root.transform, "", 0.02f, 0.26f, ElarionUi.Parchment, ElarionUi.FontMicro, TextAlignmentOptions.Center, 0.06f, 0.94f, bold: true)` then **`EnsureFont(caption, FontRole.Body)`** (`:1108-1112`) |
| `ElarionUiKit.Label` | `ElarionUiKit.cs:1905-1923` | `t.characterSpacing = spacing` (**0**, not passed by `SetCaption`); `if (bold) t.fontStyle = FontStyles.Bold` |

**Nothing on that path calls `MedievalUiSkin.ApplyButton` or `EnsureFont(..., FontRole.Title)`.**

### ⛔ SO THE VERDICT IS NEITHER OF THE WO'S TWO BRANCHES — DO NOT APPLY `MeasureFacePx`

- **The ROLE is CORRECT.** `FontRole.Body` at `HudActionBarRegression.cs:402-404` is the role the
  caption is actually drawn in — `EnsureFont(caption, FontRole.Body)` says so explicitly.
  ⛔ **Re-pointing this to Title would make the live-bar oracle falsely PESSIMISTIC**, the exact
  hazard WO-1663 §7 kept three sites away from.
- **But the caption is drawn BOLD** (`bold: true`), and `MeasureLineWidthPx` sums **regular-weight**
  advances with no weight term (`ElarionUiKitObsidian.cs:2896-2897` says so in its own docs).
  **So the oracle under-measures the shipped bar by the bold weight.** There is no
  `characterSpacing` term to worry about here — it is 0 — which is what makes this *narrower* than
  WO-1663's defect, not the same one.

**This is the same shape as `RumorBoardPanel.PageButtonBoldSlack = 1.10f`** (`RumorBoardPanel.cs:126-128`),
whose own doc comment concedes it: *"MeasureLineWidthPx sums regular-weight advances; the button is
bold."* Two places, one missing term.

## b2. HOW MUCH IT MATTERS — measured, at the worst slot the solver permits

`HudDockLayout.MinSlotPx = ElarionUiKit.MinTouchPx` = **112** (`HudDockLayout.cs:62`), and the
oracle's box is `sol.SlotWidthPx * CaptionInset` with `CaptionInset = 0.88f`
(`HudActionBarRegression.cs:441`), applied at `:399`. So the **narrowest** box the solver can hand a caption is
`112 * 0.88` = **98.56 ref px**. English captions at `FontHardFloor` (20), measured offline from the
committed TMP YAML:

| caption | Body (what the oracle charges) | Body x1.10 (with a weight term) | vs 98.56 |
|---|---|---|---|
| TALK | 45.22 | 49.74 | fits |
| HERO | 50.92 | 56.02 | fits |
| BUILD | 54.04 | 59.44 | fits |
| MANAGE | 83.72 | 92.09 | fits |
| **JOURNEY** | **86.63** (88% of the box) | **95.29 (96.7% of the box)** | **fits, by 3.3 px** |

**So English does not red today** — the gap is latent, not live. It is still a defect: the oracle
reports the worst caption at 88% of its floor-width box when the honest figure is ~97%, i.e. it is
under-reporting the one number a reader uses to decide whether there is room. ⚠ **Do not turn "it
fits" into "there is nothing here" — a 3.3 px margin misreported as 12 px is exactly how the next
copy change ships cut.**

## b3. FIX SHAPE

Add a **bold-only** slack to this oracle's caption measurement — a named constant with the same
reasoning as `RumorBoardPanel.PageButtonBoldSlack`, applied to a **Body** measurement.

- ⛔ **NOT `MeasureFacePx`, NOT `SkinnedFaceRole`, NOT `SkinnedFaceWidthSlack`.** Those carry the
  Title role and cover bold **+ characterSpacing 2**; this face has neither. Reusing them here
  would be borrowing a constant for a term it does not describe — the failure CLAUDE.md §2/§5/§16
  keep naming. If a shared helper is wanted, it is a `boldSlack`-parameterised measurer, and that
  is the kit-side unification WO-1663 §9 describes — **not this ticket**.
- Name the constant where it is used, state that it covers **weight only**, and say why 1.10 (the
  value the repo already chose for exactly this term) rather than inventing a second number.
- The unmeasurable branch at `HudActionBarRegression.cs:405-411` must keep behaving as a **stated
  skip** (`-1` is "no font resolvable", never "it fits"). Do not let a slack multiply the sentinel.

## b4. RED-FIRST — and be honest that this half may NOT red on English

⚠ **Read this before running it.** From b2, English at the floor-width slot still fits with the
slack. A RED is therefore **not guaranteed**, and manufacturing one would be worse than not having
one. What IS required:

1. **Print the SOLVED widths.** The oracle already computes `sol.SlotWidthPx` per shipping surface
   (`DockSurfaces`); log the per-surface `boxW` and the per-caption measured width **before and
   after** the slack. That is the evidence the change took — the numbers must move by ~10%.
   **This replaces a red as the proof of take.** Record the table in the RESULT.
2. **If any caption reds at a real surface, JOURNEY is the one to expect** (86.63 -> 95.29 at the
   floor slot). Name it if it happens; do not predict it if it does not.
3. ⛔ **If something reds, the fix is NOT to weaken the slack.** Remedies in the repo's ruled order:
   fewer characters in canon (`hud.nav.*`), or the dock solver giving that tier more room. Never
   lower `FontHardFloor`, never shrink `CaptionInset`, never drop the weight term you just added.

## b5. ⚠ A THIRD THING FOUND WHILE MEASURING — RECORD IT, DO NOT FIX IT HERE

The oracle measures `HudStrings.Get(key)`, i.e. **the active locale only** — English in a gate run.
Other shipped locales are longer, and two already exceed the floor-width box **in Body alone**,
before any weight term:

| locale | caption | Body @20 | vs 98.56 |
|---|---|---|---|
| es | `CONSTRUIR` | **108.82** | **OVER by 10.3** |
| es | `GESTIONAR` | **108.32** | **OVER by 9.8** |
| de | `VERWALTEN` | **111.30** | **OVER by 12.7** |

⚠ **This is NOT proof of a shipped defect** — it is the *floor-width* slot, not the solved width at
a real surface, and `FitSingleLine` ellipsises rather than overflowing. But it does mean **no oracle
in this repo measures a non-English dock caption**, which is a coverage gap on a screen every player
sees. **Write it up as its own ticket; do not smuggle a localisation sweep into this one.**

---

## WHAT NOT TO TOUCH

- ⛔ **`MeasureFacePx`, `SkinnedFaceRole`, `SkinnedFaceWidthSlack`, `SkinnedFaceWhy`,
  `TryWrapLines`, `CheckNightMarketTitleFit`** (WO-1662/1663) — and specifically **do not apply
  them in Part (b)**; see b3.
- ⛔ **`HudLabelFitRegression` 15d** (WO-1666). Part (a) must NOT widen `Case7_OneDoor` to duplicate
  it — the two are deliberately disjoint, and the mutation table proves the split is real.
- ⛔ **The `:1932` prose comment** in `HudKitController.cs` — it is documentation, not the bug (a3).
- ⛔ **The retirement itself.** `// BuildQueueStatusChip(pool);` stays commented; this ticket
  strengthens the guard, it does not revisit the owner's 2026-08-07 ruling.
- ⛔ **`MedievalUiSkin`, `ElarionUiKit.Label`, `ActionSlotHandle.SetCaption`,
  `MeasureLineWidthPx`.** Part (b) fixes a CALLER's missing term, not the kit. Changing
  `SetCaption` restyles the shipped bar.
- ⛔ **Every font floor** (`FontFloor` 30, `FontHardFloor` 20, `ElarionUi.FontMicro`),
  `HudDockLayout.MinSlotPx` (== `MinTouchPx` 112, the touch floor), and `CaptionInset` 0.88.
- ⛔ **The `hud.nav.*` canon strings** and their locale twins — b5 is a separate ticket.
- ⛔ **The `GlyphBaseline` array** (`UICaptureLaunch.cs`) — shrink-only.
- ⛔ **The `CLI_LANES_WO_NUMBERS.md` banner** — this number was pre-assigned by the lead.

---

## ACCEPTANCE

- [ ] (a) `Case7_OneDoor` **REDS** on a scratch mutation that replaces the `:811` retirement with a
      live call, and is green on HEAD. The chosen fix shape (i or ii) is named, with the rejected
      one explained.
- [ ] (a) `HudKitController.cs:1933`'s claim about Case7 is corrected in the SAME change.
- [ ] (a) The RESULT states that row 3 (live call elsewhere, comment intact) is 15d's job, not
      Case7's, and that this is deliberate.
- [ ] (b) The caption's face is **re-read at source** and the finding confirmed or corrected: role
      Body **correct**, weight term **missing**, `characterSpacing` **0**. If any of those three is
      wrong on a fresh read, **say so and stop** — the fix shape changes with it.
- [ ] (b) A **weight-only** slack is added to that oracle, named where used, with `MeasureFacePx`
      and the Title role explicitly NOT used, and the `-1` sentinel still a stated skip.
- [ ] (b) Per-surface solved `boxW` and per-caption widths **before and after** are recorded in the
      RESULT. A red, if one occurs, names the caption; if none occurs, that is stated plainly and
      is not treated as failure.
- [ ] (b) b5's locale gap is written up as its own ticket, not fixed here.
- [ ] `python tools/gate_brace.py <every .cs touched>` clean, NUL-free.
- [ ] `COMPILE_GATE_OK` + `REGRESSION_OK <n>/<n> suites` on fresh logs (markers, never exit codes).
      **`<n>/<n>` must be UNCHANGED** — no suite is added or removed by either half.
- [ ] The WO's own `**Status:**` line is flipped and a `.RESULT.md` written **by the lane that owns
      it**, both paths reported (CLAUDE.md §11 cadence).

---

## WHY ONE TICKET

Both halves are the same class of bug this file family keeps producing: **an assertion whose text
no longer describes what it asserts.** (a) checks for a string that its own documentation supplies;
(b) charges regular-weight advances for a face drawn bold. Neither is a big change; both need the
same discipline — read the thing at source, then prove the guard moves. ⚠ They touch **different
files** and can land separately if (b)'s numbers need more time.
