# WORK ORDER 1784 — **412 of 1439 material slots** in the Bastion raid scene have no albedo, and the census names only **12 of them**

**Status:** DONE - committed 88e620a30, gated (COMPILE_GATE_OK/REGRESSION_OK or node --test as applicable). PRIOR: READY FOR LEAD REVIEW

**Lane note (2026-09-17):** verified NOT stale at source — `RaidUntexturedCensus.cs` was last touched by
`4d3ec15c5` (WO-1751, its creation) and still carried `MaxReportedPerPass = 12` with no grouping. The
board's drift flag named `RaidBase_IronBastion.unity` + `RaidBaseDresser.cs`, which are this ticket's
DO-NOT-TOUCH list; `bca258130` (the WO-1767 re-bake this ticket asks to re-run against) is confirmed an
ancestor of HEAD, so §3-item-3 is now satisfiable by a fresh run. Census code widened this lane:
grouped `(material, shader, verdict)` rows with slot/renderer counts + largest-example path, a
reconciliation statement on the header line, and a COMPLETE (uncapped) grouped + per-instance registry
appended to `<persistentDataPath>/raid-untextured-census-<scene>.md`. **No Unity run fired by this lane**
— the fresh-capture acceptance (§4 bullets 1-3) and the committed registry artefact are the lead's run.

Scope note: the census widening is clear from source; the ~400 unnamed offenders cannot be fixed until they are named, which is what this ticket delivers.

**Minted:** 2026-09-16 by the raid-polish audit lane (number PRE-ASSIGNED from the block 1777-1790; this lane did NOT touch `CLI_LANES_WO_NUMBERS.md`)

**Silo:** raid art diagnostics — the `[Flow:RaidArt]` census emitter. **No `.unity`, no bake in this ticket, no material authoring in this ticket.**

**Build under test:** `2026.09.16.371701`, built 2026-09-15 22:06 from `f6653501f`.

**Video path:** every wide shot of act 2. Nearly a third of the scene's material slots are untextured and 400 of them are unidentified. P0-enabling — nothing else in the art lane can be prioritised until this ticket runs.

---

## 1. SYMPTOM

The Bastion raid scene contains a large population of flat grey / untextured surfaces. The in-game census counts them all and then **lists only twelve**, so the remaining ~400 are invisible to the people fixing them.

## 2. EVIDENCE

From `Logs/device/pull-20260916-143101-bastion-owner-run/logcat_full.txt` (grepped, 2026-09-16), scene `RaidBase_IronBastion` — the census runs three times and agrees with itself:

```
[Flow:RaidArt] UNTEXTURED CENSUS (deferred+3s)  scene='RaidBase_IronBastion': 411 offending slot(s) across 881 mesh renderer(s) / 1440 slot(s).
[Flow:RaidArt] UNTEXTURED CENSUS (deferred+20s) scene='RaidBase_IronBastion': 412 offending slot(s) across 880 mesh renderer(s) / 1439 slot(s).
[Flow:RaidArt] UNTEXTURED CENSUS (deferred+45s) scene='RaidBase_IronBastion': 412 offending slot(s) across 893 mesh renderer(s) / 1462 slot(s).
  Listing the 12 LARGEST by renderer bounds - the first line is the best candidate for a player-visible grey box.
```

**412 / 1439 = 28.6% of every material slot in the scene.**

**All 72 `NO ALBEDO … (flat grey slab)` detail lines in the whole capture name one object family** — the arena boundary ring:

| path prefix | detail lines |
|---|---|
| `RaidBase_iron_bastion/ArenaBoundary_Ring/ArenaBoundary_E*` | 72 |
| `…/ArenaBoundary_W*` | 24 |
| `…/ArenaBoundary_S*` | 24 |
| `…/ArenaBoundary_N*` | 24 |

⛔ **THE BOUNDARY RING IS ALREADY FIXED AND THIS CAPTURE IS EXPECTED TO SHOW IT BROKEN — do not re-open WO-1758.** Proven by the scene bytes, not by a doc: `grep -c 'ArenaBoundary_Stone_Shadow' Assets/Scenes/RaidBase_IronBastion.unity` returns **0** at `fdba44553` (the WO-1758 commit) and **0** at `f6653501f` (the build under test), but **1** at `bca258130` (the WO-1767 re-bake, 2026-09-16) and **1** in the current worktree — and **1** in `ArenaPractice_IronBastion.unity` and `OwnedTown_IronBastion.unity` too. WO-1758's fix is a **bake-time editor guard** (`Assets/Editor/ArenaBoundaryRing.cs:782 RebindLightUntexturedSlots`, called from `:637`), and its RESULT says so: *"the bake re-creates the tone every run"*, *"Acceptance-2 evidence on the next bake"*. **The re-bake had not happened when this APK was built; it has since landed.**

⚠ **So the ring accounts for the 12 the census listed — and says nothing about the other ~400.** That is the whole point of this ticket.

## 3. THE FIX

1. **Widen the census.** The emitter lists the 12 largest by renderer bounds; it must be able to emit **all** offenders. Options, in order of preference: a bounded page-through (largest N per line, continued across lines), or a written census artefact under `Logs/debug/` when running on device. ⛔ Do **not** dump 412 lines into a per-frame path — CLAUDE.md §12 records that the Flow firehose evicts the boot window out of the logcat ring (memory `logcat-ring-buffer-destroys-evidence`); the census is a deferred one-shot, so emit it once, bounded, and say how many were withheld if any are.
2. **Group by prefab / address, not by instance.** 412 slots across 880 renderers is almost certainly a handful of shared materials repeated. A census that reports `<material, shader, count, one example path>` turns 412 lines into a fixable list of maybe a dozen rows. That grouping is the deliverable.
3. **Re-run against a build that carries the `bca258130` re-bake** so the ring is out of the way and the residue is the real backlog.

⚠ **WO-1757 is CLOSED - INVALID** (*"The census was never truncated; the LEAD's grep was wrong."*). This ticket is **not** that claim: the census is not truncated, it is **deliberately limited to 12 by design**, and the limit is what needs raising.

## 4. ACCEPTANCE

- A fresh capture (device or headless) on a build containing `bca258130`, in which the census emits a **grouped, complete** offender list for `RaidBase_IronBastion`, and the number of rows reconciles with the total it reports.
- The resulting list committed as a durable registry, not a one-off report (memory `audit-outputs-as-known-dictionaries`) — it is the input to the art lane.
- `grep -c 'ArenaBoundary_Ring.*NO ALBEDO'` on that capture = **0** (confirms the re-bake, closes WO-1758's outstanding acceptance).
- `python tools/gate_brace.py` exit 0, then `COMPILE_GATE_OK` + `REGRESSION_OK <n>/<n>` on fresh logs.

## 5. DO NOT TOUCH

`Assets/Editor/ArenaBoundaryRing.cs` (WO-1758's, already landed). `Assets/Editor/WallTools/RaidBaseDresser.cs` and the bake pipeline (WO-1723 Lane C). Any `.unity` file. Do not author or re-point a single material in this ticket — name them first.
