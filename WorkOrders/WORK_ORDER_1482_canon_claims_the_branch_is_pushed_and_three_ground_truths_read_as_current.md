# WO-1482: canon says the branch is pushed - 103 commits are not - and three CANON_GROUND_TRUTH files read as current

**Status:** CLOSED 2026-09-17 - owner felt-test PASS (validated 2026-09-17T18:19:05, build 2026.09.17.373943). PRIOR STATUS: FIXED 2026-09-07 - every 'pushed' claim in the load-bearing docs replaced by the rev-list pointer; the live anchor is defined by its BANNER (exactly one unbannered root CANON_GROUND_TRUTH), 19 superseded anchors moved to docs/_archive/root/; board_build.py now prints ANCHOR_OK/ANCHOR_FAIL and fails the check on != 1 live anchor (the s5 guard); HOME.html regenerated. PRIOR STATUS: READY TO IMPLEMENT
**Silo:** docs/canon. Pairs with WO-1481 (same class, different section).
**Source:** read-only audit fleet 2026-09-06 (CLI seat), minted from the banner
(`CLI_LANES_WO_NUMBERS.md`, main line 1482 -> 1483 in the same edit).

## 1. EVIDENCE

```
git rev-list --count origin/feat/synty-art-retheme..HEAD   = 101   (103 by git status -sb)
```

while FIVE load-bearing docs say the branch is pushed:

```
CANON_GROUND_TRUTH_2026-09-03.md:21 | docs/HANDOVER.md:10 | PROJECT_INDEX.md:5
SESSION_CANON_LOADER.md:8 | KEY_FACTS.md:61
```

Anchors and pointers that contradict the tree:

```
SESSION_CANON_LOADER.md:638,682   anchor to 09-02, while :1 names 09-03; ~12 stacked "live anchor is X" headers
CANON_GROUND_TRUTH_2026-08-23.md, _2026-09-02.md   no SUPERSEDED banner - both read as current
docs/MASTER_CATALOG.md:189        names wip/village2-and-f8-tickets
docs/ARCHITECTURE.md:108-118      names MainCastle_Hall as the hub and OuterWorld as streaming additively
                                  (hub is Main_Castle_Overworld, SceneRouter.cs:146,168;
                                   OuterWorld.unity has ZERO hits in git ls-files)
docs/MASTER_CATALOG/devtools-settings-onboarding.md:242,245   describes TutorialDirector.cs as live
                                  (removed by 17cf8736b, WO-971)
docs/MASTER_CATALOG/hud.md:51     HudKitController "1,836 lines"   (actual: 5,298)
PROJECT_INDEX.md:110              cites HUDManager.cs + VirtualDPadLean.cs - NEITHER EXISTS
```

CLAUDE.md sec.15 says exactly ONE ground truth may be current. Three are.

## 2. FIX SHAPE

- Banner the two stale `CANON_GROUND_TRUTH_*.md` as SUPERSEDED (do not rewrite their bodies - frozen ledgers).
  Archive the 18 stale root `CANON_GROUND_TRUTH_*.md` under `docs/_archive/`, leaving 09-03 and the 07-22
  module anchor at root.
- Replace the five "pushed" assertions with the git command, per WO-1481's pointer-table rule.
- Fix the seven pointer lines above to name what exists.
- REWRITE `SESSION_CANON_LOADER.md` (700 lines, ~12 stacked anchors) rather than patching it; a loader whose
  own anchor contradicts its title cannot be repaired by another header.
- Add a `board_build`-style check that FAILS when more than one root `CANON_GROUND_TRUTH_*.md` lacks a banner.

## 3. WHAT NOT TO DO
- Do not push the 103 commits to make the docs true. Whether to push is the owner's call.

## 4. ACCEPTANCE
- [ ] Exactly one unbannered ground truth at root; the check exists and goes red on a second.
- [ ] Zero "pushed" assertions; each replaced by the git command.
- [ ] The seven pointer lines corrected; grep for `MainCastle_Hall`, `HUDManager.cs`, `TutorialDirector`
      pasted in the RESULT.

---

## 5. SPEC FOR THE CLI — the `ANCHOR` check in `tools/board_build.py` (docs lane wrote this, did not implement it)

**Added 2026-09-07 by the docs lane, which is not permitted to edit `tools/board_build.py`.** Everything
in §1–§4 above is done in the working tree; this is the one remaining acceptance item and it needs a
code seat.

**What to add:** one pass in `tools/board_build.py` that FAILS the existing `--check` contract when the
repo root holds **more than one** `CANON_GROUND_TRUTH_*.md` lacking a `SUPERSEDED` banner.

**Exact rule.** Glob `CANON_GROUND_TRUTH_*.md` **at the repo root only** (`ROOT`, non-recursive — the
archive under `docs/_archive/root/` is out of scope and must never be scanned, regardless of the
banner state of anything in it). For each, read the **first 6 lines** and test
case-insensitively for the literal `SUPERSEDED`. Count the files with no hit.

- `== 1` → `print("ANCHOR_OK 1 live ground truth, N bannered")` and do not touch the exit status.
- `> 1` → append to the existing `problems` list at `board_build.py:1834-1841` so the run prints
  **`BOARD_CHECK_FAIL`**, with the message naming **every** offending filename:
  `f"{n} root CANON_GROUND_TRUTH files lack a SUPERSEDED banner ({', '.join(names)}) - CLAUDE.md §15 allows exactly ONE"`.
- `== 0` → **also a FAIL**, message `"no live CANON_GROUND_TRUTH at root - every one is bannered"`.
  Do not skip this branch: an over-eager banner pass leaves the repo with no anchor at all, which reads
  as "clean" to a count-only check and is strictly worse than two anchors.

**Why FAIL and not WARN** — deliberately unlike `BOARD_DRIFT` / `DUPLICATE_WO_NUMBERS` / `STALE_CAPTURE`
(`board_build.py:583-587`), which are evidence and stay outside the exit contract. This one is not
evidence: CLAUDE.md §15 states a hard invariant ("keep exactly ONE current"), the condition is exact
arithmetic with no false-positive shape, and the remedy is one banner line. It was violated for
**months** and nothing in the repo could see it — `KEY_FACTS.md` even flagged it in prose and left it
unfixed because pruning was "out of that file's lane". A warning nobody is required to clear is how it
survived that long.

⚠ **Do NOT write "and exits non-zero" into the implementation's contract.** `board_build.py:1840`
returns `1 if check else 0` — an ordinary board build prints `BOARD_CHECK_FAIL` and still **exits 0**,
by design (`:24-26`: *"Judge the board by its own check markers, not by this script's exit code"*).
Reusing `problems` gives you the right behaviour for free: red under `--check`, loud-but-non-fatal on a
plain build. Do not add a bespoke `sys.exit`.

**Judge by the marker on a fresh log, never the exit code** (CLAUDE.md §8): `ANCHOR_OK` present, or
`BOARD_CHECK_FAIL` naming the files. Absence of both is a failure.

**Regression to pin it:** construct a temp dir with two unbannered files, point the root override at
it, assert `BOARD_CHECK_FAIL`; then banner one and assert `ANCHOR_OK 1 live ground truth`. ⚠ Assert the
**success** path too, not only the refusal — a guard that fails every good run while exiting 0 has
shipped here before (memory `prove-the-success-path-not-just-the-refusal`).

**State it will find on the tree as handed over (measured 2026-09-07):** root holds exactly **two**
`CANON_GROUND_TRUTH_*.md` — the live anchor, plus `CANON_GROUND_TRUTH_2026-07-22.md`, kept at root
**deliberately** as the deep module anchor many docs still cite. The 07-22 file **is** bannered, so the
check passes at 1. ⚠ That is precisely why the rule must test the **banner** and not simply count files
at root: a count-only check would go red on a tree that is correct.
