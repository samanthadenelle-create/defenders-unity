#!/usr/bin/env python3
"""
board_build.py - generate BOARD.html (the live work-order board) FROM the repo.

The repo's markdown is the single source of truth; this board is a derived view,
so it can never drift the way a hand-mirrored Notion board does. Re-run any time:

    python tools/board_build.py

Reads:  WorkOrders/*.md  (status line, title, RESULT markers)
        CLI_LANES_WO_NUMBERS.md  (next-free mint numbers per seat)
Writes: BOARD.html (repo root) - open in any browser; links open the md files.
"""
import os, re, glob, html, json, time, datetime, sys, subprocess, hashlib

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import owner_validations  # the DURABLE owner-validation record - see that module's header
import board_close_pass    # WO-1355: the owner-Pass -> CLOSED pass, run BELOW inside main()

# Windows consoles default to cp1252, which cannot encode the characters this repo's
# work orders actually use (the U+26D4 no-entry sign, box drawing, arrows). On
# 2026-08-22 that raised UnicodeEncodeError from a print() AFTER BOARD.html had been
# written successfully -- so the board was fine and the run still looked like a hard
# failure, which is the worst of both: a false alarm that also hides real check output
# (BOARD_CHECK_OK / DUPLICATE_WO_NUMBERS / BANNER_OK all print after that point).
# Judge the board by its own check markers, not by this script's exit code.
for _stream in (sys.stdout, sys.stderr):
    try:
        _stream.reconfigure(encoding="utf-8", errors="replace")
    except Exception:
        pass  # older Python, or a stream that is not reconfigurable; prints degrade, not die

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
# EOA_WO_DIR is overridable for the same reason EOA_BOARD_OUT is: the self-checks must
# be able to exercise the real code against a throwaway WorkOrders/ - and since WO-1355 a
# board build REWRITES status lines, so a test pointed at the live tree could destroy the
# very record it is proving safe.
WO_DIR = os.environ.get("EOA_WO_DIR") or os.path.join(ROOT, "WorkOrders")
# Overridable so the validation round-trip self-check can run the REAL entry point
# without overwriting the owner's live board with test data mid-run.
OUT = os.environ.get("EOA_BOARD_OUT") or os.path.join(ROOT, "BOARD.html")

# ── parser SCOPE: what in WorkOrders/ is actually a work order? (WO-937 A) ────
# WorkOrders/ holds two kinds of file, and only one of them owes the board a status:
#
#   1. WORK ORDERS      WORK_ORDER_*.md          - a unit of work; MUST carry **Status:**
#   2. COMPANION DOCS   AUDIT_/BRIEF_/HANDOFF_/NOTES_/QA_CHECKLIST_/DESIGN_/README.md/...
#                                                - references, not work; a status line would be absurd
#
# Before this, EVERY .md in the folder was treated as a work order, so 18 companion docs parsed
# with no **Status:** line and landed in "Unlabeled" - which docs/BOARD.md defines as a DEFECT.
# That made the Unlabeled number a mix of real defects and category errors, so --check could not
# be gated on honestly.
#
# We BUCKET them as "Doc" rather than EXCLUDING them. Excluding is the smaller diff and the wrong
# call: several of these are live references people need to find (WO541_MODEL_API.md,
# DESIGN_CONNECTOR_IS_THE_ONLY_CONTRACT.md, the raid audits, DUNGEON_WO_INDEX.md), and this board is
# the only index of WorkOrders/ anyone actually opens. Dropping them would trade a cosmetic
# miscount for a discoverability hole - a strictly worse bug, and a silent one.
#
# SCOPE IS BY DOCUMENT KIND (filename prefix), NOT BY "does it have a number". That distinction is
# load-bearing: 18 files are named WORK_ORDER_<slug>.md with no number (WORK_ORDER_ad_generator.md,
# WORK_ORDER_second_grom_companion.md, ...). Those ARE work orders - legacy, unnumbered, but real
# work - so a missing status on one is a GENUINE defect and must keep counting. Scoping on the
# number instead would have laundered 5 real defects into the non-defect bucket.
_WO_FILENAME = re.compile(r"^WORK_ORDER_", re.IGNORECASE)

# A SECOND companion shape the prefix rule above cannot see (owner-reported 2026-08-23):
# WORK_ORDER_<num>_<KIND>.md, where KIND is an ALL-CAPS document kind rather than a slug -
# WORK_ORDER_1114_IMPLEMENTATION_PLAN.md, WORK_ORDER_1038_VERIFICATION.md. These carry the
# WORK_ORDER_ prefix, so they parsed as work orders, owed a **Status:** they will never have,
# and sat in Unlabeled - which docs/BOARD.md defines as a DEFECT. They are companions to a
# numbered WO that already carries the status, not units of work.
#
# ⛔ SCOPED TO A NUMBER + AN EXPLICIT KIND WHITELIST, deliberately. A loose "is it ALL-CAPS"
# test would swallow the 18 legacy UNNUMBERED work orders (WORK_ORDER_ad_generator.md and
# friends) whose missing status IS a genuine defect - laundering real defects into the
# non-defect bucket is the exact failure the comment above warns about. Add a kind here only
# when it is genuinely a companion to a numbered WO.
_WO_COMPANION_KIND = re.compile(
    r"^WORK_ORDER_\d+_(IMPLEMENTATION_PLAN|VERIFICATION|AUDIT|INDEX|NOTES|ADDENDUM)",
    re.IGNORECASE)

def is_work_order(basename):
    """True for a real work order (numbered or legacy-unnumbered); False for a companion doc."""
    if _WO_COMPANION_KIND.match(basename): return False
    return bool(_WO_FILENAME.match(basename) or _WO_DASH_FILENAME.match(basename))

OWNER_AREAS = [
    "UI / HUD / Panels", "Village / Buildings", "Combat / Heroes / Gear",
    "Dungeons / Portals", "Economy / Crafting / Resources",
    "Store / Wallet / Monetization", "NPC / Tutorial / Quests",
    "World / Misc Player Experience", "Technical / Build / Backend",
]

def owner_area(row):
    """Put a felt-test ticket near the place the owner will exercise it."""
    text = " ".join((row.get("file", ""), row.get("title", ""), row.get("status", ""))).lower()
    rules = [
        ("Store / Wallet / Monetization", ("store", "wallet", "purchase", "iap", "monet", "receipt", "levelplay", "rewarded ad", "solana")),
        ("Dungeons / Portals", ("dungeon", "portal", "raid", "outpost", "boss room")),
        ("Combat / Heroes / Gear", ("combat", "battle", "hero", "gear", "equip", "weapon", "armor", "army muster", "troop")),
        ("Village / Buildings", ("village", "building", "build mode", "structure", "wall", "pallet", "spire", "town")),
        ("Economy / Crafting / Resources", ("economy", "craft", "resource", "crystal", "food", "wood", "iron", "harvest", "queue")),
        ("NPC / Tutorial / Quests", ("npc", "tutorial", "quest", "onboarding", "dialog", "rumor")),
        ("Technical / Build / Backend", ("backend", "database", "schema", "api", "android", "addressable", "manifest", "telemetry", "softlock", "regression")),
        ("UI / HUD / Panels", ("ui", "hud", "panel", "layout", "button", "modal", "toast", "safe area", "touch", "label")),
    ]
    for area, words in rules:
        if any(word in text for word in words): return area
    return "World / Misc Player Experience"

def apk_identity():
    """Identify the artifact without pretending later board commits are inside it."""
    apk = os.path.join(ROOT, "Builds", "Android", "DefendersOfTheRealm.apk")
    apk_mtime = os.path.getmtime(apk) if os.path.exists(apk) else time.time()
    catalogs = glob.glob(os.path.join(ROOT, "ServerData", "Android", "catalog_*.hash"))
    eligible = [p for p in catalogs if os.path.getmtime(p) <= apk_mtime + 120]
    chosen = max(eligible or catalogs, key=os.path.getmtime) if catalogs else ""
    match = re.search(r"catalog_(.+)\.hash$", os.path.basename(chosen))
    build_id = match.group(1) if match else "unknown-apk"
    try:
        before = datetime.datetime.fromtimestamp(apk_mtime).isoformat()
        source = subprocess.check_output(["git", "rev-list", "-1", f"--before={before}", "HEAD"], cwd=ROOT, text=True, stderr=subprocess.DEVNULL).strip()
    except Exception:
        source = "unknown"
    return build_id, source or "unknown"

# ── status bucketing (keyword priority order) ─────────────────────────────────
def classify_status(status_text, has_result, is_wo=True):
    """Return (bucket, used_substring_fallback) for one status line."""
    # Companion docs are out of the status workflow entirely - never Unlabeled, never a defect.
    if not is_wo: return "Doc", False
    s = (status_text or "").upper()
    # ⛔ THE VERDICT IS THE FIRST WORD. Everything after it is commentary (2026-08-23).
    #
    # Every keyword below except FIXED used to be tested as a bare SUBSTRING, so a status whose
    # verdict was READY/BLOCKED/SPEC was yanked into Done or Closed because the word appeared
    # LATER in the sentence: "the PRE-ACK hole closed" read as Closed; "design complete, can be
    # implemented" read as Done; "UNBLOCKED" read as Blocked. A board sweep found FOURTEEN
    # tickets mis-bucketed this way.
    #
    # ⚠ AND THE ERROR ONLY EVER RAN ONE WAY: toward "finished". Live work rendered as done, so
    # nobody looked at it again. A board that hides open work is worse than no board.
    #
    # Rewording the fourteen statuses cured the instances and not the mechanism - the next author
    # who writes "hole closed" in a Fixed line reopens it. So the leading-word test that FIXED
    # already had (see the block below, added the same day after it bit) is now promoted to ALL
    # of them.
    #
    # THE SUBSTRING PASS IS KEPT AS A FALLBACK, deliberately: many legacy statuses lead with a
    # non-canonical word ("PARTIAL 2026-08-22 - ..."), and a leading-word-only rule would dump
    # every one of them into Unlabeled, trading a silent mis-bucket for a loud false defect.
    # First word wins when it is canonical; otherwise we fall back to the old behaviour.
    lead = s.lstrip().split(None, 1)
    lead = lead[0].strip("*:-—,.") if lead else ""
    # -- "Verify": built, but the OWNER has not yet judged it against the picture -----
    # Owner ruling 2026-09-07 01:10, verbatim: *"fix the board so those tickets dont say done
    # and update the goal to be screenshots proving these match"*.
    #
    # On 2026-09-06 commit 949e848a0 declared "all nine screens match the owner's mockup -
    # twenty-four capture rounds", on the strength of HEADLESS captures a seat read itself.
    # That night the owner walked all nine Manage screens on the device beside
    # docs/mockups/manage/MANAGE_MOCKUP_8_SCREENS.png and NONE of them matched. Twelve
    # board rows were sitting in the finished buckets on that claim - ten leading
    # IMPLEMENTED, two leading FIXED - and Done is the one bucket nobody re-opens.
    #
    # Fixed could not carry these: Fixed means "on her device, awaiting the FELT test". These
    # are a different debt - a PIXEL comparison the owner has not made yet. Both are HER queue:
    # since 2026-09-07 the validation panel lists Verify rows beside Fixed rows and
    # board_close_pass.OWNER_JUDGED closes either on her Pass (and bounces either on her Fail)
    # - her match IS the close for a Verify row, and nothing else is. (Before that day the
    # panel listed Fixed only, so she walked all fifteen on the device and had no box to tick.)
    #
    # !! TESTED ON THE LEADING PHRASE, AND IT MUST STAY IN THIS BLOCK, ABOVE THE SUBSTRING
    # FALLBACK. The status these tickets carry keeps its own history inline - "AWAITING OWNER
    # MATCH - ... (was: IMPLEMENTED - 2026-09-06 ...)" - so the fallback at the bottom of this
    # function would read the word IMPLEMENTED out of the parenthetical and put the row
    # straight back into Done, and `has_result` would do it again for any ticket with a
    # RESULT.md. The exact failure this whole comment block exists to describe.
    #
    # NOT a bare "AWAITING": "AWAITING OWNER RULING" is a different state entirely (it is a
    # contradiction phrase in _STATUS_CONTRADICTIONS below) and must not be swallowed here.
    if s.lstrip().startswith("AWAITING OWNER MATCH"): return "Verify", False
    if lead in ("SUPERSEDED", "CLOSED", "CANCELLED"): return "Closed", False
    if lead == "FIXED": return "Fixed", False
    if lead in ("DONE", "IMPLEMENTED", "COMPLETE"): return "Done", False
    if lead == "BLOCKED": return "Blocked", False
    if lead in ("READY", "SPEC", "DRAFT", "PROPOSAL", "PARKED", "FUTURE", "LATENT"):
        return ("Ready" if lead == "READY" else "Spec"), False

    if "SUPERSEDED" in s or "CLOSED" in s or "CANCELLED" in s: return "Closed", True
    # FIXED = built, gated and on disk, but NOT closed: it is waiting on the owner's felt test
    # (CLAUDE.md 13 - "PO felt-verifies + CLOSES"; headless cannot judge feel). Owner ruling
    # 2026-08-23.
    #
    # THIS TEST MUST STAY ABOVE THE "Done" LINE, and the reason is the `has_result` term in it.
    # A Fixed ticket normally DOES have a RESULT.md - that is what writing up the work produces.
    # If Fixed were tested after, `has_result` would swallow it into Done and the ticket would
    # read as finished the moment the write-up landed, silently skipping the felt test that is
    # the entire point of the bucket. Ordering IS the guarantee here, not a style preference.
    #
    # ⛔ LEADING KEYWORD, NOT A SUBSTRING - and this one genuinely bit on the first build.
    # A plain `"FIXED" in s` matched ELEVEN already-finished tickets whose prose merely contains
    # the word: "DONE - owner-confirmed fixed", "CLOSED - SUPERSEDED ... fixed-layout castle",
    # "PARTIAL - 2 fixed, 1 deferred". Because this test sits above the Done/Closed lines, every
    # one of them was yanked back out of Done into the owner's to-test queue - handing her work
    # she had already closed, which is the exact opposite of what the bucket is for. The status
    # VERDICT is the first word of the line; anything later is commentary.
    if s.lstrip().startswith("FIXED"): return "Fixed", True
    if has_result or "DONE" in s or "IMPLEMENTED" in s or "COMPLETE" in s: return "Done", True
    if "BLOCKED" in s: return "Blocked", True
    if "READY" in s or "IN PROGRESS" in s: return "Ready", True
    # PARKED / FUTURE / LATENT are all "real, understood, deliberately not scheduled" - the same
    # shape as SPEC, and all three were in live use on WO status lines while landing in Unlabeled
    # (owner-reported 2026-08-23: WO-1148 "FUTURE - not scheduled", WO-1140 "LATENT GUARD - NOT AN
    # ACTIVE DEFECT"). Bucketing them is right BECAUSE they are considered decisions, not gaps.
    if ("DRAFT" in s or "SPEC" in s or "NOT STARTED" in s or "PROPOSAL" in s
            or "PARKED" in s or "FUTURE" in s or "LATENT" in s): return "Spec", True
    return "Unlabeled", False

def bucket_of(status_text, has_result, is_wo=True):
    """Compatibility wrapper for callers that need only the bucket."""
    return classify_status(status_text, has_result, is_wo)[0]

def has_landed_partial(status_text):
    """True when a status says that a slice landed while the ticket remains open."""
    status = status_text or ""
    # A badge name followed by its type is documentation, not a claim.  WO-1197's
    # finished handback is the live regression: "the PARTIAL sub-badge" must not
    # make the row partial or contradict its finished verdict.
    return (re.search(r"\bPARTIAL\b(?!\s+(?:SUB-?BADGE|BADGE|KEYWORD|LABEL|WORD)\b)",
                      status, re.IGNORECASE) is not None
            or re.search(r"\bSLICE\b.{0,40}\bLANDED\b", status, re.IGNORECASE) is not None)

_OFFTREE = re.compile(
    r"\bOFFTREE\s+(RETURNED|AWAITING-REVIEW)\s+lane=([^\s,;]+)", re.IGNORECASE)

def parse_offtree(status_text):
    """Return (workflow state, lane) for an explicit off-tree claim, else None."""
    match = _OFFTREE.search(status_text or "")
    if not match:
        return None
    return match.group(1).upper(), match.group(2).rstrip(". )]}")

# Exact markdown is part of the board contract. A near miss remains visible, but is
# named as a defect instead of being silently accepted (WO-1180).
_STATUS_EXACT = re.compile(r"^\*\*Status:\*\*\s*(.+)$", re.MULTILINE)
_STATUS_MALFORMED = re.compile(r"^\*\*Status\b[^\r\n]*$", re.MULTILINE)

def parse_status(text):
    """Return (status text, malformed_marker) without dropping malformed rows.

    Returns (status, malformed, near_miss).

    A malformed marker is REPORTED, never dropped - a row that vanishes is the failure
    WO-1180 exists to prevent.

    TWO TIERS, deliberately, because they are not the same risk:
      * malformed = the row's verdict CAME FROM a near-miss line, because the file has no
        exact `**Status:**` at all. These are the rows one edit from vanishing (the WO-932
        shape). This is the drainable worklist.
      * near_miss  = the file has a good `**Status:**` AND also carries a near-miss line
        somewhere. Cosmetic under the exact pattern - but it is what shadowed WO-414 under
        the OLD loose pattern, which took whichever line came first. Counted, not listed in
        full: 264 of these exist (mostly the legacy `**Status: READY TO IMPLEMENT**`), and a
        264-line wave is a report nobody drains.
    """
    exact = _STATUS_EXACT.search(text)
    bad = [m.group(0).strip() for m in _STATUS_MALFORMED.finditer(text)
           if not _STATUS_EXACT.match(m.group(0))]
    if exact:
        return exact.group(1).strip(), False, bool(bad)
    if not bad:
        return "", False, False
    value = re.sub(r"^\*\*Status\s*:?[\s*]*", "", bad[0], count=1,
                   flags=re.IGNORECASE).strip()
    value = re.sub(r"\*\*\s*$", "", value).strip()
    return value, True, False

# WORK REMAINING != VERIFICATION REMAINING (WO-1181, and this is the whole ship/no-ship line).
# CLAUDE.md 13 reserves CLOSING for the PO, so EVERY correctly handled Fixed row says it is
# awaiting the owner. A lint that flags "awaiting owner felt-verify" flags the entire healthy
# bucket and gets switched off inside a day. Ban the PHRASE, never the WORD.
#
# Each entry is (pattern, close_context_exempt). The close_context_exempt ones are the OPEN
# family: "4 still open" is work remaining, but "owner felt-close still open" is the NORMAL
# state of a healthy row - that false positive is what WO-999 cost on the first HEAD run.
_STATUS_CONTRADICTIONS = (
    (re.compile(r"\bPARTIAL\b(?!\s+(?:SUB-?BADGE|BADGE|KEYWORD|LABEL|WORD)\b)",
                re.IGNORECASE), False),
    (re.compile(r"\bNOT(?:\s+YET)?\s+(?:DONE|BUILT|ADDED)\b", re.IGNORECASE), False),
    (re.compile(r"\bSTILL\s+OPEN\b", re.IGNORECASE), True),
    (re.compile(r"\b(?:IS|REMAINS)\s+OPEN\b", re.IGNORECASE), True),
    (re.compile(r"\bOWNER\s+RULING\s+(?:IS\s+)?OPEN\b", re.IGNORECASE), False),
    (re.compile(r"\bAWAITING\s+OWNER\s+RULING\b", re.IGNORECASE), False),
    (re.compile(r"\bNUMBERS\s+OPEN\b", re.IGNORECASE), False),
    (re.compile(r"\bBULK\s+OF\s+THE\s+TICKET\b", re.IGNORECASE), False),
    # Bare "owed" is mostly healthy verification debt. Name implementation artefacts only.
    (re.compile(r"\b(?:R2\s+)?PUSH\s+OWED\b", re.IGNORECASE), False),
    (re.compile(r"\b(?:CODE|IMPLEMENTATION|MIGRATION|GATE|RESULT\s+FILE)\s+OWED\b",
                re.IGNORECASE), False),
)

# What turns an OPEN-family match into VERIFICATION debt rather than work debt: the words
# immediately in front of it are the owner's close / felt test / sign-off.
_CLOSE_CONTEXT = re.compile(
    r"(?:OWNER|PO)\b[^.;:|]{0,40}?\b(?:FELT[-\s]?(?:TEST|VERIFY|CHECK)?|CLOSE|CLOSES|CLOSURE|"
    r"SIGN[-\s]?OFF|VERIFY|VERIFICATION)\b[^.;:|]{0,25}$", re.IGNORECASE)

# A phrase inside quotation marks is being REPORTED, not asserted - the same reason
# "PRIOR STATUS:" and "(THIS LINE SAID ...)" are already stripped below. WO-1157 reads
#: the slice this line called "not done" IS IN THE TREE - a refutation, not a confession.
_QUOTED_SPAN = re.compile(r"\"[^\"]{0,200}\"|\u201c[^\u201d]{0,200}\u201d")

# ...and an OPEN-family match that is being DENIED is not a claim of work remaining either.
# PROD-007 reads: preservePrefabRotation ... is NOT evidence 6 is open.
_REFUTATION_CONTEXT = re.compile(r"\b(?:NOT|NO)\s+(?:EVIDENCE|PROOF)\b[^.;:|]{0,40}$", re.IGNORECASE)

_FINISHED_LEADS = ("FIXED", "DONE", "IMPLEMENTED", "COMPLETE",
                   "CLOSED", "SUPERSEDED", "CANCELLED")

def status_contradiction(status, bucket, filename=""):
    """Return the first contradictory phrase on a finished verdict, or an empty string."""
    # *.RESULT.md is EXEMPT. CLAUDE.md 15 freezes RESULT files ("never rewrite"), so a finding
    # on one would demand an edit canon forbids and this lint's acceptance could never go
    # green. (They are already skipped upstream in parse_wos; this states the contract.)
    if filename.endswith(".RESULT.md"):
        return ""
    # Lint only an explicit finished verdict, never a legacy row merely rescued into a
    # finished bucket by substring fallback. WO-1180 reports that separate fragility.
    upper = (status or "").lstrip().upper()
    lead = upper.split(None, 1)
    lead = lead[0].strip("*:-,.\u2014") if lead else ""
    if lead not in _FINISHED_LEADS:
        return ""
    # Historical status prose is evidence, not a claim about current work.
    active = re.split(r"\bPRIOR\s+STATUS\s*:", status or "", maxsplit=1,
                      flags=re.IGNORECASE)[0]
    active = re.sub(r"\*?\(THIS LINE SAID .*?\)\*?", "", active, flags=re.IGNORECASE)
    quoted = [m.span() for m in _QUOTED_SPAN.finditer(active)]
    for pattern, close_exempt in _STATUS_CONTRADICTIONS:
        for match in pattern.finditer(active):
            if any(a <= match.start() and match.end() <= b for a, b in quoted):
                continue  # reported, not asserted
            before = active[:match.start()]
            if close_exempt and _CLOSE_CONTEXT.search(before):
                continue  # verification remaining, not work remaining
            if close_exempt and _REFUTATION_CONTEXT.search(before):
                continue  # the line is denying it, not admitting it
            return match.group(0)
    return ""

# Fixed sits directly after Ready because that is the owner's queue: what is built and waiting
# on her, before anything that is merely proposed. It is deliberately NOT next to Done - Done is
# finished, Fixed is owed a test.
# Verify sits directly after Fixed because both are the OWNER's queue and neither is finished:
# Fixed is owed a felt test, Verify is owed a look at a device frame beside a mockup panel.
# Neither belongs anywhere near Done (owner ruling 2026-09-07).
BUCKET_ORDER = ["Ready", "Fixed", "Verify", "Blocked", "Spec", "Unlabeled", "Done", "Closed", "Doc"]
# Owner is RED/GREEN COLOURBLIND: every bucket is labelled in TEXT and the colour is decoration
# only. Fixed is a cyan that also separates from Ready's amber and Done's green in GREYSCALE
# (luminance ~0.62 vs 0.72 and 0.63) - but never let the hue carry the meaning.
# Verify's violet is a decoration like every other hue here: with nine buckets on one strip
# no palette can keep every pair apart in greyscale, so the BUCKET WORD is the carrier and
# always has been. Its luminance (~0.53) does separate it from its two dangerous neighbours,
# Fixed (~0.62) and Done (~0.63) - the two it must never be mistaken for.
BUCKET_COLOR = {"Ready": "#e0b341", "Fixed": "#4fb3c4", "Verify": "#b473d6",
                "Blocked": "#d06060", "Spec": "#7fa8d9",
                "Unlabeled": "#999999", "Done": "#6fae6f", "Closed": "#777777",
                "Doc": "#a98fd0"}

# ── the WO-numbering authority (WO-1112) ──────────────────────────────────────
# THE BANNER IS PROSE, NOT A TABLE. This parser expected markdown table rows
# (`| **main line** | ... | **1112** |`) and CLI_LANES_WO_NUMBERS.md contains ZERO of
# them - the live banner is blockquote prose at line 3:
#     > ## RECONCILED 2026-08-16 (CLI): main line next free = **1112**.
# so both regexes returned None and the board drew "Next mint - CLI: ?, UI seat: ?".
#
# That silence is the actual bug, worse than the wrong regex: a "?" reads as "nobody
# filled this in yet", not as "the numbering authority is UNREADABLE" - and an unreadable
# authority is the exact precondition for the five-collision day the banner itself
# documents (a seat that cannot read the next free number mints on top of another seat).
# So a parse failure is now LOUD in all three places a person could be looking: the board
# renders a visible error strip instead of a question mark, the console prints the miss,
# and --check refuses.
#
# Forms are tried NEWEST-FIRST BY FILE ORDER (the file is prepend-ordered, newest banner on
# top) and superseded headers strike their number through (`~~1000~~`), which no pattern
# matches - so a retired banner cannot win over the live one.
_BANNER_MAIN = [
    r"main line next free\s*=\s*\*\*(\d+)\*\*",                       # live prose form
    r"\|\s*\*\*main line\*\*\s*\|[^|]*\|\s*\*\*(\d+)\*\*",            # legacy table row
]
_BANNER_UI = [
    r"UI[- ]seat bumped\s*\d+\s*->\s*\*?\*?(\d+)",                    # live prose form
    r"UI seat next free\s*=\s*\*\*(\d+)\*\*",
    r"\|\s*\*\*1000[^|]*\*\*\s*\|[^|]*\|\s*\*\*(\d+)\*\*",            # legacy table row
]

def _first_match(text, patterns):
    """Earliest match in FILE ORDER across all patterns (newest banner is at the top)."""
    best = None
    for pat in patterns:
        m = re.search(pat, text)
        if m and (best is None or m.start() < best.start()):
            best = m
    return best.group(1) if best else None

def parse_banner():
    """(main_next, ui_next, errors) from the numbering authority. Never silently None."""
    path = os.path.join(ROOT, "CLI_LANES_WO_NUMBERS.md")
    errors = []
    try:
        text = open(path, encoding="utf-8", errors="replace").read()
    except OSError as e:
        return None, None, ["CLI_LANES_WO_NUMBERS.md unreadable ({}) - the WO-numbering "
                            "authority cannot be parsed at all.".format(e)]
    main = _first_match(text, _BANNER_MAIN)
    ui = _first_match(text, _BANNER_UI)
    if main is None:
        errors.append("could not parse the MAIN-LINE next-free number from CLI_LANES_WO_NUMBERS.md "
                      "(expected e.g. 'main line next free = **1112**')")
    if ui is None:
        errors.append("could not parse the UI-SEAT next-free number from CLI_LANES_WO_NUMBERS.md "
                      "(expected e.g. 'UI-seat bumped 1030 -> 1031')")
    return main, ui, errors

# ── CREATED date, never modified date (WO-940) ────────────────────────────────
# The board's date column used to be os.path.getmtime - LAST MODIFIED. Any edit (a status
# fix, a banner sweep) reset a ticket's apparent age, inverting the exact signal the owner
# wants ("opened within"). The date shown is the CREATED date, resolved in priority order:
#   1. **Minted:** YYYY-MM-DD parsed from the WO body  - authored by a human, most trustworthy
#   2. git first-add date of the file                  - ONE git call for all ~950 files
#   3. mtime                                           - last resort, visibly marked '~' (estimate)
# Age is DERIVED at generation time, never typed into the WO files (a stored age is stale the
# next morning - the disease this exists to cure).
_MINTED = re.compile(r"^\*\*Minted:?\*\*:?[^\n]*?(\d{4}-\d{2}-\d{2})", re.MULTILINE)

def git_added_dates():
    """Map basename -> YYYY-MM-DD of the commit that first ADDED the file.
    ONE git call over WorkOrders/ (a per-file loop over ~950 files would kill the
    2-second build). --no-renames so a file moved into WorkOrders/ still registers
    an Add there (rename detection would filter it to R and lose the date)."""
    dates = {}
    try:
        out = subprocess.run(
            ["git", "log", "--reverse", "--no-renames", "--diff-filter=A",
             "--date=short", "--format=%x01%ad", "--name-only", "--", "WorkOrders/"],
            cwd=ROOT, capture_output=True, text=True, timeout=120)
        cur = None
        for line in out.stdout.splitlines():
            if line.startswith("\x01"):
                cur = line[1:].strip()
            elif line.strip() and cur:
                # --reverse walks oldest-first; setdefault keeps the FIRST add.
                dates.setdefault(os.path.basename(line.strip()), cur)
    except Exception:
        pass  # no git / not a repo: every row falls back to the marked mtime estimate
    return dates

# ── WO-1080: CAPTURE PROVENANCE, and why a DATE cannot do this job ────────────
# Four layout tickets (WO-1075/1076/1077/1078) were minted from ONE aged capture log,
# `Builds/wo1060-capture.log`, and described a tree that had already moved on. WO-1076 was
# reopened against a panel fixed three days earlier (a2162f17d) and cost a seat a morning.
#
# ⛔ A CAPTURE LOG'S FILE DATE IS NOT EVIDENCE OF THE TREE IT MEASURED. That log's mtime is
# 2026-08-23 and its in-log licensing stamp is 2026-08-23T17:39:59Z; the fix it fails to
# contain landed 2026-08-21. The log is NEWER than the commit it does not have — so any
# mtime comparison is defeated by the exact case that motivated it. Only the COMMIT identifies
# the tree, which is why `UICaptureLaunch.RunCaptureHeadless` now stamps
# `UI_CAPTURE_HEAD <sha> <branch> dirty=<bool>` into every log it writes.
#
# A layout/touch ticket therefore carries, in its header block:
#
#     **Capture:** `Builds/<log>.log` @ `<sha>` — targets `Assets/.../<File>.cs`
#
# and this parser + the check below turn "is that citation still true?" into arithmetic:
# if the newest commit touching the target is NOT reachable from the cited sha, the capture
# predates the target's current state and the ticket is STALE-CAPTURE.
#
# REPORT-ONLY, exactly like DUPLICATE_WO_NUMBERS: a collision is its own finding and the board
# flags it rather than silently repairing it. A WO with no `**Capture:**` line is untouched —
# this binds layout/touch tickets, not the whole board.
_CAPTURE = re.compile(
    r"^\*\*Capture:?\*\*:?\s*`([^`]+)`\s*@\s*`([0-9a-fA-F]{7,40})`(.*)$",
    re.MULTILINE)

def parse_capture(text):
    """(log, sha, [target paths]) or (None, None, []) when the WO cites no capture."""
    m = _CAPTURE.search(text)
    if not m:
        return None, None, []
    log, sha, tail = m.group(1).strip(), m.group(2).strip().lower(), m.group(3)
    # Every backticked path on the rest of the line is a target. Multiple targets are
    # allowed on purpose: one ticket may legitimately name a panel and its VM.
    targets = [t.strip() for t in re.findall(r"`([^`]+)`", tail) if t.strip()]
    return log, sha, targets

def _git(args, timeout=30):
    """(returncode, stdout) — never raises. Returns (None, '') when git is unavailable."""
    try:
        out = subprocess.run(["git"] + args, cwd=ROOT, capture_output=True,
                             text=True, timeout=timeout)
        return out.returncode, (out.stdout or "").strip()
    except Exception:
        return None, ""

def check_capture_staleness(rows):
    """Annotate rows carrying a **Capture:** line with capture_verdict / capture_detail.

    Only rows that cite a capture cost a git call, so the ~2 s build is unaffected while
    the count of such tickets is small. Verdicts:
      FRESH   — the newest commit touching every target is reachable from the cited sha
      STALE   — it is NOT: the capture measured a tree that predates the target's state
      UNKNOWN — the sha, the target, or git itself could not be resolved (never silently FRESH)
    """
    cited = [r for r in rows if r.get("capture_sha")]
    if not cited:
        return []
    rc, _ = _git(["rev-parse", "--git-dir"])
    if rc != 0:
        for r in cited:
            r["capture_verdict"] = "UNKNOWN"
            r["capture_detail"] = "git unavailable, so the citation could not be checked"
        return cited

    newest_cache = {}
    for r in cited:
        sha = r["capture_sha"]
        rc, resolved = _git(["rev-parse", "--verify", "--quiet", sha + "^{commit}"])
        if rc != 0 or not resolved:
            r["capture_verdict"] = "UNKNOWN"
            r["capture_detail"] = "cited sha %s is not a commit in this clone" % sha
            continue
        if not r.get("capture_targets"):
            r["capture_verdict"] = "UNKNOWN"
            r["capture_detail"] = ("the **Capture:** line names no target file, so there is "
                                   "nothing to date the citation against")
            continue

        stale_for = []
        unknown_for = []
        for target in r["capture_targets"]:
            if target not in newest_cache:
                newest_cache[target] = _git(
                    ["log", "-1", "--format=%H", "--", target])[1]
            newest = newest_cache[target]
            if not newest:
                unknown_for.append(target + " (no commit touches it)")
                continue
            # Reachability, not dates: a commit made on another branch at an earlier clock
            # time can still be absent from the cited tree, and a later clock time can still
            # be an ancestor. `--is-ancestor` answers the only question that matters.
            anc, _ = _git(["merge-base", "--is-ancestor", newest, resolved])
            if anc != 0:
                stale_for.append("%s newest=%s" % (target, newest[:12]))

        if stale_for:
            r["capture_verdict"] = "STALE"
            r["capture_detail"] = ("cited capture %s predates: " % sha[:12]) + "; ".join(stale_for)
        elif unknown_for:
            r["capture_verdict"] = "UNKNOWN"
            r["capture_detail"] = "; ".join(unknown_for)
        else:
            r["capture_verdict"] = "FRESH"
            r["capture_detail"] = "cited capture %s contains every target's newest commit" % sha[:12]
    return cited

# ── BOARD_DRIFT: a READY ticket whose own named files have already moved (2026-09-06) ──
#
# On 2026-09-06 NINE tickets were found whose work had LANDED - the files they name were
# committed days earlier - while their **Status:** line still read READY. The board is
# derived from those lines (CLAUDE.md §2), so nine finished tickets sat in the owner's
# to-do column and one of them was nearly re-dispatched to a second lane. Nothing in the
# repo could see it: `--check` asks whether a status line is WELL-FORMED, never whether it
# is still TRUE.
#
# This turns "is this READY ticket still really open?" into arithmetic: if a path the
# ticket names under Assets/, api/ or tools/ has a commit NEWER than the ticket's mint
# date, someone has been in that file since the ticket was written.
#
# ⚠ WARNING, NEVER A FAIL, and that is deliberate rather than timid. Drift is EVIDENCE,
# not a verdict: a READY ticket legitimately names a file another lane touched for an
# unrelated reason, and a ticket that cites a whole subsystem would fail the board every
# day. It reports exactly like DUPLICATE_WO_NUMBERS and STALE_CAPTURE above - named, never
# repaired, and outside the --check exit contract.
#
# ONE git call, not one per path. Measured 2026-09-06: 46 READY rows, and a per-path loop
# over their cited paths would be hundreds of subprocess spawns in a build that runs at
# session boot, in the check-in gate and in a hook (this single-pass form kept the whole
# board build at 2 s) - so this walks the log ONCE, newest-first, windowed to the OLDEST mint
# date in the set. First sighting of a path in that walk IS its newest commit; a path that
# never appears has no commit inside the window and therefore cannot have drifted. Same
# single-pass shape as git_added_dates() above, for the same reason.
#
# The path regex requires an extension so a bare directory ("under Assets/_Modules/HUD")
# is not treated as a file, and strips the `File.cs:266-274` line suffix this repo writes
# everywhere - an unstripped one resolves to no commit and would read as a silent miss.
_DRIFT_PATH = re.compile(
    r"(?<![A-Za-z0-9_/\\])(?:Assets|api|tools)[/\\][A-Za-z0-9_.\-/\\]*[A-Za-z0-9_\-][.][A-Za-z0-9]{1,6}")
_DRIFT_DATE = re.compile(r"\b(\d{4}-\d{2}-\d{2})\b")

def _drift_paths(text):
    """Repo-relative paths a ticket names under Assets/ api/ tools/, de-duped, order kept."""
    seen, out = set(), []
    for raw in _DRIFT_PATH.findall(text):
        p = raw.replace("\\", "/").rstrip(".,;:)*`'\"")
        if p and p not in seen:
            seen.add(p)
            out.append(p)
    return out

def ready_drift(rows):
    """[(row, path, sha, commit_date, mint_date)] for READY tickets whose files moved."""
    ready = [r for r in rows if r.get("is_wo") and r["bucket"] == "Ready"]
    if not ready:
        return []
    targets = []
    for r in ready:
        # The date IN the status line wins when there is one (it is what the author wrote
        # about THIS verdict); otherwise the row's resolved mint date - **Minted:** header,
        # then the git add, then the file mtime.
        m = _DRIFT_DATE.search(r.get("status", "") or "")
        mint = m.group(1) if m else r.get("created")
        if not mint:
            continue
        try:
            text = open(os.path.join(WO_DIR, r["file"]), encoding="utf-8",
                        errors="replace").read(60000)
        except OSError:
            continue
        paths = _drift_paths(text)
        if paths:
            targets.append((r, mint, paths))
    if not targets:
        return []

    since = min(t[1] for t in targets)
    rc, out = _git(["log", "--since=" + since, "--no-renames", "--format=%x01%h %cs",
                    "--name-only", "--", "Assets", "api", "tools"], timeout=180)
    if rc != 0:
        return []   # no git / not a repo: silence beats a fabricated verdict
    newest = {}
    cur = None
    for line in out.splitlines():
        if line.startswith("\x01"):
            parts = line[1:].strip().split(None, 1)
            cur = (parts[0], parts[1]) if len(parts) == 2 else None
        elif line.strip() and cur:
            newest.setdefault(line.strip(), cur)   # newest-first walk: first sighting wins

    hits = []
    for r, mint, paths in targets:
        for p in paths:
            if p in newest:
                sha, cdate = newest[p]
                if cdate > mint:   # day granularity, strictly after the mint
                    hits.append((r, p, sha, cdate, mint))
    return hits

def resolve_created(text, base, mtime, added_dates):
    """(YYYY-MM-DD, is_estimate) per the priority order above."""
    m = _MINTED.search(text)
    if m:
        return m.group(1), False
    if base in added_dates:
        return added_dates[base], False
    return datetime.datetime.fromtimestamp(mtime).strftime("%Y-%m-%d"), True

# ── the RECURSIVE "does this work order have a status line at all?" sweep (WO-1492) ──────
#
# The row parser above globs `WorkOrders/*.md` ONLY - one flat level - and decides work-order
# kind from the `WORK_ORDER_` prefix. Both assumptions failed at once on the ManageRedesign
# program: seventeen tickets live in `WorkOrders/ManageRedesign/` and are named `WO-2001_*.md`,
# so they were invisible to the board TWICE OVER (wrong directory, wrong filename shape) and
# thirteen of them carried no `**Status:**` line at all. The largest lane in flight rendered as
# nothing, and no marker said so - the exact failure docs/BOARD.md exists to prevent.
#
# This sweep is deliberately NARROW: it answers only "is a **Status:** line PRESENT", never what
# the status means. Bucketing stays with classify_status on the rows the page renders, so this
# cannot change how a single existing row is classified - it can only add a named defect.
#
# Top-level files the row parser already owns are skipped, so a missing status is reported once
# (as Unlabeled) rather than twice under two different markers.
_WO_DASH_FILENAME = re.compile(r"^WO-\d+[_\-]", re.IGNORECASE)

def is_work_order_file(basename):
    """Filename test for the recursive sweep: WORK_ORDER_*.md or WO-<n>_*.md, never a RESULT."""
    if not basename.lower().endswith(".md"): return False
    if basename.endswith(".RESULT.md"): return False
    if _WO_COMPANION_KIND.match(basename): return False
    return bool(_WO_DASH_FILENAME.match(basename)) or bool(_WO_FILENAME.match(basename))

def missing_status_sweep():
    """Every work-order file under WorkOrders/ (any depth) with no exact **Status:** line."""
    out = []
    wo_root = os.path.abspath(WO_DIR)
    for dirpath, _dirnames, filenames in os.walk(WO_DIR):
        flat = os.path.abspath(dirpath) == wo_root
        for name in sorted(filenames):
            if not is_work_order_file(name): continue
            # Already parsed as a row above; a missing status there lands in Unlabeled.
            if flat and is_work_order(name): continue
            path = os.path.join(dirpath, name)
            try:
                text = open(path, encoding="utf-8", errors="replace").read(20000)
            except OSError:
                continue
            if not _STATUS_EXACT.search(text):
                # relpath raises across drives on Windows (EOA_WO_DIR can point at a temp dir
                # on another volume, which is exactly how the self-check runs). A reporting
                # path must never be the thing that crashes the board.
                try:
                    rel = os.path.relpath(path, ROOT)
                except ValueError:
                    rel = path
                out.append(rel.replace("\\", "/"))
    return sorted(out)

def parse_wos():
    rows = []
    added_dates = git_added_dates()
    today = datetime.date.today()
    # Glob both top-level and subdirectories for RESULT files to enable pairing with subdirectory WOs
    results = {os.path.basename(p).replace(".RESULT.md", ".md")
               for p in glob.glob(os.path.join(WO_DIR, "**/*.RESULT.md"), recursive=True)}
    for path in sorted(glob.glob(os.path.join(WO_DIR, "**/*.md"), recursive=True)):
        base = os.path.basename(path)
        if base.endswith(".RESULT.md"):
            continue
        try:
            text = open(path, encoding="utf-8", errors="replace").read(20000)
        except OSError:
            continue
        # ── TWO NUMBER NAMESPACES (owner ruling 2026-08-17, "PROD WO 001") ──────────
        # Echoes of Elarion is LIVE on the Solana dApp Store. Numbering restarts at
        # PROD-001 to draw a hard line at that date: everything before it was
        # pre-launch, everything after it ships to real players.
        #
        # LEGACY WO-#### IS FROZEN AND NEVER RENAMED. 1,154 files carry those numbers,
        # and so do code comments, commit messages, regression-suite headers and every
        # canon doc. Renaming would break far more than it tidies — the point is a
        # clean START, not a rewritten history.
        #
        # ⚠ WHY THIS PARSE EXISTS AT ALL: `is_work_order` matches any "WORK_ORDER_"
        # prefix, so a PROD file was already RECOGNISED — but the old
        # `WORK_ORDER_(\d+)` could not read "PROD-001", so every PROD ticket would have
        # silently joined the 18 legacy UNNUMBERED files: no sort order and, worse, no
        # duplicate detection. The collision guard would have been off for exactly the
        # tickets that matter most, and nothing would have said so.
        #
        # `prod` is carried alongside `num` (never merged into it) so PROD-001 can
        # never collide with legacy WO-1 in any downstream count or dedup.
        # MON is a LANE TAG, not a number series (owner ruling 2026-08-22: "add as a
        # tag WO XX - MON"). It rides on an ORDINARY banner-minted WO number, so there
        # stays exactly ONE numbering authority and the duplicate guard keeps working on
        # it - the thing that would have been silently off for a private series. The tag
        # only sets the DISPLAY label and the priority sort.
        # Matches WORK_ORDER_<num>_MON_<slug> and WORK_ORDER_<num>_MON-<slug>.
        #
        # UI-### is a THIRD sanctioned series, on exactly the PROD- footing (lead ruling
        # 2026-08-24). Same failure mode as PROD before it was taught: `is_work_order`
        # already RECOGNISED WORK_ORDER_UI-001_*.md, but no pattern could read its number,
        # so two live tickets rendered as the unassignable "WO-?" - including UI-001, which
        # is on the owner's active felt-test route. `ui` is carried alongside `num` and
        # `prod`, never merged, so UI-001 can never collide with legacy WO-1 or PROD-001.
        mon_tag = bool(re.match(r"WORK_ORDER_\d+[_-]MON[_-]", base, re.IGNORECASE))
        prod_m  = re.match(r"WORK_ORDER_PROD-(\d+)", base, re.IGNORECASE)
        ui_m    = re.match(r"WORK_ORDER_UI-(\d+)", base, re.IGNORECASE)
        num_m   = re.match(r"WORK_ORDER_(\d+)", base)
        wo_dash_m = re.match(r"WO-(\d+)[_\-]", base, re.IGNORECASE)  # ManageRedesign/WO-2001_*.md format
        mon     = None
        prod    = int(prod_m.group(1)) if prod_m else None
        ui      = int(ui_m.group(1)) if ui_m else None
        num     = int(wo_dash_m.group(1)) if wo_dash_m else (int(num_m.group(1)) if (num_m and not prod_m and not ui_m) else None)
        title_m = re.search(r"^#\s+(.+)$", text, re.MULTILINE)
        title = title_m.group(1).strip() if title_m else base
        title = re.sub(r"[*`]", "", title)
        status, malformed_status, near_miss_status = parse_status(text)
        status = re.sub(r"[*`]", "", status).strip()
        has_result = base in results
        is_wo = is_work_order(base)
        mtime = os.path.getmtime(path)
        created, created_est = resolve_created(text, base, mtime, added_dates)
        try:
            age_days = (today - datetime.date(*map(int, created.split("-")))).days
        except ValueError:
            age_days = 0
        bucket, fallback_bucketed = classify_status(status, has_result, is_wo)
        cap_log, cap_sha, cap_targets = parse_capture(text)   # WO-1080; (None, None, []) if absent
        rows.append({
            "retest_text": "\n".join(re.findall(r"(?m)^\*\*Retest:\*\*.*$", text)),
            "capture_log": cap_log, "capture_sha": cap_sha, "capture_targets": cap_targets,
            "num": num, "prod": prod, "ui": ui, "mon_tag": mon_tag, "file": base, "title": title, "status": status,
            "bucket": bucket, "result": has_result, "malformed_status": malformed_status,
            "near_miss_status": near_miss_status,
            "landed_partial": has_landed_partial(status),
            "offtree": parse_offtree(status),
            "fallback_bucketed": fallback_bucketed,
            "is_wo": is_wo, "mtime": mtime,
            "created": created, "created_est": created_est, "age_days": max(0, age_days),
        })
    return rows

def build_html(rows):
    main_next, ui_next, banner_errors = parse_banner()
    counts = {b: 0 for b in BUCKET_ORDER}
    for r in rows: counts[r["bucket"]] += 1
    stamp = datetime.datetime.now().strftime("%Y-%m-%d %H:%M")
    apk_build, apk_source = apk_identity()

    # ── the DURABLE validation record (proof/owner-validations.json) ─────────────
    # READ ONLY. A board rebuild must never be able to lose a sign-off, so this
    # function has no write path to the record at all - the only writer is
    # `--ingest`, which is an explicit, separate act.
    #
    # An unreadable record raises rather than reading as empty: rendering "0 / 244
    # verified" over a corrupt file would look completely normal and quietly invite
    # the owner to re-do work she had already done.
    disk_validation = owner_validations.entries()
    retest_previous = {}
    retest_notices = {}
    for r in rows:
        st = disk_validation.get(r["file"], {})
        receipt, error = board_close_pass.retest_receipt(r.get("retest_text", ""), st)
        if error:
            retest_notices[r["file"]] = error + "; owner finding remains actionable"
        elif receipt and r["bucket"] in board_close_pass.OWNER_JUDGED:
            retest_previous[r["file"]] = owner_validations.normalize(st)
            retest_notices[r["file"]] = ("Previous " + st["verdict"] +
                "; awaiting retest of " + receipt["revision"] + ". " + receipt["reason"])

    def current_validated(r):
        return bool(disk_validation.get(r["file"], {}).get("validated")) and r["file"] not in retest_previous

    def row_html(r):
        # PROD tickets render as PROD-001 (post-launch, zero-padded so they sort as text
        # too); legacy pre-launch tickets keep WO-####. The two are deliberately visually
        # distinct on the board — at a glance you can see whether a row predates going live.
        if r.get("prod") is not None:
            num = f"PROD-{r['prod']:03d}"
        elif r.get("ui") is not None:
            num = f"UI-{r['ui']:03d}"
        elif r["num"] is not None:
            num = f"WO-{r['num']}"
        else:
            num = "DOC" if not r["is_wo"] else "WO-?"
        # LANE TAG appended to the number, so the board reads "WO-1146 - MON" exactly as
        # the owner asked. It is a SUFFIX on the real number, never a replacement - the
        # banner stays the one numbering authority and the duplicate guard keeps working.
        if r.get("mon_tag"):
            num += " - MON"
        # CREATED date + age in days (WO-940) - never mtime. '~' marks an mtime-estimated
        # date so nobody mistakes a guess for a creation date.
        est = "~" if r["created_est"] else ""
        days = r["age_days"]
        # Older than 7 days (SUNDAY_HOUSEKEEPING threshold): word/symbol PLUS colour -
        # the owner is red/green colourblind, a hue alone is invisible to her.
        old = ' <span class="oldm">7d+</span>' if days > 7 else ""
        color = BUCKET_COLOR[r["bucket"]]
        partial = (' <span class="partial">PARTIAL</span>'
                   if r.get("landed_partial") and r["bucket"] == "Ready" else "")
        offtree_data = r.get("offtree")
        offtree = (f' <span class="offtree">OFF-TREE · {html.escape(offtree_data[0])}'
                   f' · {html.escape(offtree_data[1])}</span>'
                   if offtree_data and r["bucket"] == "Ready" else "")
        res = ' <span class="res">RESULT</span>' if r["result"] else ""
        if r["file"] in retest_notices:
            res += ' <span class="oldm">' + html.escape(retest_notices[r["file"]]) + '</span>'
        # WO-1080. Rendered ONLY on a row that cites a capture, so every other row's HTML is
        # byte-identical to before. Reuses the existing word-plus-colour badge class: the
        # owner is red/green colourblind, so the WORD carries the finding, never the hue.
        cap = r.get("capture_verdict")
        if cap == "STALE":
            res += ' <span class="oldm">STALE-CAPTURE</span>'
        elif cap == "UNKNOWN":
            res += ' <span class="oldm">CAPTURE-UNVERIFIED</span>'
        # filename is in the search text so companion docs stay FINDABLE by name - that
        # discoverability is the whole reason they are bucketed rather than dropped (WO-937 A).
        search = " ".join((num, r["file"], r["title"], r["status"])).lower()
        return (f'<tr class="row" data-bucket="{r["bucket"]}" data-age-days="{days}" '
                f'data-text="{html.escape(search)}">'
                f'<td class="num"><a href="WorkOrders/{html.escape(r["file"])}">{num}</a></td>'
                f'<td class="title">{html.escape(r["title"][:110])}{res}</td>'
                f'<td><span class="badge" style="border-color:{color};color:{color}">'
                f'{r["bucket"]}</span>{partial}{offtree}</td>'
                f'<td class="status">{html.escape(r["status"][:80])}</td>'
                f'<td class="age">{est}{r["created"]} &middot; {days}d{old}</td></tr>')

    # Within a bucket: PROD tickets FIRST (newest first), then legacy WO (newest first).
    # Post-launch work outranks pre-launch work on sight — that is the whole point of the
    # namespace split, and a board that interleaved them would bury PROD-001 among 1,154
    # legacy rows.
    rows_sorted = sorted(rows, key=lambda r: (
        BUCKET_ORDER.index(r["bucket"]),
        0 if r.get("mon_tag") else 1,              # MON is the priority lane (owner 2026-08-22)
        0 if r.get("prod") is not None else 1,
        -(r.get("prod") or 0),
        0 if r.get("ui") is not None else 1,      # UI-### ranks after PROD, ahead of legacy
        -(r.get("ui") or 0),
        -(r["num"] or 0)))
    body_rows = "\n".join(row_html(r) for r in rows_sorted)
    # The owner validation panel lists HER QUEUE: Fixed (owed the felt test) AND Verify (owed
    # her look at a device frame beside its mockup panel - ruling 29). Until 2026-09-07 this
    # read Fixed only, so the fifteen Verify rows had no checkbox: she walked them on the device
    # and could not mark one. board_close_pass.OWNER_JUDGED is the same pair - one authority.
    fixed_rows = [r for r in rows_sorted if r["bucket"] in ("Fixed", "Verify") and r["is_wo"]]
    validation_groups = []
    disk_done = 0
    for area in OWNER_AREAS:
        items = [r for r in fixed_rows if owner_area(r) == area]
        if not items: continue
        # Validated tickets sink to the BOTTOM of their group. The owner is red/green
        # colourblind, so a validated row is marked THREE ways that survive greyscale:
        # the word "VALIDATED", the button label flipping to "Validated", and this
        # POSITION. Never a hue swap alone. Stable sort, so untested order is unchanged.
        items.sort(key=lambda r: 1 if current_validated(r) else 0)
        item_html = []
        for r in items:
            if r.get("prod") is not None: key = f"PROD-{r['prod']:03d}"
            elif r.get("ui") is not None: key = f"UI-{r['ui']:03d}"
            elif r.get("num") is not None: key = f"WO-{r['num']}"
            else: key = r["file"]
            # Filename is the durable unique state key. This repo has historical duplicate WO
            # numbers, so using the friendly label here would make two unrelated rows share a
            # felt-test result in the record.
            st = disk_validation.get(r["file"], {})
            vd = st.get("verdict") or ""
            done = current_validated(r)
            if done: disk_done += 1
            # SERVER-SIDE RENDER of the disk state. This is the whole point of the fix:
            # the sign-off is visible on a cold load, in another browser, on the CLI's
            # screen, with JavaScript or site data blocked entirely.
            # The badge span is ALWAYS emitted and shown/hidden by the .isvalidated
            # class, so JS toggling a mark needs no DOM surgery and a no-JS load still
            # shows the word.
            badge = ' <span class="vok">[X] VALIDATED</span>'
            if r["file"] in retest_notices:
                badge += ' <span class="vretest">' + html.escape(retest_notices[r["file"]]) + '</span>'
            opts = "".join(
                f'<option{" selected" if vd == v else ""}>{v}</option>'
                for v in ("Pass", "Fail", "Needs Work"))
            item_html.append(
                f'<div class="vitem{" isvalidated" if done else ""}" '
                f'data-ticket="{html.escape(r["file"])}" '
                f'data-disk="{html.escape(json.dumps(st, sort_keys=True))}">'
                f'<button class="validated" type="button">{"Validated" if done else "Validate"}</button>'
                f'<a href="WorkOrders/{html.escape(r["file"])}">{html.escape(key)}</a>'
                f'<span class="vtitle">{html.escape(r["title"][:120])}{badge}</span>'
                f'<select class="verdict" aria-label="felt-test result">'
                f'<option value=""{"" if vd else " selected"}>Untested</option>{opts}</select>'
                f'<input class="vnote" aria-label="validation notes" '
                f'placeholder="Optional device notes" value="{html.escape(st.get("note") or "")}"></div>')
        # COLLAPSED BY DEFAULT (owner request 2026-08-27: "start with the validation
        # sections minimized. then i can expand as ready to test"). The summary still
        # carries the "<area> 0 / N" count, so a collapsed board still shows at a glance
        # where the felt-testing stands without opening anything.
        #
        # data-area is the localStorage key for the open/closed choice. Whichever areas
        # she expands STAY expanded across rebuilds - the board is regenerated constantly,
        # and re-collapsing an area she is actively testing on every regeneration would
        # make the feature worse than leaving them all open.
        # The count is rendered FROM DISK, not seeded at 0 and fixed up by script: a
        # collapsed board must show where felt-testing stands even with JS disabled.
        gdone = sum(1 for r in items if current_validated(r))
        validation_groups.append(f'<details class="vgroup" data-area="{html.escape(area)}"><summary>{html.escape(area)} '
            f'<span class="gcount">{gdone} / {len(items)}</span></summary>' + "".join(item_html) + '</details>')
    validation_html = "".join(validation_groups)
    filters = "".join(
        f'<button class="fbtn" data-f="{b}" style="border-color:{BUCKET_COLOR[b]}">'
        f'{b} <span class="cnt">{counts[b]}</span></button>' for b in BUCKET_ORDER)
    # LANE CHIP (owner 2026-08-22): MON is a dedicated, prioritised lane, so it gets its
    # own filter beside the buckets. It filters on the row TEXT containing "- mon", which
    # is the same suffix row_html writes into the number cell - one source, so the chip
    # and the label can never disagree. It composes with the bucket chips and the search
    # box via the existing AND, and is deliberately NOT a bucket: a MON ticket is still
    # Ready or Done like any other.
    mon_count = sum(1 for r in rows if r.get("mon_tag"))
    lane_filters = (f'<button class="lbtn" data-l="- mon" style="border-color:#d98f2b">'
                    f'MON <span class="cnt">{mon_count}</span></button>') if mon_count else ""

    # "Opened within" filter (WO-940): by CREATED age in days, composing with the
    # bucket chips and the search box (AND), never replacing them.
    def _within(d): return sum(1 for r in rows if r["age_days"] <= d)
    age_filters = (
        '<span class="agef">opened within: '
        + "".join(f'<button class="abtn" data-a="{d}">{d}d '
                  f'<span class="cnt">{_within(d)}</span></button>' for d in (7, 30, 90))
        + f'<button class="abtn on" data-a="all">all <span class="cnt">{len(rows)}</span></button>'
        '</span>')

    # A parse miss is NEVER a quiet "?" (WO-1112). The mint numbers are the collision guard;
    # if the board cannot read them it must SAY so, in the place the reader is already looking.
    if banner_errors:
        mint_html = ('<span class="bannererr">MINT NUMBERS UNREADABLE &mdash; '
                     + html.escape("; ".join(banner_errors))
                     + ' &mdash; do NOT mint from this board; read CLI_LANES_WO_NUMBERS.md directly.</span>')
    else:
        mint_html = f'Next mint &mdash; CLI: <b>{main_next}</b>, UI seat: <b>{ui_next}</b>'

    canon_links = "".join(
        f'<a href="{p}">{n}</a>' for n, p in [
            ("Canon loader", "SESSION_CANON_LOADER.md"), ("Handover", "docs/HANDOVER.md"),
            ("Master catalog", "docs/MASTER_CATALOG.md"), ("Pipeline state", "PIPELINE_STATE.md"),
            ("WO numbers (authority)", "CLI_LANES_WO_NUMBERS.md"), ("Key facts", "KEY_FACTS.md"),
            ("Docs index", "docs/README.md"), ("Project index", "PROJECT_INDEX.md"),
        ])

    return f"""<!DOCTYPE html><html><head><meta charset="utf-8">
<title>EoA Board - {stamp}</title>
<style>
 body{{background:#14151a;color:#ddd;font:14px/1.5 'Segoe UI',sans-serif;margin:0;padding:24px}}
 h1{{color:#e0b341;font-size:22px;margin:0 0 4px}}
 .sub{{color:#888;margin-bottom:14px}} .sub b{{color:#bbb}}
 .canon a{{color:#7fa8d9;margin-right:14px;text-decoration:none}} .canon{{margin-bottom:16px}}
 #q{{background:#1e2027;border:1px solid #333;color:#ddd;padding:8px 12px;width:340px;border-radius:6px}}
 .lbtn{{background:#1e2027;border:1px solid #d98f2b;color:#e8c07a;padding:6px 12px;margin-left:10px;border-radius:14px;cursor:pointer;font-weight:600}} .lbtn.off{{opacity:.35}}
 .fbtn{{background:#1e2027;border:1px solid #555;color:#ccc;padding:6px 12px;margin-left:6px;
        border-radius:14px;cursor:pointer}} .fbtn.off{{opacity:.35}}
 .cnt{{color:#888}}
 table{{border-collapse:collapse;width:100%;margin-top:14px}}
 td{{padding:6px 10px;border-bottom:1px solid #24262e;vertical-align:top}}
 .num a{{color:#e0b341;text-decoration:none;white-space:nowrap}}
 .badge{{border:1px solid;border-radius:10px;padding:1px 9px;font-size:12px;white-space:nowrap}}
 .partial{{border:1px solid #d0a050;color:#e8c07a;border-radius:10px;padding:1px 7px;
           margin-left:5px;font-size:10px;font-weight:700;white-space:nowrap}}
 .offtree{{border:1px solid #62b7c8;color:#9ee7f2;background:#163039;border-radius:4px;
           padding:1px 7px;margin-left:5px;font-size:10px;font-weight:700;white-space:nowrap}}
 .status{{color:#999;font-size:12px}} .age{{color:#666;font-size:12px;white-space:nowrap}}
 .res{{background:#2c4a2c;color:#9c9;font-size:10px;padding:1px 6px;border-radius:8px;margin-left:6px}}
 .oldm{{border:1px solid #b08030;color:#d0a050;font-size:10px;padding:0 5px;border-radius:8px;margin-left:5px}}
 .bannererr{{background:#5a1f1f;border:1px solid #d06060;color:#ffd7d7;padding:2px 8px;
             border-radius:6px;font-weight:bold}}
 .agef{{margin-left:18px;color:#888;font-size:13px}}
 .abtn{{background:#1e2027;border:1px solid #555;color:#ccc;padding:6px 12px;margin-left:6px;
        border-radius:14px;cursor:pointer;opacity:.35}} .abtn.on{{opacity:1;border-color:#e0b341}}
 .validation{{margin:18px 0;padding:16px;border:1px solid #3a3d48;border-radius:10px;background:#191b21}}
 .validation h2{{margin:0;color:#e0b341;font-size:18px}} .vmeta{{color:#999;margin:3px 0 12px}}
 .vtoolbar{{display:flex;gap:10px;align-items:center;margin-bottom:10px}} #vprogress{{font-weight:700}}
 #needsFelt{{background:#242730;border:1px solid #4fb3c4;color:#ddd;border-radius:14px;padding:6px 12px;cursor:pointer}} #needsFelt.on{{background:#17343a}}
 .vgroup{{border-top:1px solid #30333d;padding:8px 0}} .vgroup summary{{cursor:pointer;font-size:15px;font-weight:650}} .gcount{{color:#888;font-weight:400}}
 .vitem{{display:grid;grid-template-columns:82px 76px minmax(220px,1fr) 120px minmax(180px,1fr);gap:8px;align-items:center;padding:6px 0}}
 .vitem a{{color:#e0b341;text-decoration:none}} .vtitle{{overflow:hidden;text-overflow:ellipsis;white-space:nowrap}}
 .vretest{{display:block;white-space:normal;color:#e8c07a;font-size:12px}}
 .validated,.verdict,.vnote{{background:#20232b;border:1px solid #484c59;color:#ddd;border-radius:5px;padding:5px}} .validated{{cursor:pointer}}
 .vitem.isvalidated .validated{{background:#24513d;border-color:#66bb88}} .vitem.isvalidated{{opacity:.72}}
 /* The owner is red/green colourblind: a validated row is marked by the WORD, the button
    label and its POSITION (validated rows sink to the bottom of their group), never by hue
    alone. The badge is always in the DOM and revealed by the class, so it also shows with
    JavaScript off. */
 .vok{{display:none}} .vitem.isvalidated .vok{{display:inline;color:#9fd6b4;border:1px solid #66bb88;border-radius:4px;padding:0 5px;font-size:11px;font-weight:700;letter-spacing:.4px}}
 .vhint{{color:#9aa0ad;font-size:12px;margin:4px 0}} .vexport{{border-top:1px solid #30333d;padding:8px 0}}
 .vexport summary{{cursor:pointer;font-size:14px;font-weight:650}}
 #vjson{{width:100%;background:#171a21;border:1px solid #484c59;color:#cfd3dc;border-radius:6px;font:12px/1.4 monospace;padding:8px}}
 #vcopy{{background:#242730;border:1px solid #4fb3c4;color:#ddd;border-radius:14px;padding:8px 16px;cursor:pointer}}
 #vdl{{color:#e0b341}}
 /* SUBMIT - the primary control of the whole section, so it is the one button that
    is filled rather than outlined. min-height 44px UNCONDITIONALLY (not only under
    the phone media query): she may submit from either device and a 30px target is a
    miss on a phone. */
 #vsubmit{{background:#3a3f2a;border:1px solid #e0b341;color:#f0e4c0;border-radius:14px;
          padding:10px 18px;min-height:44px;font-size:15px;font-weight:700;cursor:pointer}}
 #vsubmitstat{{display:block;margin-top:6px}}
 /* PHONE FIRST - she validates from a phone. 44px minimum tap target on every control. */
 @media(max-width:850px){{.vitem{{grid-template-columns:1fr}}.verdict,.vnote{{grid-column:1}}
  .validated,.verdict,.vnote,#vcopy,#needsFelt,#vsubmit{{min-height:44px;font-size:15px}}
  .vitem{{padding:10px 0;border-bottom:1px solid #262932}}.vtitle{{white-space:normal}}
  .vgroup summary,.vexport summary{{padding:10px 0}}}}
</style></head><body>
<h1>Echoes of Elarion — Work Order Board</h1>
<p style=\"margin:6px 0 16px\"><a href=\"PROOF.html\" style=\"color:#e0b341;font-weight:600\">&#9654; PROOF &mdash; screenshots and gate evidence for every completed item</a></p>
<div class="sub">Generated <b>{stamp}</b> from the repo (WorkOrders/*.md) — the repo is the source of
 truth, this page is a view. Regenerate: <b>python tools/board_build.py</b>
 &nbsp;|&nbsp; {mint_html}</div>
<div class="canon">{canon_links}</div>
<section class="validation"><h2>Owner Validation</h2>
<div class="vmeta">
 <b>A mark you make here is NOT saved yet.</b> It lives only in this browser until you tap
 <b>Submit</b> - that is the step that writes a file the CLI can read. Once submitted and taken
 in, it is stored in <b>proof/owner-validations.json</b>, which is committed, survives every
 commit and every rebuild, and is <b>NOT</b> tied to a build.
 <br>This page shows <b>{disk_done}</b> mark(s) already in that record (rendered from disk, no
 JavaScript needed) plus anything you have marked on this device since.
 <br>The <b>N / M verified</b> count below is <b>the saved record only</b> - it never counts a
 mark that is still sitting in this browser. Those are counted on their own line underneath it.
 <br>What your verdict does on the next board build: <b>Pass + Validated</b> flips the ticket from
 Fixed to <b>CLOSED</b> (WO-1355); <b>Fail</b> or <b>Needs Work</b> sends it back to <b>READY</b>
 carrying your note into the ticket (WO-1356). Current APK <b>{html.escape(apk_build)}</b>
 &middot; source commit <b>{html.escape(apk_source[:12])}</b> is recorded as provenance on each mark.
</div>
<div class="vtoolbar"><span id="vprogress">{disk_done} / {len(fixed_rows)} verified</span><button id="needsFelt" type="button">Needs Felt-Test</button></div>
<div class="vtoolbar"><button id="vsubmit" type="button">Submit marks to the CLI</button></div>
<div id="vpending" class="vhint">Any marks you make on this device are not counted in
 the number above until you tap Submit. They stay safe in this browser until then.</div>
<div id="vsubmitstat" class="vhint">Not submitted yet on this device.</div>
<div id="vmigrated" class="vhint"></div>
<details class="vexport"><summary>Export for the CLI &mdash; the manual fallback, if Submit did not produce a file</summary>
<p class="vhint">This is the older hand-off and it still works: tap <b>Copy</b>, paste the text
 to the CLI, and it runs <b>python tools/board_build.py --ingest -</b> to fold your marks into
 proof/owner-validations.json. Use it only when <b>Submit</b> above did not save a file - Submit
 does the same thing with one tap instead of a paste. Either way, marks that have not reached the
 record live only in this browser.</p>
<div class="vtoolbar"><button id="vcopy" type="button">Copy</button>
 <a id="vdl" download="owner-validations.json" href="#">Save as file</a>
 <span id="vcopystat" class="vhint"></span></div>
<textarea id="vjson" readonly rows="8" aria-label="validation export JSON"></textarea>
</details>{validation_html}</section>
<input id="q" placeholder="Search number / title / status...">{filters}{lane_filters}{age_filters}
<table><tbody id="tb">
{body_rows}
</tbody></table>
<script>
const q=document.getElementById('q'), rows=[...document.querySelectorAll('.row')];
const active=new Set({BUCKET_ORDER!r});
let ageMax=Infinity; // "opened within" (WO-940): ANDs with buckets + search
let lane=''; // LANE chip (MON): ANDs with buckets + search + age. '' = every lane.
function apply(){{const t=q.value.toLowerCase();
 rows.forEach(r=>{{r.style.display=(active.has(r.dataset.bucket)&&r.dataset.text.includes(t)
  &&(+r.dataset.ageDays<=ageMax)&&(lane===''||r.dataset.text.includes(lane)))?'':'none'}})}}
q.addEventListener('input',apply);
document.querySelectorAll('.fbtn').forEach(b=>b.addEventListener('click',()=>{{
 const f=b.dataset.f; if(active.has(f)){{active.delete(f);b.classList.add('off')}}
 else{{active.add(f);b.classList.remove('off')}} apply()}}));
document.querySelectorAll('.lbtn').forEach(b=>b.addEventListener('click',()=>{{
 const l=b.dataset.l; if(lane===l){{lane='';b.classList.add('off')}}
 else{{lane=l;b.classList.remove('off')}} apply()}}));
document.querySelectorAll('.abtn').forEach(b=>b.addEventListener('click',()=>{{
 ageMax=(b.dataset.a==='all')?Infinity:+b.dataset.a;
 document.querySelectorAll('.abtn').forEach(x=>x.classList.remove('on'));
 b.classList.add('on'); apply()}}));
/* ── DURABLE VALIDATION STATE ────────────────────────────────────────────────────
   Full reasoning: tools/owner_validations.py header.

   The RECORD is proof/owner-validations.json - committed, diffable, readable by
   both seats - and it is already rendered into this page server-side (each .vitem
   carries its disk state in data-disk). localStorage is now only the UNEXPORTED
   OVERLAY: marks made on this device that have not reached the record yet.

   ⛔ THE KEY IS DELIBERATELY UNVERSIONED. It used to be
      'eoa-owner-validation:<apk build>:<commit sha>'
   so every commit minted a fresh key and orphaned every sign-off she had made -
   with the CLI committing hourly, the one person whose sign-off closes a ticket
   was losing her work hourly. Never put a build id or a sha back into this key.
   Provenance lives INSIDE each entry ('at' + 'build'), where it costs nothing. */
const validationKey='eoa-owner-validation';
const buildId={json.dumps(apk_build)};
const disk={{}};
document.querySelectorAll('.vitem').forEach(i=>{{try{{disk[i.dataset.ticket]=JSON.parse(i.dataset.disk||'{{}}')}}catch(_e){{disk[i.dataset.ticket]={{}}}}}});
let validation={{}}; try{{validation=JSON.parse(localStorage.getItem(validationKey)||'{{}}')}}catch(_e){{validation={{}}}}
let needsOnly=false;
/* ONE-TIME MIGRATION of the orphaned per-commit keys. Every 'eoa-owner-validation:*'
   key still in this browser is work she already did, stored where nothing reads it.
   Sweep them into the durable overlay - only filling gaps, so a newer mark is never
   overwritten by an older orphan - and leave the old keys in place rather than
   deleting them, because a failed sweep must stay recoverable. Best effort by
   nature: it can only reach keys in THIS browser and origin. */
try{{
 if(!localStorage.getItem('eoa-owner-validation-migrated')){{
  let swept=0;
  for(let i=0;i<localStorage.length;i++){{
   const k=localStorage.key(i);
   if(!k||k.indexOf('eoa-owner-validation:')!==0) continue;
   let old={{}}; try{{old=JSON.parse(localStorage.getItem(k)||'{{}}')}}catch(_e){{continue}}
   for(const t in old){{
    const s=old[t]; if(!s||typeof s!=='object') continue;
    const cur=validation[t]||{{}};
    if(!cur.validated&&!cur.verdict&&(s.validated||s.verdict||s.note)){{
     validation[t]=Object.assign({{}},s); swept++;}}
   }}
  }}
  localStorage.setItem('eoa-owner-validation-migrated','1');
  if(swept){{
   localStorage.setItem(validationKey,JSON.stringify(validation));
   const n=document.getElementById('vmigrated');
   if(n) n.textContent='Recovered '+swept+' mark(s) from the old per-commit keys. '
    +'Open "Export for the CLI" and hand them over so they reach the record.';}}
 }}
}}catch(_e){{}}
/* Effective state = the committed record, overlaid with anything unexported here. */
function eff(t){{return Object.assign({{}},disk[t]||{{}},validation[t]||{{}})}}
function exportPayload(){{
 const out={{}}; const keys={{}};
 Object.keys(disk).forEach(k=>keys[k]=1); Object.keys(validation).forEach(k=>keys[k]=1);
 Object.keys(keys).sort().forEach(t=>{{const s=eff(t);
  if(s.validated||s.verdict||s.note) out[t]=s;}});
 return JSON.stringify({{validations:out}},null,1);}}
function saveValidation(){{
 try{{localStorage.setItem(validationKey,JSON.stringify(validation))}}catch(_e){{}}
 renderValidation();}}
function mark(t,patch){{const s=Object.assign({{}},eff(t),patch);
 s.at=new Date().toISOString().slice(0,19); s.build=buildId;
 validation[t]=s; saveValidation();}}
/* Which validation areas the owner has expanded. Groups render COLLAPSED; whatever
   she opens stays open across the constant board rebuilds. Deliberately keyed
   WITHOUT the build id, unlike validationKey - felt-test RESULTS belong to one APK,
   but "I am currently testing raids" should survive the next build. Wrapped in
   try/catch because a browser with site data blocked throws on access, and the board
   must still render. */
const vopenKey='eoa-owner-validation-open';
let vopen={{}}; try{{vopen=JSON.parse(localStorage.getItem(vopenKey)||'{{}}')}}catch(_e){{vopen={{}}}}
document.querySelectorAll('.vgroup').forEach(g=>{{
 const a=g.dataset.area; if(vopen[a]) g.open=true;
 g.addEventListener('toggle',()=>{{vopen[a]=g.open;
  try{{localStorage.setItem(vopenKey,JSON.stringify(vopen))}}catch(_e){{}}}});}});
/* [ORACLE:counts] Pure, argument-only. tools/board_validation_roundtrip_test.py extracts
   THIS EXACT BLOCK and runs it under node against fixture inputs, so the number the owner
   reads is the number the oracle tests - not a re-implementation of it.
   THE RULING (owner 2026-09-03): "Count only what is saved." The headline counts ONLY marks
   that have reached proof/owner-validations.json - the same bytes the close and bounce passes
   read. A mark still sitting in this browser has NOT reached it and must NEVER inflate that
   number: the defect was a board reading '43 / 78 verified' while the record held ZERO and the
   pass reported BOARD_CLOSE_OK closed 0, so she reasonably expected 43 tickets to have moved.
   Pending marks are counted separately and said in WORDS (she is red/green colourblind, so a
   state is never carried by hue). */
function vMarked(s){{return !!(s&&(s.validated||s.verdict||s.note));}}
const retestPrevious={json.dumps(retest_previous, ensure_ascii=True).replace('<', chr(92) + 'u003c')};
function vRetestPending(t,s){{const old=retestPrevious[t]; if(!old) return false;
 return ['validated','verdict','note','at','build'].every(k=>
  k==='validated'?!!(s||{{}})[k]===!!old[k]:((s||{{}})[k]||'')===(old[k]||''));}}
function vDurableDone(tickets,diskMap){{let n=0;
 tickets.forEach(t=>{{if(vRetestPending(t,diskMap[t])) return;
  if((diskMap[t]||{{}}).validated) n++;}}); return n;}}
function vPending(tickets,diskMap,localMap){{let n=0;
 tickets.forEach(t=>{{const l=localMap[t]; if(!vMarked(l)) return; const d=diskMap[t]||{{}};
  if(!!l.validated!==!!d.validated||(l.verdict||'')!==(d.verdict||'')||(l.note||'')!==(d.note||'')||
     (vRetestPending(t,d)&&!vRetestPending(t,l)))
   n++;}}); return n;}}
/* [/ORACLE:counts] */
function renderValidation(){{
 document.querySelectorAll('.vitem').forEach(item=>{{const state=eff(item.dataset.ticket);
  const pending=vRetestPending(item.dataset.ticket,state), done=!!state.validated&&!pending;
  item.querySelector('.verdict').value=state.verdict||'';item.querySelector('.vnote').value=state.note||'';
  item.classList.toggle('isvalidated',done);item.style.display=(needsOnly&&done)?'none':'';
  const notice=item.querySelector('.vretest'); if(notice&&retestPrevious[item.dataset.ticket]) notice.style.display=pending?'':'none';
  item.querySelector('.validated').textContent=pending?'Confirm retest':done?'Validated':'Validate';}});
 document.querySelectorAll('.vgroup').forEach(g=>{{const xs=[...g.querySelectorAll('.vitem')],n=xs.filter(x=>{{const t=x.dataset.ticket,s=eff(t);return s.validated&&!vRetestPending(t,s);}}).length;g.querySelector('.gcount').textContent=`${{n}} / ${{xs.length}}`;}});
 const vtickets=[...document.querySelectorAll('.vitem')].map(i=>i.dataset.ticket);
 document.getElementById('vprogress').textContent=`${{vDurableDone(vtickets,disk)}} / {len(fixed_rows)} verified`;
 const vpend=document.getElementById('vpending');
 if(vpend){{const p=vPending(vtickets,disk,validation);
  vpend.textContent = p
   ? p+' mark'+(p===1?'':'s')+' on this device '+(p===1?'is':'are')+' waiting to be submitted. '
    +'Nothing is lost - '+(p===1?'it is':'they are')+' safe in this browser. Tap "Submit marks '
    +'to the CLI" above and '+(p===1?'it':'they')+' will be saved and counted in the number above.'
   : 'Nothing waiting - every mark on this device is already in the saved record.';}}
 const ta=document.getElementById('vjson');
 if(ta){{ta.value=exportPayload();
  const dl=document.getElementById('vdl');
  if(dl) dl.href='data:application/json;charset=utf-8,'+encodeURIComponent(ta.value);}}}}
document.querySelectorAll('.vitem').forEach(item=>{{const t=item.dataset.ticket;
 item.querySelector('.validated').addEventListener('click',()=>mark(t,{{validated:vRetestPending(t,eff(t))?true:!eff(t).validated}}));
 item.querySelector('.verdict').addEventListener('change',e=>mark(t,{{verdict:e.target.value}}));
 item.querySelector('.vnote').addEventListener('change',e=>mark(t,{{note:e.target.value}}));}});
/* Clipboard on a phone: navigator.clipboard needs a secure context and this board is
   often opened over file://, so the execCommand path is the one that actually fires
   there. Both are attempted, and the status line says which happened - a Copy button
   that silently does nothing is worse than no button. */
const vcopy=document.getElementById('vcopy');
if(vcopy) vcopy.addEventListener('click',()=>{{
 const ta=document.getElementById('vjson'), st=document.getElementById('vcopystat');
 const say=m=>{{if(st) st.textContent=m;}};
 ta.removeAttribute('readonly'); ta.focus(); ta.select(); ta.setSelectionRange(0,ta.value.length);
 let ok=false; try{{ok=document.execCommand('copy')}}catch(_e){{ok=false}}
 ta.setAttribute('readonly','');
 if(ok){{say('Copied - paste it to the CLI.'); return;}}
 if(navigator.clipboard&&navigator.clipboard.writeText){{
  navigator.clipboard.writeText(ta.value).then(()=>say('Copied - paste it to the CLI.'),
   ()=>say('Copy blocked - the text is selected above, copy it by hand or use Save as file.'));
 }} else say('Copy blocked - the text is selected above, copy it by hand or use Save as file.');}});
document.getElementById('needsFelt').addEventListener('click',e=>{{needsOnly=!needsOnly;e.currentTarget.classList.toggle('on',needsOnly);renderValidation();}});
/* ── SUBMIT (WO-1356) ────────────────────────────────────────────────────────────
   Owner ruling 2026-09-03: "add a submit button so you run a script to close the ones
   passed". The step being removed is the PASTE: Export > Copy > hand the text to the
   CLI. A mechanism with friction is one that stops getting used, and the proof it was
   already failing is that the board read 43/78 verified while the record held ZERO.

   ⛔ THE CONSTRAINT: this page is opened over file://. There is no server to POST to,
   and a file:// page cannot write into the repo. What it CAN do is hand the browser a
   file to save. So Submit triggers a DOWNLOAD to a known, sortable filename, and the
   CLI picks the newest one up with `python tools/board_build.py --submit`.

   Blob first, data: URI as the fallback - both are download paths a file:// page is
   allowed to take, and the data: form is the one the existing "Save as file" link has
   been using here all along.

   ⚠ THE STATUS LINE IS PART OF THE FEATURE, not decoration. A control that looks like
   it worked and did not is the exact failure this whole rework exists to kill, and the
   browser gives no completion callback for a download - so the message says the COUNT,
   the exact FILENAME to look for, the command the CLI runs next, and what to do if no
   file appeared. Success and failure are carried by the WORDS 'SUBMITTED' and 'NOT
   SUBMITTED' (the owner is red/green colourblind - never by hue). */
const vsubmit=document.getElementById('vsubmit');
if(vsubmit) vsubmit.addEventListener('click',()=>{{
 const st=document.getElementById('vsubmitstat');
 const say=m=>{{if(st) st.textContent=m;}};
 const payload=exportPayload();
 let n=0; try{{n=Object.keys((JSON.parse(payload)||{{}}).validations||{{}}).length}}catch(_e){{n=0}}
 if(!n){{say('NOTHING TO SUBMIT - no ticket carries a mark yet. Set a verdict or tap Validate first.');return;}}
 const d=new Date(), p=x=>String(x).padStart(2,'0');
 const name='eoa-validations-'+d.getUTCFullYear()+p(d.getUTCMonth()+1)+p(d.getUTCDate())
  +'T'+p(d.getUTCHours())+p(d.getUTCMinutes())+p(d.getUTCSeconds())+'Z.json';
 let url='', revoke=false;
 try{{url=URL.createObjectURL(new Blob([payload],{{type:'application/json'}}));revoke=true;}}
 catch(_e){{url='data:application/json;charset=utf-8,'+encodeURIComponent(payload);}}
 try{{
  const a=document.createElement('a');a.href=url;a.download=name;a.rel='noopener';
  a.style.display='none';document.body.appendChild(a);a.click();
  setTimeout(()=>{{a.remove();if(revoke){{try{{URL.revokeObjectURL(url)}}catch(_e){{}}}}}},4000);
  say('SUBMITTED '+n+' mark'+(n===1?'':'s')+' as '+name+' - check your Downloads folder. '
   +'The next board build picks it up on its own (python tools/board_build.py - no flag to '
   +'remember): it folds the file into proof/owner-validations.json, CLOSES the Pass+Validated '
   +'tickets and sends the Fail / Needs Work ones back to READY with your note. '
   +'IF NO FILE APPEARED IN DOWNLOADS, this did NOT work - open "Export for the CLI" below instead.');
 }}catch(err){{
  say('NOT SUBMITTED - the browser refused to save the file ('+((err&&err.name)||'unknown error')
   +'). Nothing left this page. Open "Export for the CLI" below and hand the text over.');
 }}}});
renderValidation();
</script></body></html>"""

# ── WO-1356: WHERE A BROWSER "Submit" LANDS ─────────────────────────────────────
# BOARD.html is opened over file://. It has no server to POST to and cannot write into
# the repo, so the ONE thing it can do to get bytes onto disk is hand the browser a file
# to save. The button downloads eoa-validations-<UTC stamp>.json; this is the other end.
#
# The stamp is in the NAME rather than trusting mtime alone: a browser that re-downloads
# an identical name appends " (1)", and a name that sorts is the difference between "the
# newest submission" being a fact and being a guess. Both are used - newest mtime wins,
# and the name is printed so the seat can see WHICH file was taken.
SUBMIT_GLOB = "eoa-validations-*.json"


def submit_dirs():
    """Directories a Submit could have landed in, most specific first."""
    # EOA_SUBMIT_DIR is EXCLUSIVE, not additive. The self-check runs against a throwaway
    # drop directory, and a stray eoa-validations-*.json sitting in the real Downloads
    # folder would otherwise decide which file the test ingested - a test whose input
    # depends on the operator's Downloads folder proves nothing.
    env = os.environ.get("EOA_SUBMIT_DIR")
    if env:
        dirs = [p for p in env.split(os.pathsep) if p]
    else:
        dirs = [os.path.join(ROOT, "inbox")]   # a deliberate drop spot, if one is made
        home = os.path.expanduser("~")
        dirs.append(os.path.join(home, "Downloads"))
        dirs.append(os.path.join(home, "OneDrive", "Downloads"))  # redirected Downloads
    seen, out = set(), []
    for d in dirs:
        k = os.path.normcase(os.path.abspath(d))
        if k in seen:
            continue
        seen.add(k)
        out.append(d)
    return out


SUBMIT_STAMP = re.compile(r"eoa-validations-(\d{8}T\d{6}Z)", re.I)


def _submit_rank(path):
    """Sort key for a drop file. NEWEST FIRST when reversed.

    ORDER: the UTC stamp the page put in the FILENAME wins; mtime only breaks a tie;
    the name breaks a tie after that. The stamp is authoritative because it is what the
    owner's browser wrote at the moment she tapped Submit, while mtime is whatever the
    filesystem / copy / sync did to the bytes afterwards - and the whole risk here is a
    STALE submission resurrecting marks she has since changed. Two files carrying the
    SAME stamp (a re-download Chrome renamed "... (1).json") fall to mtime, then to the
    greater filename - deterministic either way, and if their bytes are identical the
    consumed ledger makes the second one a no-op regardless.
    """
    m = SUBMIT_STAMP.search(os.path.basename(path))
    return (m.group(1).upper() if m else "", os.path.getmtime(path), os.path.basename(path))


def submission_candidates():
    """Every eoa-validations-*.json across submit_dirs(), NEWEST FIRST."""
    found = []
    for d in submit_dirs():
        if not os.path.isdir(d):
            continue
        for f in glob.glob(os.path.join(d, SUBMIT_GLOB)):
            if os.path.isfile(f):
                found.append(f)
    return sorted(found, key=_submit_rank, reverse=True)


def newest_submission():
    """(mtime, path) of the newest drop file, or None. Stamp-first (see _submit_rank)."""
    cands = submission_candidates()
    if not cands:
        return None
    return (os.path.getmtime(cands[0]), cands[0])


# -- The CONSUMED LEDGER (auto-submit) ------------------------------------------
# The ordinary build now ingests her latest submission by itself (owner 2026-09-03:
# "i would expect you to do this everytime you build the board"), and the ordinary
# build runs many times a day. So "have I already taken this file?" has to be a FACT
# on disk, not an assumption about merge semantics.
#
# Identity is the SHA-256 of the BYTES; name and mtime are kept only as human
# provenance. Hash-first because the same payload can arrive under a second name
# (Chrome's " (1)" rename, a copy into inbox/) and that is still the same submission.
#
# Lives beside the record and follows EOA_VALIDATIONS_PATH, so the temp-dir harness
# gets its own ledger and can never consume - or be confused by - the real one.
def consumed_path():
    return owner_validations.PATH + ".consumed.json"


def _sha256(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(65536), b""):
            h.update(chunk)
    return h.hexdigest()


def _consumed_load():
    try:
        with open(consumed_path(), encoding="utf-8") as f:
            data = json.load(f)
        return [e for e in data.get("consumed", []) if isinstance(e, dict)]
    except FileNotFoundError:
        return []
    except Exception as e:
        # A corrupt ledger must never block the board: say so and treat it as empty,
        # which is the SAFE direction - a repeat ingest of an identical payload is a
        # proven no-op (roundtrip stage 11b), a blocked board is not.
        print("VALIDATIONS_CONSUMED_UNREADABLE %s: %s (treated as empty; re-ingesting the "
              "same file is a no-op)" % (type(e).__name__, e))
        return []


def _consumed_has(entries, digest):
    return any(e.get("sha256") == digest for e in entries)


def _consumed_remember(entries, path, digest):
    entries = [e for e in entries if e.get("sha256") != digest]
    entries.append({"sha256": digest, "name": os.path.basename(path),
                    "mtime": round(os.path.getmtime(path), 3),
                    "at": time.strftime("%Y-%m-%dT%H:%M:%S")})
    entries = entries[-100:]          # bounded; only the recent past can repeat
    try:
        with open(consumed_path(), "w", encoding="utf-8", newline="\n") as f:
            json.dump({"_readme": "Drop files already folded into the owner-validation "
                                  "record. Identity is sha256; name/mtime/at are "
                                  "provenance. Safe to delete: the worst case is one "
                                  "no-op re-ingest.",
                       "consumed": entries}, f, indent=1)
            f.write("\n")
    except Exception as e:
        print("VALIDATIONS_CONSUMED_WRITE_FAIL %s: %s (the ingest DID happen; the next "
              "build may re-ingest the same file, which is a no-op)"
              % (type(e).__name__, e))


def auto_submit():
    """Take her newest un-consumed submission on an ORDINARY build. Never fatal.

    Owner ruling 2026-09-03: "i would expect you to do this everytime you build the
    board. CAn you add it to the rebuild script". Same reasoning that put the close
    pass inside the build (CLAUDE.md 16): a FLAG the CLI has to remember is the same
    failure as a second COMMAND it has to remember. She taps Submit; the next board
    build - whenever it happens, for whatever reason - picks it up.

    RULES, each answering one way this could go wrong:
      S1 ONLY THE SINGLE NEWEST candidate is ever considered, ranked stamp-first. If it
         is already consumed the pass STOPS - it never falls back to an older file. That
         is what stops a stale drop still sitting in Downloads from resurrecting marks
         she has since changed in the browser.
      S2 Already consumed (by sha256) -> say so and skip. Silence on a routine path is
         how "did my Submit work?" becomes unanswerable.
      S3 A malformed / unreadable drop file REPORTS AND CONTINUES: the board still
         renders (a half-written download must not freeze the board) and the file is NOT
         marked consumed, so the complaint repeats on EVERY build until it is dealt with.
         It is never silently skipped.
      S4 Opt-OUT only: EOA_BOARD_SUBMIT=0 / --no-submit / --check. An opt-IN would
         rebuild the exact hole this closes.
    """
    if os.environ.get("EOA_BOARD_SUBMIT", "1") == "0":
        print("VALIDATIONS_SUBMIT_SKIPPED auto-ingest opted out (EOA_BOARD_SUBMIT=0 / "
              "--no-submit / --check); nothing was read from any Downloads folder")
        return
    cands = submission_candidates()
    if not cands:
        print("VALIDATIONS_SUBMIT_NONE no " + SUBMIT_GLOB + " to ingest in: "
              + ", ".join(submit_dirs()))
        return
    path = cands[0]                                             # S1
    others = len(cands) - 1
    try:
        digest = _sha256(path)
    except Exception as e:                                      # S3
        print("VALIDATIONS_SUBMIT_UNREADABLE %s: %s: %s (the board still rebuilt; nothing "
              "was ingested from it)" % (path, type(e).__name__, e))
        return
    entries = _consumed_load()
    age_min = (time.time() - os.path.getmtime(path)) / 60.0
    if _consumed_has(entries, digest):                          # S2
        print("VALIDATIONS_SUBMIT_ALREADY %s  (saved %.0f min ago) - already ingested, "
              "skipping. Newer marks reach the record when she taps Submit again."
              % (path, age_min))
        return
    print("VALIDATIONS_SUBMIT_FILE %s  (saved %.0f min ago)%s"
          % (path, age_min,
             ("  [newest of %d; the %d older one(s) are ignored]" % (others + 1, others))
             if others else ""))
    rc, _changed, _merged = _ingest_path(path)
    if rc != 0:                                                 # S3
        print("VALIDATIONS_SUBMIT_UNREADABLE the submitted file could not be ingested; it "
              "was NOT marked consumed, so this reports again on the next build. The board "
              "was still rebuilt and nothing was closed or bounced from it.")
        return
    _consumed_remember(entries, path, digest)
    print("VALIDATIONS_SUBMIT_OK ingested on an ordinary build; the close and bounce "
          "passes below act on it")


def _ingest_path(src_path):
    """Read + fold one payload into the record. Returns (rc, changed, merged).

    Shared by --ingest and --submit so the two entry points can never disagree about
    what an ingest is; only where the bytes came from differs.
    """
    try:
        raw = sys.stdin.read() if src_path == "-" else open(src_path, encoding="utf-8").read()
        payload = json.loads(raw)
    except Exception as e:
        print(f"VALIDATIONS_INGEST_FAIL could not read {src_path}: {type(e).__name__}: {e}")
        return 1, [], {}
    try:
        changed, merged = owner_validations.ingest(payload)
    except owner_validations.ValidationsUnreadable as e:
        print(f"VALIDATIONS_PARSE_FAIL {e}")
        print("    The existing record is corrupt. It has NOT been overwritten - "
              "repair or restore it (git checkout) before ingesting.")
        return 1, [], {}
    print(f"VALIDATIONS_INGEST_OK {len(changed)} changed, {len(merged)} total -> "
          f"{_record_label()}")
    for k in changed:
        s = merged[k]
        # An entry naming a file that is not in WorkOrders/ is REPORTED, never dropped:
        # a renamed or moved WO must not silently swallow a sign-off.
        miss = "" if os.path.isfile(os.path.join(WO_DIR, k)) else "   <- NO SUCH WO FILE"
        print(f"    {'[X]' if s.get('validated') else '[ ]'} "
              f"{s.get('verdict') or 'Untested':<11} {k}{miss}")
    return 0, changed, merged


def _record_label():
    """Repo-relative path to the validation record, or the absolute path when it is
    outside the repo (the self-check redirects it to a temp dir, which on Windows can
    sit on a different DRIVE - relpath raises across mounts)."""
    try:
        rel = os.path.relpath(owner_validations.PATH, ROOT)
    except ValueError:
        return owner_validations.PATH
    return rel.replace("\\", "/") if not rel.startswith("..") else owner_validations.PATH

def main():
    # WO-1011: --check makes the vocabulary ENFORCEABLE. An Unlabeled row is a defect in the
    # WO file (its **Status:** line contains no canonical keyword), and the board renders it
    # faithfully as "Unlabeled" — which reads like a category rather than a mistake. With this
    # flag the check-in gate can reject the drift instead of drawing it. Report-only by default:
    # a plain run must never start failing builds because a WO file is sloppy.
    check = "--check" in sys.argv
    # --no-close is an OPT-OUT (so is EOA_BOARD_CLOSE=0). The close runs by default; an
    # opt-IN flag would rebuild the "a human remembers a second command" hole WO-1355 shut.
    if "--no-close" in sys.argv:
        os.environ["EOA_BOARD_CLOSE"] = "0"
    # --no-submit / EOA_BOARD_SUBMIT=0 is the auto-ingest OPT-OUT (see auto_submit S4).
    #
    # --check IMPLIES IT, and that is the pin, not a nicety: tools/regression/checkin_gate.ps1
    # stage 1b runs `board_build.py --check` on a developer's machine. A gate that started
    # reading whoever's ~/Downloads and writing the shared record as a side effect would be a
    # new failure surface of its own. --check is report-only, structurally.
    if "--no-submit" in sys.argv or check:
        os.environ["EOA_BOARD_SUBMIT"] = "0"

    # ── --ingest: the ONLY writer of the owner-validation record ─────────────────
    # A browser cannot write to the repo, so this is the hand-off half of the loop:
    # BOARD.html's "Export for the CLI" produces JSON, the owner taps Copy on her
    # phone, and this folds it into proof/owner-validations.json. Deliberately an
    # explicit, separate act from a rebuild - a rebuild must never be able to write
    # (or lose) a sign-off. It does NOT touch any **Status:** line: closing a ticket
    # is the owner's act (CLAUDE.md 13), and tools/board_close_validated.py is where
    # that happens.
    if "--ingest" in sys.argv:
        i = sys.argv.index("--ingest")
        rc, _changed, _merged = _ingest_path(
            sys.argv[i + 1] if len(sys.argv) > i + 1 else "-")
        if rc == 0:
            print("    Rebuild the board to render them: python tools/board_build.py")
        return rc

    # ── WO-1356: --submit, the other end of the board's Submit button ───────────
    # The owner taps Submit on BOARD.html (file://, no server), the browser saves
    # eoa-validations-<stamp>.json into Downloads, and this reads the newest one, folds
    # it into the record, and then FALLS THROUGH to an ordinary build - so the same one
    # command closes the Passed tickets and bounces the Fail / Needs Work ones. That
    # fall-through is the point: a second command a human has to remember is not a
    # mechanism (CLAUDE.md 16), and remembering one was the friction being removed.
    if "--submit" in sys.argv:
        found = newest_submission()
        if not found:
            print("VALIDATIONS_SUBMIT_FAIL no " + SUBMIT_GLOB + " found in: "
                  + ", ".join(submit_dirs()))
            print("    Nothing was ingested and no **Status:** line was touched. Ask her "
                  "to tap Submit on BOARD.html (the button names the file it saved), or "
                  "use the Export/Copy fallback with --ingest -.")
            return 1
        mtime, path = found
        age_min = (time.time() - mtime) / 60.0
        print(f"VALIDATIONS_SUBMIT_FILE {path}  (saved {age_min:.0f} min ago)")
        rc, _changed, _merged = _ingest_path(path)
        if rc != 0:
            print("VALIDATIONS_SUBMIT_FAIL the submitted file could not be ingested")
            return rc
        # The EXPLICIT form deliberately ingests even if the ledger already holds this
        # file - a seat that typed --submit is asking for it - but it still RECORDS the
        # consumption, so the ordinary auto-ingest does not pick the same file up again
        # on the next build and print a second, confusing report of the same marks.
        try:
            _consumed_remember(_consumed_load(), path, _sha256(path))
        except Exception as e:
            print(f"VALIDATIONS_CONSUMED_WRITE_FAIL {type(e).__name__}: {e}")
        print("VALIDATIONS_SUBMIT_OK ingested; continuing into the board build, which "
              "closes the Pass+Validated tickets and bounces the Fail / Needs Work ones")

    # -- WO-1356 follow-up: THE AUTO-INGEST, on the ORDINARY build ---------------
    # Owner 2026-09-03: "i would expect you to do this everytime you build the board. CAn
    # you add it to the rebuild script". `--submit` above is now only the EXPLICIT form
    # (it still fails loudly when there is no file, because a seat that typed it is
    # asking a question and deserves an answer); the default path below takes the same
    # newest un-consumed drop file and is never fatal. Rules + risks: auto_submit().
    #
    # BEFORE the record is read, so the page written by THIS run already renders the marks
    # it just took in, and the close/bounce passes below act on them in the same command.
    if "--submit" not in sys.argv:
        auto_submit()

    # Loaded BEFORE anything is rendered. An unreadable record must ABORT the rebuild:
    # writing a board that shows "0 verified" over a corrupt file looks completely
    # normal and would invite the owner to redo work she had already signed off.
    try:
        _validations = owner_validations.entries()
    except owner_validations.ValidationsUnreadable as e:
        print(f"VALIDATIONS_PARSE_FAIL {e}")
        print("    BOARD.html was NOT rebuilt, so no sign-off is hidden or lost. "
              "Repair the record (git checkout proof/owner-validations.json) and re-run.")
        print("BOARD_CHECK_FAIL owner-validation record unreadable")
        return 1

    # ── WO-1355: THE CLOSE PASS, run BEFORE the rows are parsed ─────────────────
    # Owner ruling 2026-09-03: "i test and sign off in the owner validation section when
    # you do board next you flip all passed and validated to closed". It lives INSIDE the
    # board build - not in a second script a seat has to remember - because CLAUDE.md 16
    # settles that shape: a gate whose remedy is "a human remembers a second command" is
    # not a gate, and this one was being forgotten often enough that she had to ask twice.
    #
    # BEFORE parse_wos() so the page written by THIS run already shows the tickets it just
    # closed. Running it after would print a close and render a board that still says Fixed.
    #
    # Only verdict Pass AND validated, only on a FIXED ticket, never re-stamping a CLOSED
    # one, and the old status body is preserved after "PRIOR STATUS:". Full rules and the
    # reasoning: tools/board_close_pass.py. `_validations` is passed in rather than re-read
    # so the rebuild and the close can never act on two different reads of the record.
    _close_ok, _close_res = board_close_pass.run(entries=_validations, wo_dir=WO_DIR)
    if not _close_ok and _close_res is None:
        print("BOARD_CHECK_FAIL close pass aborted on an unreadable validation record")
        return 1

    # ── WO-1356: THE BOUNCE, the other half of the very same sign-off ───────────
    # Owner ruling 2026-09-03: "move the needs work and failed back to ready with a
    # note". It runs HERE, immediately after the close and off the SAME `_validations`
    # read, for the identical reason the close moved in: a routing step that only
    # happens when a seat remembers a second script is a routing step that does not
    # happen. Her note travels into the ticket, so the next seat reads why it failed in
    # the file rather than hunting a screenshot.
    #
    # Order matters and is not arbitrary: close first, bounce second. The two verdict
    # sets are disjoint (Pass vs Fail/Needs Work) so neither can see the other's
    # rewrite, but running the bounce first would leave a freshly-READY ticket in front
    # of a close pass that would then have to reason about it. Disjoint AND ordered is
    # cheaper to keep true than disjoint alone.
    _bounce_ok, _bounce_res = board_close_pass.run_bounce(entries=_validations, wo_dir=WO_DIR)
    if not _bounce_ok and _bounce_res is None:
        print("BOARD_CHECK_FAIL bounce pass aborted on an unreadable validation record")
        return 1

    rows = parse_wos()
    # Parsed here as well as inside build_html so the MISS reaches the console (and the
    # --check exit code), not just the page. A seat reading the terminal must not have to
    # open the HTML to discover the numbering authority went unreadable (WO-1112).
    _main_next, _ui_next, banner_errors = parse_banner()
    # WO-1080: annotate BEFORE the HTML is built, so a stale citation is visible on the page
    # as well as on the console. Costs nothing while no ticket cites a capture.
    cited_captures = check_capture_staleness(rows)
    html_text = build_html(rows)
    with open(OUT, "w", encoding="utf-8") as f:
        f.write(html_text)
    from collections import Counter
    c = Counter(r["bucket"] for r in rows)
    n_wo = sum(1 for r in rows if r["is_wo"])
    print(f"BOARD.html written: {len(rows)} rows = {n_wo} work orders + {len(rows) - n_wo} docs "
          f"({', '.join(f'{b}:{c.get(b,0)}' for b in BUCKET_ORDER)})")

    # Doc rows can never be Unlabeled (bucket_of short-circuits), so this is a pure defect list:
    # real work orders whose **Status:** line carries no canonical keyword. Nothing else.
    unlabeled = [r for r in rows if r["bucket"] == "Unlabeled"]
    if unlabeled:
        # Named, not just counted — "91 Unlabeled" is unactionable; a list is a to-do.
        print(f"UNLABELED {len(unlabeled)} work order(s) — the **Status:** line carries no "
              f"canonical keyword (READY / FIXED / DONE / BLOCKED / SPEC / CLOSED). Fix the WO file:")
        for r in unlabeled[:40]:
            print(f"    WO-{r.get('num','?')}  {r.get('file','?')}  status={r.get('status','') !r}")
        if len(unlabeled) > 40:
            print(f"    ... and {len(unlabeled) - 40} more")

    malformed = [r for r in rows if r.get("malformed_status")]
    if malformed:
        print(f"MALFORMED_STATUS_MARKER {len(malformed)} work order(s) whose verdict comes from a "
              f"near-miss marker - no exact **Status:** in the file (row still rendered):")
        for r in malformed:
            print(f"    {r['file']}  status={r.get('status', '')!r}")
    near_miss = [r for r in rows if r.get("near_miss_status")]
    if near_miss:
        print(f"NEAR_MISS_STATUS_MARKER {len(near_miss)} work order(s) carry a `**Status: ...**` "
              f"line alongside a good marker (cosmetic now; it SHADOWED the real line under the "
              f"pre-WO-1180 pattern - e.g. WO-414). First 5:")
        for r in near_miss[:5]:
            print(f"    {r['file']}")

    fallback = [r for r in rows if r.get("fallback_bucketed")]
    print(f"FALLBACK_BUCKETED {len(fallback)} work order(s):")
    for r in fallback:
        print(f"    {r['file']}  -> {r['bucket']}  status={r.get('status', '')!r}")

    contradictions = []
    for r in rows:
        phrase = status_contradiction(r.get("status", ""), r["bucket"], r["file"])
        if phrase:
            contradictions.append((r, phrase))
    if contradictions:
        print(f"STATUS_CONTRADICTION {len(contradictions)} finished-verdict status line(s):")
        for r, phrase in contradictions:
            print(f"    {r['file']}  phrase={phrase!r}  status={r.get('status', '')!r}")

    # WO-1180: an UNNUMBERED work order renders as "WO-?", and "WO-?" is NOT an assignable
    # key - every unnumbered file shares it, so two unrelated tickets (the Ad Generator and
    # the Economy Store Packs) answer to the same handle and the duplicate guard above cannot
    # see them (it keys on `num`, which is None here). Report them by name; a ticket nobody
    # can address by number is a ticket that gets lost.
    unnumbered = [r["file"] for r in rows if r["is_wo"] and r["num"] is None
                  and r["prod"] is None and r.get("ui") is None]
    if unnumbered:
        print(f"UNNUMBERED_WO {len(unnumbered)} work order(s) render as WO-? "
              f"(not an assignable key - mint a number from the CLI_LANES_WO_NUMBERS.md banner):")
        for f in sorted(unnumbered):
            print(f"    {f}")

    # WO-937: duplicate WO numbers are REPORTED, never silently renumbered - a collision
    # is its own finding. Report-only: it does not change the --check exit contract.
    by_num = {}
    for r in rows:
        if r["num"] is not None:
            by_num.setdefault(r["num"], []).append(r["file"])
    dupes = {n: fs for n, fs in by_num.items() if len(fs) > 1}
    if dupes:
        print(f"DUPLICATE_WO_NUMBERS {len(dupes)} number(s) claimed by more than one file "
              f"(flagged, not renumbered - resolve first-on-disk-and-referenced-wins):")
        for n in sorted(dupes):
            print(f"    WO-{n}: " + " | ".join(sorted(dupes[n])))
    # WO-1080: a layout/touch ticket minted from a capture that predates its own target is
    # the defect that produced WO-1075/1076/1077/1078. Reported, never repaired - the same
    # contract as DUPLICATE_WO_NUMBERS above, and deliberately NOT part of the --check exit
    # code, so a plain run's existing pass/fail contract is unchanged.
    if cited_captures:
        stale = [r for r in cited_captures if r.get("capture_verdict") == "STALE"]
        unknown = [r for r in cited_captures if r.get("capture_verdict") == "UNKNOWN"]
        fresh = len(cited_captures) - len(stale) - len(unknown)
        if stale:
            print(f"STALE_CAPTURE {len(stale)} work order(s) cite a capture log taken BEFORE the "
                  f"newest commit touching their own target file - their measured geometry "
                  f"describes a tree that has moved on (do not act on the numbers; re-run the "
                  f"capture and re-mint):")
            for r in stale:
                print(f"    {r['file']}  {r.get('capture_detail','')}")
        if unknown:
            print(f"CAPTURE_UNVERIFIED {len(unknown)} work order(s) cite a capture that could not "
                  f"be checked (absence of proof is NOT freshness):")
            for r in unknown:
                print(f"    {r['file']}  {r.get('capture_detail','')}")
        print(f"CAPTURE_PROVENANCE {len(cited_captures)} cited - {fresh} fresh, "
              f"{len(stale)} stale, {len(unknown)} unverified")

    # WO-1112: the mint numbers are the two-seat collision guard. An unreadable banner is a
    # LOUD failure, never a "?" on the page - a seat that cannot read the next free number
    # mints on top of another seat, which is the five-collision day the banner documents.
    if banner_errors:
        print("BANNER_PARSE_FAIL - CLI_LANES_WO_NUMBERS.md could not be read for mint numbers:")
        for e in banner_errors:
            print(f"    {e}")
        print("    The board renders a visible error instead of a mint number. "
              "Fix the banner (or tools/board_build.py's patterns) before minting.")
    else:
        print(f"BANNER_OK next mint - CLI: {_main_next}, UI seat: {_ui_next}")

    # ⛔ THE MARKER EMITS ON EVERY RUN, not only under --check (lead ruling 2026-08-24).
    # This repo judges gates by MARKER PRESENCE ON A FRESH LOG, never by exit code
    # (CLAUDE.md 8/16; memory `gates-report-success-without-proving-it`). A marker you only
    # get when a human remembers a second flag is not a gate - it is the exact shape of
    # tools/regression/checkin_gate.ps1, which looked like a gate while failing to parse
    # under PS 5.1 for months. A plain `python tools/board_build.py` must now leave either
    # BOARD_CHECK_OK or BOARD_CHECK_FAIL on the log, so ABSENCE stays a failure signal.
    #
    # WHAT --check STILL OWNS, and it is the only thing: the EXIT CODE. Report-only by
    # default is deliberate (a plain run must never start failing builds because a WO file
    # is sloppy); the check-in gate opts into rejection. The checks themselves are
    # unchanged and unweakened - a dirty board never prints OK.
    # WO board validation record: the marker so ABSENCE is a failure signal, same
    # contract as every other gate in this repo (CLAUDE.md 8/16 - judge the marker on
    # a fresh log, never the exit code). It states the count that SURVIVED the
    # rebuild, which is the whole property that used to be broken.
    _rel = _record_label()
    _vdone = sum(1 for s in _validations.values() if s.get("validated"))
    print(f"VALIDATIONS_OK {len(_validations)} recorded, {_vdone} validated, "
          f"preserved across rebuild - {_rel}")

    # WO-1492: a work order with NO **Status:** line anywhere under WorkOrders/ is invisible to
    # the board. Named, not just counted - the list is the to-do.
    no_status = missing_status_sweep()
    if no_status:
        print(f"MISSING_STATUS_LINE {len(no_status)} work order(s) under WorkOrders/ carry no "
              f"**Status:** line at all - they are invisible to the board. Fix the WO file:")
        for p in no_status[:40]:
            print("    " + p)
        if len(no_status) > 40:
            print(f"    ... and {len(no_status) - 40} more")

    # 2026-09-06 (tooling lane): READY tickets whose named files have already been
    # committed against. The
    # marker prints on EVERY run, 0 included - absence is a failure signal in this repo
    # (see the BOARD_CHECK_OK note above), and a warning you only get when it fires cannot
    # be distinguished from a check that silently stopped running.
    drift = ready_drift(rows)
    for r, p, sha, cdate, mint in drift[:40]:
        label = (f"PROD-{r['prod']:03d}" if r.get("prod") is not None
                 else f"UI-{r['ui']:03d}" if r.get("ui") is not None
                 else f"WO-{r['num']}" if r.get("num") is not None else r["file"])
        print(f"READY_BUT_MOVED {label} {p} {sha} {cdate} (minted {mint})")
    if len(drift) > 40:
        print(f"    ... and {len(drift) - 40} more")
    n_drift_wo = len({h[0]["file"] for h in drift})
    print(f"BOARD_DRIFT {n_drift_wo} READY ticket(s) name a file committed after their mint "
          f"date - WARNING, not a fail: verify the ticket is still open before dispatching it")

    # -- WO-1482: exactly ONE live canon anchor at the repo root -------------------------
    # The live anchor is defined by its BANNER, not by its date: a root CANON_GROUND_TRUTH_*.md
    # whose first six lines carry no SUPERSEDED marker is live. Root legitimately holds two files
    # today (the live one + the bannered 2026-07-22 deep-module anchor), so this counts banners,
    # never files. Zero live anchors is ALSO a fail: an over-eager banner pass would otherwise read
    # as clean to a count-only check. The archived set lives in docs/_archive/root/.
    anchors = sorted(glob.glob(os.path.join(ROOT, "CANON_GROUND_TRUTH_*.md")))
    live_anchors = []
    for a in anchors:
        try:
            with open(a, encoding="utf-8", errors="replace") as fh:
                head = "".join([next(fh, "") for _ in range(6)])
        except OSError:
            head = ""
        if "SUPERSEDED" not in head:
            live_anchors.append(os.path.basename(a))
    if len(live_anchors) == 1:
        print(f"ANCHOR_OK live canon anchor = {live_anchors[0]} (root holds {len(anchors)})")
    else:
        print(f"ANCHOR_FAIL {len(live_anchors)} root CANON_GROUND_TRUTH file(s) lack a SUPERSEDED "
              f"banner (need exactly 1): {', '.join(live_anchors) or 'none'}")

    problems = []
    if len(live_anchors) != 1: problems.append(f"{len(live_anchors)} live canon anchor(s) (need 1)")
    if unlabeled: problems.append(f"{len(unlabeled)} unlabeled")
    if no_status: problems.append(f"{len(no_status)} missing status line(s)")
    if contradictions: problems.append(f"{len(contradictions)} status contradiction(s)")
    if banner_errors: problems.append(f"{len(banner_errors)} banner parse error(s)")
    if problems:
        print("BOARD_CHECK_FAIL " + ", ".join(problems))
        return 1 if check else 0
    print("BOARD_CHECK_OK 0 unlabeled, 0 missing status lines, 0 status contradictions, "
          "mint numbers readable")
    return 0

if __name__ == "__main__":
    sys.exit(main())
