# WO-1356 RESULT — Board: a Submit button, and Fail / Needs Work bounce back to READY with her note

**Verdict:** ALREADY ON HEAD — every acceptance item verified at source 2026-09-09, lane BOARD.
**No code was written by this lane.** The `**Status:**` line was NOT touched (see "Why the status
line was left alone").

**Landing commits (read-only `git log -S`, 2026-09-09):**
- `56fed789c` — *"board: a Submit button, and a bounce that carries her note (WO-1356)"* (first
  introduction of `vsubmit` in `tools/board_build.py` and `def run_bounce` in
  `tools/board_close_pass.py`)
- `759063f3f` — *"board: the headline counts only what is SAVED, and a plain build ingests her
  Submit (WO-1356 follow-up)"* (first introduction of `def auto_submit`)

`git status --porcelain tools/ docs/BOARD.md WorkOrders/WORK_ORDER_1356_*.md` = clean; only the
derived `BOARD.html` is modified in the tree. Nothing in this lane is uncommitted work.

---

## 1. Why the status line was left alone

The lane brief routed this as *"the oldest READY ticket"* and offered the flip
`IMPLEMENTED - <sha> on HEAD (was READY)`. **That premise is stale.** The ticket is already in a
TERMINAL state that is stronger than IMPLEMENTED:

```
**Status:** CLOSED 2026-09-04 - owner felt-test PASS (validated 2026-09-04T13:25:21,
build 2026.09.04.354315). PRIOR STATUS: FIXED 2026-09-03 - ...
```

- The board agrees: `BOARD.html:842` renders the row as `data-bucket="Closed"` with the `Closed`
  badge. It is not in the Ready bucket and never was on this board.
- `docs/READY_RCA_2026-09-09.md` — `grep -n 1356` returns **zero hits**. The RCA ledger does not
  carry this ticket either.

Writing `IMPLEMENTED ... (was READY)` over that line would **downgrade an owner felt-test sign-off**
and re-open a closed ticket. Per CLAUDE.md §11B, the honest act is to report the premise as wrong
rather than execute the instruction that assumed it.

### ⭐ The status line proves the loop closed itself

The line is a byte-for-byte instance of the stamp this very work order shipped.
`tools/board_close_pass.py:127-132` builds:

```python
head = f"CLOSED {today} - owner felt-test PASS"
if prov:  head += " (" + ", ".join(prov) + ")"     # prov = "validated <at>", "build <b>"
if note:  head += f' - "{note}"'
return f"{head}. PRIOR STATUS: {prior}"
```

So WO-1356's own Status line was written **by the close pass it delivered**, off a mark the owner
made in the browser and pushed through the Submit button. The feature closed its own ticket.

**Second field proof it is in live use:** the live record now reads
`VALIDATIONS_OK 369 recorded, 367 validated, preserved across rebuild - proof/owner-validations.json`
(fresh `--check` run, 2026-09-09). At authoring time (§1 of the WO) that same file held **ZERO** and
the close pass reported `BOARD_CLOSE_OK closed 0`. The Submit → ingest → close/bounce loop has been
used in anger for six days.

---

## 2. Acceptance items, each PROVEN at file:line (all paths opened 2026-09-09)

### HALF 1 — the Submit button (§2 of the WO)

| Item | Verdict | Proof |
|---|---|---|
| A `Submit marks to the CLI` button exists in the Owner Validation toolbar | PROVEN | `tools/board_build.py:1073` `<button id="vsubmit" ...>Submit marks to the CLI</button>` |
| It downloads `eoa-validations-<UTC stamp>.json` (stamp in the NAME) | PROVEN | `tools/board_build.py:1273` builds the name from `getUTCFullYear()/Month/Date/…` |
| Success / failure / empty are carried by WORDS, never hue (owner is red/green colourblind) | PROVEN | `:1282` `SUBMITTED …`, `:1288` `NOT SUBMITTED - the browser refused to save the file (…)`, `:1271` `NOTHING TO SUBMIT - no ticket carries a mark yet.` |
| 44px touch floor unconditionally, not only under the phone media query | PROVEN | `:1043` `#vsubmit{…min-height:44px…}` at top level; `:1047` repeats it inside the phone query |
| `--submit` ingests the newest drop file and falls through into the ordinary build | PROVEN | `tools/board_build.py:1572` (the `--submit` branch), `:1590` `VALIDATIONS_SUBMIT_FILE …(saved N min ago)`, `:1582`/`:1593` `VALIDATIONS_SUBMIT_FAIL` |
| Search order `EOA_SUBMIT_DIR` → `<repo>/inbox/` → `~/Downloads` → OneDrive | PROVEN | `submission_candidates()` `tools/board_build.py:1349`, glob `SUBMIT_GLOB` `:1303`, stamp regex `:1330` |
| The Export/Copy escape hatch is kept and unchanged | PROVEN | `#vcopy` still present and still bound (`:1047` styling row, toolbar block `:1073-1076`) |

### HALF 2 — the bounce with her note (§3 of the WO)

| Item | Verdict | Proof |
|---|---|---|
| The bounce lives in `board_close_pass.py`, beside the close | PROVEN | `sanitize_note` `:307`, `bounce_stamp` `:351`, `bounce_pass` `:366`, `run_bounce` `:409` |
| Exactly ONE bouncer; `board_close_validated.apply()` is a thin adapter | PROVEN | `tools/board_close_validated.py:47` `import board_close_pass`, `:155` `board_close_pass.run_bounce(entries=entries)`; its own header `:12-21` states the reason |
| `board_build.py` runs the bounce right after the close, off the same record read | PROVEN | `tools/board_build.py:1662` `board_close_pass.run_bounce(entries=_validations, wo_dir=WO_DIR)` |
| B4 — the existing status body survives verbatim after `PRIOR STATUS:` | PROVEN | `board_close_pass.py:363` `f"{head}. Bounced from Fixed. PRIOR STATUS: {prior}"` |
| B6 — her words are not reworded; only named, mechanical transforms | PROVEN | `sanitize_note` `:307-349`; the `PRIOR STATUS:` forge-guard at `:342-343`; every transform appended to `applied` and echoed |
| Markers `BOARD_BOUNCE_OK / _FAIL / _SKIPPED` | PROVEN | `:440` (OK), `:444` + `:423` (FAIL), `:414` (SKIPPED, on `EOA_BOARD_CLOSE=0`) |

### FOLLOW-UP F1 — "count only what is saved"

| Item | Verdict | Proof |
|---|---|---|
| Headline is server-rendered from the RECORD on disk | PROVEN | `disk_done` accumulated `tools/board_build.py:893` / `:914`; rendered `:1072` `<span id="vprogress">{disk_done} / {len(fixed_rows)} verified</span>` |
| With JS, the headline still counts only the disk map | PROVEN | `vDurableDone(tickets,diskMap)` `:1197`; assigned `:1211` |
| Unsubmitted marks get their own words on a separate line, never inflating the headline | PROVEN | `vPending` `:1199`, wired `:1212-1213`; server-rendered default `:1074` |
| The counting functions are fenced pure so the oracle runs the SHIPPED code under node | PROVEN | `/* [ORACLE:counts] … */` `:1186` → `/* [/ORACLE:counts] */` `:1203` |

### FOLLOW-UP F2 — the auto-ingest on the ORDINARY build

| Item | Verdict | Proof |
|---|---|---|
| A plain `python tools/board_build.py` auto-ingests before the record is read | PROVEN | `auto_submit()` defined `tools/board_build.py:1433`, called `:1616` (before the close at `:1630`+ and the bounce at `:1662`) |
| S1 — newest by FILENAME stamp; mtime only breaks a tie; only the single newest is considered | PROVEN | `_submit_rank` `:1333`, `submission_candidates` `:1349` ("NEWEST FIRST"), `SUBMIT_STAMP` `:1330` |
| S2 — never re-ingest the same bytes (sha256 ledger) | PROVEN | `consumed_path` `:1381`, `_sha256` `:1385`, `_consumed_load` `:1393`, `_consumed_has` `:1409`, `_consumed_remember` `:1413`; `VALIDATIONS_SUBMIT_ALREADY` `:1476` |
| S3 — a malformed drop reports loudly, does NOT block, is NOT consumed | PROVEN | `VALIDATIONS_SUBMIT_UNREADABLE` `:1470` and `:1486` |
| S4 — it always says what it did | PROVEN | `_FILE` `:1480`, `_ALREADY` `:1476`, `_NONE` `:1462`, `_SKIPPED` `:1457`, `_UNREADABLE` `:1470`/`:1486` |
| S5 — opt-OUT only (`EOA_BOARD_SUBMIT=0` / `--no-submit`) | PROVEN | `:1456` reads the env default `"1"`; `:1553-1554` maps `--no-submit` onto it |
| S6 — `--check` IMPLIES the opt-out inside `board_build.py` (structural pin for the gate) | PROVEN | `tools/board_build.py:1553` `if "--no-submit" in sys.argv or check:` → `:1554` sets `EOA_BOARD_SUBMIT=0`. Second lock in the gate: `tools/regression/checkin_gate.ps1:180-184` |

### Canon (CLAUDE.md §15 — updated in the same breath)

| Item | Verdict | Proof |
|---|---|---|
| `docs/BOARD.md` documents the Submit path, the guard table, "count only what is saved", and the bounce rules | PROVEN | `docs/BOARD.md:240` (Submit is the path), `:257-261` (the S1–S5 guard table), `:292-296` (COUNT ONLY WHAT IS SAVED), `:305-310` (only an ingest writes the record), `:375` (the adapter), `:382-411` (§6f, the bounce pass and its B-rules) |
| No second status store — the board stays DERIVED from the `**Status:**` line (CLAUDE.md §2) | PROVEN | The passes REWRITE the `**Status:**` line in the WO file itself (`board_close_pass.write_status` `:135`); `proof/owner-validations.json` holds only the owner's marks, never a status. No status is read from anywhere but the ticket |

---

## 3. Oracle run — `tools/board_validation_roundtrip_test.py` (this session, live tree)

`python tools/board_validation_roundtrip_test.py` → **84 ok, 1 FAIL**, final line
`VALIDATION_ROUNDTRIP_FAIL BOARD_CHECK_OK still printed`.

**Every WO-1356 stage printed `ok`:** 5b (bounce + close in one run, note verbatim, body preserved,
DONE held), 6 (idempotent over 4 runs, `already-ready`), 7 (missing WO named), 8 (corrupt record →
`VALIDATIONS_PARSE_FAIL` + `BOARD_BOUNCE_FAIL`, no status touched), 9b (4 bounce mutations caught),
10/10b (`--submit` end to end; no-file → `VALIDATIONS_SUBMIT_FAIL`), 11/11b (headline under node),
12/12b/12c/12d/12e (ordinary-build ingest, stamp beats mtime, never twice, loud no-op, malformed
reports-and-continues, opt-out incl. `--check`), 12f (4 auto-ingest mutations caught, unmutated
success path asserted FIRST).

**The single FAIL is not this ticket's code.** It is stage 3, one assertion:

```
tools/board_validation_roundtrip_test.py:125
    check("BOARD_CHECK_OK" in log, "BOARD_CHECK_OK still printed")
```

which runs the real entry point against the **live `WorkOrders/`** and therefore inherits whatever
the live tree's lint says. See §4.

Nothing was read from this operator's Downloads: the suite pins
`os.environ["EOA_BOARD_SUBMIT"] = "0"` at start-up (`tools/board_validation_roundtrip_test.py:63`)
and only opts back in against a throwaway `EOA_SUBMIT_DIR` (`:695`, `:793`).

---

## 4. ⚠ `BOARD_CHECK_OK` was NOT PRODUCED — cause named, and it is out of this lane

`EOA_BOARD_SUBMIT=0 EOA_BOARD_CLOSE=0 python tools/board_build.py --check` (fresh, 2026-09-09) ends:

```
BOARD_CHECK_FAIL 1 status contradiction(s)
```

and the naming line is:

```
STATUS_CONTRADICTION 1 finished-verdict status line(s):
    WORK_ORDER_1099_harvest_result_over_cap_reads_zero_and_reassures_falsely.md
      phrase='still open'
      status='IMPLEMENTED (presentation) - awaiting gate (2026-09-09 lane HARVEST-COPY);
              owner question still open'
```

- **It is WO-1099, lane HARVEST-COPY, stamped today.** Not WO-1356, and not any file this lane owns.
- The lane brief forbids touching `WorkOrders/*.md` other than 1356, so the fix is not mine to make:
  lane HARVEST-COPY must reword that status so a finished verdict (`IMPLEMENTED`) does not carry the
  phrase `still open`.
- Same root causes the one oracle FAIL above.

**Stated per CLAUDE.md §11B:** marker absence on a fresh log is a FAILURE, not an unknown.
`BOARD_CHECK_OK` is **NOT PROVEN this session**, the cause is identified and external, and the
check-in gate (`checkin_gate.ps1` stage 1b) will fail on it until WO-1099's status line is reworded.
It does **not** change WO-1356's verdict — no assertion about 1356 failed.

Everything else the run printed was green: `BANNER_OK next mint - CLI: 1619, UI seat: 1100`,
`VALIDATIONS_OK 369 recorded, 367 validated`, `ANCHOR_OK live canon anchor =
CANON_GROUND_TRUTH_2026-09-06.md`. `BOARD_DRIFT 5` is a documented WARNING, not a fail.

---

## 5. Corrections to the lane brief (record, so the next seat is not re-seeded)

1. **`tools/ingest_validation.py` does not exist.** The submit path is `board_build.py --submit`
   (`tools/board_build.py:1572`) plus `auto_submit()` (`:1433`, called from `main()` at `:1616`).
   The status rewriters live in `tools/board_close_pass.py`.
2. **WO-1356 is not READY and is not the oldest READY ticket** — it is CLOSED (§1).
3. `cc1a1328666d1a5691391114ca3dc5163b0801dc` is merely the *last* commit to touch
   `tools/board_build.py` (a WO-1482 docs change). The WO-1356 landings are `56fed789c` and
   `759063f3f` (§ header).

## 6. Not proven from here

- **The `file://` download in real Chrome was not re-driven this session.** The WO records it was
  verified over CDP on 2026-09-03 against `file:///D:/eoa/...` with the produced filename and byte
  count. This lane re-read the button's source but did not re-run a browser. It is unproven-today,
  not disproven; the cheap close is re-running that CDP probe.
- **`node --check` on the shipped `BOARD.html` JS was not re-run** this session (the WO records
  `NODE_CHECK_OK`). Stage 11's node extraction ran green inside the oracle, which exercises the
  `[ORACLE:counts]` block under node but not the whole script.

---

*Result written 2026-09-09 by lane BOARD. No `.cs` touched, no `.unity` touched, no git run, no
`**Status:**` line written by this lane. `BOARD.html` was not regenerated by this lane beyond the
read-only `--check` runs above.*
