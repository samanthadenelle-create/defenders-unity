# WORK ORDER 1772 — Two source-text lints pin the shape this ruling widens

**Status:** READY TO IMPLEMENT

**Minted:** 2026-09-16 by the lead from WO-1765 §17.4

**Silo:** camera / regression

---

## Symptom / Evidence

Two regression suites require specific dungeon-only gate wording. Because of them the R1 rename (`ApplyDungeonProfileIfNeeded` → `ApplySceneCameraProfileIfNeeded`, `_dungeonProfileActive` → `_lockedOtsProfileActive`) was not done in WO-1765, and the heartbeat call site is written `if (_dungeonProfileActive) … else if (_raidProfileActive) …` instead of one `||`. Both suites are green and the behaviour is identical, but **a lint that pins the old shape is the real defect** — re-shape those two assertions (dungeon-gating is what they care about, not the identifier spelling), then the rename is free.

**Evidence:**
- `Assets/Editor/Regression/DungeonFpvRegression.cs:168-172` requires the literal `ApplyDungeonProfileIfNeeded` (and `HubScenes.IsDungeon` within 4000 chars of it).
- `Assets/Editor/Regression/DungeonCameraTightRoomRegression.cs:100` requires the regex `if\s*\(_dungeonProfileActive\)\s*\n\s*EmitDungeonHeartbeat\(dt\)` — i.e. it pins the exact dungeon-only heartbeat gate WO-1765 widens to raids.

---

## Files to edit

- `Assets/Editor/Regression/DungeonFpvRegression.cs:168-172` — re-shape the assertion to test dungeon-gating behaviour, not the method name
- `Assets/Editor/Regression/DungeonCameraTightRoomRegression.cs:100` — re-shape the regex to test dungeon-gating, not the identifier spelling

---

## What NOT to touch

- SmartMobileCamera.cs WO-1765 heartbeat and profile logic; do not change method/field names until the regressions are re-shaped.
- No changes to the renamed identifiers themselves until this WO's assertions are updated.

---

## Acceptance criteria

1. Both regression assertions now test the *intent* (dungeon profile is active / dungeon heartbeat fires) rather than the literal identifier.
2. The rename can then proceed in a follow-up without turning the suites red.
3. `python tools/gate_brace.py` clean on both files; existing dungeon suite tests still pass.

---

## Source

WO-1765 RESULT §13 item 2 and WO §17.4
