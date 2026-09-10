# WO-1484: the Unity heap climbs 66 MB in four minutes without leaving the scene

**Status:** READY TO IMPLEMENT - PARKED (owner 2026-09-09: measure in Pi Browser on the Seeker first)

### OWNER RULING 2026-09-09 (recorded by the CLI lead)
Both WO-1484 and WO-1314 need measured over the Seeker in Pi Browser to verify. The 2026-09-09 desktop/controlled-run retractions in docs/READY_RCA_2026-09-09.md do not close them. A measurement session must run the game in Pi Browser ON THE SEEKER (not desktop, not headless).
**Silo:** Perf / memory.
**Source:** read-only audit fleet 2026-09-06 (CLI seat), minted from the banner
(`CLI_LANES_WO_NUMBERS.md`, main line 1484 -> 1485 in the same edit).

## 1. EVIDENCE

`wallet-session-2026-09-06.log`, both samples in the SAME scene with no load between them:

```
:856      13:23:55.760   mem=449MB   scene=Main_Castle_Overworld
:169966   13:28:01.197   mem=515MB   scene=Main_Castle_Overworld
```

+66 MB in 4 min 5 s, about 16 MB a minute. On a device already sampling 415-449 MB at rest, that trajectory
reaches an OS kill inside a long session.

## 2. FIX SHAPE

- Run a 15-minute IDLE device capture in the town, sampling `mem` throughout. If growth is linear it is a
  leak; if it plateaus it is cache warm-up and this closes.
- Named suspects, to be confirmed or eliminated by the capture, not before:
  1. VFX loop slots that never release (WO-1473 - 14 of 24 held, aged 303 s);
  2. offline sync queue string growth (WO-1455 - unbounded, observed at depth 112);
  3. log buffer growth behind the 320/s probe storm (WO-1450).
- Fix only the one the capture names.

## 3. WHAT NOT TO DO
- Do not call `GC.Collect` on a timer as the fix; that masks a leak and costs frames.
- Do not act on the three suspects first. All three are already ticketed; if one of them IS the leak the
  capture will say so, and if none are, guessing burns the session.

## 4. ACCEPTANCE
- [ ] A 15-minute idle capture attached; the growth curve described (linear / plateau) with sample values.
- [ ] If linear: the retaining allocation NAMED, with a fix and a before/after curve.
- [ ] `REGRESSION_OK n/n` on a fresh log.
