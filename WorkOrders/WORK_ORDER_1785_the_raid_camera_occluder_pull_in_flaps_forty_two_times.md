# WORK ORDER 1785 — The raid camera's occluder pull-in **enters and releases 42 times in a ~100 second raid**

**Status:** BLOCKED - needs data (the capture named in the ticket)

Why NEEDS DATA: WO-1765 (`abba2d89e`, raid camera profile) landed **after** the build this was measured on, so the count must be re-measured on a post-1765 build before any code is written. §5 names the capture.

**Minted:** 2026-09-16 by the raid-polish audit lane (number PRE-ASSIGNED from the block 1777-1790; this lane did NOT touch `CLI_LANES_WO_NUMBERS.md`)

**Silo:** camera — the occluder pull-in / fade contract. Same lane as WO-1765 / WO-1770 / WO-1771; **one agent at a time on these files.**

**Build under test:** `2026.09.16.371701` (`ProjectSettings/ProjectSettings.asset:148`), built 2026-09-15 22:06 from `f6653501f`. WO-1765 `abba2d89e` (2026-09-16 15:33) is **NOT** in it.

**Video path:** act 2 throughout. A camera that jumps in and out ~25 times a minute is visible in every take, and it is the single most fixable thing standing between the footage and looking finished. P1 (P0 if the count survives WO-1765).

---

## 1. SYMPTOM

Inside the Bastion raid the camera repeatedly snaps closer to the hero and then springs back. The Bastion is a walled scene, so walls pass between the seat and the hero constantly — and each pass triggers the pull-in and then its release.

## 2. EVIDENCE

From `Logs/device/pull-20260916-143101-bastion-owner-run/logcat_full.txt`, restricted to the raid window (log lines 3038129-3062000, `scene=RaidBase_IronBastion` from 13:24:28):

| `[Flow:Camera]` line | count in the raid window |
|---|---|
| `OCCLUDER PULL-IN ENTERED - the seat-side gap … is inside…` | **42** (33 + 8 + 1 message variants) |
| `OCCLUDER PULL-IN RELEASED - back to the FADE contract; seat N.Nm of N.Nm.` | **42** (41 + 1) |
| `OCCLUDER FADED xN … - seat HELD at N.Nm of N.Nm. WO-N contract: fade the wall, hold the seat` | 3 |

**So the contract the project chose — *fade the wall, hold the seat* — held 3 times and was abandoned for a pull-in 42 times, in about a hundred seconds.**

## 3. WHY THIS IS NEEDS DATA AND NOT READY

Three landed or open tickets touch this exact surface and the honest position is that **none of them has been measured against this count**:

- **WO-1765** (`**Status:** IMPLEMENTED, NOT YET GATED`, commit `abba2d89e`) — *"raid scenes get the over-the-shoulder profile; the framing scan never frames a wall."* It is **not in the build measured above** and it plausibly changes this behaviour. ⛔ Writing code before re-measuring would be the inference-fix CLAUDE.md §12 forbids.
- **WO-1753** (`.RESULT.md` present) — *"camera pull-in gates on the hero side occluder not the seat"* — the previous pass on the same gate.
- **WO-1771** (`**Status:** BLOCKED - ask the owner whether it is felt before any work`) — the **town** twin of this defect, *"camera flaps pull-in 78 per minute."* ⚠ Its block is an owner-felt question. **This raid ticket should be put to her in the same breath** — if the town flap is not felt, the raid flap may not be either, and both close for free.
- **WO-1770** (`**Status:** READY TO IMPLEMENT`) — `Garrison_*` / `Outpost1-2` raids still run the town camera. Iron Bastion is not in that set, so 1770 does not cover this.

## 4. IF THE COUNT SURVIVES — the shape of the fix

Prefer the contract the project already ruled: **fade the occluder, hold the seat.** A pull-in should be the exception for a genuinely unrecoverable view, not the default response to a wall. The two knobs are the seat-side gap threshold that triggers `ENTERED` and a hysteresis/dwell so a single wall pass cannot produce an enter-and-release pair. **Instrument the chosen threshold** so the next capture reports the count directly.

## 5. ACCEPTANCE — the capture to take, first

1. Build from a tree containing `abba2d89e` (WO-1765) and capture **one** Bastion raid on the Seeker.
2. Report, with the raid window's first and last `scene=RaidBase` line numbers quoted: `grep -c "OCCLUDER PULL-IN ENTERED"`, `grep -c "OCCLUDER PULL-IN RELEASED"`, `grep -c "OCCLUDER FADED"`, and the raid's duration in seconds — i.e. **enters per minute**.
3. If enters/minute is already low, **close this ticket as fixed by WO-1765** and say so with the numbers.
4. If it is not: ask the owner the WO-1771 question (*is this felt?*) before writing code, then fix and re-measure.
5. Screenshot or screen recording either way — a count is not a judgement of how it looks (memory `screenshots-are-primary-evidence-for-visual-defects`).

## 6. DO NOT TOUCH

The town camera (WO-1771, blocked on the owner). `Garrison_*` / `Outpost1-2` profiles (WO-1770). The frame budget (WO-1779 lane). Any `.unity` file.
