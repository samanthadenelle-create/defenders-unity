# WORK ORDER 1772 — Two source-text lints pin the shape this ruling widens

**Status:** DONE - committed 4f5f5141d, gated (COMPILE_GATE_OK/REGRESSION_OK or node --test as applicable). PRIOR: READY FOR LEAD REVIEW

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

---

## Implementation (2026-09-17, lane; awaiting lead gate)

**The anchor a rename cannot move is the ENUM.** Every reshaped assertion resolves the dungeon-active
flag's *name* out of `SmartMobileCamera.cs`'s own derivation
(`<flag> = <want> == CameraSceneProfile.Dungeon;`) via one new helper,
`DungeonFpvRegression.DungeonActiveFlagName` — `internal static`, ONE copy, CALLED (not re-typed) by the
tight-room suite, which is in the same namespace/assembly. It returns `null` when the derivation is gone
and both callers then fail LOUDLY rather than pass vacuously (§12: no silent failures).

Four pins reshaped. Two are the ones this WO cites; two more are the same identifier pins in the same two
files, and AC2 ("the rename can then proceed without turning the suites red") is unreachable without them:

| File:line (pre-edit) | Was | Now |
|---|---|---|
| `DungeonFpvRegression.cs:168-172` | literal `ApplyDungeonProfileIfNeeded` + `IsDungeon` within 4000 chars of its FIRST occurrence (which is the `Awake` call ~300 lines above the body) | applier captured from its DECLARATION `private void (Apply\w*ProfileIfNeeded)\(`, and the `HubScenes.IsDungeon` window anchored to that declaration's body |
| `DungeonFpvRegression.cs:189-191` *(AC2)* | `Contains("_dungeonProfileActive")` | flag resolves from the enum + the town seat is assigned back (`_followOffset = _villageFollowOffset;`) |
| `DungeonCameraTightRoomRegression.cs:96` *(AC2)* | literal flag in the room-seat gate | resolved flag, still required to be a SINGLE-flag gate — room topology must stay dungeon-only (the inverse leak `CameraRaidFramingRegression.cs:381-393` forbids) |
| `DungeonCameraTightRoomRegression.cs:100` | pinned the exact dungeon-only heartbeat gate | every `if (...)`/`else if (...)` heartbeat call site is collected; at least one condition must mention the dungeon flag. Admits the current two-branch shape, a future single `||`, and either operand order; still red if the guard is deleted or re-gated raid-only |

`SmartMobileCamera.cs` deliberately UNTOUCHED (per "What NOT to touch").

### Verification — offline, no Unity fired

Both patterns sets were run verbatim through Python `re` against the real
`Assets/_Modules/Village/Hero/SmartMobileCamera.cs` and five mutated copies. All six runs behaved as
designed: current HEAD all-green; the WO-1765 R1 rename + collapsed `||` all-green (AC2 proven); reversed
operand order all-green; guard deleted → heartbeat pins RED; heartbeat re-gated raid-only → dungeon-gating
pin RED; room seat widened to raids → room-seat pin RED; enum derivation removed → resolver `null`, gates RED.
`python tools/gate_brace.py` → `GATE_BRACE_SUMMARY bad=0 of 2`; 0 NUL bytes in both files.

### Follow-ups the rename WO must carry (out of scope here)

1. `SmartMobileCamera.cs:1769-1775` — the comment explaining that the two-branch shape exists to keep this
   lint green goes STALE the moment this lands. Collapse the comment and the two branches together.
2. `CameraRaidFramingRegression.cs:381-393` — those INVERSE pins are literal leak-strings containing
   `_dungeonProfileActive`. A rename does not turn them red, it turns them **vacuous** (silently
   unmatchable). Reshape them the same definition-capture way in the rename WO, or the raid-leak guard
   quietly stops guarding.
3. **One residual shape pin, left in deliberately.** The heartbeat capture `\(([^)]*)\)` cannot match a
   condition containing a nested paren, and requires the flag to be named literally. So a future
   `if (ShouldEmitYawEvidence(_profileSceneName)) EmitDungeonHeartbeat(dt);` — that predicate already
   exists at `SmartMobileCamera.cs:759` and is *exactly* "does this scene emit the heartbeat" — would go
   red. Both of this WO's ACs (the R1 rename and the `||` collapse) pass, so widening the regex to accept
   a predicate name is out of scope AND would be the same disease: a lint accepting a second spelling. If
   that refactor is wanted, resolve the predicate from its declaration the way the applier now is.

*Cross-assembly note:* both suites live in `DeNelle.EditorRegression`
(`Assets/Editor/Regression/DeNelle.EditorRegression.asmdef`, verified 2026-09-17 — no nested asmdef
between them), so the one-copy cross-suite call resolves inside a single assembly.
