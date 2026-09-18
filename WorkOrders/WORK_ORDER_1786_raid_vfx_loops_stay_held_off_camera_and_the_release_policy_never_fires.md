# WORK ORDER 1786 — Enemy-caster VFX loops stay held off-camera for 89 s inside the raid, and WO-1473's release policy says so itself

**Status:** DONE - committed 7c726388a, gated (COMPILE_GATE_OK/REGRESSION_OK or node --test as applicable). PRIOR: READY FOR LEAD REVIEW

**Minted:** 2026-09-16 by the raid-polish audit lane (number PRE-ASSIGNED from the block 1777-1790; this lane did NOT touch `CLI_LANES_WO_NUMBERS.md`)

**Silo:** VFX loop budget — the `[Flow:Vfx]` loop registry / release policy (WO-1473's owner). **No camera code (WO-1785 lane), no perf probe (WO-1779 lane), no `.unity`, no bake.**

**Build under test:** `2026.09.16.371701` (`ProjectSettings/ProjectSettings.asset:148`), built 2026-09-15 22:06 from `f6653501f`.

**Video path:** act 2. This is a contributor to the 18-27 fps measured in WO-1779, and the held slots starve the effects the video actually needs. P1.

---

## 1. SYMPTOM

Looping VFX that should be released when they leave the camera are held indefinitely. In the raid this consumes over half the loop budget and the slots are spent on enemies and props nobody can see.

## 2. EVIDENCE — the trace line indicts itself

From `Logs/device/pull-20260916-143101-bastion-owner-run/logcat_full.txt` (grepped, 2026-09-16):

```
[Flow:Vfx] STUCK LOOP Aura_EnemyCaster owner='RaidGuard (orc-shaman-Lv7-8)'#-218406 age=89s
  — OFF CAMERA for 6s and STILL HELD: past the 6s release grace. Holding 1 of 24 loop slots (now 9/24).
  The policy exists (WO-1473) — if this line appears, the policy is not doing it[s job]
```

⛔ **That last clause is the finding.** The instrument was written to be an assertion: its author recorded that the line appearing *at all* means WO-1473's release policy failed. It appears **five times**, on four distinct owners:

| held loop | owner | age |
|---|---|---|
| `Aura_EnemyCaster` | `RaidGuard (orc-shaman-Lv7-8)` | **89 s** |
| `Harvest_Wood` | `LumberMill` | **231 s** |
| `Harvest_Iron` | `IronMine` | 17 s |
| `Collector_Ready` | `LumberMill` | 15 s |

**Budget pressure inside the raid window** (log lines 3038129-3062000): `LOOPS 3/24 held, 8 registered` → `LOOPS 11/24`, `13/24 held, 18 registered`, `13/24 held, 20 registered`. **More than half the 24-slot budget, in a raid.**

**And town/overworld loops are still resident during the raid.** In the same window:

```
[Flow:Vfx] Portal_Threshold_Aura  owner='DungeonWorldPortal_dg_hollow_roads'#-N  age=Ns  [SUSPENDED …]
[Flow:Vfx] Portal_Threshold_Aura  owner='DungeonWorldPortal_dg_ember_deep'#-N     [SUSPENDED …]
[Flow:Vfx] PP_GroundFog  owner='DungeonWorldPortal_dg_hollow_roads'   [SUSPENDED …]
[Flow:Vfx] PP_GroundFog  owner='DungeonWorldPortal_dg_healers_cottage'[SUSPENDED …]
[Flow:Vfx] Poi_NodeAura  owner='(unparented) [Hovl_Poi_NodeAura]'#-N  age=Ns
```

⚠ Note `owner='(unparented) [Hovl_Poi_NodeAura]'` — **a loop whose owner has no parent**, i.e. an orphan holding a slot. `Poi_NodeAura` also appears in the whole-capture `STUCK LOOP` set.

## 3. WHAT TO DO

1. **Find WO-1473's release policy and prove why the 6 s grace does not expire** for an off-camera owner. The line already tells you the grace was exceeded, so the defect is in the release path, not the detection — instrument the release decision (`FlowTrace.Step` on *why it chose to hold*), capture, then fix the step the data names (CLAUDE.md §12). ⛔ Do not "raise the budget" — that hides it.
2. **Release orphans unconditionally.** A loop whose owner is `(unparented)` or destroyed has no camera test to pass; it should never be able to hold a slot.
3. **Dungeon-portal and collector loops must not survive the raid scene handoff.** Either they are released on scene unload, or the registry is scene-scoped. This is the same class of leak as the town probe in WO-1779 (a town system alive inside a raid) — **but a different file, so keep the lanes separate.**
4. Leave every `FlowTrace` call in place — instrumentation is permanent (CLAUDE.md §12, owner ruling 2026-08-09). Flagging off is allowed later; stripping never is.

## 4. ACCEPTANCE

A fresh Seeker capture of one Bastion raid:

1. `grep -c "STUCK LOOP"` inside the raid window = **0**.
2. No `Portal_Threshold_Aura`, `PP_GroundFog`, `Harvest_*` or `Collector_*` loop appears in any `[Flow:Vfx] LOOPS`/held listing after the raid scene loads.
3. Peak `LOOPS n/24 held` in the raid is reported, and it is **below 8**.
4. `grep -c "(unparented)"` in the loop listings = **0**.
5. `python tools/gate_brace.py` exit 0, then `COMPILE_GATE_OK` + `REGRESSION_OK <n>/<n>` on fresh logs.

## 4B. FINDINGS 2026-09-17 — ⛔ THE HEADLINE PREMISE IS REFUTED BY THE WO'S OWN LOG

Implementation lane, read directly out of the capture this WO cites
(`Logs/device/pull-20260916-143101-bastion-owner-run/logcat_full.txt`, 377 MB, grepped at source).

### The release policy fires. Every time. The DETECTOR was racing it.

Two facts settle it, and the second is decisive:

1. **All 14 `STUCK LOOP` lines in the whole capture read exactly `OFF CAMERA for 6s`** — never 7 s,
   never 12 s, never 89 s off-camera. The `age=89s` in §2 is the loop's total lifetime, not its
   off-camera streak.
2. **Every one is followed by its own `LOOP RELEASED … reason=off camera 6s` within 52–218 ms:**

| STUCK LOOP at | matching LOOP RELEASED at | gap |
|---|---|---|
| `13:17:56.723` `PP_GroundFog` / `Poi_NodeAura` ×3 | `13:17:56.775` / `.776` | **52 ms** |
| `13:20:02.207` `Collector_Ready` / `TreeofLifeAura_Aura` | `13:20:02.285` | **78 ms** |
| `13:24:06.394` `Harvest_Wood` (age=231s) | `13:24:06.612` | **218 ms** |
| `13:25:56.756` `Aura_EnemyCaster` `orc-shaman-Lv7-8` (age=89s) | `13:25:56.892` | **136 ms** |

The capture carries **189 `LOOP RELEASED` and 146 `LOOP RESUMED`** lines. Nothing was "held
indefinitely"; the longest hold past the grace was a fifth of a second.

**Root cause:** `AuditRegistryAges` re-derived "stuck" from its own copy of
`OFFSCREEN_RELEASE_GRACE` (6 s) on the **5 s** audit cadence, while `TickLoopReleasePolicy` acts on
the **0.5 s** policy cadence. Two clocks at one threshold. Any audit landing in the ≤0.5 s window
after the streak crossed 6 s but before the next policy tick printed *"the policy is not doing its
job"* about a policy that was about to do it — and `rec.Warned` latched the accusation **for ever**,
so that record could never be reported again if it later went genuinely stuck. Duplicated state at a
threshold, the failure class CLAUDE.md §2/§5/§16 each describe. **This WO was raised off those false
lines.**

### Acceptance, item by item, against the data

- **§4.1 `grep -c "STUCK LOOP"` = 0** — addressed at the real cause (the oracle), not by muting it.
  The detector now reports the **policy's own** consecutive should-have-released tick count, taken
  **after** the action ran, so it still trips on a release that was decided and did not take effect.
- **§4.2 no portal/harvest/collector loop in a held listing after the raid loads** — ⚠ **already
  true, and the WO misreads its own dump.** The raid-window peak block (`logcat_full.txt:3038503`,
  `13:24:30`, `RaidBase_IronBastion`) is `LOOPS 13/24 held, 18 registered`. All five town/portal
  rows carry **`[SUSPENDED …s, holds no slot]`**. The 18−13 difference *is* those five. They are
  registered, never held. The dump line says so in its own text.
- **§4.3 peak `LOOPS n/24 held` below 8** — ⛔ **cannot be met without deleting live content, and
  should not be.** The 13 held slots in that block are `1× Aura_Necromancer` (RaidBoss) +
  `12× Aura_EnemyCaster` (RaidGuard hollow-acolytes and orc-shamans), **every one `age=3s`**, every
  one a raid enemy on camera. There is no town leak in the peak. A sub-8 ceiling means fewer enemy
  auras — a content ruling for the owner, not a bug fix. **Left unimplemented; flagged for ruling.**
- **§4.4 `grep -c "(unparented)"` = 0** — ⛔ **declined, item §3.2 is factually wrong.** The WO says
  an unparented loop "has no camera test to pass". It does: the frustum test runs on
  `rec.Host.transform.position` (`VFXManager.TickLoopReleasePolicy`), not on the owner. Proof — the
  unparented records are released **by that very test**: `LOOP RELEASED Poi_NodeAura
  owner='(unparented) [Hovl_Poi_NodeAura]'#-34714 age=42s reason=off camera 6s`. These are
  world-space POI markers the player looks at; releasing them unconditionally would delete a working
  feature. The WO conflates **unparented** (never had an owner — normal) with **orphaned** (had one,
  it was destroyed — a defect). `LoopRecord.Unparented` exists to keep them apart.
- **§3.3 scene handoff** — the registry genuinely is **not** scene-scoped (`DontDestroyOnLoad`,
  `VFXManager.cs:94`; zero `SceneManager` hooks in the file). But the data shows the cost is **zero
  held slots** — the policy suspends them on `owner disabled` / `off camera`. **No change made:** a
  scene hook that returned hosts whose callers still hold live `VFXHandle`s would reintroduce the
  WO-955 double-return the block comment at `VFXManager.cs:1518` exists to prevent. Suspended-and-
  not-freed is the correct state. Raise a separate ticket if the ~5 idle registry rows matter.

### What changed

- `VfxLoopReleasePolicy.ShouldHaveReleased(...)` + `IsStuck(int)` + `StuckAfterPolicyTicks = 2` —
  pure, no Unity, headless-pinnable.
- `LoopRecord.PolicyTicksHeldPastGrace` / `HeldReason` / `HeldLogged`; the policy feeds them once per
  tick and clears them (and `Warned`) on recovery, so the oracle keeps no clock of its own.
- New `FlowTrace.Warn` `LOOP HELD AGAINST POLICY` naming the measured decision inputs, **latched one
  per record** (never a 0.5 s × N firehose — `logcat-ring-buffer-destroys-evidence`).
- A `LOOP POLICY STALLED` heartbeat was written and **deleted before review**: it could not fire
  (the audit and the policy check run in the same `SweepOneshots` call, so the tick can never be
  measurably stale when the audit reads it). The reasoning is recorded at the site instead of a
  check that reads as coverage.
- The audit now skips accessibility loops (WO-1229) — the old one would have reported that ruling
  working as a bug.
- `VfxLoopFlagRegression`: 10 new oracle cases + a 128-combination `ShouldHaveReleased`/`Decide`
  agreement sweep, with the red proof written at the case head; and a dated correction to the
  `:753` header citation, which presented a bare `STUCK LOOP` line as proof of a policy failure.

**Not verified here:** no Unity process was run by this lane (concurrent lanes; one seat gates).
`COMPILE_GATE_OK` / `REGRESSION_OK` and a fresh Seeker capture are the lead's.

---

## 5. DO NOT TOUCH

The Mage ability VFX mappings (WO-1776 lane), ability SFX (WO-1781 lane), `TownActivityProbe` (WO-1779 lane), the camera (WO-1785 lane), any `.unity` file.
