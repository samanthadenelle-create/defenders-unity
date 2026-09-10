# WO-1514: hardcoded repo roots survive in twelve scripts, after the 2026-08-09 machine-dependent ruling

**Status:** PARTIALLY IMPLEMENTED - d6511b8e5 on HEAD, landed 2026-09-07 (was: uncommitted, awaiting gate); gated by the 2026-09-09 wave (REGRESSION 492/492 on Builds/wave1-reg3); owner felt-test closes (was: IN PROGRESS - lane handed back edits 2026-09-06 (uncommitted, awaiting the wave-two compile + regression gate); prior: READY TO IMPLEMENT)
**Silo:** Tooling. Two untracked root scripts and ten under `dev/tmp/`.
**Source:** read-only audit fleet 2026-09-06 (CLI seat), minted from the banner
(`CLI_LANES_WO_NUMBERS.md`, main line 1514 -> 1515 in the same edit).

## 1. EVIDENCE

```
slice-manage-ui.py:139, 194        hardcoded root
slice-manage-ui-v2.py:40, 345      hardcoded root
dev/tmp/*.py (ten files)           ROOT = r'D:\eoa'      -- written 2026-09-01
```

CLAUDE.md sec.0 is explicit and dated 2026-08-09: the repo root is machine-dependent (`C:\eoa` on one seat,
`D:\eoa` on another) and must be resolved at runtime, never hardcoded. These were written THREE WEEKS after
that ruling. On the other machine every one of them fails on a path that does not exist.

Also noted and NOT a defect:

```
ProjectSettings.asset:273   keystore path -- overwritten by AndroidBuild.cs:399 from keystore.properties
```

That one resolves at build time; leave it.

## 2. FIX SHAPE

- Resolve the root from the SCRIPT'S OWN LOCATION (walk up to the directory containing `Assets/`), in all
  twelve files. One helper, imported, not twelve copies of the same walk.
- The two `slice-manage-ui*.py` files are untracked; decide whether they belong in `tools/` (then track them)
  or in `dev/tmp/` (then they are scratch and can be deleted).

## 3. WHAT NOT TO DO
- Do not read the root from an environment variable a seat must remember to set. Sec.0's point is that no
  human step stands between the script and the right path.

## 4. ACCEPTANCE
- [ ] Zero hits for `D:\eoa` or `C:\eoa` in tracked scripts (grep pasted).
- [ ] The two root scripts either tracked under `tools/` or deleted.
- [ ] One resolver helper, twelve callers.
