# WO-1450 RESULT - ProbeForStructure no longer floods the logcat ring

**Status:** IMPLEMENTED - d6511b8e5 on HEAD 2026-09-09 (was READY); owner felt-test closes.
**Verified:** 2026-09-09, SILO 0A verify-and-flip pass, HEAD 184c8ff06, branch dev. Read-only:
no .cs edited, no Unity run, no device capture, no commit.

## 1. Landing sha

```
d6511b8e5 feat(wave-three): the owner's UI rulings land - Manage words and doors, raid loop
          closes, deploy screen redesign, harvest result, defense-report chip, quest claim,
          wardrobe gate, skills depth; wave-two lanes gated in
```

Found by `git log --oneline -S"_lastProbeTargetId" -- Assets/_Modules/Village/Enemies/Enemy.cs`.
`git merge-base --is-ancestor d6511b8e5 HEAD` = **ancestor**. (The WO's own status said the lane
had handed back UNCOMMITTED edits on 09-06; they are committed and on HEAD.)

## 2. Proof at HEAD, file:line

**The trace is KEPT and double-gated** - `Assets/_Modules/Village/Enemies/Enemy.cs:1954-1969`:

```
// WO-1450: this was FlowTrace.Step - unthrottled, once per frame per enemy,
// 38,018 lines at ~320/sec with a managed stack walk each. It is now gated
// TWICE: it fires only when the acquired target CHANGED ... and even then at
// most 1/sec per enemy. The trace is KEPT, not stripped (CLAUDE.md sec.12).
var hitMb = _currentTarget as MonoBehaviour;
int hitId = hitMb != null ? hitMb.GetInstanceID() : 0;
if (hitId != _lastProbeTargetId)
{
    _lastProbeTargetId = hitId;
    FlowTrace.Throttle("EnemyAggro", $"probe-hit-{_enemyId}", 1f, ... "-> stopping agent to attack (target CHANGE)");
}
```

- **Gate 1, change-gate:** `_lastProbeTargetId` declared at `Enemy.cs:286`, with the RCA written
  above it at `:281-285` (38,018 lines, 12:59:05-14:37:52, ~320/sec, 256 KiB Android ring, memory
  `logcat-ring-buffer-destroys-evidence`).
- **Gate 2, cadence:** `FlowTrace.Throttle(..., 1f, ...)` at `:1966` - at most 1/sec per enemy.
- **Not stripped:** the line still exists and still names the acquired target
  (CLAUDE.md sec.12 - instrumentation is permanent).
- **The physics call itself is also gated:** `Enemy.cs:278-279`
  `private const float ProbeIntervalSeconds = 0.25f;   // <= 4 probes/sec/enemy` plus `_nextProbeAt`.
- **The null branch was already throttled** and stays so: `:1950-1951`
  `FlowTrace.Throttle("EnemyAggro", $"probe-fail-{_enemyId}", 1f, ...)`.
- **Stack frames:** `FlowTrace.Throttle` is the FlowTrace path, which does not carry the managed
  stack walk the raw `Debug.Log` from `Enemy:Update()` did.

**The shape is PINNED by a regression** - `Assets/Editor/Regression/EnemyProbeCadenceRegression.cs`,
registered into the full gate at `Assets/Editor/Regression/DataRegression.cs:1762`. Its eight pins
(header `:17-40`): `[no-step]` a revert to `FlowTrace.Step` FAILS; `[kept]` deleting the trace FAILS;
`[change-gate]` `_lastProbeTargetId` must be present; `[cadence]` `_nextProbeAt` gate + a real
bounded interval; `[bounded]` the interval must not be so long the enemy goes blind;
`[drop-paths]` all three `_currentTarget = null` sites carry a throttled trace; `[reset]` the pool
reset clears both fields; `[semantics]` target selection is unchanged.

## 3. GREEN on a fresh log

`Builds/ready-rca-checkpoint-regression.log`, read with PowerShell `Select-String` (Unity logs are
UTF-16, `grep` misses them):

```
[enemy-probe-cadence] SOURCE LINT PASS (shape only - the device line count and the frame cost
are proven by a capture, not by this suite): acquire-trace=Throttle(probe-hit)
change-gate=_lastProbeTargetId interval=0.25s drop-paths=3/3 pool-reset=clears-both
semantics=faction+hero-primary intact
```

## 4. Acceptance, honestly scored

| # | Acceptance | Verdict |
|---|---|---|
| 1 | A one-minute device capture in combat shows `< 100` `ProbeForStructure` lines and no stack frames | **NOT PROVEN** - no device capture was taken. This is the felt/device item. |
| 2 | The trace still fires at least once per newly acquired target | **PROVEN by source** (`Enemy.cs:1962-1968`, the change-gate fires exactly on a target change) and pinned by `[change-gate]` + `[kept]`. Not proven on a device. |
| 3 | `REGRESSION_OK n/n` on a fresh log | **NOT PROVEN** - the fresh full run is `REGRESSION_FAIL: 2 failure(s) (472/474 registered suites green, 0 skipped)`. Both failures are UNRELATED (`[hero-element-cast]` ordering; `MANAGE_BUILD_DOOR_FAIL` for pet-house / market / workshop, WO-2007). **This suite is green on that same log.** |

## 5. What the owner should felt-test

This one is a device capture, not a feel - but it is cheap and it is the last open item:

1. Play a town or raid fight on the Seeker for one minute, then pull the log
   (`adb logcat -d`, or the F8 harness).
2. Count `ProbeForStructure` lines. Acceptance is **fewer than 100** in that minute; before the fix
   it was ~19,000. They should read `ProbeForStructure ACQUIRED '<name>' -> stopping agent to attack
   (target CHANGE)`.
3. Confirm no managed stack frames hang off those lines.
4. Confirm the boot window and other `[Flow:*]` traces SURVIVE in the same pull - that is the actual
   point of this ticket: the evidence window is no longer evicted.
5. Combat behaviour must be unchanged - enemies still peel onto walls and still chase the hero
   first. If anything reads as blind or hesitant, say so; the 0.25s probe gate is the suspect.

## 6. What is NOT proven

- **No device capture exists for this fix.** Acceptance 1 is entirely unproven; the shipped suite
  says so about itself in its own reason string ("the device line count and the frame cost are
  proven by a capture, not by this suite").
- **The suite is a SOURCE LINT, not a measurement** (`EnemyProbeCadenceRegression.cs:8-14`, the
  WO-1494 honest-scope header). It can only stop the guards being reverted; it cannot prove the
  cadence at runtime.
- **`REGRESSION_OK n/n` is not on any fresh log** - the full run is red for two unrelated suites.
- **WO-1459 is NOT closed by this.** The two tickets share this emitter, but 1459's 11-fps capture
  is confounded by SEVERE thermal throttling and cannot rank log I/O against gameplay cost. A new
  post-fix capture is the only way to separate them.
