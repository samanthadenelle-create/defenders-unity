# WO-1481: CLAUDE.md section 8 is a dated snapshot embedded in the read-first law, and it has rotted

**Status:** IMPLEMENTED - d6511b8e5 on HEAD 2026-09-09 (was READY); owner felt-test closes. See `WORK_ORDER_1481_claude_md_section_8_is_a_rotted_snapshot_make_it_a_pointer_table.RESULT.md`. PRIOR STATUS: READY TO IMPLEMENT
**Silo:** `CLAUDE.md` sec.7 + sec.8 (+ `PIPELINE_STATE.md`, `KEY_FACTS.md` for the duplicated facts).
Docs only; no code.
**Source:** read-only audit fleet 2026-09-06 (CLI seat), minted from the banner
(`CLI_LANES_WO_NUMBERS.md`, main line 1481 -> 1482 in the same edit).

## 1. EVIDENCE

Three measured contradictions between the read-first law and the tree, this session:

```
CLAUDE.md:317   "Save schema v38"          vs   SaveSchema.cs:41   CurrentVersion = 41
CLAUDE.md:174   "19 .asmdef"               vs   find Assets/_Modules -name '*.asmdef' | wc -l  = 25
CLAUDE.md sec.7 "FeatureFlags.MapTab ... OFF"  vs  FeatureFlags.cs:843  DELETED 2026-09-05,
                                                   absence PINNED by
                                                   PublicNavigationRetirementRegression.cs:91-95
```

v39, v40 and v41 are recorded in NO canon doc. "schema v38" additionally exists in three places
(`CLAUDE.md:320`, `PIPELINE_STATE.md:27-28`, `KEY_FACTS.md:724` and `:1195`) - four copies of one fact.

This is the failure sec.2, sec.5 and sec.16 each describe in their own words: a hand-maintained number
tracking a live value is duplicated state. Section 8 is a whole section of it.

## 2. FIX SHAPE

- One pass converting every FACT in sec.8 into a fact -> AUTHORITY COMMAND row, matching the shape
  `PIPELINE_STATE.md:15-22` already uses (e.g. "save schema: read `SaveSchema.CurrentVersion`", "asmdef count:
  `find Assets/_Modules -name '*.asmdef'`").
- Delete the MapTab paragraph from sec.7 and cite `PublicNavigationRetirementRegression` as the authority for
  its absence.
- Delete the three duplicate "schema v38" strings elsewhere and point them at the same command.

## 3. WHAT NOT TO DO
- Do not update the numbers to today's values. That is what the last two passes did and it rotted again inside
  a month. The copy is the bug, not the value in it.

## 4. ACCEPTANCE
- [ ] No literal save-schema version, asmdef count, or flag state remains in `CLAUDE.md`.
- [ ] Zero hits for `schema v38` repo-wide outside dated frozen ledgers (grep pasted).
- [ ] Each removed fact replaced by a command a seat can run.
