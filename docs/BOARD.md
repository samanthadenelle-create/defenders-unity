# BOARD.md — how the work-order board works (WO-1011)

> **Owner ruling, 2026-09-10:** any implemented item whose remaining work is proof
> from a test build moves to **Fixed**. The owner tests Fixed items and their
> findings determine **Closed** or reopening to **Ready**. Record the pending
> proof explicitly; diagnosis and unimplemented acceptance remain Ready. This
> supersedes the older device-install prerequisite below. A build or gate pass
> alone does not supply the owner's sign-off.

**The board is `BOARD.html` at the repo root. It is GENERATED. Never hand-edit it.**

```
python tools/board_build.py          # ~2 s — regenerate the view
python tools/board_build.py --check  # same, but exits 1 if any WO is Unlabeled
python tools/board_build.py --ingest -   # fold the owner's pasted validations into the record (§6d)
python tools/board_validation_roundtrip_test.py   # prove a rebuild cannot lose a sign-off
```

---

## 1. The model — the repo IS the board

`WorkOrders/*.md` **`**Status:**` lines**, `*.RESULT.md` markers, and the
`CLI_LANES_WO_NUMBERS.md` banner **are the data**. `BOARD.html` is a two-second derived **view** of
them.

There is nothing to sync, mirror, or update in a second system. The consequence is the whole point:

> **The board is exactly as truthful as the status lines in the WO files.**
> A finished work order whose file still says `READY` is a lie the board will faithfully render.

So status hygiene is not paperwork — **it IS the board**. This is also why the board cannot drift the
way the retired Notion mirror did: there is no second copy to fall behind.

---

## 2. The protocol

1. **Regenerate at session boot, and before any board read.** Never read a stale `BOARD.html`;
   never hand-edit it (it is generated output — your edit is destroyed on the next run).
2. **Flip the status line IN THE SAME COMMIT as the work.** This is the §15 canon rule extended to
   statuses: finishing an implementation means flipping `**Status:**` *and* writing the `.RESULT.md`
   in that same commit. A status flip deferred to "later" is a board that lies until later.
3. **Never mirror to Notion — no writes, and no reads**, regardless of what older docs say.
   `NOTION_SOURCE_OF_TRUTH.md` is superseded. Any doc still pointing at Notion as the board gets a
   `STALE:` flag when touched (banner only on frozen dated ledgers, §15).
4. **The status vocabulary below is canon.** It is read from the **first** `**Status:**` line in the
   file, by keyword priority.

---

## 3. Status vocabulary (keyword priority, first match wins)

### 3a. Scope — which files owe a status at all (WO-937)

`WorkOrders/` holds **two kinds of file**, and only one of them is in the status workflow. The
parser decides by **filename prefix — the document's KIND, not whether it has a number:**

| File | Kind | Bucket |
|---|---|---|
| `WORK_ORDER_*.md` — **numbered `WORK_ORDER_<n>_*.md` or legacy unnumbered `WORK_ORDER_<slug>.md`** | a work order | bucketed by the status table in §3b |
| anything else — `README.md`, `AUDIT_*`, `BRIEF_*`, `HANDOFF_*`, `NOTES_*`, `QA_CHECKLIST_*`, `DESIGN_*`, `RESEARCH_BRIEF_*`, `REVIEW_*`, `WO541_MODEL_API.md`, … | a **companion doc** | **Doc** |

**`Doc` is a NON-DEFECT bucket and `--check` ignores it entirely.** These files are references, not
units of work — demanding a `**Status:**` line from a `README.md` or an audit brief would be absurd.
They are still **rendered, linked, and searchable by filename**, because this board is the only index
of `WorkOrders/` anyone actually opens; dropping them would trade a miscount for a discoverability
hole. Filter them out with the `Doc` chip when you want a pure work view.

> ⚠ **Unnumbered `WORK_ORDER_<slug>.md` files are REAL work orders and still owe a status.** They
> render as `WO-?`. Scoping on "has a number" instead of "is a work order" would silently launder
> their missing statuses into the non-defect bucket — do not do that.

### 3b. Status buckets (work orders only)

| Keyword in the `**Status:**` line | Bucket |
|---|---|
| `SUPERSEDED` / `CLOSED` / `CANCELLED` | **Closed** |
| leading `AWAITING OWNER MATCH` — **beats `.RESULT.md` and any later keyword** | **Verify** |
| leading `FIXED` | **Fixed** |
| `DONE` / `IMPLEMENTED` / `COMPLETE` — **or a `.RESULT.md` exists** | **Done** |
| `BLOCKED` | **Blocked** |
| `READY` (any phrasing containing it) | **Ready** |
| `IN PROGRESS` (substring fallback only - never a leading verdict) | **Ready** |
| `DRAFT` / `SPEC` / `NOT STARTED` / `PROPOSAL` | **Spec** |
| anything else | **Unlabeled** |

An assignable Ready row whose status says `PARTIAL`, or says that a named `SLICE ... LANDED`,
stays in **Ready** and also renders a visible **PARTIAL** sub-badge. The sub-badge is presentation,
not a fourth bucket: it warns that work already landed while preserving the open residual as
assignable. Keep the landed and residual scope explicit in the status line; the badge does not infer
either scope from source files.

An assignable Ready row whose implementation exists only in another development lane uses
`OFFTREE RETURNED lane=<branch>` or `OFFTREE AWAITING-REVIEW lane=<branch>` in its status line. It
stays in **Ready** and renders a visually distinct **OFF-TREE · state · branch** sub-badge. `RETURNED`
means the lane owns requested revisions; `AWAITING-REVIEW` means the lead owes review. The required
lane value makes the work findable, and the explicit grammar prevents prose that merely names the
PARTIAL or OFFTREE sub-badge from asserting either state. This is presentation, not a new bucket.

**`Unlabeled` is a DEFECT in the WO file, not a category.** It means the status line carries no
canonical keyword, so the row cannot be bucketed and silently drops out of every real query.
Since the §3a scope fix it is a *pure* defect count — a companion doc can never land there.

Compound statuses are read left-to-right by that priority, which trips people up:

- ❌ `DELIVERED — defect pass open` → contains neither `DONE` nor `READY` → **Unlabeled**
- ❌ `IN PROGRESS — defect pass open, DELIVERED core` → still **Unlabeled**
- ✅ `READY TO IMPLEMENT (defect pass)` → **Ready**
- ✅ `DONE (pending felt-verify)` → **Done**

Write the truth *and* include one canonical keyword. The nuance belongs after it, not instead of it.

> ⚠ **A `.RESULT.md` forces the Done bucket** regardless of the status line. If you file a RESULT for
> partially-complete work, say so loudly in the status line **and** the RESULT body — the board will
> call it Done either way, so the file has to carry the caveat the bucket cannot.
>
> **The two exceptions, and both are leading-word tests above the `has_result` line:** a status
> leading `FIXED` stays **Fixed**, and one leading `AWAITING OWNER MATCH` stays **Verify**.

**`Verify` = built, and the OWNER has not yet judged it against the picture** (owner ruling
2026-09-07; `WorkOrders/ManageRedesign/OWNER_RULINGS_LOCKED.md` ruling 29). It exists because
commit `949e848a0` declared nine Manage screens matched the owner's mockup on the strength of
headless frames a seat read itself, and twelve tickets sat in Done and Fixed until she walked the
build and matched none of them. `Verify` is deliberately **ineligible for `board_close_pass.py`**,
which closes only `Fixed`: the debt here is a pixel comparison she has not made, not a felt test.
A `Verify` ticket moves on only when the owner says the frame matches its mockup panel.

---

## 4. Adding a new status keyword

Edit **both** in the **same commit**, or the doc and the parser start disagreeing:

1. `bucket_of()` in `tools/board_build.py`
2. the table in §3b above

Same rule for a change of **scope** (what counts as a work order): `is_work_order()` in
`tools/board_build.py` and the table in §3a move together.

A keyword the parser knows but this doc does not is invisible to every human; a keyword this doc
promises but the parser does not know silently produces `Unlabeled` rows.

---

## 5. `--check` and the check-in gate

`python tools/board_build.py --check` regenerates as normal, then **exits 1** if any WO is
`Unlabeled`, printing the offending numbers and files (capped at 40, with a remainder count) so the
output is a to-do list rather than a bare number.

It fails on **genuine defects only** — real work orders with no canonical keyword. `Doc` rows (§3a)
are out of scope and never counted, which is what makes the number honest enough to gate on.

A plain run is **report-only** and always exits 0 — adding the flag to a gate is a deliberate act, and
no one's build should start failing because a WO file is sloppy.

**Wired into the check-in gate (WO-937 C, 2026-08-16):** `tools/regression/checkin_gate.ps1` runs
`board_build.py --check` as stage 1b (right after the static gate, ~1 s, no Unity). Unlabeled hit 0
first, so the gate enforces the vocabulary without failing anyone retroactively. A board-check FAIL
fails the gate summary but does not short-circuit the code stages — it is a docs defect, not a
compile one.

**Recursive missing-status sweep (WO-1492, 2026-09-06):** every run also sweeps
`WorkOrders/**` at ANY depth for work-order files (`WORK_ORDER_*.md` **or** `WO-<n>_*.md`, never a
RESULT) that carry no `**Status:**` line at all, prints `MISSING_STATUS_LINE <n>` naming each, and
folds the count into `BOARD_CHECK_FAIL`. The flat `WorkOrders/*.md` glob plus the `WORK_ORDER_`
prefix rule missed the seventeen-ticket `WorkOrders/ManageRedesign/` program twice over - wrong
directory AND wrong filename shape - so the largest lane in flight rendered as nothing and no
marker said so. The sweep only asks whether a status line is PRESENT; bucketing stays with
`classify_status`, so it can never re-classify an existing row.

Both runs also print a report-only `DUPLICATE_WO_NUMBERS` block — WO numbers claimed by more than one
file (56 known legacy collisions). Duplicates are **flagged, never silently renumbered** (a collision
is its own finding; resolve first-on-disk-and-referenced-wins). They do not affect the exit code.

### Created date + "opened within" (WO-940)

Every row's date column is the ticket's **CREATED date, never last-modified** — an edit must not
reset a ticket's apparent age (`SUNDAY_HOUSEKEEPING.md` §4 makes age the primary validity evidence).
Resolution order: `**Minted:** YYYY-MM-DD` from the WO body → git first-add date (one repo-wide git
call) → mtime as a last resort, visibly marked `~` as an estimate. The cell renders
`YYYY-MM-DD · <age>d`; rows older than 7 days carry a literal `7d+` badge (word + colour, never
colour alone — the owner is red/green colourblind). "opened within" buttons (7d / 30d / 90d / all)
compose with the bucket chips and the search box. Age is **derived at generation time, never typed
into a WO file**.

---

## 6. Priority when tickets conflict (owner 2026-08-15)

Two rules keep a 900+ Ready column from thrashing the team:

### 6a. Recency window — last 50–100 WOs win on contradiction

The **last ~50–100 work orders by WO number** (both mint blocks: main line *and* UI seat)
are weighted as **valid** when they **contradict** older tickets.

- **Floor (refresh when minting):** as of 2026-08-15, last 100 ≈ **WO-915+**, last 50 ≈ **WO-965+**
  (max on disk was **1020**). Recompute as `max(WO#) − 99` / `max − 49` when the banner moves.
- **On conflict** (same system, opposite acceptance criteria, or a Done RESULT that kills an older
  Ready goal): **implement / believe the newer WO.** Close or partial-scope the older one
  (`CLOSED — SUPERSEDED by WO-NNN` or `READY — PARTIAL: <remainder that does not fight NNN>`).
- **Not a free close-all:** older tickets that are **orthogonal** (no contradiction) still need the
  age/validity check (§6b). Recency only breaks ties.

Live priority stack + known supersession pairs: **`BOARD_NOW.md`** (repo root).

### 6b. Age — >14 days verify first (outside the recency window)

Any open ticket whose file has been idle **>14 days** and sits **below** the last-100 floor is
**VERIFY FIRST** before implementation: still broken at HEAD, partial, done+RESULT, or closed.
Do not treat a multi-month Ready status as a work order.

### 6c. Working stack

`BOARD_NOW.md` is the human-ordered pull list (P0–P3). `BOARD.html` is the full inventory.
When they disagree on *what to do next*, **`BOARD_NOW.md` wins** until regenerated after a
priority session; when they disagree on *status*, the WO file’s `**Status:**` line wins
(regenerate the HTML).

---

## 6d. Owner validations — the DURABLE record (2026-09-03)

The board's **Owner Validation** section is where the PO felt-tests a Fixed ticket and signs it
off. Her sign-off is the only thing that closes a ticket (CLAUDE.md §13), so it is DATA, not
view state.

**The record: `proof/owner-validations.json`** — committed, human-readable, one ticket per line,
keys sorted (so two seats validating different tickets merge without a human, and a same-ticket
conflict is a real disagreement that should stop and be read). Keyed by work-order **filename**,
because this repo has duplicate WO numbers and the friendly label would make two unrelated files
share one sign-off. Full reasoning lives in the header of `tools/owner_validations.py`.

**⛔ IT IS NOT BUILD-SCOPED, and never becomes so again.** The old code kept sign-offs in browser
`localStorage` under `eoa-owner-validation:<apk build>:<commit sha>`, so **every commit minted a
new key and orphaned every mark she had made** — with the CLI committing hourly, the one person
whose sign-off closes a ticket was losing her work hourly, and the CLI could not see any of it
(hence `tools/board_close_validated.py`, which scraped Chrome's LevelDB out of her user profile).
A sign-off is a judgement about a **fix** ("the wolf routes correctly now"), not about a binary;
it does not stop being true because a doc got committed. Provenance is kept *inside* each entry
(`at` + `build`), so "was this signed off before the current APK?" stays answerable per ticket
instead of being force-answered "all of it is stale" once an hour.

**How a mark gets from her phone into the record** (a browser cannot write to the repo).
**The SUBMIT button is the path** (WO-1356, owner ruling *"add a submit button so you run a script
to close the ones passed"*):

```
BOARD.html > Owner Validation > [ Submit marks to the CLI ]      # one tap; saves a file
python tools/board_build.py             # ANY ordinary build takes the newest eoa-validations-*.json,
                                        # ingests it, closes the Passes and bounces the rest
```

**The ingest is part of the ORDINARY build** (owner ruling 2026-09-03: *"i would expect you to do
this everytime you build the board. CAn you add it to the rebuild script"*). Same reasoning that put
the close pass inside the build: **a flag the CLI has to remember is the same failure as a second
command it has to remember.** `--submit` still exists as the EXPLICIT form (it fails loudly when
there is no file, because a seat that typed it asked a question); the default path is never fatal.

| Guard | Behaviour |
|---|---|
| Never twice | Each consumed drop file is recorded by **sha256** in `<record>.consumed.json`; a second build prints `VALIDATIONS_SUBMIT_ALREADY` and does not re-ingest |
| Never a stale resurrection | **Only the single newest candidate is ever considered**, ranked by the **UTC stamp in the filename** first (mtime only breaks a tie, then the name). If that one is already consumed the pass STOPS - it never falls back to an older file still sitting in Downloads |
| Malformed drop file | **Reports and continues**: `VALIDATIONS_SUBMIT_UNREADABLE` / `VALIDATIONS_INGEST_FAIL`, the board still renders, and the file is **not** marked consumed - so it complains again on every build until it is dealt with. Never silently skipped |
| Says what it did, always | `VALIDATIONS_SUBMIT_FILE <path> (saved N min ago)` on ingest, `VALIDATIONS_SUBMIT_ALREADY`, `VALIDATIONS_SUBMIT_NONE` when there is nothing, `VALIDATIONS_SUBMIT_SKIPPED` when opted out |
| Opt-OUT only | `EOA_BOARD_SUBMIT=0`, `--no-submit`, and **`--check` implies it** - so `tools/regression/checkin_gate.ps1` stage 1b can never ingest from a developer's `~/Downloads` as a side effect of a check-in. An opt-IN would rebuild the hole |

⛔ **The constraint that shapes this: she opens the board over `file://` and there is no server.**
A `file://` page cannot write into the repo and has nothing to POST to. What it *can* do is hand
the browser a file to save — so Submit downloads `eoa-validations-<UTC stamp>.json`, and `--submit`
picks up the newest one from `EOA_SUBMIT_DIR`, then `<repo>/inbox/`, then `~/Downloads` and
`~/OneDrive/Downloads`. **Verified, not assumed:** driven through real Chrome over CDP against a
real `file:///D:/eoa/...` page, the anchor download reported `Browser.downloadProgress
state=completed` and a 397-byte payload landed on disk (an `http://` control behaved identically,
so nothing about `file://` refuses it).

The button **says what it did** — the count, the exact filename, the command the CLI runs next, and
what to do if no file appeared. Success and failure read as the WORDS `SUBMITTED` / `NOT SUBMITTED`
/ `NOTHING TO SUBMIT`, never as a colour (the owner is red/green colourblind). The browser gives no
completion callback for a download, so the message never claims more than it knows.

**The Export/Copy path is KEPT as the fallback**, unchanged, for the case where the browser refuses
the save:

```
BOARD.html > Owner Validation > "Export for the CLI" > Copy   (or "Save as file")
python tools/board_build.py --ingest -        # paste it; or --ingest <file>
python tools/board_build.py                   # rebuild; the marks now render from disk
```

Tradeoff, stated plainly: **the marks still travel as a file, not over a wire.** It needs no server,
no auth and no network, it works over `file://` or any static host, and it cannot lose a mark —
which a write endpoint reachable from a phone browser could not match without new infrastructure to
secure. Marks that have not reached the record live only in that browser, and the page says so in
those words: *"A mark you make here is NOT saved yet."*

⭐ **COUNT ONLY WHAT IS SAVED** (owner ruling 2026-09-03). The `N / M verified` headline is derived
from **the record on disk** - the same bytes the close and bounce passes read - and is
**server-rendered**, so it is right with JavaScript off. Marks still sitting in the browser get
their **own line underneath**, in words: *"43 marks on this device are waiting to be submitted.
Nothing is lost - they are safe in this browser. Tap Submit..."*, and *"Nothing waiting - every mark
on this device is already in the saved record."* when there are none. The per-row `[X] VALIDATED`
badge is unchanged and still shows local marks, and so does the **Needs Felt-Test** filter (a
to-do filter: a ticket she just marked is one she has tested, so it hides on the spot).
*Why this is written this hard:* the headline used to be computed from `localStorage`, so the board
read **43 / 78 verified** while `proof/owner-validations.json` held **zero** and the pass reported
`BOARD_CLOSE_OK closed 0`. She reasonably expected 43 tickets to have moved. A headline may never
overstate what is durable.

- The record is written **only** by an ingest - `--ingest`, `--submit`, or the ordinary build's
  auto-ingest, all through the same `_ingest_path()`. Nothing else in a rebuild can write it, and
  no rebuild can ever *lose* a mark.
  *(This bullet used to read "a rebuild has no write path to it at all". That stopped being true on
  2026-09-03 when the auto-ingest landed - a rebuild now writes the record when, and only when, it
  takes in a drop file she submitted.)*
- Newest `at` wins on merge, so a stale paste from a second device cannot overwrite a newer mark.
- An **unreadable** record ABORTS the rebuild (`VALIDATIONS_PARSE_FAIL`) rather than rendering
  "0 verified" over corrupt bytes — which would look normal and invite her to redo signed-off work.
- Marker: every run prints `VALIDATIONS_OK <n> recorded, <m> validated, preserved across rebuild`.
  Judge it by marker presence on a fresh log, never the exit code (CLAUDE.md §8/§16).
- Self-check: `python tools/board_validation_roundtrip_test.py` → `VALIDATION_ROUNDTRIP_OK`. It
  proves a rebuild preserves a sign-off **and** proves its own assertions have teeth by stubbing
  the read path back to the old always-empty behaviour and requiring the failure to be caught.
- A validated row is distinguishable **without colour** (the owner is red/green colourblind): the
  word `[X] VALIDATED`, the button label flipping to `Validated`, and the row sinking to the
  bottom of its group. All three are server-rendered, so they show with JavaScript off.
- One-time in-page migration sweeps any leftover `eoa-owner-validation:*` key into the durable
  overlay and reports what it recovered. Best effort by nature — it can only reach keys in the
  same browser and origin, and it never deletes the old keys.
- Marking in the browser still changes **no** `**Status:**` line. The board build applies it — see
  §6e.

## 6e. The CLOSE pass — Pass + Validated becomes CLOSED, on the next board build (WO-1355)

Owner ruling 2026-09-03: *"i test and sign off in the owner validation section when you do board
next you flip all passed and validated to closed"* — and, defining the state she signs off FROM:
*"once you move to device for testing gets moved to fixed"*.

```
new issue -> ticket -> assign an SME -> check in when complete
          -> ON HER DEVICE = FIXED            (not "code complete", not "committed")
          -> she signs off in Owner Validation (Passed + Validated)
          -> the NEXT board build flips those to CLOSED
```

**It runs inside `python tools/board_build.py` itself** (`tools/board_close_pass.py`, called before
the work orders are parsed, so the page that run writes already shows the closes). It is not a
second command, because it kept being forgotten and she had to ask twice — CLAUDE.md §16 settles
that shape: *a gate whose remedy is "a human remembers a second command" is not a gate*.

The pass REWRITES `**Status:**` lines from a data file, so the rules are the design:

1. **Both signals or nothing** — `verdict == "Pass"` **and** `validated == true`. Fail, Needs Work,
   a blank verdict, a Pass never validated, and a validated entry with no verdict all close
   nothing and are counted as *held*. An **unrecognised** verdict cannot even reach the pass:
   `owner_validations.normalize()` coerces anything it does not know to `""`.
2. **Only a FIXED ticket is eligible** — Fixed means "it reached her device", the only state a
   felt-test sign-off can validly follow. READY / SPEC / BLOCKED / DONE are never closed by a
   stale mark. Eligibility is read through `board_build.classify_status`, the board's own status
   vocabulary, so the closer and the Fixed bucket can never disagree.
3. **Never resurrect, never re-stamp** — an already-CLOSED ticket is left alone. Ten runs produce
   the same bytes as one.
4. **The existing status text survives verbatim** — the stamp is prepended and the old line is
   carried after `PRIOR STATUS:` (already this repo's convention for historical status prose, and
   the marker `status_contradiction` splits on, so preserving the body cannot manufacture a false
   defect). Those FIXED lines hold the real engineering record; erasing them would destroy exactly
   what the board exists to keep.
5. **Auditable** — the stamp carries the entry's `at` and `build`, so which sign-off on which build
   closed a ticket is readable off the status line.
6. **A malformed record ABORTS the pass** — `VALIDATIONS_PARSE_FAIL` + `BOARD_CLOSE_FAIL`, nothing
   written. Never a partial close, never a silent zero reported as success.
7. **A validation naming a missing WO file is reported**, never dropped.

- Marker: `BOARD_CLOSE_OK closed <n>, held <n>, already-closed <n>, missing <n>` (or
  `BOARD_CLOSE_FAIL ...`). Judge it on a fresh log, never by the exit code.
- Opt-out for tests and emergencies only: `--no-close`, or `EOA_BOARD_CLOSE=0`, which prints
  `BOARD_CLOSE_SKIPPED`. It is deliberately an opt-OUT; an opt-IN would restore the forgotten-
  second-command hole.
- `tools/board_close_validated.py` keeps only the legacy Chrome-LevelDB salvage; its `apply()` is
  now a thin adapter onto `board_close_pass.bounce_pass`. Both status rewriters live in one module —
  a drifted copy of a status rewriter rewrites live tickets.
- Covered by `tools/board_validation_roundtrip_test.py` stages 5-9 against a throwaway
  `WorkOrders/` (`EOA_WO_DIR`): both-signals, idempotency across three runs, a CLOSED ticket
  untouched, a non-FIXED ticket never closed, the body preserved, abort on a corrupt record — and
  four source mutations that each must be caught.

## 6f. The BOUNCE pass — Fail / Needs Work go back to READY, with her note (WO-1356)

Owner ruling 2026-09-03: *"move the needs work and failed back to ready with a note"*.

The other half of the same sign-off, run by the same `python tools/board_build.py` immediately
after the close, off the same read of the record (`tools/board_close_pass.py`, `run_bounce`). Her
note is the most valuable artefact in the whole loop — it is *why* the ticket failed, in her
words — so it lands **in the ticket**, not in a screenshot someone has to go find.

Resulting status line, the live acceptance case (WO-1184, Validated **and** "Needs Work"):

```
**Status:** READY TO IMPLEMENT - owner felt-test 2026-09-03 Needs Work
 (marked 2026-09-03T22:08:02, build 2026.09.03.354093) - "right now its a red d".
 Bounced from Fixed. PRIOR STATUS: FIXED 2026-08-27 — implemented; awaiting owner felt-verify to CLOSE.
```

1. **Verdict alone bounces** — `validated` is *not* required, deliberately unlike the close. A close
   is terminal so it demands two signals; a bounce is the recoverable direction and is fully
   reversible from the `PRIOR STATUS:` text it preserves. Requiring the extra tap would leave a
   ticket she marked Fail sitting silently in Fixed forever.
2. **Only a FIXED ticket bounces**, read through the same `board_build.classify_status` as the
   close, so the two passes can never disagree about what "Fixed" means. A DONE/CLOSED/SPEC row is
   held and reported.
3. **Idempotent.** A bounce writes a status leading with READY, which the next run classifies Ready
   and skips (`already-ready <n>`). Three runs = one run's bytes; no stacked notes, no nested
   `PRIOR STATUS:` chains. Consequence stated plainly: editing a note *after* a bounce does not
   re-stamp the ticket — the ticket is already back in the queue, so a person edits it.
4. **The existing status body survives verbatim** after `PRIOR STATUS:`, same as the close.
5. **An empty note is legitimate.** She may mark Needs Work having typed nothing; it bounces anyway
   and the stamp simply carries no quote. A reason is never invented.
6. **Her words are not reworded.** `sanitize_note()` only makes a note safe for a single-line ASCII
   status field, and every transformation is printed on the log: smart punctuation folded to ASCII,
   any remaining non-ASCII/control character replaced with a space, line breaks flattened, a literal
   `PRIOR STATUS:` inside the note written `PRIOR STATUS -` so the marker cannot be forged, and a
   160-character truncation. No rewording, re-ordering, capitalising or summarising.
7. **A corrupt record aborts** and a mark naming a missing WO file is reported — same as the close.

- Marker: `BOARD_BOUNCE_OK bounced <n>, already-ready <n>, held <n>, missing <n>` (or
  `BOARD_BOUNCE_FAIL ...`). `EOA_BOARD_CLOSE=0` / `--no-close` skips it too
  (`BOARD_BOUNCE_SKIPPED`).
- Covered by the round-trip test stages 5b, 6, 7, 8, 9b and 10 — including four bounce-source
  mutations that each must be caught, and the live acceptance case: **WO-1184 bounces and must not
  close, while the seven Pass+Validated rows close.**

## 7. What this board is NOT

- Not a service, not CI, not a database. It is one Python file and one HTML output.
- Not a place to record work: the WO markdown is. The board only *shows* what the markdown says.
- Not a Notion replacement to be re-mirrored anywhere. The retired mirror's failure mode — a second
  copy nobody could reach — is exactly what "derive it in 2 seconds" prevents.

## 8. Related

- `BOARD_NOW.md` — prioritized pull list + recency supersession table
- `CLI_LANES_WO_NUMBERS.md` — the **sole** numbering authority (bump the banner in the same edit as a mint)
- `WorkOrders/` — the data
- `tools/board_build.py` — the generator (and `--ingest`, the record's only writer)
- `proof/owner-validations.json` — the owner's durable felt-test sign-offs (§6d)
- `tools/board_close_pass.py` — the Pass+Validated -> CLOSED pass the board build runs (§6e)
- `tools/owner_validations.py` — the record's shape, merge rule and reasoning
- `tools/board_validation_roundtrip_test.py` — proves a rebuild cannot lose a sign-off
- `docs/HANDOVER.md`, `SESSION_CANON_LOADER.md` — session boot, which instructs the regen
