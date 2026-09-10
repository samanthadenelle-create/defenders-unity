# WORK ORDER 1099 — RESULT (presentation half only)

**Status:** IMPLEMENTED (presentation) - awaiting gate (2026-09-09 lane HARVEST-COPY); owner question still open
**Lane:** HARVEST-COPY (edit-only; no Unity run, no git operation)
**Branch:** `dev`, on top of `e9ac5279a`

---

## 1. What shipped

The **presentation half that needed no ruling**. The `HarvestResultVM` seam now computes and exposes
the three amounts as numbers per resource, and the footer stops reassuring the player in the one state
where the reassurance is a promise the game cannot keep.

### Files changed

| File | Change |
|---|---|
| `Assets/_Modules/Core/UI/HarvestResultVM.cs` | header block for WO-1099; `HarvestResultRow` gains `BankedUnits`, `PendingUnits`, `OverCapUnits`, `OverCapText`, `OverCap`; VM gains `FooterOverCap`, `TotalBanked`, `TotalPending`, `TotalOverCap`; `Build` accumulates before the `MaxRows` cut; a THIRD footer branch for over-cap; `TraceLine` carries the amounts; one `FlowTrace.Step` at the end of `Build`; private `JoinWords` helper |
| `Assets/Editor/Regression/HarvestOverCapCopyRegression.cs` | **NEW** — 7 cases, marker `HARVEST_OVERCAP_COPY_OK` / `_FAIL` |
| `WorkOrders/WORK_ORDER_1099_*.md` | `**Status:**` flipped |

**`HarvestOverflowModal.cs` was NOT touched.** The View stays a dumb skin: it already draws
`vm.FooterLine` (`HarvestOverflowModal.cs:211-230`) and already emits `vm.TraceLine`
(`:146`), so the whole delivery lives in the VM. MVVM strict is preserved — no logic moved into
the View, and no number is re-derived there.

**Path correction for the dispatch note:** there is no `IPanelViewModel` under
`Assets/_Modules/Core/UI/Mvvm/` for this panel, and the VM does not live under
`Assets/_Modules/Village/Harvest/`. The real pair is
`Assets/_Modules/Core/UI/HarvestResultVM.cs` (decides) +
`Assets/_Modules/Core/UI/HarvestOverflowModal.cs` (draws). `HarvestResultVM` is a plain pure static
seam, not an `IPanelViewModel` implementation — same shape as `WelcomeBackDoorsVM` (WO-1408).

### The footer, before and after

Before (HEAD, `HarvestResultVM.cs:337` as the WO cites it) — fired on `!anyBurned`, **cap-blind**:

> `Nothing was lost - every waiting unit banks as soon as there is room.`

After, on the owner's seq=4974 frame:

> `Spend Wood, Iron or Stone to get back under your Lumberyard, Foundry and Stoneyard caps - storage is 1,680,847 over, so nothing banks yet. 10,685 waiting stays safe until it does.`

Leads with the action; names every container as the ceiling; states how far over; keeps WO-1434's
safety promise in the tail rather than at the front. ASCII only, invariant-culture figures.

The branch also requires `TotalOverCap > 0`, not just the `OverCap` flag: `Merge` takes the **minimum**
`Current` and the **maximum** `Max` across a resource's producers, so a status flagged over-cap by one
producer can still land at or under the merged ceiling. There the player is genuinely not above the
cap after the collect, "storage is 0 over" would be nonsense, and the ordinary reassurance is the true
sentence — so it falls through.

### Why the verb is SPEND and not "spend or upgrade"

The dispatch asked for "spend **or upgrade storage**". Above the cap that would be a second false
promise, and three separate authorities already pin it:

- `BankOverflowStatus.OverCap` (`Assets/_Modules/Core/Economy/TownBankCapacity.cs:227-236`) spends a
  paragraph on over-cap being a **different situation** from a full bank.
- The bank's own over-cap `FlowTrace.Warn` (`TownBankCapacity.cs:781-785`) deliberately **drops** the
  `Build or upgrade a {container}` clause it prints when merely full (`:789-791`).
- `HarvestResultShapeRegression` case 9 `[overcap-spends]` fails the build if the over-cap door reads
  anything but `SPEND WOOD`.

34,000 is the L6 ceiling, so at 732,031 no container the game can ever build makes room. The
container is still **named** — as the cap the player is above, not as a thing to buy.

### Scope note the owner should see

The ticket says "**at or over** cap". Only the **over**-cap half moved. The **at-cap** half is pinned
against change by `HarvestResultShapeRegression` case 7 `[said-once]` and case 13(d), whose fixtures
are `FULL` (not `OVER`) and which **require** the reassurance there — and that file is not this lane's
to edit. It is also defensible at cap: room genuinely does arrive by spending **or** upgrading, and
those rows carry an `UPGRADE`/`BUILD` door. Case 6 `[full-still-reassures]` in the new suite pins that
the at-cap frame is unchanged, so this lane can prove it broke nothing it does not own.

---

## 2. Regression — registration line

New suite, **not** registered by this lane (no `DataRegression.cs` edits, per the lane rules). Add
verbatim, in the harvest block beside `:748`/`:750`:

```csharp
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "harvest-overcap-copy suite", () => { if (!DeNelle.Editor.Regression.HarvestOverCapCopyRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[harvest-overcap-copy] " + r); });
```

Standalone: `run-unity-method DeNelle.Editor.Regression.HarvestOverCapCopyRegression.RunAll`
Markers: `HARVEST_OVERCAP_COPY_OK` / `HARVEST_OVERCAP_COPY_FAIL`.

Fixture is the owner's seq=4974 frame verbatim (Wood 732,031 / Iron 639,336 / Stone 411,480, all
against 34,000, all banking 0, all `Collectors`), plus an at-cap control frame.

### Cases

| # | Case | Asserts |
|---|---|---|
| 1 | `[overcap-numbers]` | `BankedUnits` / `PendingUnits` / `OverCapUnits` per row equal the bank's figures; totals sum |
| 2 | `[no-false-comfort]` | neither "nothing was lost" nor "as soon as there is room" appears; `FooterReassures` false |
| 3 | `[leads-with-action]` | footer starts with the verb, says spend, names every container, states the over-by; no "upgrade"/"build" offer; every door reads `SPEND` |
| 4 | `[safety-survives]` | pending total still on the footer and still called safe; rows still marked retained |
| 5 | `[ascii-only]` | every produced character is ASCII |
| 6 | `[full-still-reassures]` | the at-cap control still reassures exactly once (proves `[said-once]` unbroken) |
| 7 | `[trace-names-amounts]` | `TraceLine` carries `banked=` / `pending=` / `overBy=` / `footer='overcap'` |

---

## 3. RED-first proof — **static RED, UNRUN. Stated as unproven.**

This lane ran **no Unity** (lane rule). The suite was authored RED against `e9ac5279a` and the RED is
**cited, not observed**. Each assertion names the HEAD fact it contradicted:

| Case | Why it was RED on HEAD |
|---|---|
| 1 | `HarvestResultRow.BankedUnits` / `PendingUnits` / `OverCapUnits` / `OverCapText` / `OverCap` **did not exist**, and `HarvestResultVM.TotalBanked` / `TotalPending` / `TotalOverCap` did not exist — the case did not compile |
| 2 | HEAD chose the footer on `!anyBurned` alone (the WO cites `HarvestResultVM.cs:337`), so the over-cap fixture produced exactly the banned sentence with `FooterReassures == true` |
| 3 | `vm.FooterOverCap` did not exist; the HEAD footer began "Nothing was lost", named no container and carried no over-by figure |
| 4 | vacuously true on HEAD (the reassurance mentions safety) — this case exists to stop the FIX from over-correcting, not to fail HEAD. Named as such, not counted as RED |
| 5 | vacuously true on HEAD — an invariant carried forward |
| 6 | vacuously true on HEAD — the control, and the only reason it is in the suite is to prove the change did not reach the at-cap path |
| 7 | `TraceLine` on HEAD emitted no `banked=`/`pending=`/`overBy=` token and could never emit `footer='overcap'` |

So: cases **1, 2, 3, 7 are genuine RED** (2 of them compile-fail, which is the strongest RED
available); cases **4, 5, 6 are guards, and are declared as guards** rather than dressed up as RED.

**What is NOT proven by this lane:** that the suite passes. `COMPILE_GATE_OK` and
`REGRESSION_OK <n>/<n>` on a fresh log are owed by whoever gates. Nothing here claims a green run.

---

## 4. Brace + NUL

```
Assets/_Modules/Core/UI/HarvestResultVM.cs            braces 31/31 MATCH | NUL 0 | non-ASCII 0
Assets/Editor/Regression/HarvestOverCapCopyRegression.cs braces 21/21 MATCH | NUL 0 | non-ASCII 0
```

(`python`, byte-read, run 2026-09-09 in this lane.)

---

## 5. ⛔ THE ONE OWNER QUESTION — STILL OPEN, NOT GUESSED

Quoted verbatim from the work order, section **"What 'here' meant — OPEN, do not guess"**:

> The flag landed at **13:06:17**. WO-1098's quiescence failure at **13:01:52** named
> **`'Harvest Result'`** as a modal still open after an arena win, its own `IsOpen` probe reporting
> VISIBLE. **This is very likely the same panel instance, still on screen five minutes later.**
>
> So "here" is either:
> 1. *"this panel is stuck open"* → the flag is corroborating evidence for **WO-1098**, and this ticket
>    is the numbers; or
> 2. *"these numbers are nonsense"* → this ticket is the point and WO-1098 is separate.
>
> Both are recorded because **I cannot tell from the capture**, and the two readings send the work to
> different places. The owner's one-line answer settles it.

**This lane did not answer it and did not act on either reading.** The presentation fix is correct
under both: reading 1 leaves it as a copy improvement that is independently true, reading 2 makes it
the point. Context the owner may want when she answers: `docs/READY_RCA_2026-09-09.md` row 1098 records
that the passive/repeat-open cause was already fixed on HEAD by `f4e4630e3`, and that the zero clock
was the documented effect of a correctly-owned visible modal — so reading 1 does **not** reopen a
timeScale leak.

**A second item also stays hers:** acceptance criterion "in wording the owner approved" is
**unticked**. What shipped is a *proposal* for the wording, not an approved string. Changing it is a
one-line edit to the footer branch.

---

## 6. Acceptance criteria — honest state

| Criterion | State |
|---|---|
| Origin of the over-cap balance established, cited at `file:line` | **DONE, elsewhere** — `docs/READY_RCA_2026-09-09.md` row 1099: tester/dev codes; normal accrual not proven broken |
| If unclamped: deposit path clamps + regression | **N/A on that finding** — not unclamped; and `OfflineHarvestService.cs` / `ResourceCollectorService.cs` are other lanes' files, untouched here |
| At or over cap, footer states the situation and names the action | **OVER-cap: DONE.** AT-cap: deliberately unchanged (section 1, scope note) |
| …in wording the owner approved | **NOT TICKED** — proposal only |
| Editor-side F8 capture writer links nearby screenshots | **NOT THIS LANE** — `logs/f8-inbox` tooling is out of the owned file set |
| Owner says which reading of "here" she meant | **OPEN** (section 5) |
| Brace balance; gate markers on a fresh log | Braces DONE (section 4). **Gate markers OWED** — this lane ran no Unity |

## 7. Unproven / not claimed

- **The suite has not been run.** No `COMPILE_GATE_OK`, no `REGRESSION_OK`, no marker on any log. The
  RED is static and cited, never observed.
- **No device or capture proof of the new footer.** The label band is `0.225f..0.29f`
  (`HarvestOverflowModal.cs:230`) and the new sentence is longer than the one it replaces; whether it
  fits at 2670x1200 without ellipsising is a **capture claim** and is owed, exactly as
  `HarvestResultShapeRegression`'s own header owes its plate-fit claim.
- **Closed, though:** one of the WO's own Unproven items. *"Whether a banked column of `0` is correct
  behaviour at over-cap or a second defect."* It is **computed, not defaulted**:
  `TownBankCapacity.cs:781-785` logs `earned {requested}, added 0` on the over-cap branch by design,
  and `Granted` on the published `BankOverflowStatus` is that same zero. The VM reads it; it does not
  re-derive it.
- **Not re-verified this session:** the 21x figure and the L6-ceiling reading are quoted from the WO
  and from `docs/READY_RCA_2026-09-09.md`, not re-measured against the save.
- **Known log duplication, declared so nobody RCAs it:** a modal open now emits **two** near-identical
  `[Flow:Bank]` lines — `harvest-result rows: <TraceLine>` from `HarvestOverflowModal.cs:146` and
  `harvest-result vm: <TraceLine>` from the VM. That is deliberate: the dispatch asked for a Step on
  the **VM build**, and the VM seam has callers/oracles that never construct a modal. It is not a
  double harvest. No suite pins the VM's purity — the only source-text lint in the harvest suites is
  `[one-close-owner]` at `HarvestResultShapeRegression.cs:489-501`, and it reads
  `HarvestOverflowModal.cs`, not the VM. If the extra line is judged noise, delete the Step and the
  `using DeNelle.Core.Diagnostics;`; the enriched tokens still reach the log via `:146`.

## 8. Notes for the committer

- `Assets/Editor/Regression/DataRegression.cs` is **already dirty in the shared tree** (another lane,
  8 lines). Add the registration line from section 2 **on top of** that lane's edit — do not assume a
  clean file, and do not revert their hunk.
- `BOARD.html` is also already dirty from another lane. This lane regenerated nothing; the board rebuild
  after the `**Status:**` flip belongs to whoever gates.
- This lane ran **no Unity and no git operation**. The tree is **not gated**.
