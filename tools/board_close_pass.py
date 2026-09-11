#!/usr/bin/env python3
"""board_close_pass.py - flip owner Pass-validated FIXED tickets to CLOSED.

THE OWNER'S RULING (2026-09-03)
    "i test and sign off in the owner validation section when you do board next you
     flip all passed and validated to closed"

    and, defining the state she signs off FROM:

    "once you move to device for testing gets moved to fixed"

    So the lifecycle is:

        new issue -> ticket -> assign an SME -> check in when complete
                  -> ON HER DEVICE          = FIXED   (not "code complete", not "committed")
                  -> she signs off in Owner Validation (Passed + Validated)
                  -> the NEXT board build flips those to CLOSED

WHY THIS LIVES INSIDE THE BOARD BUILD
    It used to be a second command (tools/board_close_validated.py) that a seat had to
    remember to run, and the seat kept forgetting - the owner had to ask twice. CLAUDE.md
    16 states the rule that settles it: *a gate whose remedy is "a human remembers a second
    command" is not a gate*. So tools/board_build.py now runs this pass itself, before it
    parses the work orders, and the page it writes already shows the closes. There is
    exactly ONE implementation of the close and both entry points call it.

⛔ THIS MODULE REWRITES **Status:** LINES FROM A DATA FILE. The safety rules are the design:

  1. BOTH SIGNALS, or nothing.  verdict == "Pass" AND validated == True. A Fail, a
     "Needs Work", a blank/unrecognised verdict, a Pass that was never validated, or a
     validated entry with no verdict all CLOSE NOTHING. (owner_validations.normalize()
     has already coerced any unknown verdict string to "", so an unrecognised verdict
     arrives here as blank and is held, never guessed at.)
  2. ONLY A FIXED TICKET IS ELIGIBLE. Fixed now means "it reached her device", and a
     felt-test sign-off can only validly follow that state. A READY / SPEC / BLOCKED /
     DONE ticket is NEVER closed by a stale mark - the mark is reported and held.
     Eligibility is read through board_build.classify_status, the board's own status
     vocabulary, so the closer and the Fixed bucket can never disagree about what
     "Fixed" means.
  3. NEVER RESURRECT, NEVER DOWNGRADE. An already-CLOSED ticket is not rewritten - not
     re-stamped, not re-dated. Ten runs produce the same bytes as one.
  4. THE EXISTING STATUS TEXT SURVIVES VERBATIM. Those FIXED lines carry the real
     engineering record - what shipped, what is HELD awaiting a retag, findings
     deliberately not fixed. The stamp is PREPENDED and the old line is carried after
     "PRIOR STATUS:", which is already this repo's convention for historical status prose
     (board_build.status_contradiction splits on exactly that marker, so preserving the
     body cannot manufacture a false "contradiction" defect).
  5. AUDITABLE. The stamp records the "at" and "build" from the validation entry, so
     which sign-off on which build closed the ticket is readable off the status line.
  6. A MALFORMED / UNREADABLE RECORD ABORTS THE PASS. It never closes a partial set and
     never reports success while silently closing nothing - same discipline as the board
     build's own VALIDATIONS_PARSE_FAIL abort.
  7. A VALIDATION NAMING A MISSING WO FILE IS REPORTED, never dropped. A renamed or moved
     work order must not silently swallow a sign-off.

MARKER
    BOARD_CLOSE_OK   closed <n>, held <n>, already-closed <n>, missing <n>
    BOARD_CLOSE_FAIL <why>
    Judge it by the marker on a fresh log, never by the exit code (CLAUDE.md 8/16).
"""
from __future__ import annotations

import datetime
import glob
import os
import re
import sys
import hashlib
import json

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import owner_validations

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
# Overridable so the round-trip self-check can exercise the REAL close path against a
# throwaway WorkOrders/ instead of the live tickets. A test must never be able to rewrite
# the status lines it exists to protect.
WO_DIR = os.environ.get("EOA_WO_DIR") or os.path.join(ROOT, "WorkOrders")

_STATUS_LINE = re.compile(r"(?m)^(\*\*Status:\*\*[ \t]*)(.+)$")

# The escape hatch is deliberately INVERTED: the close runs by default and a caller must
# opt OUT. An opt-IN flag would restore the exact hole this module closes.
# The OWNER'S QUEUE - the two buckets a sign-off may act on. Fixed = on her device, owed the
# FELT test. Verify = built, owed her LOOK at a device frame beside its mockup panel (ruling 29,
# 2026-09-07). Both are closed by HER Pass and bounced by HER Fail / Needs Work, and by nothing
# else. Until 2026-09-07 the panel listed only Fixed rows and this pass accepted only Fixed, so
# the fifteen Manage screens she walked on the device had no checkbox and no way to close -
# she said so in chat ("the 15 verify ... THose I verified"). READY / SPEC / BLOCKED / DONE stay
# ineligible: a stale mark can never close what never reached her.
OWNER_JUDGED = ("Fixed", "Verify")

def close_disabled() -> bool:
    return (os.environ.get("EOA_BOARD_CLOSE") or "").strip() in ("0", "false", "no", "off")


def read_status(path: str) -> str:
    try:
        text = open(path, encoding="utf-8", errors="replace").read()
    except OSError:
        return ""
    m = _STATUS_LINE.search(text)
    return m.group(2).strip() if m else ""


def _bucket(status: str) -> str:
    """The board's own vocabulary, imported lazily.

    Lazy because tools/board_build.py imports THIS module at its top; importing it back
    at module scope would be a cycle. By the time a close actually runs, board_build is
    fully loaded. Sharing the function is the point: if the board calls a row Fixed, the
    closer must consider that same row eligible, and vice versa - two copies of the
    keyword rules would drift, and a drifted copy here rewrites files.
    """
    import board_build
    return board_build.classify_status(status, has_result=False, is_wo=True)[0]


def stamp_for(state: dict, today: str, prior: str) -> str:
    """The replacement status line. PREPENDS a close stamp; never replaces the body."""
    prov = []
    if state.get("at"):
        prov.append(f"validated {state['at']}")
    if state.get("build"):
        prov.append(f"build {state['build']}")
    note = (state.get("note") or "").replace("\n", " ").strip()
    if len(note) > 160:
        note = note[:157] + "..."
    head = f"CLOSED {today} - owner felt-test PASS"
    if prov:
        head += " (" + ", ".join(prov) + ")"
    if note:
        head += f' - "{note}"'
    return f"{head}. PRIOR STATUS: {prior}"


def write_status(path: str, new_status: str) -> bool:
    text = open(path, encoding="utf-8", errors="replace").read()
    # A FUNCTION replacement, not a template: a status body can legitimately contain a
    # backslash or a `\1`, and a string replacement would try to expand it.
    new_text, n = _STATUS_LINE.subn(lambda m: m.group(1) + new_status, text, count=1)
    if n != 1:
        return False
    # No BOM, LF endings - other tools read these files.
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write(new_text)
    return True


def resolve_ticket(wo_dir: str, name: str) -> str:
    """Resolve a validation's filename to the ticket on disk.

    The record is keyed by BASENAME (owner_validations.py), and the board discovers tickets
    RECURSIVELY (board_build.py: `**/*.md`, added for WorkOrders/ManageRedesign/WO-2001_*.md).
    Until 2026-09-07 this pass joined wo_dir + name flat, so the owner's Pass on WO-2003 /
    2005 / 2011 / 2017 was reported MISSING and BOARD_CLOSE_FAIL'd while the files sat one
    folder down. Flat first (the common case, and what the tests fixture); then exactly one
    recursive lookup by basename. Returns "" when nothing matches.
    """
    flat = os.path.join(wo_dir, name)
    if os.path.isfile(flat):
        return flat
    hits = sorted(glob.glob(os.path.join(wo_dir, "**", name), recursive=True))
    return hits[0] if hits else ""


def close_pass(entries=None, wo_dir=None, today=None):
    """Apply the close pass. Returns a result dict; raises nothing on ordinary data.

    entries: {filename: state}. None means "read the durable record" - and an unreadable
             record propagates ValidationsUnreadable to the caller, which must ABORT.
    """
    wo_dir = wo_dir or WO_DIR
    today = today or datetime.date.today().isoformat()
    if entries is None:
        entries = owner_validations.entries()   # may raise ValidationsUnreadable - by design

    res = {"closed": [], "held": [], "already": [], "missing": [], "unwritable": []}
    for name in sorted(entries):
        state = entries.get(name) or {}
        if not isinstance(state, dict):
            res["held"].append((name, "entry is not an object"))
            continue
        verdict = state.get("verdict") or ""
        validated = bool(state.get("validated"))
        # ── RULE 1: both signals, or nothing ────────────────────────────────────
        if verdict != "Pass" or not validated:
            res["held"].append(
                (name, f"verdict={verdict or 'blank'} validated={'yes' if validated else 'no'}"))
            continue
        path = resolve_ticket(wo_dir, name)
        # ── RULE 7: a mark naming a missing file is REPORTED ─────────────────────
        if not path:
            res["missing"].append(name)
            continue
        prior = read_status(path)
        bucket = _bucket(prior)
        # ── RULE 3: never resurrect, never re-stamp ──────────────────────────────
        if bucket == "Closed":
            res["already"].append(name)
            continue
        # ── RULE 2: only a ticket in the OWNER'S QUEUE (Fixed / Verify) is eligible ──
        if bucket not in OWNER_JUDGED:
            res["held"].append((name, f"not Fixed/Verify (bucket={bucket}) - a sign-off cannot "
                                      f"close a ticket that never reached the device"))
            continue
        if write_status(path, stamp_for(state, today, prior)):
            res["closed"].append(name)
        else:
            res["unwritable"].append(name)
    return res


def run(entries=None, wo_dir=None, today=None, echo=print):
    """Print the pass in the house style and return (ok, result). Never raises."""
    if close_disabled():
        echo("BOARD_CLOSE_SKIPPED EOA_BOARD_CLOSE=0 - no **Status:** line was touched")
        return True, {"closed": [], "held": [], "already": [], "missing": [], "unwritable": []}
    try:
        res = close_pass(entries=entries, wo_dir=wo_dir, today=today)
    except owner_validations.ValidationsUnreadable as e:
        echo(f"VALIDATIONS_PARSE_FAIL {e}")
        echo("    The close pass ABORTED. No **Status:** line was touched - a partial "
             "close off a damaged record is worse than none. Repair the record "
             "(git checkout proof/owner-validations.json) and re-run.")
        echo("BOARD_CLOSE_FAIL owner-validation record unreadable")
        return False, None
    for name in res["closed"]:
        echo(f"    CLOSED  {name}")
    for name in res["missing"]:
        echo(f"    MISSING {name}   <- validated, but no such file in {os.path.basename(wo_dir or WO_DIR)}/")
    for name in res["unwritable"]:
        echo(f"    NO-STATUS-LINE {name}   <- validated Pass but the file has no **Status:** line")
    ok = not res["missing"] and not res["unwritable"]
    line = (f"BOARD_CLOSE_OK closed {len(res['closed'])}, held {len(res['held'])}, "
            f"already-closed {len(res['already'])}, missing {len(res['missing'])}")
    if not ok:
        line = (f"BOARD_CLOSE_FAIL closed {len(res['closed'])}, held {len(res['held'])}, "
                f"already-closed {len(res['already'])}, missing {len(res['missing'])}, "
                f"no-status-line {len(res['unwritable'])}")
    echo(line)
    return ok, res


# ==============================================================================
# THE BOUNCE (WO-1356) - Fail / Needs Work go BACK TO READY, carrying her note
# ==============================================================================
# THE OWNER'S RULING (2026-09-03)
#     "move the needs work and failed back to ready with a note"
#
# This is the OTHER half of the same sign-off. A Pass closes; a Fail or a Needs Work
# ROUTES THE TICKET BACK TO WORK - and the single most valuable artefact in the whole
# felt-test loop is the sentence she typed saying WHY. It must land IN the ticket, so
# the next seat reads it in the file instead of hunting a screenshot.
#
# It lives HERE, beside the close, because both are the same dangerous act: rewriting a
# **Status:** line from a data file. There is exactly ONE implementation of that act, and
# tools/board_close_validated.py now CALLS this instead of keeping a second copy of the
# rules (its own header already says why: a drifted copy rewrites the owner's tickets).
#
# THE RULES, and each is a safety property:
#  B1. VERDICT ALONE BOUNCES - 'validated' is NOT required, unlike the close.
#      Deliberately asymmetric. A close is the terminal state of a ticket, so it demands
#      two signals; a bounce sends work back to the queue, which is the RECOVERABLE
#      direction and is fully reversible from the PRIOR STATUS: text it preserves.
#      Requiring the extra tap would mean a ticket she marked Fail sits silently in
#      Fixed forever, which is the failure this whole loop exists to end.
#  B2. ONLY A FIXED TICKET BOUNCES. Same eligibility gate as the close, read through the
#      same board_build.classify_status, so the two passes can never disagree about what
#      "Fixed" means. A ticket already back in READY is reported as already-bounced.
#  B3. IDEMPOTENT. A bounce writes a status whose leading word is READY, so the very next
#      run classifies it Ready and skips it (B2). Three runs produce one run's bytes. The
#      consequence, stated plainly: EDITING A NOTE AFTER A BOUNCE DOES NOT RE-STAMP THE
#      TICKET. That is the correct trade - a re-stamp would nest PRIOR STATUS: chains and
#      stack a second note on every rebuild - and a changed note is added by editing the
#      ticket, which is a person's act on a ticket that is already back in the queue.
#  B4. THE EXISTING STATUS BODY SURVIVES VERBATIM, after "PRIOR STATUS:", exactly as the
#      close preserves it. Those FIXED lines carry what shipped and what is still held.
#  B5. AN EMPTY NOTE IS LEGITIMATE. She may mark Needs Work having typed nothing. The
#      ticket bounces anyway and the stamp simply carries no quote. A reason is NEVER
#      invented, and a missing reason NEVER blocks the routing.
#  B6. HER WORDS ARE NOT REWORDED. sanitize_note() only makes the note SAFE for a
#      single-line ASCII status field, and every transformation it applies is REPORTED on
#      the log - see its docstring for the exhaustive list.
#  B7. A MALFORMED RECORD ABORTS, and a mark naming a missing WO file is REPORTED -
#      identical to close rules 6 and 7.
#
# MARKER
#     BOARD_BOUNCE_OK   bounced <n>, already-ready <n>, held <n>, missing <n>
#     BOARD_BOUNCE_FAIL <why>
#     Judge it by the marker on a fresh log, never by the exit code (CLAUDE.md 8/16).

BOUNCE_VERDICTS = ("Fail", "Needs Work")


def verdict_fingerprint(state):
    """WO-1702: identity of one finding, never of an APK or an unrelated commit."""
    normalized = owner_validations.normalize(state)
    return hashlib.sha256(json.dumps(normalized, sort_keys=True, separators=(",", ":"),
                                    ensure_ascii=True).encode("utf-8")).hexdigest()


def retest_receipt(text, state):
    """Return (matching receipt, error). Missing/different findings remain actionable.

    The CLI explicitly records a delivered revision beside the ticket's status. This
    acknowledges an exact old failure without editing the owner's durable record.
    Malformed/duplicate metadata cannot suppress a bounce; the caller reports it.
    """
    lines = re.findall(r"(?m)^\*\*Retest:\*\*[^\S\r\n]*(.*)$", text[:20000])
    if not lines:
        return None, None
    try:
        if len(lines) != 1:
            raise ValueError("expected exactly one Retest receipt")
        receipt = json.loads(lines[0])
        if not isinstance(receipt, dict):
            raise ValueError("receipt must be an object")
        digest = receipt.get("priorVerdictSha256")
        if not isinstance(digest, str) or not re.fullmatch(r"[0-9a-f]{64}", digest):
            raise ValueError("priorVerdictSha256 must be 64 lowercase hex characters")
        for key in ("revision", "reason"):
            if not isinstance(receipt.get(key), str) or not receipt[key].strip():
                raise ValueError(key + " must name the delivered work")
        if state.get("verdict") not in BOUNCE_VERDICTS or digest != verdict_fingerprint(state):
            return None, None
        return receipt, None
    except (ValueError, TypeError) as exc:
        return None, "invalid Retest receipt: " + str(exc)

# Smart punctuation a phone keyboard produces on its own. Folded to the ASCII character
# it IS, never dropped - "don't" must not become "dont".
_ASCII_FOLD = {
    "\u2018": "'", "\u2019": "'", "\u201a": "'", "\u201b": "'",
    "\u201c": '"', "\u201d": '"', "\u201e": '"',
    "\u2013": "-", "\u2014": "-", "\u2212": "-", "\u2011": "-",
    "\u2026": "...", "\u00a0": " ", "\u00b7": "-", "\u2022": "-",
}

_PRIOR_MARK = re.compile(r"(?i)\bPRIOR\s+STATUS\s*:")

NOTE_MAX = 160


def sanitize_note(note):
    """Make ONE note safe for a single-line ASCII **Status:** field.

    Returns (text, transformations). EVERY transformation is named in that list and
    printed by run_bounce, because "sanitised" without saying what changed is how a
    quote quietly stops being a quote.

    The complete list of what this does, and it does nothing else:
      * folds smart quotes / dashes / ellipsis / nbsp to their ASCII equivalents
      * replaces any REMAINING non-ASCII (or control) character with one space
      * flattens line breaks and runs of whitespace to single spaces - a status line
        is one line, and a raw newline would split it and orphan the tail
      * neutralises a literal "PRIOR STATUS:" inside her text to "PRIOR STATUS -", so
        the marker that carries the preserved old status cannot be forged from a note
      * truncates at 160 characters with a trailing "..."

    It never rewords, re-orders, capitalises, spell-corrects or summarises.
    """
    raw = str(note or "")
    applied = []
    folded = "".join(_ASCII_FOLD.get(ch, ch) for ch in raw)
    if folded != raw:
        applied.append("smart punctuation folded to ASCII")

    def _ok(ch):
        return 32 <= ord(ch) <= 126 or ch in "\t\n\r"

    dropped = sum(1 for ch in folded if not _ok(ch))
    if dropped:
        folded = "".join(ch if _ok(ch) else " " for ch in folded)
        applied.append(str(dropped) + " non-ASCII/control character(s) replaced with a space")
    flat = " ".join(folded.split())
    if flat != folded.strip():
        applied.append("line breaks / repeated whitespace flattened to single spaces")
    if _PRIOR_MARK.search(flat):
        flat = _PRIOR_MARK.sub("PRIOR STATUS -", flat)
        applied.append('a literal "PRIOR STATUS:" in the note was written "PRIOR STATUS -" '
                       "so the preserved-status marker cannot be forged")
    if len(flat) > NOTE_MAX:
        flat = flat[:NOTE_MAX - 3] + "..."
        applied.append("truncated to " + str(NOTE_MAX) + " characters")
    return flat, applied


def bounce_stamp(verdict, state, note_s, today, prior):
    """The replacement status line. PREPENDS the bounce; never replaces the body (B4)."""
    prov = []
    if state.get("at"):
        prov.append(f"marked {state['at']}")
    if state.get("build"):
        prov.append(f"build {state['build']}")
    head = f"READY TO IMPLEMENT - owner felt-test {today} {verdict}"
    if prov:
        head += " (" + ", ".join(prov) + ")"
    if note_s:
        head += f' - "{note_s}"'
    return f"{head}. Bounced from Fixed. PRIOR STATUS: {prior}"


def bounce_pass(entries=None, wo_dir=None, today=None):
    """Apply the bounce pass. Mirrors close_pass(); the rules are in the block above."""
    wo_dir = wo_dir or WO_DIR
    today = today or datetime.date.today().isoformat()
    if entries is None:
        entries = owner_validations.entries()   # may raise ValidationsUnreadable - by design

    res = {"bounced": [], "held": [], "already": [], "missing": [], "unwritable": [],
           "sanitized": []}
    for name in sorted(entries):
        state = entries.get(name) or {}
        if not isinstance(state, dict):
            res["held"].append((name, "entry is not an object"))
            continue
        verdict = state.get("verdict") or ""
        # A Pass, or a blank/unrecognised verdict, is the CLOSE pass's business. Silence
        # here is correct: close_pass already reports every entry it holds, and echoing
        # the same rows twice would bury the bounces in the noise.
        if verdict not in BOUNCE_VERDICTS:
            continue
        path = resolve_ticket(wo_dir, name)
        if not path:                                        # B7
            res["missing"].append(name)
            continue
        prior = read_status(path)
        bucket = _bucket(prior)
        if bucket == "Ready":                               # B3 - already bounced
            res["already"].append(name)
            continue
        if bucket not in OWNER_JUDGED:                      # B2
            res["held"].append((name, f"not Fixed (bucket={bucket}) - a felt-test verdict "
                                      f"cannot re-open a ticket that is not on her device"))
            continue
        with open(path, encoding="utf-8", errors="replace") as handle:
            receipt, receipt_error = retest_receipt(handle.read(), state)
        if receipt_error:
            res["sanitized"].append((name, [receipt_error + "; failure remains actionable"]))
        if receipt:
            res["held"].append((name, "RETEST_PENDING previous " + verdict +
                                "; delivered " + receipt["revision"] + "; " + receipt["reason"]))
            continue
        note_s, applied = sanitize_note(state.get("note"))   # B5/B6 - empty is fine
        if applied:
            res["sanitized"].append((name, applied))
        if write_status(path, bounce_stamp(verdict, state, note_s, today, prior)):
            res["bounced"].append((name, verdict, note_s))
        else:
            res["unwritable"].append(name)
    return res


def run_bounce(entries=None, wo_dir=None, today=None, echo=print):
    """Print the bounce in the house style and return (ok, result). Never raises."""
    empty = {"bounced": [], "held": [], "already": [], "missing": [], "unwritable": [],
             "sanitized": []}
    if close_disabled():
        echo("BOARD_BOUNCE_SKIPPED EOA_BOARD_CLOSE=0 - no **Status:** line was touched")
        return True, empty
    try:
        res = bounce_pass(entries=entries, wo_dir=wo_dir, today=today)
    except owner_validations.ValidationsUnreadable as e:
        echo(f"VALIDATIONS_PARSE_FAIL {e}")
        echo("    The bounce pass ABORTED. No **Status:** line was touched - a partial "
             "bounce off a damaged record is worse than none. Repair the record "
             "(git checkout proof/owner-validations.json) and re-run.")
        echo("BOARD_BOUNCE_FAIL owner-validation record unreadable")
        return False, None
    for name, verdict, note_s in res["bounced"]:
        echo(f"    BOUNCED {name}   {verdict} -> READY"
             + (f'   "{note_s}"' if note_s else "   (no note typed - bounced anyway)"))
    for name, applied in res["sanitized"]:
        for what in applied:
            echo(f"      note adjusted for the status line: {what}   [{name}]")
    for name in res["missing"]:
        echo(f"    MISSING {name}   <- Fail/Needs Work, but no such file in "
             f"{os.path.basename(wo_dir or WO_DIR)}/")
    for name, why in res["held"]:
        echo(f"    HELD    {name}   <- {why}")
    for name in res["unwritable"]:
        echo(f"    NO-STATUS-LINE {name}   <- marked for bounce but the file has no "
             f"**Status:** line")
    ok = not res["missing"] and not res["unwritable"]
    line = (f"BOARD_BOUNCE_OK bounced {len(res['bounced'])}, "
            f"already-ready {len(res['already'])}, held {len(res['held'])}, "
            f"missing {len(res['missing'])}")
    if not ok:
        line = (f"BOARD_BOUNCE_FAIL bounced {len(res['bounced'])}, "
                f"already-ready {len(res['already'])}, held {len(res['held'])}, "
                f"missing {len(res['missing'])}, "
                f"no-status-line {len(res['unwritable'])}")
    echo(line)
    return ok, res


def main() -> int:
    ok, _ = run()
    ok_b, _ = run_bounce()
    return 0 if (ok and ok_b) else 1


if __name__ == "__main__":
    raise SystemExit(main())
