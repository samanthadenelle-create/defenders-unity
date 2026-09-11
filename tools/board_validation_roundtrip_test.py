#!/usr/bin/env python3
"""board_validation_roundtrip_test.py - prove a board REBUILD cannot lose a sign-off.

    python tools/board_validation_roundtrip_test.py

Prints VALIDATION_ROUNDTRIP_OK (all assertions passed, and they have teeth) or
VALIDATION_ROUNDTRIP_FAIL <what>. Judge it by the marker, not the exit code.

WHY THIS EXISTS
    The board's owner-validation state used to live in browser localStorage under a
    key carrying the APK build id AND the source commit sha, so every commit orphaned
    every sign-off. The fix moved the record to proof/owner-validations.json and made
    tools/board_build.py READ it. The property that must never regress is therefore:
    "regenerate the board and the sign-offs are still there, on the page and on disk."

    Nothing here touches the live record or the live BOARD.html - both are redirected
    to a temp directory via EOA_VALIDATIONS_PATH / EOA_BOARD_OUT. A test must not be
    able to damage the evidence it exists to protect.

THE RED PROOF (stage 4)
    Assertions that cannot fail prove nothing, so this test breaks the read path on
    purpose - stubs the record loader back to "always empty", the exact behaviour of
    the old code - and requires that stage 1's assertions then FAIL. If the break
    goes undetected, the test reports FAIL: the guard is asleep.
"""
from __future__ import annotations

import glob
import json
import os
import shutil
import subprocess
import sys
import tempfile
import time
import types
import contextlib
import io

TOOLS = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(TOOLS)

failures: list[str] = []


def check(ok, what):
    if not ok:
        failures.append(what)
    print(f"  {'ok  ' if ok else 'FAIL'} {what}")
    return ok


def main() -> int:
    tmp = tempfile.mkdtemp(prefix="eoa-validation-roundtrip-")
    rec = os.path.join(tmp, "owner-validations.json")
    out = os.path.join(tmp, "BOARD.html")
    os.environ["EOA_VALIDATIONS_PATH"] = rec
    os.environ["EOA_BOARD_OUT"] = out
    # An ordinary board build now AUTO-INGESTS the newest eoa-validations-*.json drop file
    # (WO-1356 follow-up). Pinned OFF for the whole suite: a test whose input depends on
    # whatever is sitting in the operator's ~/Downloads proves nothing. Stage 12 opts back
    # in explicitly, always against a throwaway EOA_SUBMIT_DIR.
    os.environ["EOA_BOARD_SUBMIT"] = "0"

    sys.path.insert(0, TOOLS)
    import board_build as bb          # noqa: E402  (env must be set before import)
    import owner_validations as ov    # noqa: E402

    rows = bb.parse_wos()
    fixed = [r for r in rows if r["bucket"] == "Fixed" and r["is_wo"]]
    if not check(bool(fixed), "the repo has at least one Fixed work order to validate"):
        print("VALIDATION_ROUNDTRIP_FAIL no Fixed rows to exercise")
        return 1
    ticket = fixed[0]["file"]

    # ── stage 1: a validation on disk RENDERS ────────────────────────────────────
    print(f"stage 1 - a recorded sign-off renders ({ticket})")
    ov.ingest({ticket: {"validated": True, "verdict": "Pass", "note": "roundtrip probe",
                        "at": "2026-09-03T00:00:00", "build": "test-build"}})
    before = open(rec, encoding="utf-8").read()

    def assertions(page):
        """Stage 1's contract, reused verbatim by the RED proof. Returns failures."""
        bad = []
        i = page.find(f'data-ticket="{ticket}"')
        if i < 0:
            return [f"the validated ticket {ticket} is not on the page at all"]
        item = page[i - 200:i + 1400]
        if "isvalidated" not in item: bad.append("row is not marked isvalidated")
        if "[X] VALIDATED" not in item: bad.append("row carries no VALIDATED word badge")
        if ">Validated<" not in item: bad.append("button still reads Validate, not Validated")
        if "<option selected>Pass</option>" not in item: bad.append("verdict Pass is not pre-selected")
        if "roundtrip probe" not in item: bad.append("the note did not render")
        return bad

    page = bb.build_html(rows)
    bad = assertions(page)
    for f in bad:
        check(False, f)
    check(not bad, "row renders VALIDATED + Pass + note, server-side, from the record")
    check('<span class="gcount">1 /' in page,
          "a group count renders 1 done straight from disk (no JS needed)")

    # ── stage 2: a REBUILD preserves it (the whole point) ────────────────────────
    print("stage 2 - rebuild twice; the record and the render both survive")
    bb.build_html(rows)
    page2 = bb.build_html(rows)
    check(not assertions(page2), "the sign-off still renders after two more rebuilds")
    check(open(rec, encoding="utf-8").read() == before,
          "the record file is byte-identical after rebuilds (board never writes it)")

    # ── stage 3: the REAL entry point emits the marker ───────────────────────────
    print("stage 3 - the real entry point emits VALIDATIONS_OK")
    # EOA_BOARD_CLOSE=0: this run points at the LIVE WorkOrders/ while the temp record now
    # carries a Pass+validated mark on a REAL Fixed ticket, so without the opt-out the
    # WO-1355 close pass would rewrite that ticket's status line for real. Stage 5 proves
    # the close through this same entry point against a THROWAWAY WorkOrders/ instead.
    env = dict(os.environ, EOA_BOARD_CLOSE="0")
    proc = subprocess.run([sys.executable, os.path.join(TOOLS, "board_build.py")],
                          cwd=ROOT, capture_output=True, text=True,
                          encoding="utf-8", errors="replace", env=env)
    log = (proc.stdout or "") + (proc.stderr or "")
    check("VALIDATIONS_OK 1 recorded, 1 validated" in log,
          "VALIDATIONS_OK reports 1 recorded / 1 validated")
    check("BOARD_CHECK_OK" in log, "BOARD_CHECK_OK still printed")
    check("BOARD_CLOSE_SKIPPED" in log,
          "the close pass honoured EOA_BOARD_CLOSE=0 against the live WorkOrders/")
    check(os.path.exists(out) and not assertions(open(out, encoding="utf-8").read()),
          "the written page carries the sign-off")
    check(open(rec, encoding="utf-8").read() == before,
          "a full board_build run left the record untouched")

    # ── stage 4: RED PROOF - break the read path, demand a FAIL ──────────────────
    print("stage 4 - RED proof: stub the record loader empty (the OLD behaviour)")
    real = bb.owner_validations.entries
    try:
        bb.owner_validations.entries = lambda *a, **k: {}
        broken = assertions(bb.build_html(rows))
    finally:
        bb.owner_validations.entries = real
    check(bool(broken), f"a broken read path FAILS the stage-1 assertions "
                        f"({len(broken)} caught: {broken[0] if broken else 'none'})")
    check(not assertions(bb.build_html(rows)),
          "and the restored read path renders it again")


    # == WO-1355: THE CLOSE PASS =================================================
    # Owner ruling 2026-09-03: a Pass+validated sign-off on a FIXED ticket is flipped to
    # CLOSED by the NEXT board build. That pass REWRITES **Status:** lines from a data
    # file, so everything below runs against a THROWAWAY WorkOrders/ (EOA_WO_DIR) and a
    # THROWAWAY record - the live tickets are never in reach of this test.
    import board_close_pass as bcp        # noqa: E402

    wodir = os.path.join(tmp, "WorkOrders")
    rec2 = os.path.join(tmp, "close-record.json")
    out2 = os.path.join(tmp, "BOARD-close.html")

    # ONE ticket per rule, so a failure names the rule it broke.
    FIXTURES = {
        # eligible: FIXED + Pass + validated. Its body carries real engineering detail,
        # which rule 4 says must survive the close verbatim.
        "WORK_ORDER_9001_fixed_pass.md":
            "FIXED 2026-09-01 - wolf routing shipped; HELD awaiting her retag; "
            "finding 3 deliberately not fixed.",
        # rule 2: never closed - a sign-off cannot close what never reached the device.
        "WORK_ORDER_9002_ready_pass.md":
            "READY TO IMPLEMENT - not built yet.",
        # rule 3: already CLOSED - never re-stamped, never re-dated.
        "WORK_ORDER_9003_closed_pass.md":
            "CLOSED 2026-08-01 - owner felt-test PASS. PRIOR STATUS: FIXED 2026-07-30.",
        # rule 1: FIXED but the verdict is Fail - closes nothing (it bounces, elsewhere).
        "WORK_ORDER_9004_fixed_fail.md":
            "FIXED 2026-09-01 - shipped, awaiting her felt test.",
        # rule 1: FIXED + Pass but NEVER validated - one signal is not two.
        "WORK_ORDER_9005_fixed_unvalidated.md":
            "FIXED 2026-09-01 - shipped, awaiting her felt test.",
        # rule 1: FIXED + validated but the verdict is blank. This is also where an
        # UNRECOGNISED verdict lands: owner_validations.normalize() coerces anything it
        # does not know to "", so a garbage verdict can never be read as a Pass.
        "WORK_ORDER_9006_fixed_novrd.md":
            "FIXED 2026-09-01 - shipped, awaiting her felt test.",
        # rule 2: DONE + Pass + validated - a finished-but-not-Fixed row stays untouched.
        "WORK_ORDER_9007_done_pass.md":
            "DONE 2026-08-20 - landed and verified headless.",
        # ── WO-1356 THE BOUNCE ────────────────────────────────────────────────
        # B1/THE ACCEPTANCE CASE, taken from her live board: WO-1184 is Validated AND
        # "Needs Work" WITH A NOTE. It must BOUNCE to READY carrying that note, and it
        # must NOT close. If an eligibility rule ever closes this row, the rule is wrong.
        "WORK_ORDER_9008_fixed_needswork.md":
            "FIXED 2026-08-27 - implemented; HELD awaiting her retag; awaiting owner "
            "felt-verify to CLOSE.",
        # B1 + B5: verdict ALONE bounces (validated is False here) and an EMPTY note is
        # legitimate - she may mark Fail having typed nothing. It bounces anyway, and no
        # reason is invented for her.
        "WORK_ORDER_9009_fixed_fail_unvalidated_nonote.md":
            "FIXED 2026-09-01 - shipped, awaiting her felt test.",
        # B2: a DONE ticket marked Needs Work is HELD, never bounced - a felt-test
        # verdict cannot re-open a ticket that is not on her device.
        "WORK_ORDER_9010_done_needswork.md":
            "DONE 2026-08-20 - landed and verified headless.",
        # B6: the note is hostile to a single-line ASCII status field - a curly
        # apostrophe, a double quote, a newline and a forged "PRIOR STATUS:" marker.
        "WORK_ORDER_9011_fixed_needswork_gnarly.md":
            "FIXED 2026-09-01 - shipped, awaiting her felt test.",
    }
    # (verdict, validated, note) - the note matters now: it is the whole point of the
    # bounce, and it is the thing that used to be thrown away.
    HER_NOTE = "right now its a red d"          # verbatim from WO-1184 on her live board
    GNARLY = ('she said \u201cit still flickers\u201d\n'
              'PRIOR STATUS: forged marker, and a curly apostrophe: don\u2019t')
    MARKS = {
        "WORK_ORDER_9001_fixed_pass.md":        ("Pass", True, ""),
        "WORK_ORDER_9002_ready_pass.md":        ("Pass", True, ""),
        "WORK_ORDER_9003_closed_pass.md":       ("Pass", True, ""),
        "WORK_ORDER_9004_fixed_fail.md":        ("Fail", True, "gate is still red on device"),
        "WORK_ORDER_9005_fixed_unvalidated.md": ("Pass", False, ""),
        "WORK_ORDER_9006_fixed_novrd.md":       ("", True, ""),
        "WORK_ORDER_9007_done_pass.md":         ("Pass", True, ""),
        "WORK_ORDER_9008_fixed_needswork.md":   ("Needs Work", True, HER_NOTE),
        "WORK_ORDER_9009_fixed_fail_unvalidated_nonote.md": ("Fail", False, ""),
        "WORK_ORDER_9010_done_needswork.md":    ("Needs Work", True, "not on the device"),
        "WORK_ORDER_9011_fixed_needswork_gnarly.md": ("Needs Work", True, GNARLY),
    }
    CLOSE_ELIGIBLE = "WORK_ORDER_9001_fixed_pass.md"
    BOUNCE_ELIGIBLE = ["WORK_ORDER_9004_fixed_fail.md",
                       "WORK_ORDER_9008_fixed_needswork.md",
                       "WORK_ORDER_9009_fixed_fail_unvalidated_nonote.md",
                       "WORK_ORDER_9011_fixed_needswork_gnarly.md"]
    # A close-ONLY run must leave every non-close row alone, bounce rows included.
    UNTOUCHED = [k for k in FIXTURES if k != CLOSE_ELIGIBLE]
    # A close+bounce run touches both sets; everything else must still be byte-identical.
    UNTOUCHED_BOTH = [k for k in FIXTURES
                      if k != CLOSE_ELIGIBLE and k not in BOUNCE_ELIGIBLE]

    def seed_wos(target):
        if os.path.isdir(target):
            shutil.rmtree(target)
        os.makedirs(target)
        for name, status in FIXTURES.items():
            body = "# " + name + "\n\n**Status:** " + status + "\n\nBody text.\n"
            with open(os.path.join(target, name), "w", encoding="utf-8", newline="\n") as f:
                f.write(body)
        return {n: open(os.path.join(target, n), encoding="utf-8").read() for n in FIXTURES}

    def marks(extra=None):
        out = {}
        for name, (verdict, validated, note) in MARKS.items():
            out[name] = {"validated": validated, "verdict": verdict, "note": note,
                         "at": "2026-09-03T08:00:00", "build": "2026.09.03.353742"}
        if extra:
            out.update(extra)
        return out

    def close_assertions(target, baseline, untouched=None):
        """The close pass's whole contract, reused verbatim by the RED proof below."""
        untouched = UNTOUCHED if untouched is None else untouched
        bad = []
        got = {n: open(os.path.join(target, n), encoding="utf-8").read() for n in FIXTURES}
        eligible = CLOSE_ELIGIBLE
        st = bcp.read_status(os.path.join(target, eligible))
        if not st.upper().startswith("CLOSED"):
            bad.append("the eligible FIXED+Pass+validated ticket was not closed: " + repr(st[:40]))
        if FIXTURES[eligible] not in st:
            bad.append("rule 4: the original FIXED status body did not survive the close")
        if "PRIOR STATUS:" not in st:
            bad.append("rule 4: no PRIOR STATUS: marker carrying the old line")
        if "2026-09-03T08:00:00" not in st or "2026.09.03.353742" not in st:
            bad.append("rule 5: the close stamp records neither the 'at' nor the 'build'")
        if "Body text." not in got[eligible]:
            bad.append("the rest of the work-order file did not survive")
        for name in untouched:
            if got[name] != baseline[name]:
                bad.append("rule 1/2/3: " + name + " was rewritten and must not have been: "
                           + repr(bcp.read_status(os.path.join(target, name))[:50]))
        return bad

    def bounce_assertions(target, baseline):
        """The BOUNCE's whole contract (WO-1356), reused verbatim by its RED proof."""
        bad = []
        got = {n: open(os.path.join(target, n), encoding="utf-8").read() for n in FIXTURES}
        st = {n: bcp.read_status(os.path.join(target, n)) for n in FIXTURES}

        # THE ACCEPTANCE CASE - Validated + "Needs Work" + a note.
        acc = "WORK_ORDER_9008_fixed_needswork.md"
        a = st[acc]
        if not a.upper().startswith("READY"):
            bad.append("acceptance case: Validated + Needs Work did NOT bounce to READY: "
                       + repr(a[:60]))
        if a.upper().startswith("CLOSED"):
            bad.append("acceptance case: Validated + Needs Work was CLOSED - the "
                       "eligibility rule is wrong")
        if HER_NOTE not in a:
            bad.append("acceptance case: her note did not land in the ticket verbatim: "
                       + repr(a[:120]))
        if "Needs Work" not in a:
            bad.append("acceptance case: the verdict is not named on the status line")
        if "PRIOR STATUS:" not in a or FIXTURES[acc] not in a:
            bad.append("B4: the FIXED status body did not survive the bounce")
        if "2026-09-03T08:00:00" not in a or "2026.09.03.353742" not in a:
            bad.append("the bounce stamp records neither the 'at' nor the 'build'")
        if "Body text." not in got[acc]:
            bad.append("the rest of the bounced work-order file did not survive")

        # B1 + B5: verdict alone, empty note - still bounces, and invents no reason.
        e = st["WORK_ORDER_9009_fixed_fail_unvalidated_nonote.md"]
        if not e.upper().startswith("READY"):
            bad.append("B1/B5: a Fail with no note and no Validated tap did not bounce: "
                       + repr(e[:60]))
        if '"' in e.split("PRIOR STATUS:")[0]:
            bad.append("B5: a reason was invented for an empty note: " + repr(e[:90]))

        # A noted Fail bounces too.
        f = st["WORK_ORDER_9004_fixed_fail.md"]
        if not f.upper().startswith("READY") or "gate is still red on device" not in f:
            bad.append("a Fail with a note did not bounce with its note: " + repr(f[:80]))

        # B6: the hostile note is made SAFE without being reworded.
        g = st["WORK_ORDER_9011_fixed_needswork_gnarly.md"]
        if "\n" in g or "\r" in g:
            bad.append("B6: a newline survived into the status line and split it")
        if any(ord(c) > 126 for c in g):
            bad.append("B6: a non-ASCII character survived into the status line")
        if "it still flickers" not in g or "don't" not in g:
            bad.append("B6: her words did not survive the sanitising: " + repr(g[:140]))
        head, _sep, _tail = g.partition("Bounced from Fixed.")
        if "PRIOR STATUS:" in head:
            bad.append("B6: a forged 'PRIOR STATUS:' inside the note was not neutralised")
        if g.count("PRIOR STATUS:") != 1:
            bad.append("B6: the status line carries " + str(g.count("PRIOR STATUS:"))
                       + " PRIOR STATUS: markers, expected exactly 1")

        # B2: a DONE ticket marked Needs Work is HELD, not bounced.
        h = "WORK_ORDER_9010_done_needswork.md"
        if got[h] != baseline[h]:
            bad.append("B2: a DONE ticket was bounced by a felt-test verdict: "
                       + repr(st[h][:60]))
        return bad

    # -- stage 5: the close runs INSIDE the real board_build entry point ----------
    print("stage 5 - python tools/board_build.py performs the close itself (no 2nd command)")
    base5 = seed_wos(wodir)
    ov.ingest(marks(), path=rec2)
    env2 = dict(os.environ, EOA_WO_DIR=wodir, EOA_VALIDATIONS_PATH=rec2, EOA_BOARD_OUT=out2)
    env2.pop("EOA_BOARD_CLOSE", None)
    p5 = subprocess.run([sys.executable, os.path.join(TOOLS, "board_build.py")],
                        cwd=ROOT, capture_output=True, text=True,
                        encoding="utf-8", errors="replace", env=env2)
    log5 = (p5.stdout or "") + (p5.stderr or "")
    check("BOARD_CLOSE_OK closed 1, held 9, already-closed 1, missing 0" in log5,
          "board_build emits BOARD_CLOSE_OK closed 1, held 9, already-closed 1, missing 0")
    for f in close_assertions(wodir, base5, UNTOUCHED_BOTH):
        check(False, f)
    check(not close_assertions(wodir, base5, UNTOUCHED_BOTH),
          "exactly the FIXED+Pass+validated ticket closed; body preserved; the rest untouched")

    # ── WO-1356: the SAME run performs the bounce ───────────────────────────────
    print("stage 5b - the same board_build run bounces Fail / Needs Work back to READY")
    check("BOARD_BOUNCE_OK bounced 4, already-ready 0, held 1, missing 0" in log5,
          "board_build emits BOARD_BOUNCE_OK bounced 4, already-ready 0, held 1, missing 0")
    for f in bounce_assertions(wodir, base5):
        check(False, f)
    check(not bounce_assertions(wodir, base5),
          "Needs Work + Fail bounced to READY with her note verbatim; Pass closed; "
          "a DONE row held; the status body preserved on both paths")
    check(HER_NOTE in bcp.read_status(os.path.join(wodir, "WORK_ORDER_9008_fixed_needswork.md")),
          "THE ACCEPTANCE CASE: Validated + Needs Work + note bounced (not closed), "
          "note verbatim in the ticket")

    # -- stage 6: IDEMPOTENCY - three runs, one run's bytes ----------------------
    print("stage 6 - idempotency: rebuild twice more, the bytes must not move")
    after1 = {n: open(os.path.join(wodir, n), encoding="utf-8").read() for n in FIXTURES}
    for _ in (2, 3):
        subprocess.run([sys.executable, os.path.join(TOOLS, "board_build.py")],
                       cwd=ROOT, capture_output=True, text=True,
                       encoding="utf-8", errors="replace", env=env2)
    after3 = {n: open(os.path.join(wodir, n), encoding="utf-8").read() for n in FIXTURES}
    check(after1 == after3, "runs 2 and 3 produce byte-identical work-order files "
                            "(a closed ticket is never re-stamped or re-dated)")
    # And the second run must SAY it recognised the bounces rather than silently
    # skipping them: a bounced ticket is now READY, so it reports as already-bounced.
    p6 = subprocess.run([sys.executable, os.path.join(TOOLS, "board_build.py")],
                        cwd=ROOT, capture_output=True, text=True,
                        encoding="utf-8", errors="replace", env=env2)
    log6 = (p6.stdout or "") + (p6.stderr or "")
    check("BOARD_BOUNCE_OK bounced 0, already-ready 4, held 1, missing 0" in log6,
          "a re-run bounces 0 and reports already-ready 4 (no stacked note, no nested "
          "PRIOR STATUS: chain)")
    after4 = {n: open(os.path.join(wodir, n), encoding="utf-8").read() for n in FIXTURES}
    check(after1 == after4, "and a fourth run still produces the same bytes")

    # -- stage 7: a mark naming a MISSING WO file is reported, never dropped ------
    print("stage 7 - a validation naming a file that does not exist is REPORTED")
    base7 = seed_wos(wodir)
    lines = []
    ok7, res7 = bcp.run(entries=marks({"WORK_ORDER_9999_ghost.md": {
        "validated": True, "verdict": "Pass", "at": "2026-09-03T08:00:00"}}),
        wo_dir=wodir, echo=lines.append)
    log7 = "\n".join(lines)
    check("WORK_ORDER_9999_ghost.md" in log7 and "MISSING" in log7,
          "the missing WO is named on the log, not silently dropped")
    check("BOARD_CLOSE_FAIL" in log7 and "missing 1" in log7,
          "and the pass reports FAIL rather than a clean OK")
    check(not ok7, "run() returns not-ok so a caller can act on it")
    check(not close_assertions(wodir, base7),
          "the real tickets still closed/held correctly around the ghost entry")
    # And the bounce reports its own missing file rather than dropping the routing.
    lines = []
    ok7b, _res7b = bcp.run_bounce(entries=marks({"WORK_ORDER_9999_ghost.md": {
        "validated": True, "verdict": "Needs Work", "note": "gone",
        "at": "2026-09-03T08:00:00"}}), wo_dir=wodir, echo=lines.append)
    log7b = "\n".join(lines)
    check("WORK_ORDER_9999_ghost.md" in log7b and "MISSING" in log7b and not ok7b,
          "the bounce also NAMES a validation pointing at a missing WO file, and fails")

    # -- stage 8: a CORRUPT record ABORTS the close, closing no partial set -------
    print("stage 8 - a corrupt validation record aborts the close pass")
    base8 = seed_wos(wodir)
    bad_rec = os.path.join(tmp, "corrupt.json")
    with open(bad_rec, "w", encoding="utf-8", newline="\n") as f:
        f.write('{"_schema": 1, "validations": [ this is not json')
    saved = ov.PATH
    try:
        ov.PATH = bad_rec
        lines = []
        ok8, res8 = bcp.run(entries=None, wo_dir=wodir, echo=lines.append)
    finally:
        ov.PATH = saved
    log8 = "\n".join(lines)
    check("VALIDATIONS_PARSE_FAIL" in log8 and "BOARD_CLOSE_FAIL" in log8,
          "a corrupt record prints VALIDATIONS_PARSE_FAIL + BOARD_CLOSE_FAIL")
    check(res8 is None and not ok8, "the pass aborts instead of closing a partial set")
    now8 = {n: open(os.path.join(wodir, n), encoding="utf-8").read() for n in FIXTURES}
    check(now8 == base8, "not one **Status:** line was touched during the abort")
    base8b = seed_wos(wodir)
    try:
        ov.PATH = bad_rec
        lines = []
        ok8b, res8b = bcp.run_bounce(entries=None, wo_dir=wodir, echo=lines.append)
    finally:
        ov.PATH = saved
    log8b = "\n".join(lines)
    check("VALIDATIONS_PARSE_FAIL" in log8b and "BOARD_BOUNCE_FAIL" in log8b,
          "a corrupt record prints VALIDATIONS_PARSE_FAIL + BOARD_BOUNCE_FAIL")
    check(res8b is None and not ok8b, "the bounce aborts instead of bouncing a partial set")
    now8b = {n: open(os.path.join(wodir, n), encoding="utf-8").read() for n in FIXTURES}
    check(now8b == base8b, "and not one **Status:** line was touched by that abort either")

    # -- stage 9: RED PROOF - mutate the close pass, demand the oracle catch it ---
    # Assertions that cannot fail prove nothing. Each mutation below deletes exactly one
    # safety rule from a COPY of the module source (the file on disk is never touched)
    # and the stage-5 contract must go red for it.
    print("stage 9 - RED proof: four mutations of the close pass, each must be caught")
    src = open(os.path.join(TOOLS, "board_close_pass.py"), encoding="utf-8").read()
    # ANCHORS INCLUDE THEIR TRAILING NEWLINE, deliberately. board_close_pass.py now holds
    # the BOUNCE as well, and it tests the same predicates ('if bucket not in OWNER_JUDGED:') with a
    # trailing rule comment - so a bare substring anchor matches TWICE and the mutation is
    # skipped. A skipped mutation is a RED proof that proves nothing, which is worse than a
    # failing one because it reports as a warning rather than a wrong answer.
    MUTANTS = [
        ([('if verdict != "Pass" or not validated:', 'if verdict != "Pass":')],
         "rule 1 - drop the 'validated' signal"),
        ([('if bucket not in OWNER_JUDGED:\n', 'if bucket not in ("Fixed", "Verify", "Ready", "Done"):\n')],
         "rule 2 - let a non-FIXED ticket close"),
        # Rule 3 needs BOTH edits: the already-Closed early return is what protects a
        # closed ticket, and the Fixed check would catch it afterwards. Removing one and
        # not the other proves nothing, so this mutant removes the pair.
        ([('if bucket == "Closed":\n', 'if bucket == "NeverMatches":\n'),
          ('if bucket not in OWNER_JUDGED:\n', 'if bucket not in ("Fixed", "Verify", "Closed"):\n')],
         "rule 3 - re-stamp a ticket that is already CLOSED"),
        ([('return f"{head}. PRIOR STATUS: {prior}"', 'return head')],
         "rule 4 - throw away the existing status body"),
    ]
    for edits, what in MUTANTS:
        mutated = src
        anchors_ok = True
        for find, repl in edits:
            if mutated.count(find) != 1:
                anchors_ok = False
                break
            mutated = mutated.replace(find, repl)
        if not anchors_ok:
            check(False, "RED proof could not apply mutation (" + what + "): anchor not unique")
            continue
        mod = types.ModuleType("board_close_pass_mutant")
        mod.__file__ = os.path.join(TOOLS, "board_close_pass.py")
        exec(compile(mutated, mod.__file__, "exec"), mod.__dict__)
        base9 = seed_wos(wodir)
        mod.run(entries=marks(), wo_dir=wodir, echo=lambda *a: None)
        broke = close_assertions(wodir, base9)
        check(bool(broke), "mutation caught (" + what + ") -> "
              + (broke[0] if broke else "NOTHING"))
    base9 = seed_wos(wodir)
    bcp.run(entries=marks(), wo_dir=wodir, echo=lambda *a: None)
    check(not close_assertions(wodir, base9),
          "and the UNmutated close pass is green again (the success path, proven)")

    # -- stage 9b: RED PROOF for the BOUNCE - four mutations, each must be caught --
    print("stage 9b - RED proof: four mutations of the BOUNCE pass, each must be caught")
    BOUNCE_MUTANTS = [
        ([('    if note_s:\n        head += f\' - "{note_s}"\'',
           '    if False:\n        head += f\' - "{note_s}"\'')],
         "B-note - drop her note from the stamp"),
        ([('    return f"{head}. Bounced from Fixed. PRIOR STATUS: {prior}"',
           '    return head')],
         "B4 - throw away the existing status body"),
        ([('        if bucket not in OWNER_JUDGED:                      # B2',
           '        if bucket not in ("Fixed", "Verify", "Done"):       # B2')],
         "B2 - let a DONE ticket be bounced by a felt-test verdict"),
        ([('        note_s, applied = sanitize_note(state.get("note"))   # B5/B6 - empty is fine',
           '        note_s, applied = sanitize_note(state.get("note"))\n'
           '        if not note_s:\n            continue')],
         "B5 - skip a ticket whose note is empty"),
    ]
    for edits, what in BOUNCE_MUTANTS:
        mutated = src
        anchors_ok = True
        for find, repl in edits:
            if mutated.count(find) != 1:
                anchors_ok = False
                break
            mutated = mutated.replace(find, repl)
        if not anchors_ok:
            check(False, "RED proof could not apply mutation (" + what + "): anchor not unique")
            continue
        mod = types.ModuleType("board_bounce_mutant")
        mod.__file__ = os.path.join(TOOLS, "board_close_pass.py")
        exec(compile(mutated, mod.__file__, "exec"), mod.__dict__)
        base = seed_wos(wodir)
        mod.run(entries=marks(), wo_dir=wodir, echo=lambda *a: None)
        mod.run_bounce(entries=marks(), wo_dir=wodir, echo=lambda *a: None)
        broke = bounce_assertions(wodir, base)
        check(bool(broke), "mutation caught (" + what + ") -> "
              + (broke[0] if broke else "NOTHING"))
    base = seed_wos(wodir)
    bcp.run(entries=marks(), wo_dir=wodir, echo=lambda *a: None)
    bcp.run_bounce(entries=marks(), wo_dir=wodir, echo=lambda *a: None)
    check(not bounce_assertions(wodir, base),
          "and the UNmutated bounce pass is green again (the success path, proven)")

    # == stage 10: --submit, the button's other end ==============================
    # The owner taps Submit on a file:// page, the browser saves
    # eoa-validations-<stamp>.json, and ONE CLI command ingests it and runs the whole
    # pass. Proven here end to end against a throwaway drop dir, record and WorkOrders/.
    print("stage 10 - python tools/board_build.py --submit ingests the newest drop file")
    drop = os.path.join(tmp, "drop")
    os.makedirs(drop, exist_ok=True)
    rec3 = os.path.join(tmp, "submit-record.json")
    out3 = os.path.join(tmp, "BOARD-submit.html")
    base10 = seed_wos(wodir)
    # An OLDER file that must lose to the newer one - "newest wins" has to be a fact.
    with open(os.path.join(drop, "eoa-validations-20260101T000000Z.json"), "w",
              encoding="utf-8", newline="\n") as f:
        json.dump({"validations": {}}, f)
    newest = os.path.join(drop, "eoa-validations-20260903T220802Z.json")
    with open(newest, "w", encoding="utf-8", newline="\n") as f:
        json.dump({"validations": marks()}, f)
    os.utime(newest, (time.time(), time.time()))
    env3 = dict(os.environ, EOA_WO_DIR=wodir, EOA_VALIDATIONS_PATH=rec3,
                EOA_BOARD_OUT=out3, EOA_SUBMIT_DIR=drop)
    env3.pop("EOA_BOARD_CLOSE", None)
    p10 = subprocess.run([sys.executable, os.path.join(TOOLS, "board_build.py"), "--submit"],
                         cwd=ROOT, capture_output=True, text=True,
                         encoding="utf-8", errors="replace", env=env3)
    log10 = (p10.stdout or "") + (p10.stderr or "")
    check(os.path.basename(newest) in log10 and "VALIDATIONS_SUBMIT_FILE" in log10,
          "--submit names the exact file it took, and takes the NEWEST one")
    check("VALIDATIONS_SUBMIT_OK" in log10, "VALIDATIONS_SUBMIT_OK is emitted")
    check("VALIDATIONS_INGEST_OK" in log10 and os.path.exists(rec3),
          "the submitted marks were folded into the record")
    check("BOARD_CLOSE_OK closed 1, held 9, already-closed 1, missing 0" in log10
          and "BOARD_BOUNCE_OK bounced 4, already-ready 0, held 1, missing 0" in log10,
          "the SAME command then closed the Passed ticket and bounced the 4 others "
          "(no second command to remember)")
    check(not close_assertions(wodir, base10, UNTOUCHED_BOTH)
          and not bounce_assertions(wodir, base10),
          "and the work-order files match the full close+bounce contract")

    print("stage 10b - --submit with no drop file FAILS loudly and touches nothing")
    empty_drop = os.path.join(tmp, "drop-empty")
    os.makedirs(empty_drop, exist_ok=True)
    base10b = seed_wos(wodir)
    env3b = dict(env3, EOA_SUBMIT_DIR=empty_drop)
    p10b = subprocess.run([sys.executable, os.path.join(TOOLS, "board_build.py"), "--submit"],
                          cwd=ROOT, capture_output=True, text=True,
                          encoding="utf-8", errors="replace", env=env3b)
    log10b = (p10b.stdout or "") + (p10b.stderr or "")
    check("VALIDATIONS_SUBMIT_FAIL" in log10b,
          "VALIDATIONS_SUBMIT_FAIL when no submission file exists")
    check("BOARD_CLOSE_OK" not in log10b and "BOARD_BOUNCE_OK" not in log10b,
          "it stops before the passes rather than running them on stale data")
    now10b = {n: open(os.path.join(wodir, n), encoding="utf-8").read() for n in FIXTURES}
    check(now10b == base10b, "not one **Status:** line was touched")

    # == stage 11: THE HEADLINE COUNTS ONLY WHAT IS SAVED ========================
    # Owner ruling 2026-09-03: "Count only what is saved." The board read "43 / 78
    # verified" from localStorage while proof/owner-validations.json held ZERO and the
    # close pass reported "closed 0" - so she reasonably expected 43 tickets to have
    # moved. The headline must be the DURABLE record, never the browser overlay.
    #
    # The page's counting functions are pure and fenced with [ORACLE:counts]; this stage
    # extracts THAT EXACT BLOCK out of the shipped HTML and runs it under node, so what
    # is tested is what she reads - not a Python re-implementation of it.
    print("stage 11 - the headline counter reads the RECORD, never the browser overlay")
    page11 = bb.build_html(rows)
    b0 = page11.find("/* [ORACLE:counts]")
    b1 = page11.find("/* [/ORACLE:counts] */")
    check(b0 > 0 and b1 > b0, "the page carries the fenced [ORACLE:counts] block")
    counts_js = page11[b0:b1] if (b0 > 0 and b1 > b0) else ""
    check("vprogress').textContent=`${vDurableDone(vtickets,disk)}"
          in page11.replace("\n", ""),
          "the headline is assigned from vDurableDone(tickets, disk) - the disk map only")
    check("`${done}" not in page11,
          "no effective-state counter is left assigning the headline")

    # SERVER-RENDERED half (no JS at all): the durable count is already in the HTML.
    disk_now = ov.entries()
    fixed_now = [r["file"] for r in rows if r["bucket"] == "Fixed" and r["is_wo"]]
    exp_disk = sum(1 for f in fixed_now if disk_now.get(f, {}).get("validated"))
    check('id="vprogress">%d /' % exp_disk in page11,
          "no-JS: the server-rendered headline is the record's count (%d)" % exp_disk)
    check('id="vpending"' in page11,
          "no-JS: the pending line renders with a default that is true with JS off")

    node = shutil.which("node")
    if not node:
        print("  --   node not on PATH: the browser-side count assertions are SKIPPED")
    else:
        def run_counts(js, disk_map, local_map, tickets):
            driver = (js + "\nconst LOCAL=" + json.dumps(local_map) + ";\n"
                      + "const D=" + json.dumps(disk_map) + ", T=" + json.dumps(tickets) + ";\n"
                      + "console.log(JSON.stringify({d:vDurableDone(T,D),"
                        "p:vPending(T,D,LOCAL)}));\n")
            f = os.path.join(tmp, "counts.js")
            with open(f, "w", encoding="utf-8", newline="\n") as fh:
                fh.write(driver)
            r = subprocess.run([node, f], capture_output=True, text=True,
                               encoding="utf-8", errors="replace")
            try:
                return json.loads((r.stdout or "").strip().splitlines()[-1])
            except Exception:
                return {"d": None, "p": None, "err": (r.stdout or "") + (r.stderr or "")}

        T = ["t1", "t2", "t3", "t4", "t5"]
        MARK = {"validated": True, "verdict": "Pass"}
        # N saved, M in the browser: the headline must read N. Never M, never N+M.
        rec_2 = {"t1": MARK, "t2": MARK}
        loc_3 = {"t3": MARK, "t4": {"verdict": "Fail"}, "t5": {"note": "flickers"}}
        a = run_counts(counts_js, rec_2, loc_3, T)
        check(a["d"] == 2, "record 2 + browser 3 -> headline 2 (got %r; M=3 and N+M=5 are "
                           "the two wrong answers)" % (a["d"],))
        check(a["p"] == 3, "...and 3 marks are reported as pending (got %r)" % (a["p"],))
        # THE CASE THAT MATTERS RIGHT NOW: record EMPTY, browser full.
        b = run_counts(counts_js, {}, loc_3, T)
        check(b["d"] == 0, "record EMPTY + browser marks -> headline 0 (got %r)" % (b["d"],))
        check(b["p"] == 3, "...and the 3 unsaved marks are counted as pending (got %r)"
              % (b["p"],))
        # A mark identical to the record is NOT pending (submitted, ingested, done).
        c = run_counts(counts_js, rec_2, {"t1": MARK}, T)
        check(c["d"] == 2 and c["p"] == 0,
              "a browser mark that already matches the record is not 'pending' (%r)" % (c,))

        # -- RED PROOF: restore the old behaviour, demand the oracle catch it -----
        print("stage 11b - RED proof: make the headline count the browser overlay again")
        mut = counts_js.replace("if((diskMap[t]||{}).validated) n++;",
                                "if((diskMap[t]||{}).validated||(LOCAL[t]||{}).validated) n++;")
        check(mut != counts_js, "the RED mutation applied (anchor found)")
        m1 = run_counts(mut, {}, loc_3, T)
        check(m1["d"] != 0, "mutation caught: the empty-record case now reads %r, not 0"
              % (m1["d"],))
        m2 = run_counts(mut, rec_2, loc_3, T)
        check(m2["d"] != 2, "mutation caught: record 2 + browser 3 now reads %r, not 2"
              % (m2["d"],))
        c2 = run_counts(counts_js, {}, loc_3, T)
        check(c2["d"] == 0, "and the UNmutated counter reads 0 again (success path proven)")

    # == stage 12: THE AUTO-INGEST IS PART OF AN ORDINARY BUILD ==================
    # Owner 2026-09-03: "i would expect you to do this everytime you build the board. CAn
    # you add it to the rebuild script". A flag the CLI must remember is the same failure
    # as a second command it must remember. Everything below runs against a throwaway drop
    # dir, record and WorkOrders/ - never the operator's Downloads, never the real tickets.
    print("stage 12 - a PLAIN board build ingests her newest drop file by itself")
    drop2 = os.path.join(tmp, "drop-auto")
    os.makedirs(drop2, exist_ok=True)
    rec4 = os.path.join(tmp, "auto-record.json")
    out4 = os.path.join(tmp, "BOARD-auto.html")

    def drop_file(name, payload):
        f = os.path.join(drop2, name)
        with open(f, "w", encoding="utf-8", newline="\n") as fh:
            json.dump(payload, fh)
        return f

    def plain_build(env_extra=None, args=()):
        e = dict(os.environ, EOA_WO_DIR=wodir, EOA_VALIDATIONS_PATH=rec4,
                 EOA_BOARD_OUT=out4, EOA_SUBMIT_DIR=drop2, EOA_BOARD_SUBMIT="1")
        e.pop("EOA_BOARD_CLOSE", None)
        e.update(env_extra or {})
        r = subprocess.run([sys.executable, os.path.join(TOOLS, "board_build.py")] + list(args),
                           cwd=ROOT, capture_output=True, text=True,
                           encoding="utf-8", errors="replace", env=e)
        return (r.stdout or "") + (r.stderr or "")

    # An OLDER submission carrying a mark she has since changed - the stale-file risk.
    stale = drop_file("eoa-validations-20260101T000000Z.json",
                      {"validations": {"WORK_ORDER_9008_fixed_needswork.md":
                                       {"validated": True, "verdict": "Pass",
                                        "at": "2026-01-01T00:00:00", "build": "old"}}})
    fresh = drop_file("eoa-validations-20260903T220802Z.json", {"validations": marks()})
    # mtime deliberately makes the STALE file look newest: the filename stamp must win.
    os.utime(stale, (time.time() + 5, time.time() + 5))
    base12 = seed_wos(wodir)
    log12 = plain_build()
    check("VALIDATIONS_SUBMIT_FILE" in log12 and os.path.basename(fresh) in log12,
          "a plain build (no --submit) names and takes the drop file")
    check(os.path.basename(stale) not in log12.split("VALIDATIONS_SUBMIT_OK")[0],
          "the NEWEST by filename stamp wins, even with a newer mtime on the stale one")
    check("VALIDATIONS_SUBMIT_OK" in log12 and "VALIDATIONS_INGEST_OK" in log12,
          "it says what it did and folded the marks into the record")
    check("BOARD_CLOSE_OK closed 1" in log12 and "BOARD_BOUNCE_OK bounced 4" in log12,
          "the same plain command then closed and bounced - no flag to remember")
    check(not close_assertions(wodir, base12, UNTOUCHED_BOTH)
          and not bounce_assertions(wodir, base12),
          "and the work-order files match the full close+bounce contract")

    print("stage 12b - the SAME file is never ingested twice")
    rec_after = open(rec4, encoding="utf-8").read()
    base12b = seed_wos(wodir)
    log12b = plain_build()
    check("VALIDATIONS_SUBMIT_ALREADY" in log12b,
          "the second plain build says it has already taken that file")
    check("VALIDATIONS_INGEST_OK" not in log12b, "and does NOT re-ingest it")
    check(open(rec4, encoding="utf-8").read() == rec_after,
          "the record is byte-identical after the second build")
    check(os.path.basename(stale) not in log12b,
          "S1: it does not fall back to the OLDER file once the newest is consumed "
          "(a stale drop can never resurrect a mark she has changed)")

    print("stage 12c - no drop file at all: a clean, LOUD no-op")
    empty2 = os.path.join(tmp, "drop-auto-empty")
    os.makedirs(empty2, exist_ok=True)
    base12c = seed_wos(wodir)
    log12c = plain_build({"EOA_SUBMIT_DIR": empty2})
    check("VALIDATIONS_SUBMIT_NONE" in log12c,
          "it says there was nothing to ingest rather than staying silent")
    check("BOARD.html written" in log12c, "and the board still rebuilt")

    print("stage 12d - a MALFORMED drop file reports, does not consume, does not block")
    bad_dir = os.path.join(tmp, "drop-bad")
    os.makedirs(bad_dir, exist_ok=True)
    with open(os.path.join(bad_dir, "eoa-validations-20260904T010101Z.json"), "w",
              encoding="utf-8", newline="\n") as fh:
        fh.write('{"validations": {"WORK_ORDER_9008')      # a half-written download
    log12d = plain_build({"EOA_SUBMIT_DIR": bad_dir})
    check("VALIDATIONS_SUBMIT_UNREADABLE" in log12d or "VALIDATIONS_INGEST_FAIL" in log12d,
          "a malformed drop file is reported, never silently skipped")
    check("BOARD.html written" in log12d,
          "and the board still rebuilt (a half-written download cannot freeze the board)")
    log12d2 = plain_build({"EOA_SUBMIT_DIR": bad_dir})
    check("VALIDATIONS_SUBMIT_UNREADABLE" in log12d2 or "VALIDATIONS_INGEST_FAIL" in log12d2,
          "it was NOT marked consumed, so it complains again on the next build")

    print("stage 12e - the opt-OUT holds (and --check implies it, pinning the gate)")
    log12e = plain_build({"EOA_BOARD_SUBMIT": "0"})
    check("VALIDATIONS_SUBMIT_SKIPPED" in log12e and "VALIDATIONS_SUBMIT_FILE" not in log12e,
          "EOA_BOARD_SUBMIT=0 reads no Downloads folder and says so")
    log12f = plain_build(args=("--no-submit",))
    check("VALIDATIONS_SUBMIT_SKIPPED" in log12f, "--no-submit does the same")
    drop_file("eoa-validations-20260905T010101Z.json", {"validations": marks()})
    log12g = plain_build(args=("--check",))
    check("VALIDATIONS_SUBMIT_SKIPPED" in log12g and "VALIDATIONS_SUBMIT_FILE" not in log12g,
          "--check implies the opt-out, so checkin_gate.ps1 stage 1b can never ingest "
          "from a developer's Downloads folder")

    # -- stage 12f: RED PROOF - four mutations of the auto-ingest, each must bite --
    # In-process: exec a mutated copy of board_build.py as its own module and drive
    # auto_submit() directly. The real file on disk is never touched.
    print("stage 12f - RED proof: four mutations of the auto-ingest, each must be caught")
    src_bb = open(os.path.join(TOOLS, "board_build.py"), encoding="utf-8").read()

    def load_bb(text):
        mod = types.ModuleType("board_build_mutant")
        mod.__file__ = os.path.join(TOOLS, "board_build.py")
        exec(compile(text, mod.__file__, "exec"), mod.__dict__)
        return mod

    def auto_log(mod, drop_dir, record, env_extra=None):
        """Run auto_submit() against a throwaway drop dir + record; return its output."""
        saved_path, saved_env = ov.PATH, dict(os.environ)
        buf = io.StringIO()
        try:
            ov.PATH = record
            os.environ["EOA_SUBMIT_DIR"] = drop_dir
            os.environ["EOA_BOARD_SUBMIT"] = "1"
            os.environ.update(env_extra or {})
            with contextlib.redirect_stdout(buf):
                mod.auto_submit()
        finally:
            ov.PATH = saved_path
            os.environ.clear()
            os.environ.update(saved_env)
        return buf.getvalue()

    MUTANTS = [
        ('    if _consumed_has(entries, digest):                          # S2',
         '    if False:                                                   # S2',
         "S2 - forget that a file was already ingested"),
        ('    path = cands[0]                                             # S1',
         '    path = cands[-1]                                            # S1',
         "S1 - let an OLDER drop file win"),
        ('    if rc != 0:                                                 # S3',
         '    if False:                                                   # S3',
         "S3 - treat an unreadable drop file as ingested"),
        ('    if os.environ.get("EOA_BOARD_SUBMIT", "1") == "0":',
         '    if os.environ.get("EOA_BOARD_SUBMIT", "1") == "never-set-by-anyone":',
         "S4 - ignore the opt-out"),
    ]

    def auto_assertions(mod, tag):
        """The auto-ingest contract, reused verbatim by the RED proof."""
        bad = []
        d = os.path.join(tmp, "red-" + tag)
        r = os.path.join(tmp, "red-" + tag + ".json")
        os.makedirs(d, exist_ok=True)
        for f in glob.glob(os.path.join(d, "*")):
            os.remove(f)
        for f in (r, r + ".consumed.json"):
            if os.path.exists(f):
                os.remove(f)
        old_marks = {"WORK_ORDER_9008_fixed_needswork.md":
                     {"validated": True, "verdict": "Pass", "at": "2026-01-01T00:00:00",
                      "build": "old"}}
        with open(os.path.join(d, "eoa-validations-20260101T000000Z.json"), "w",
                  encoding="utf-8", newline="\n") as fh:
            json.dump({"validations": old_marks}, fh)
        with open(os.path.join(d, "eoa-validations-20260903T220802Z.json"), "w",
                  encoding="utf-8", newline="\n") as fh:
            json.dump({"validations": marks()}, fh)
        l1 = auto_log(mod, d, r)
        if "20260903T220802Z" not in l1:
            bad.append("S1: the newest drop file was not the one taken: " + repr(l1[:120]))
        l2 = auto_log(mod, d, r)
        if "VALIDATIONS_INGEST_OK" in l2:
            bad.append("S2: the same file was ingested a SECOND time")
        # S3: a malformed file reports and is not consumed.
        d2 = d + "-bad"
        r2 = r + ".bad.json"
        os.makedirs(d2, exist_ok=True)
        for f in glob.glob(os.path.join(d2, "*")):
            os.remove(f)
        for f in (r2, r2 + ".consumed.json"):
            if os.path.exists(f):
                os.remove(f)
        with open(os.path.join(d2, "eoa-validations-20260904T010101Z.json"), "w",
                  encoding="utf-8", newline="\n") as fh:
            fh.write('{"validations": {"WORK_ORDER_9008')
        l3 = auto_log(mod, d2, r2)
        if "VALIDATIONS_SUBMIT_OK" in l3:
            bad.append("S3: a malformed drop file was reported as ingested")
        l4 = auto_log(mod, d2, r2)
        if "VALIDATIONS_INGEST_FAIL" not in l4 and "UNREADABLE" not in l4:
            bad.append("S3: the malformed file stopped being reported (it was consumed)")
        # S4: the opt-out.
        l5 = auto_log(mod, d, r, {"EOA_BOARD_SUBMIT": "0"})
        if "VALIDATIONS_SUBMIT_SKIPPED" not in l5:
            bad.append("S4: EOA_BOARD_SUBMIT=0 did not stop the auto-ingest")
        return bad

    real_bb = load_bb(src_bb)
    check(not auto_assertions(real_bb, "clean"),
          "the UNmutated auto-ingest satisfies S1-S4 (the success path, proven first)")
    for find, repl, what in MUTANTS:
        if src_bb.count(find) != 1:
            check(False, "RED proof could not apply mutation (" + what + "): anchor not unique")
            continue
        broke = auto_assertions(load_bb(src_bb.replace(find, repl)),
                                what.split()[0].strip("-"))
        check(bool(broke), "mutation caught (" + what + ") -> "
              + (broke[0] if broke else "NOTHING"))

    print("stage 13 - a delivered revision consumes only its exact prior failure")
    import hashlib
    retest_dir = os.path.join(tmp, "retest")
    os.makedirs(retest_dir)
    rt_name = "WORK_ORDER_9010_retest.md"
    rt_path = os.path.join(retest_dir, rt_name)
    old_mark = {"validated": True, "verdict": "Fail", "note": "still broken",
                "at": "2026-09-10T01:00:00", "build": "test-old"}
    digest = hashlib.sha256(json.dumps(ov.normalize(old_mark), sort_keys=True,
                            separators=(",", ":"), ensure_ascii=True).encode()).hexdigest()
    receipt = {"priorVerdictSha256": digest, "revision": "delivered-test-revision",
               "reason": "new diagnostic available for retest"}
    def reset_retest(value=receipt):
        with open(rt_path, "w", encoding="utf-8") as f:
            f.write("# Retest fixture\n\n**Status:** FIXED - newer implementation\n\n"
                    "**Retest:** " + json.dumps(value) + "\n")
    reset_retest()
    untouched_record = open(rec, "rb").read()
    unchanged = json.dumps(old_mark, sort_keys=True)
    for _ in range(2):
        outcome = bcp.bounce_pass(entries={rt_name: old_mark}, wo_dir=retest_dir)
        check(not outcome["bounced"] and bcp.read_status(rt_path).startswith("FIXED"),
              "exact prior Fail does not reopen explicitly redelivered work")
    check(json.dumps(old_mark, sort_keys=True) == unchanged and open(rec, "rb").read() == untouched_record,
          "redelivery never changes owner evidence")
    for field, value in [("at", "2026-09-11T01:00:00"), ("note", "different symptom"),
                         ("build", "test-new"), ("verdict", "Needs Work")]:
        reset_retest()
        newer = dict(old_mark, **{field: value})
        outcome = bcp.bounce_pass(entries={rt_name: newer}, wo_dir=retest_dir)
        check(bool(outcome["bounced"]), "new finding still reopens: " + field)
    reset_retest({"priorVerdictSha256": "bad", "revision": "x", "reason": "x"})
    check(bool(bcp.bounce_pass(entries={rt_name: old_mark}, wo_dir=retest_dir)["bounced"]),
          "malformed retest receipt cannot suppress a failure")
    reset_retest()
    passed = dict(old_mark, verdict="Pass")
    check(bool(bcp.close_pass(entries={rt_name: passed}, wo_dir=retest_dir)["closed"]),
          "new owner Pass remains able to close retested work")
    reset_retest()
    old_path, old_wo_dir = ov.PATH, bb.WO_DIR
    render_record = os.path.join(retest_dir, "marks.json")
    with open(render_record, "w", encoding="utf-8") as f:
        json.dump({"validations": {rt_name: old_mark}}, f)
    try:
        ov.PATH, bb.WO_DIR = render_record, retest_dir
        retest_rows = bb.parse_wos()
        retest_page = bb.build_html(retest_rows)
    finally:
        ov.PATH, bb.WO_DIR = old_path, old_wo_dir
    check(retest_page.count("Previous Fail; awaiting retest of delivered-test-revision") == 2,
          "prior failure and pending revision render in both board and owner review rows")
    check('id="vprogress">0 / 1 verified' in retest_page,
          "old validated failure does not count as verification of a delivered revision")
    if node:
        start = retest_page.index("/* [ORACLE:counts]")
        end = retest_page.index("/* [/ORACLE:counts] */")
        retest_js = retest_page[start:end]
        same = run_counts(retest_js, {rt_name: old_mark}, {rt_name: old_mark}, [rt_name])
        renewed = dict(old_mark, at="2026-09-11T01:00:00")
        new = run_counts(retest_js, {rt_name: old_mark}, {rt_name: renewed}, [rt_name])
        check(same == {"d": 0, "p": 0}, "browser preserves old finding without calling it current or unsaved")
        check(new == {"d": 0, "p": 1}, "confirming same Fail again creates a pending new finding")
    # A mutant that always ignores receipts must fail the redelivery contract.
    reset_retest()
    parser = bcp.retest_receipt
    try:
        bcp.retest_receipt = lambda text, state: (None, None)
        mutant = bcp.bounce_pass(entries={rt_name: old_mark}, wo_dir=retest_dir)
        check(bool(mutant["bounced"]), "RED proof: removing receipt matching reintroduces repeated bounce")
    finally:
        bcp.retest_receipt = parser

    print(f"record: {rec}")
    if failures:
        print("VALIDATION_ROUNDTRIP_FAIL " + "; ".join(failures))
        return 1
    print("VALIDATION_ROUNDTRIP_OK rebuild preserves owner validations; the board build "
          "closes only Pass+validated FIXED tickets and BOUNCES Fail / Needs Work back to "
          "READY with her note verbatim (empty note included), idempotently, with the "
          "status body preserved on both paths; --submit ingests the newest drop file and "
          "runs both passes in one command, and an ORDINARY build does the same by itself "
          "(newest-stamp wins, never twice, malformed reports and continues, opt-OUT "
          "only); the headline counts ONLY what is saved; guards proven red (read path "
          "+ 4 close-pass + 4 bounce + 1 headline + 4 auto-ingest mutations)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
