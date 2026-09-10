# WORK ORDER 1656 — Manage `Band_Notice` and `ManageWorkspaceBack` labels stand down as "text still EMPTY"

**Status:** READY TO IMPLEMENT
**Silo:** UI / Manage screen + UiKit TextFitGuard
**Opened:** 2026-09-10 by the DEVICE-FRAMES-2 lane
**Source:** device play-mode session, APK **2026.09.10.363722**, Seeker `SM02G4061955851`, PID **5095**
**Related:** WO-1652 (TextFitGuard play-mode instrumentation — this ticket is a *finding from* its trace, not a blocker on it)

---

## 1. THE CAPTURED DATA — two labels arm the fit guard and stand down declaring a bug

Both lines are quoted verbatim from `Builds/device-frames/2026-09-10_0943_363722_logcat.txt`,
filtered to PID 5095 (the only PID emitting `TextFitGuard` on that log):

```
09-10 09:12:59.072  5095  5134 W Unity   : [Flow:UI] TextFitGuard [ManageScreenUI/ObsidianPanel/PanelContent/Band_Notice/Label]: armed but text still EMPTY after 600 frames — standing down (a blank plate here is a TEXT-NEVER-SET bug, not a fit bug)
09-10 09:13:46.598  5095  5134 W Unity   : [Flow:UI] TextFitGuard [ObsidianPanel/PanelContent/ManageHeaderActions/ManageWorkspaceBack/Label]: armed but text still EMPTY after 600 frames — standing down (a blank plate here is a TEXT-NEVER-SET bug, not a fit bug)
```

Session context from the same log, so the reader knows the guard itself was healthy when these fired:

```
09-10 09:15:24.294  5095  5134 I Unity   : [Flow:UI] TextFitGuard CENSUS armCalls=503 armed=503 declinedNotPlaying=0 declinedNullText=0 evaluated=172 relaxed=8 stillBlank=0
```

`declinedNullText=0` and `stillBlank=0` — the guard armed everything it was handed and reported no
blank at census time. The two stand-downs above are the guard's *own* warning channel, not a census
failure.

### Frames that show the affected plates

- `Builds/device-frames/2026-09-10_0913a_363722_army_grid.png` — MANAGE - ARMY. The back control at
  top-left renders as a plate carrying an **arrow glyph**, no word.
- `Builds/device-frames/2026-09-10_0913b_363722_army_detail_footman.png`,
  `2026-09-10_0914b_363722_build_categories.png`,
  `2026-09-10_0914c_363722_build_economy_grid.png`,
  `2026-09-10_0915b_363722_build_detail_quarry_placed.png` — same plate, same arrow glyph, every
  workspace screen.
- `Builds/device-frames/2026-09-10_0912c_363722_manage_hub.png` — the Manage hub. **No notice text is
  drawn anywhere on the plate**, which is what `Band_Notice` being empty looks like.

---

## 2. THE PRODUCERS — read at source 2026-09-10, cite these, do not re-derive

### `Band_Notice`

- Built at `Assets/_Modules/Village/UI/Manage/ManageScreenPanel.cs:3730-3736` (`BuildNotice`). The
  label is constructed with an **empty string literal**:
  `_noticeLabel = ElarionUiKit.Label(band, "", 0f, 1f, ElarionUi.Gold, …)` (`:3731`).
- Its seat is chosen at `:1587-1589` — either `NoticeSeatBesideClose` (`:3134-3144`) or the in-body
  `Band(well, "Band_Notice", …)` band.
- Text is set in exactly two places, **both event-gated**:
  - `FlushNotice` (`:7572-7580`) — and its first line is
    `if (_vm == null || string.IsNullOrEmpty(_vm.Notice)) return;`, so with no pending VM notice the
    label is never written.
  - `RenderSessionComplete` (`:4429-4447`) — writes `SessionCompleteText` only when
    `ObsidianQueueGate.Status.AllLinesLoaded()` flips true, and **clears the label back to `""`**
    at `:4444-4445` otherwise.

### `ManageWorkspaceBack`

- Built at `Assets/_Modules/Village/UI/Manage/ManageScreenPanel.cs:4077-4096` (`BuildBackArrow`),
  named at `:4091`.
- `ApplyBackGlyph` (`:4112-4141`) **deliberately blanks the label** once the arrow sprite resolves:
  `var label = back.GetComponentInChildren<TMP_Text>(true); if (label != null) label.text = string.Empty;`
  (`:4130-4131`), then parents a child `Image` carrying `ManageArt.IconBack` (`:4133-4141`).
- The blanking is documented as intentional at `:4098-4110` (WO-1491): *"⛔ THE LABEL IS BLANKED, NOT
  DELETED, AND ONLY WHEN THE SPRITE RESOLVED."* If the sprite is missing the method returns early at
  `:4117-4123` and the button keeps its ASCII `<-` face.

---

## 3. THE TWO HYPOTHESES

**H1 — intentionally icon-only / legitimately empty; the guard's message is a FALSE POSITIVE.**
The source reading in §2 supports this for **both** labels: `ManageWorkspaceBack`'s label is blanked
on purpose by `ApplyBackGlyph`, and `Band_Notice` is authored empty and only filled on an event that
did not occur in this session (no VM notice raised, queue lines not all loaded). Under H1 the defect
is that `UiKitTextFitGuard`'s stand-down message asserts *"a blank plate here is a TEXT-NEVER-SET
bug"* about labels that are correctly empty — turning a diagnostic into recurring noise, and
devaluing the same warning when it fires on a real blank plate.

**H2 — a genuine TEXT-NEVER-SET defect.**
Under H2 one or both labels should have carried text in this session and a producer failed to run.

⚠ **This lane did NOT rule between them.** §2 is a source reading; it is not proof of runtime
behaviour. Do not treat H1 as settled without the check below.

---

## 4. THE DISCRIMINATING CHECK — run this before any edit (§12)

The two labels split cleanly, so run both halves:

1. **`ManageWorkspaceBack`** — the discriminator is whether the glyph sprite resolved. On the same
   log, `grep 'back-glyph-miss' <logcat>`. The trace at `:4119-4122` fires **only** when
   `ManageArt.LoadSprite(ManageArt.IconBack)` returns null.
   - **Absent** + the frames in §1 showing an arrow ⇒ sprite resolved ⇒ the blanking at `:4130-4131`
     ran on purpose ⇒ **H1**.
   - **Present** ⇒ the button should have kept its ASCII face and did not ⇒ **H2**.
2. **`Band_Notice`** — the discriminator is whether a notice was ever raised. `grep '\[Flow:Manage\] notice:'`
   on the same log (the trace is emitted at `:7581`, unconditionally, right after the label is written).
   - **No `notice:` line** ⇒ nothing asked for the band ⇒ **H1**.
   - **A `notice:` line present while the guard still reported EMPTY** ⇒ the write at `:7580` did not
     reach the label the guard armed (two label instances, or the band rebuilt after the write) ⇒ **H2**.

**If both resolve to H1**, the fix is in the guard, not in Manage: give `UiKitTextFitGuard` a way to
be armed on a label that is *allowed* to be empty (an opt-out at the arm site, or a stand-down message
that states the ambiguity instead of asserting a bug), and route `Band_Notice`/`ManageWorkspaceBack`
through it. **If either resolves to H2**, fix that producer and leave the guard alone.

---

## 5. ACCEPTANCE

1. **The check in §4 is run and its output quoted** — both greps, on a fresh log, with the log path.
   The verdict (H1 or H2, per label) is written into this WO before any code changes.
2. **RED first.** A regression case that reproduces the chosen branch against today's tree: under H1,
   a case asserting the guard does NOT emit a TEXT-NEVER-SET stand-down for a label declared
   empty-by-design; under H2, a case pinning that the producer writes the label.
3. **The stand-down warning no longer fires for these two labels** on a play-mode or capture log —
   proven by a fresh log with `grep 'armed but text still EMPTY'` returning no `Band_Notice` and no
   `ManageWorkspaceBack` line.
4. **No other stand-down is silenced.** A genuinely blank plate must still warn — show it still fires
   on a deliberately-blanked fixture, the WO-1652 acceptance-5 shape. A remedy that makes the warning
   go quiet everywhere is a REGRESSION of the detector, not a pass.
5. **No font floor moved.** `FontFloor` (30) and `FontHardFloor` (20) are read, never written — the
   WO-1652 acceptance-4 constraint carries into this ticket.
6. `COMPILE_GATE_OK` + `REGRESSION_OK <n>/<n> suites` on **fresh** logs, judged by the marker, not the
   exit code.

---

## 6. WHAT NOT TO TOUCH

- ⛔ **Do not delete the blanking at `ManageScreenPanel.cs:4130-4131`.** It is the WO-1491 ruling — the
  back face is the kit's arrow sprite, not a literal `<-`. Restoring the ASCII face re-ships the
  "`< -`" kerning defect that ruling was written to kill.
- ⛔ **Do not give `Band_Notice` placeholder text to quiet the warning.** An always-populated notice
  band is a worse defect than a silent one: it puts a permanent line on a plate the mockup draws
  empty, and it would mask a real notice failing to arrive.
- ⛔ **Do not remove the `TextFitGuard` stand-down branch.** §12 forbids stripping instrumentation —
  the warning is the net. Narrow it or make it declarable; never delete it.
- Do not touch the fit/relax mechanism, the 8 relaxations recorded in WO-1652's play-mode census, or
  any Manage layout number. This ticket is about **which labels are expected to be empty**, nothing else.
- Do not hand-edit `.unity` scenes.

---

## 7. WHAT IS UNPROVEN

- Whether either label is genuinely defective — §4 has not been run.
- Whether the two labels share a cause. They have **different producers** (`:3731` authored-empty vs.
  `:4130` deliberately-blanked) and may well split H1/H2. Treat them as two findings that arrived on
  one log, not one bug.
