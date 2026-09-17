# WORK ORDER 1855 — "stuck in battle model" after an arena win (BATTLE_QUIESCENCE_FAIL, rep-chase)

**Status:** READY FOR LEAD REVIEW
**Filed by:** agent (dispatched live, owner mid-playtest report "my character is stuck in battle model still")
**Numbering:** WO-1855, minted from `CLI_LANES_WO_NUMBERS.md` banner (next free was **1855**; bumped to
**1856** in the same edit as this file, see the banner's own attribution note).

---

## 1. The report

Owner, live, mid-playtest (2026-09-17): *"my character is stuck in battle model still"* — reported
while playing, in `Main_Castle_Overworld`.

## 2. Captured evidence (read BEFORE any code-read, per CLAUDE.md §12/§14)

Three device captures (`SM02G4061955851`), all `Main_Castle_Overworld`, all `BATTLE_QUIESCENCE_FAIL
(arena win)`, all naming the SAME shape:

| Capture | Local time | Enemy key | Registered probes |
|---|---|---|---|
| `logs/f8-inbox/capture-device-20260915-213321-seq5344.md` | 2026-09-15 21:33 | *(this one is a DIFFERENT invariant — `modal: 'Dialogue'` visible, unrelated to this WO; listed by the caller as a candidate but not part of this shape)* | — |
| `logs/f8-inbox/capture-device-20260916-204956-seq5490.md` | 2026-09-16 20:49 | `-812452` | 4 (`PursuitBattleProbe.Probe`, `BattleArena.<Awake>b__84_0`, `HeroCombatEngagement.<EnsureProbe>b__2_0`, `WaveManager.<OnEnable>b__135_0`) |
| `logs/f8-inbox/capture-device-20260917-103424-seq5519.md` | 2026-09-17 10:34 | `-318964` | 3 (`PursuitBattleProbe.Probe`, `BattleArena.<Awake>b__84_0`, `WaveManager.<OnEnable>b__135_0`) |

seq5519 verbatim:
```
[Flow:Quiescence] BATTLE_QUIESCENCE_FAIL (arena win) - 1 invariant(s) NOT restored after the battle:
  - battle-lock: still HELD after the battle ended. Combat input stays suppressed and the HUD cannot
    return to its town context. HOLDER(S): PursuitBattleProbe.Probe (of 3 registered:
    PursuitBattleProbe.Probe, BattleArena.<Awake>b__84_0, WaveManager.<OnEnable>b__135_0).
    PURSUIT PULSES: key=-318964 owner='OverworldEncounterSpawner/rep-chase' age=0.00s.
```

Read directly from `Logs/f8-inbox/device/SM02G4061955851/break-log.jsonl` at `t≈1174.42`, the very
NEXT line (`t≈1174.52`, one frame later on the unscaled clock) is the gate's own **self-heal**
report, proving `BattleSessionEnd.Release` was re-driven (clearing the whole pursuit ring) and the
SAME key re-stamped within one frame anyway:
```
[Flow:Quiescence] battle-lock STILL HELD after the self-heal (arena win): [PursuitBattleProbe.Probe]
(was [PursuitBattleProbe.Probe]). ... PURSUIT PULSES before the heal: key=-318964
owner='OverworldEncounterSpawner/rep-chase' age=0.00s; after (re-stamped within one frame of a full
ClearPursuits): key=-318964 owner='OverworldEncounterSpawner/rep-chase' age=0.00s.
```
(Same pairing, different key `-812452`, present for seq5490 at the same offset in the same file.)

**Screenshots** (`logs/f8-inbox/device/SM02G4061955851/break_23_error.png`, `break_24_error.png`,
timestamped the same second as seq5519): hero standing in the open overworld near the town wall/gate,
combat ability bar visible (CAST/SHELL/Mend/Arcane Bolt/Wither/ITEM), Wave 15 banner reading
"Next wave in 7m 57s" (i.e. the TOWN wave loop is idle, not the source). Two small enemy silhouettes
are visible far across the field on the opposite side from the hero. No arena/battle SCREEN is up —
the world itself is what stays in "battle" presentation.

**`Logs/debug/seeker-365962-logcat.txt`** was checked (grepped for `Quiescence`/`HeroOwner`) and
carries no matching entries for this incident — it is a different, earlier session (09-11) and was
ruled out as evidence for this ticket.

## 3. RCA — read at source, not inferred

**Where "battle model" comes from:** `Assets/_Modules/Village/Hero/HeroPoseController.cs:293`
```csharp
if (DeNelle.Core.Combat.BattleLock.IsInBattle()) return true;
```
`EvaluateCombat()` gates the hero's combat pose/animator bool + weapon-drawn presentation purely on
`BattleLock.IsInBattle()`. `HudContextEvaluator.cs:106-108` independently keeps the HUD in
battle-context on the same OR (`BattleLock.IsInBattle() || PostureSignals.PursuitActive`). So
whenever `BattleLock` reads true — for ANY reason, staged fight or mere pursuit — the hero visually
stays in combat stance. This is the "battle model" the owner is seeing.

**Is this a release bug in BattleLock / PursuitBattleProbe / BattleSessionEnd?** No — verified by
reading each against the fixes already in this tree for the identical prior shape (F8 seq4768 /
WO-1337 / WO-1603, all documented in-code at `OverworldEncounterSpawner.cs:1003-1046` and
`BattleQuiescenceGate.cs:56-63, 336-381`):

- `PostureSignals.ReportPursuit` self-expires after `PursuitTtl` (1.5s) and `PursuitBattleProbe`
  reads `PostureSignals.PursuitActive` verbatim (`PursuitBattleProbe.cs:62`) — it is the READER, not
  the producer, exactly as WO-1603 already documented.
- `BattleSessionEnd.Release` (driven both by `BattleArena.Resolve:2754` on the real exit AND by the
  gate's own self-heal, `BattleQuiescenceGate.cs:355-356`) calls `PostureSignals.ClearPursuits()`
  each time — proven by the log itself: `PURSUIT PULSES before the heal` vs `after` in the seq5519/
  5490 self-heal lines show the SAME key present both before AND immediately after a confirmed
  `ClearPursuits()` call, which is only possible if a live producer re-stamped it in the one frame
  between the clear and the re-check (`BattleQuiescenceGate.cs:350-380`, the `yield return null`
  at `:361` is exactly one frame).
- `Enemy.OnDisable`/`Enemy.Die()` both revoke their own pursuit pulse (`Enemy.cs`, pinned by
  `BattleQuiescenceRegression.DespawnRevokesPursuitAtSource`), and the engaging rep's own
  `RepEngageWatcher` is destroyed via `ConsumePack()` (`OverworldEncounterSpawner.cs:1617-1626`)
  synchronously at `Engage()` time (well before `Resolve()` runs), so the ENGAGING rep cannot be the
  re-stamper.

**Who is re-stamping, then?** `SpawnOverworldFamilyPack` (`OverworldEncounterSpawner.cs:781-849`)
gives every family exactly ONE `RepEngageWatcher` (added only to the leader, `i==0`,
`:837-848`), and every family member — leader and followers alike — is parented under a SHARED
`packRoot` (`EnemyFactory.Build(def, pos, Quaternion.identity, packRoot.transform)`, `:831`).
`Engage()`'s `ConsumePack()` destroys that whole `packRoot`, so the fighting family cannot leave a
survivor behind. The re-stamping key therefore belongs to a **second, completely independent rep
pack** — untouched by the resolving fight, never paused/resumed by it except through the blanket
`RepEngageWatcher.PauseAll/ResumeAll` (`:1075-1078`) that freezes/thaws EVERY home-scene rep — whose
leader was ALSO stung (chasing) before the fight started and is still, right now, within its own
`DeaggroRange` (26m, `:1047`) of wherever the hero's arena-win return position landed
(`BattleArena.Resolve:2577`, `_current.ReturnPosition` = the hero's position AT THE MOMENT OF
ENGAGE — i.e. exactly where the touching rep found her, which is also where any co-located second
pack would be).

`QuietNonPursuersOnBattleEnd()` (`OverworldEncounterSpawner.cs:1085-1114`) correctly PRESERVES this
second leader (`if (_stung || _engaged) return false;`) per the owner's own standing ruling, recorded
in this same file's F8 seq4768 block comment (`:1027, :1037-1040`): *"THE CURE IS A LEASH BREAK, NOT
A WIDER TOLERANCE"* / *"a chase that is closing keeps closing, forever, exactly as designed"* — i.e.
an active chaser legitimately holding the battle-lock (and therefore the combat pose) is **by
design**, not a leak, right up until it either closes to touch (a new fight) or crosses
`DeaggroRange` and lets go (`ChaseBrokeOff`, `:1158-1168`, evaluated fresh every `Update()`,
`:1335`).

**The actual gap, and why it looked unfixable from the capture alone:** `BattleLock.DescribeHolders()`
can only ever print `"PursuitBattleProbe.Probe"` (the reader) and
`PostureSignals.DescribePursuits()` can only ever print the pulse's owner tag + age — **neither line
can distinguish "a live chase that is closing and will resolve in a few seconds" from "a chase
stalled on blocked geometry that will never resolve."** The code already anticipates exactly that
second case (`ChaseStallWarnSeconds` / the `chase-stall-` `FlowTrace.Throttle` at
`OverworldEncounterSpawner.cs:1380-1406`) but that diagnostic never reaches the
`BATTLE_QUIESCENCE_FAIL` line — it is a separately-throttled `Warn` on the `Encounter` channel, not
the `Quiescence` one, so a reader triaging the FAIL capture alone (as this ticket started out doing)
cannot tell which of the two is happening without a second, targeted capture. That gap — not a lock
leak — is the root cause of why three identical-looking captures cost three separate triage passes.

## 4. What was NOT touched, and why

Per the advisor consult during this RCA and the WO-1233/1337/1603 wiring lints already pinned in
`BattleQuiescenceRegression.cs`:
- **`BattleLock.cs`** — untouched. It releases exactly what is registered against it; not the bug.
- **`PursuitBattleProbe.cs`** — untouched. `BattleQuiescenceRegression.cs:944`
  (`[wo1337-wiring]`) lints that it reads `PostureSignals.PursuitActive` verbatim; weakening it to
  "fix" this ticket would fail that pinned assertion and would also literally break the F8-46 owner
  ruling (combat abilities must stay live while genuinely pursued).
- **`BattleSessionEnd.cs`** — untouched. It already re-drives the one authoritative exit correctly, on
  both the real path (`BattleArena.Resolve:2754`) and the gate's own self-heal path.
- **No wall-clock give-up timer was added** to `RepEngageWatcher`'s chase — the owner explicitly
  ruled this out (`OverworldEncounterSpawner.cs:1034-1040`, *"DELIBERATELY NOT a wall-clock give-up
  timer"*).
- **The lock was not force-cleared.** `BattleQuiescenceGate`'s own header (`:30-32`) states it
  "OBSERVES and REPORTS," and forcing `BattleLock` false out from under a genuinely-still-pursuing
  rep would desync the lock from the actual pursuit state — the exact "quietly fixing things" the
  gate's design explicitly refuses to do.

## 5. The fix (instrumentation only — "append the holder, never force it," the same pattern as
WO-1233 / WO-1603)

Added a Core `QuiescenceProbe` named `"rep-chase"`, registered from
`Assets/_Modules/Village/Enemies/OverworldEncounterSpawner.cs` (a `[RuntimeInitializeOnLoadMethod]`
bootstrap, mirroring `WaveManager.RegisterWavePhaseQuiescenceProbe` / `PursuitBattleProbe.Install`).
Its `Check` (`CheckRepChaseQuiescence`) enumerates every live `RepEngageWatcher` that is currently
`_stung`, and for each reports directly in the `BATTLE_QUIESCENCE_FAIL` / self-heal text:
- the watcher's own GameObject name,
- its LIVE distance to the hero right now (re-measured, not cached),
- its `AggroRange`/`DeaggroRange`/touch-distance thresholds,
- seconds chasing, and seconds since it last closed any ground (the same "stall" measure the
  existing `ChaseStallWarnSeconds` diagnostic already computes, now surfaced on the gate's own
  channel instead of a separate throttled `Encounter` warn).

This converts the next occurrence from *"battle-lock: still HELD... HOLDER(S): PursuitBattleProbe.Probe"*
(unactionable — a messenger, per WO-1603's own diagnosis of the identical prior gap) into a line that
either says *"rep X is 4.2m away, chasing 6.1s, closing"* (legitimate, self-resolving, no fix needed)
or *"rep X is 19.8m away, stalled 34.7s with no progress"* (a genuinely blocked chase — the next,
now-actionable, ticket). It is **read-only**: `CheckRepChaseQuiescence` never calls `Deaggro`,
`RevokePursuit`, or `SetPackCombatPresentation` — pinned by the new regression case below.

**Files changed:**
- `Assets/_Modules/Village/Enemies/OverworldEncounterSpawner.cs` — `using DeNelle.Core.Combat;` +
  `RegisterRepChaseQuiescenceProbe()` + `CheckRepChaseQuiescence()` (added after
  `QuietNonPursuersOnBattleEnd`/`Deaggro`, before `ChaseBrokeOff`).
- `Assets/Editor/Regression/BattleQuiescenceRegression.cs` — new `RepChaseProbeIsRegistered` source-
  lint case (wired into `RunAll`, WO-1855 block), asserting: the probe is registered under the exact
  name `"rep-chase"`; `CheckRepChaseQuiescence` exists and names all four discriminating numbers
  (distance, stall time, touch distance, the watcher's own name); and the check body never mutates
  chase state (no `Deaggro(`/`RevokePursuit`/`SetPackCombatPresentation` inside it). A full
  behavioural drive was not possible from this Core-only editor assembly (`OverworldEncounterSpawner`
  is `DeNelle.Village`, not referenced here) — this is the same constraint every other
  `wo1337-wiring`/`wo1736-probe` case in this file already works under, and follows their exact
  established pattern.

**Verification run on both touched files:**
```
$ python tools/gate_brace.py Assets/_Modules/Village/Enemies/OverworldEncounterSpawner.cs Assets/Editor/Regression/BattleQuiescenceRegression.cs
GATE_BRACE_SUMMARY bad=0 of 2
```
NUL-byte scan: `0` bytes in both files (Python `bytes.count(b'\x00')`).

**Not run by this agent, per explicit instruction:** the Unity `COMPILE_GATE_OK` / `REGRESSION_OK`
batchmode gate, any build, or any commit — those are the lead's to run over the combined tree.

## 6. What the owner can do RIGHT NOW (this fix ships in the next build, not this session)

If she is still standing near that second pack right now: moving more than ~26m away from the
nearer of the two silhouettes in `break_23_error.png`/`break_24_error.png` should let the chase
break off on its own (`ChaseBrokeOff`, `DeaggroRange=26m`) within the next few seconds of walking,
which should return the hero to peaceful pose. If it does NOT clear after putting real distance
between herself and both visible enemies, that is itself the "stalled/blocked chase" case named in
§3 and is the next thing to capture (ideally with a screenshot at the moment it fails to clear).

## 7. Acceptance criteria

- [x] RCA cites captured evidence (break-log.jsonl lines, screenshots, source line numbers) for every
      claim, per CLAUDE.md §11B/§12.
- [x] No change to `BattleLock.cs`, `PursuitBattleProbe.cs`, or `BattleSessionEnd.cs`.
- [x] No wall-clock give-up timer added to the chase.
- [x] New probe is read-only (regression-pinned).
- [x] Brace balance + NUL scan pass on both touched files.
- [ ] Lead: `COMPILE_GATE_OK` + `REGRESSION_OK <n>/<n>` on a fresh log (this agent did not run it).
- [ ] Lead: commit by explicit path, flip this WO's Status line, regenerate `BOARD.html`.
- [ ] PO: felt-verify on next device build (does the "battle model" clear on its own once the owner
      moves away from a second pack; does the new capture, if it recurs, now name a rep with real
      numbers instead of only `PursuitBattleProbe.Probe`).
