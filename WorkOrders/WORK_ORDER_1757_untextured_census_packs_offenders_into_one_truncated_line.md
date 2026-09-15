# WORK ORDER 1757 — The untextured census packs 12 offenders into ONE log line, and the device log truncates it — the instrument's payload never arrives

**Status:** CLOSED - INVALID (2026-09-15, same day it was minted). The census was never truncated; the LEAD's grep was wrong.
**Minted:** 2026-09-15 by the lead, from the first device run of the census shipped hours earlier in WO-1751.
**Silo:** `Assets/_Modules/Core/Diagnostics/RaidUntexturedCensus.cs` ONLY.

## Evidence (captured, not theorised)
Device logcat, tester build `2026.09.15.371285`, F8 seq 5295-5300:
`[Flow:RaidArt] UNTEXTURED CENSUS (deferred+90s) scene='RaidBase_IronBastion': 418 offending slot(s) across 910 mesh renderer(s) / 1477 slot(s). Listing the 12 LARGEST by renderer bounds - the first line is the best candidate for a player-visible grey box.`
…and then **nothing**. The 12 entries are concatenated into the SAME `Debug.Log` string, and both Android's per-line cap and the F8 harness truncate it. The header promises a listing that never arrives, so the census cannot answer the only question it was built for: WHICH renderer is the owner's grey box.

## The work
1. **One log line per offender.** Header line, then up to N lines each naming the renderer path, material name, shader, bounds size and slot index. Never a single packed string.
2. Keep the cap (12 is fine) and the existing throttle — the fix is line SHAPE, not volume. §12 already warns that a firehose evicts the boot window from the logcat ring.
3. Re-check the criterion against the headline number before trusting it: **418 offending slots across 1477** is ~28% of the scene, which is not "a grey box" — it is either systemic, or the predicate is over-broad (e.g. a material legitimately driven by vertex colour, or a texture bound to a slot the probe does not read). Say which, with evidence, in the RESULT. ⚠ Do NOT loosen the predicate to make the number small; if it is over-broad, prove it on a named object.

## Acceptance
- A device run prints one readable line per offender and names the largest by bounds.
- The RESULT states whether 418 is real or an artefact of the predicate, with a named example either way.
- Lane flips this Status line and writes the `.RESULT.md`.


---

## CLOSED 2026-09-15 - the defect was mine, not the instrument's

`RaidUntexturedCensus.Report` (`:303-322`) already emits the header as ONE `FlowTrace.Fail` and then **one Fail per offender** in a loop - it never concatenated them. The device carried all 12 entries plus a `... 406 further offending slot(s) not listed (cap 12 per pass, biggest-first)` tail.

The lead grepped logcat for `UNTEXTURED CENSUS`, which matches only the HEADER line - the entry lines carry `  #N ...` and no such phrase - saw one line, and concluded truncation. A tool reported exactly what it was built to report and was accused of hiding it.

**The lesson is the one CLAUDE.md §12 already states:** read the captured data before forming the theory. The entries were on the device the whole time, and they NAMED the owner's grey box in one line - `ArenaBoundary_Ring/ArenaBoundary_E_7`, material `M_21_Grey_Light_LPUP`, `_BaseMap=EMPTY`. That is now **WO-1758**. The lane that investigated this ticket was right to refuse the edit and hand back findings.
