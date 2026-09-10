# WO-1655: `gate` has no display name in EITHER catalog, so its queue row reads `Unknown structure`

**Status:** IMPLEMENTED - awaiting gate 2026-09-10 (fix + RED-first oracle landed edit-only in a lane worktree off `dev` @ `efc56f67c`; no Unity run, no gate, no commit - the lead gates and commits). ⛔ **THE TICKET'S §2 CAUSE WAS A FALSE PREMISE and the RESULT says so:** the gate IS authored (`structures-catalog.json` row `gate_stone` / "Stone Gate"); the FIXTURE seeded a non-id. **NOTHING was added to either catalog and no JSON was touched.** RESULT: `WorkOrders/WORK_ORDER_1655_gate_has_no_display_name_in_either_catalog_queue_row_reads_unknown_structure.RESULT.md` *(was: READY TO IMPLEMENT)*
**Silo:** **DATA / catalog** (`BuildingTierCatalog` + `CatalogRegistry` sources). ⛔ **NOT the queue UI.**
**Number:** PRE-ASSIGNED by the lead. ⛔ **Do NOT edit `CLI_LANES_WO_NUMBERS.md`.**
**Source:** WO-1566 audit re-tick, RESULT row **8.6** (audit `2039e2c41`, re-tick `9592cdd6f`).
**Yardstick row:** WO-1566 §2 panel 8, row **8.6** — *"Rows name the structure in words."*
**Cross-links:** **WO-1651** (the refund note on this same row — see §5) · **WO-1479**
(`queue_cancel_never_quotes_the_refund_it_will_pay`, which introduced that note).

> ## ⛔ SCOPE CORRECTION — THE QUEUE UI IS NOT BROKEN. IT IS CORRECTLY REPORTING A DATA DEFECT.
> The lead's brief framed this as a queue-row presentation bug. **It is not.** `Unknown structure` is a
> deliberate, loud placeholder that the VM emits **beside a `FlowTrace.Fail`**, and the code says so in
> its own words (`ManageScreenVM.cs:1010-1013`):
> *"Neither catalog knows this id. That is a DATA DEFECT, not a formatting inconvenience, and
> CLAUDE.md section 12 says it must be LOUD. The row still paints (a queue that hides a running job is
> worse), but it paints an honest placeholder instead of a title-cased id quietly presented as a name."*
> **A lane that "fixes" this in the UI — by title-casing the id, hiding the row, or softening the
> string — destroys the only signal that found the bug and ships the raw id as a name.** The fix is in
> the DATA.

---

## 1. THE EVIDENCE

`Builds/wave5-manageflow1` (08:09, HEAD `2039e2c41`), read NUL-stripped. The VM's own failure trace,
which names the id precisely:

> `[Flow:Manage] queue row catalog MISS: neither BuildingTierCatalog ('gate') nor CatalogRegistry
> ('gate') has a display name for job 'gate:4:1' (channel Builder). The player would otherwise read
> the raw id as a structure name`

And in the picture — `ManageFlow_BUILD_queue_2670x1200.png` (opened), Builders queue, row 3:

| # | Row | Thumbnail |
|---|---|---|
| NOW | `Archer Tower - Level 2` — `7m 0s LEFT \| 0% DONE` | ✅ tower art |
| NOW | `Barracks - Level 4` — `11m 0s LEFT \| 0% DONE` | ✅ barracks art |
| **3** | ⛔ **`Unknown structure`** — `3m 0s OF WORK` | ⛔ **none** |
| 4 | `Armorer - Level 3` — `15m 0s OF WORK` | ✅ armorer art |

**The missing thumbnail is the same root cause, not a second bug:** `portraitId` is assigned from the
catalog id in the branch above (`ManageScreenVM.cs:~1006`), and the miss branch never reaches it.

## 2. THE CAUSE

The structure id **`gate`** is absent from both name sources:
- `BuildingTierCatalog` — no `gate` ladder entry;
- `CatalogRegistry` — no `gate` registration. For contrast the same log shows a healthy neighbour:
  `[Flow:Catalog] registered id='mine_crystal' (type=Resource); total=8.`

The queued job is `gate:4:1` (channel `Builder`), i.e. a real, buildable, **queueable** structure that
the player can put in the Builder line — and it has no display name anywhere.

⚠ **Gates are real in this game and this is not a phantom id.** CLAUDE.md §6 lists `Gate` among the
`IDamageableStructure` implementors, and `Assets/Resources/Portraits/Buildings/gate_stone.png` exists on
disk. **Note the portrait is keyed `gate_stone` while the job id is `gate`** — resolve which is
canonical as part of this ticket rather than adding a second name for the same thing.
⛔ Per `ManageArt.cs:306` the portrait key is the catalog id **VERBATIM, underscores intact** — so if
the catalog id becomes `gate_stone` the portrait resolves for free, and if it stays `gate` the PNG must
be renamed. **Do not add a slug/alias layer**; that rule exists because aliasing fails silently.

## 3. FILES TO EDIT

| File | Change |
|---|---|
| The catalog **data** source that feeds `BuildingTierCatalog` / `CatalogRegistry` | Give `gate` (or `gate_stone` — §2) a display name and, if it has an upgrade ladder, its tiers. ⛔ **Canonical JSON is edited in BINARY, patched from HEAD bytes, with the LF count proven afterwards** — a text-mode rewrite has flattened 12 of these files before. |
| `Assets/Editor/Regression/*` | An oracle that fails **by name** on any id a queue job can carry that resolves no display name — the shape `ManagePortraitCoverageRegression` already uses for portraits. |
| `Assets/Editor/Regression/DataRegression.cs` | Registration line only. |
| ⛔ `ManageScreenVM.cs` | **Nothing.** The miss branch (`:1009-1021`) is correct and stays exactly as it is. |

## 4. ACCEPTANCE

1. **Row 8.6 from a frame:** a fresh `RunManageFlowMapCaptureHeadless`; open
   `ManageFlow_BUILD_queue_2670x1200.png` and read a real structure name **and a thumbnail** on every
   row.
2. **`queue row catalog MISS` appears ZERO times** on the run's log. The trace already exists and is
   the cheapest proof — grep it, do not eyeball it.
3. **A coverage oracle that fails BY NAME**, so the next unnamed structure is caught by CI and not by
   an audit six weeks later. RED-first: it must fail against today's tree, naming `gate`.
4. The `gate` vs `gate_stone` question is **answered explicitly in the RESULT**, and the portrait
   resolves through `ManageArt.BuildingPortraitKey` **without any alias layer**.
5. `COMPILE_GATE_OK` + `REGRESSION_OK <n>/<n> suites` on **fresh** logs, judged by the marker.
6. `**Status:**` -> `AWAITING OWNER MATCH` per WO-1566 §2.0.

## 5. THE OTHER HALF OF THIS ROW — WO-1651, ALREADY DONE. DO NOT RE-FIX IT.

The audit found a second defect on the **same physical queue row**: the refund note
`"No refund - nothing was paid for this job"` (`ObsidianQueueVM.cs:229`, introduced by **WO-1479**)
drawing **ZERO of 33 printable glyphs** at 2670 on all three queues —
`UI_GLYPH_FAIL x12 NEW over 20 panels (17 clean, labels=361, ...)`.

⛔ **That is WO-1651 and it is `IMPLEMENTED - awaiting gate 2026-09-10`.** The fix is already in the
tree: `ManageScreenPanel.cs:7098` now runs `ElarionUiKit.FitSingleLine(refund, QueueStateFontFloorPx,
QueueLineFontPx)` — the refund note fitted to the row's own 24 px floor, like the timer line two
blocks up, with the arithmetic worked out in the comment at `:7069-7095` (the band is
`0.285 x 112 = 31.92 px`, matching the captured 31.9 to a tenth of a pixel, because at 2670 the row
pins to the touch floor). **No new ticket was minted for it and none is needed.**

⚠ **The two findings share one row and one cause upstream:** a **pre-basket job** — queued before
WO-911's `paidWood/paidFood/...` basket existed — that can neither name itself nor explain its refund.
Fixing the catalog will not fix the glyph cull and vice versa; they are genuinely separate, which is
why they are separate tickets.

## 6. WHAT NOT TO TOUCH

- ⛔ **`ManageScreenVM.cs:1009-1021`, the catalog-miss branch.** Do not title-case the id, do not hide
  the row, do not soften `Unknown structure`, do not remove the `FlowTrace.Fail`. §12 says instrumentation
  is PERMANENT and this trace is what found the bug.
- ⛔ **`ObsidianQueueHud.FormatJobTarget`** — it still speaks the developer arrow (`Archer -> L3`) for
  its OTHER callers and `ObsidianQueueRegression` pins `"Barracks -> L2"` **verbatim**.
  `ManageScreenVM.cs:1028` normalises the notation it RECEIVES (`" -> L"` -> `" - Level "`), which is
  deliberately a presentation concern on the Manage side. Do not "unify" them.
- ⛔ **`ManageScreenPanel.cs` refund-note block (`:7060-7100`)** — WO-1651's landed fix, awaiting gate.
- ⛔ **`ManageArt.BuildingPortraitKey`'s verbatim-id rule** (`ManageArt.cs:306`, `:328`).
- ⛔ Do not renumber or re-price anything in the queue; `MANAGE_QUEUE_PANEL8_OK` is green on rows
  8.1-8.5 and those all pass from the frame.
