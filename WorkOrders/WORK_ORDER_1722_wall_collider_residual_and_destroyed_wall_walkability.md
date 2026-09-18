# WORK ORDER 1722 — An INTACT wall let the player walk straight through it (same defect as the residual collider mismatch), plus ToggleBreach logging resolved

**Status:** DONE - committed aba8c4f7c, gated (COMPILE_GATE_OK/REGRESSION_OK or node --test as applicable). PRIOR: READY FOR LEAD REVIEW
Item 1 is PROVEN to be a diagnostic artifact and needs no wall fix (see the RCA section at the bottom,
2026-09-15). Item 2 is re-opened as UNEXPLAINED — the §2 explanation below rested on item 1 and falls
with it. Item 3 was already closed and is kept here as the record.
**Minted:** 2026-09-14, by the CLI lead, from the owner's own live on-device testing session, at her direct
request: "i want all the data and the screens and the data bundled into a md order."
**Device:** Solana Seeker, `com.denellestudios.echoesofelarion`, build `2026.09.14.369984`
(commit `0e656756e`, the full step-in/step-out breach-flow instrumentation build).
**Silo:** Raid walls / structure damage (`Assets/Editor/WallTools/RaidBaseDresser.cs`,
`Assets/_Modules/Village/Walls/WallSegment.cs`) — same silo as WO-1719/1720, do not run in parallel
with any other lane touching those files.

---

## 1. OPEN — Residual wall collider/renderer mismatch, found live, in a tier already called "clean"

### The proof this contradicts

The WO-1719/1720 headed multi-tier proof (`RaidWallTierProof.cs`, run earlier the same day) reported:

> Regular tier: 78 walls checked, **0 mismatches** — fully clean

That result was real and reproducible at the time — but it measured the state of the scene
**immediately after raid start**, before any gameplay. The finding below comes from **mid-raid, live
play**, ~90 seconds into combat, using a **different, independent diagnostic** already built into
`RaidDeployController.cs` (not the `RaidWallTierProof` tool) — so this is not the same measurement
disagreeing with itself; it is a second instrument finding a mismatch the first one's timing could not
have seen.

### The data, exact, unedited

Captured live via `adb logcat`, device buffer, during the owner's own play session:

```
09-14 17:52:23.779 [Flow:HeroOwner] scene='RaidBase_raider_camp_small' owner=HeroLocomotion ownerCC=none
  ownerAgent=on-mesh scriptedMove=off ... pos=(-37.77, 0.02, -29.90)

09-14 17:52:25.445 [Flow:Raid] nearest WallSegment='Wall_Outer_SE_17' colliderPresent=True
  colliderEnabled=True colliderIsTrigger=False colliderLayer=Structure
  colliderBounds=Center: (-31.00, 2.00, -19.96), Extents: (0.75, 2.00, 1.43)
  rendererBounds=Center: (-31.00, 2.32, -19.96), Extents: (5.54, 10.51, 10.61)
  colliderBoundsIntersectsRay=False rendererBoundsIntersectsRay=True

09-14 17:52:25.446 [Flow:Raid] breach tap missed every WallSegment (hit 'RaidGround') - the standing
  order, if any, is UNCHANGED.
```

Scene: `RaidBase_raider_camp_small` (Regular tier). Wall: **`Wall_Outer_SE_17`**, fully intact
(`colliderEnabled=True`, not collapsed).

**Read the numbers exactly:**

| | Center | Extents (half-size) |
|---|---|---|
| Collider | (-31.00, 2.00, -19.96) | (0.75, 2.00, 1.43) — a normal wall-sized collider, ~1.5m x 4m x 2.9m |
| Renderer | (-31.00, 2.32, -19.96) | (5.54, 10.51, 10.61) — ~11m x 21m x 21m |

The renderer is roughly **7x wider, 5x taller, 7x deeper** than the collider it's attached to. This is
a bigger and more multi-axis mismatch than the original WO-1719 bug (which was a clean, single-axis
~5x height-only mismatch). X and Z are *also* off this time, not just Y.

### Why this is NOT the same false-positive pattern flagged as a known limitation earlier today

The `RaidWallTierProof.cs` tool's own known limitation (documented in its file header and in commit
`75b3359a1`) is that its renderer search is a **1.2m-radius scan of every renderer in the scene**,
which can accidentally pick up an unrelated nearby object (a tower, a decoration) instead of the wall's
own mesh. **That limitation does not apply here.** This capture comes from a different, older piece of
code — `RaidDeployController.cs`'s own `LogBreachTapDiagnostics` (added during the original WO-1719
diagnosis) — and its renderer bounds come from
`nearest.GetComponentsInChildren<Renderer>(true)` (`RaidDeployController.cs:1050`): renderers that are
**children of the `WallSegment` GameObject itself**, not a nearby-radius guess. If this number is real,
it means `Wall_Outer_SE_17`'s own clad art, parented under its own `WallSegment`, is genuinely ~7x
oversized relative to its collider — a real, unexplained defect, not a measurement artifact.

### What is NOT yet known — do not guess past this line

- Whether this is specific to `Wall_Outer_SE_17` alone, or present on other walls in this same rebaked
  scene that simply weren't tapped/measured during this session.
- Whether something changes a wall's scale or re-parents its clad renderer **during live play** (a
  runtime event after raid start — a damage-state visual swap, a distance-based LOD change, or similar)
  that the earlier raid-start-only proof could not have observed, since it measured before any such
  event could fire.
- Whether the WO-1719 rebake (`15f3ff3cb`) is intact in this specific `RaidBase_raider_camp_small.unity`
  scene file at this specific wall's array index, or whether something re-generated/re-touched the scene
  since (check `git log` on that file since `15f3ff3cb` before assuming the rebake regressed).

### Instrument-first plan (CLAUDE.md §12 — do not fix before this)

1. Read `RaidBaseDresser.cs`'s `CladRing`/`SyncWallColliderHeight` path specifically for
   `Wall_Outer_SE_17`'s index in `raider_camp_small`'s ring, and confirm at source whether X/Z sync was
   ever part of the WO-1719 fix (the original fix, per its own commit message, only resynced **Y** —
   "preserving the collider's ground-seat offset and never touching X/Z, per the existing
   footprint-X/Z-untouched convention" — so an X/Z mismatch this large may be a **pre-existing,
   never-fixed** issue the original diagnosis never looked for, not a regression of the Y-only fix).
2. Add or extend logging so a live capture can distinguish "clad renderer's own local bounds are wrong
   at bake time" vs. "something scales/reparents this renderer during live play after raid start" —
   this determines whether the fix belongs in the bake (`RaidBaseDresser.cs`) or in a runtime system.
3. Re-run `RaidWallTierProof.cs` (or an extended version) with a **mid-raid delayed measurement**, not
   only immediately at raid start, to see if this specific mismatch is reproducible from a cold scene
   load or only appears after some minutes of live play.

---

## 2. CLARIFIED — this is the SAME defect as section 1, not a separate destroyed-wall question

Owner, verbatim, correcting the CLI's initial mis-framing of her report: *"my issue was that the wall
was not destroyed and i could walk through."* The wall in question was **fully intact** (not
collapsed) — the report is that an undamaged, standing wall had no effective collision across most of
its visible width.

**This is now understood to be the same bug as section 1's `Wall_Outer_SE_17` finding, not a second,
independent question.** That wall's real collider is only ~1.5m wide (Extents.x = 0.75) while its
visible rendered mesh is ~11m wide (Extents.x = 5.54) — both centered on the same X/Z point. A player
walking anywhere across that extra ~9.5m of visual wall outside the real, much-narrower collider would
correctly experience zero collision, because there genuinely is none there — the wall LOOKS solid across
its full visible width but only a fraction of that width is actually solid. This fully explains "the
wall was not destroyed and i could walk through" without requiring any separate mechanism: fix section
1's collider/renderer mismatch (make the collider's real footprint match what the player actually sees),
and this walkability report closes as the same fix, not a second one.

**Do not implement a separate fix for "walking through intact walls."** Confirm this understanding by
re-running the walk-test (owner walks directly at a wall reported by the live diagnostic as mismatched,
not merely destroyed) once section 1's fix lands, and capturing whether the collision now matches the
visible wall's full width.

**Kept separate and still genuinely open — the destroyed-wall case:** whether a wall the game reports as
COLLAPSED still blocks movement is a different, still-unproven question, now tracked in WO-1721
(destroyed wall visual/collision, including the owner's "step-over collider" and "NavMeshLink if needed"
rulings from this same session) rather than here — WO-1721 is the right ticket for a genuinely-destroyed
wall's collision behavior; this ticket (1722) is about intact walls only.

---

## 3. RESOLVED — ToggleBreach FlowTrace lines were never missing; the logcat ring buffer evicted them

Investigated in depth this session (an Opus lane ruled out four candidate causes with hard evidence:
a reflection write path confined to an editor-only assembly that never ships to the player, a stale
duplicate `RaidDeployController.cs` proven to predate WO-1719 entirely, `FlowTrace.Allowed()`'s
per-system (not per-caller) gating read in full with no mechanism that could split two call sites
sharing the tag `"Raid"`, and `ElarionUiKit.Button`'s click dispatch read in full with no
exception-swallowing). The lane's remaining hypothesis — the project's own documented
`logcat-ring-buffer-destroys-evidence` pattern, where `HandleBreachTap`'s high-frequency lines evict the
much rarer `ToggleBreach` lines from the buffer before a pull — was then **proven directly**: buffer
cleared (`adb logcat -c`), owner pressed Breach exactly once, immediate pull:

```
17:50:36.261 [Flow:Raid] ToggleBreach IN - player touched the Breach button. wasOn=False -> ARMING breach mode
17:50:36.262 [Flow:Raid] ToggleBreach OUT - breach mode ARMED. Deploy disarmed, rally disarmed, tiles/rally button refreshed
```

No code change needed. Recorded here only so the "instrumentation was broken" theory does not get
re-litigated — it wasn't; the evidence just wasn't being read fast enough relative to the buffer's churn.

---

## 4. Screenshots (device, this session, `logs/device/pull-20260914-owner-screenshots/`)

- `Screenshot_20260914-172704.png` — Breach armed, HUD, undamaged wall in view, Regular tier.
- `Screenshot_20260914-172741.png` — "Breach ordered" toast live, troop attacking the ordered wall
  section with a visible ability effect.
- `Screenshot_20260914-173743.png` — second successful "Breach ordered" toast, different wall
  (`Wall_Outer_SN_15`).
- `Screenshot_20260914-173751.png` — "Breach: that is not a wall" toast, tap landed near the top of
  the screen (compass/tower band), not on the wall body — a mis-tap, not a defect.

## 5. Raw log captures (this session, `logs/device/`)

- `pull-20260914-172704-breach-success-playthrough/logcat_full.txt` — 191,302 lines, full device
  buffer, contains the first confirmed live breach-order success (`Wall_Outer_SS_15`, hp=100 at order
  time) and the wall taking real damage afterward.
- `pull-20260914-173751-breachflow-full-trace/logcat_full.txt` — 14,608 lines, the new full
  step-in/step-out `HandleBreachTap`/outcome= trail across two successes and two mis-tap bursts.
- `watch-wall-collision-check/logcat_live.txt` — live capture containing the `Wall_Outer_SE_17`
  finding in section 1 above; left running for the still-open walk-test in section 2.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01Wo8roH1dHRWvBXgGaDeGg7

---

## RCA 2026-09-15 (read-only lane)

Read-only RCA lane, main tree, branch `dev`. No `Assets/` edit, no Unity run, no git action — this
section and line 3 are the only writes. Every file:line below was opened this session; every log line
was read out of the named capture this session (CLAUDE.md §11B).

Three raid commits landed after this WO was minted, plus two uncommitted lanes, and all five change
what the two items even measure:

| Commit | Time | What it changed under this ticket |
|---|---|---|
| `55e3464b4` WO-1723 Lane B | 09-14 20:58 | `CladRing` stopped computing its own partition: one clad panel per `WallSegment`, sized from that segment's own collider, re-parented onto it (`RaidBaseDresser.cs:741-808`) |
| `8b88a5053` WO-1723 | 09-14 19:57 | `RaidNavBake.IsUnderCladZone` added — the visible clad ring is now EXCLUDED from `NavigationStatic` (`RaidNavBake.cs:66-75, 105-117`) |
| `6879f32fa` / `452fc14fd` / `3b4b98834` | 09-14 21:03 → 09-15 15:02 | the three raid scenes regenerated + re-baked (78 → 58 outer segments) |
| uncommitted `Assets/Editor/RaidWallTierProof.cs` | +112 lines | the WO-1722 follow-up scan (`MeasureTierChildFootprint`, `IsTransientCastMarker`, the 90 s idle re-measure) |
| uncommitted `Assets/_Modules/Village/Troops/TroopController.cs` | +74 lines | `DescribeRouteGap` path-gap tracing only — reads `NavMeshPath`, touches no wall collider or obstacle. Not in scope here. |

⛔ **`Wall_Outer_SE_17` — the object §1 is about — NO LONGER EXISTS.** Counted this session out of
`Assets/Scenes/RaidBase_raider_camp_small.unity`: **58** `m_Name: Wall_Outer_*` segments, highest
`SE` index **9**, zero `Wall_Outer_SE_17`. Also present: **66** `Clad_*` prefab-name overrides
(58 per-segment panels + 8 `Clad_Corner_*`), **58** `RuinStep`, **62** `NavMeshObstacle`. Any re-run
of a proof written against the 78-segment ring will not find the WO's wall ids.

---

### ITEM 1 — CLOSED at source. The oversized "renderer" is a combat VFX parented to the wall, not the wall.

**Root cause, proven instance-level, not by inference.** `CastingTelegraphVfx.TryBeginTargetMarker`
(`Assets/_Modules/Village/Vfx/CastingTelegraphVfx.cs:256-269`) instantiates the target-lock prefab
**parented to the targeted unit** — `Object.Instantiate(prefab, targetUnit.position,
Quaternion.identity, targetUnit)` (`:257`), named `"CastTargetMarker"` (`:265`), self-destroying
`windup + 1 s` later (`:268`). When the target is a `WallSegment`, that marker is a CHILD of the wall,
and `LogBreachTapDiagnostics` unions **every** child renderer's bounds
(`RaidDeployController.cs:1051-1057`, `GetComponentsInChildren<Renderer>(true)`), so the AoE-scaled
marker becomes "the wall's visual footprint".

The proving line already exists and was already captured — `logs/device/watch-wall-collision-check/logcat_live.txt:1371`,
**1.238 s before** the measurement this ticket quotes (`:1849`):

```
09-14 17:52:24.207 [Flow:CastTelegraph] target-marker START unit=Wall_Outer_SE_17
  path=VFX/UI/TalentNodePointer caster=Hero (Blaise) ability='target lock (auto)' windup=6.00s
```

Same log, `:97` / `:522` / `:945` — the same marker re-armed on `Wall_Outer_SE_17` at 17:52:06, :12,
:18. The hero had that exact wall target-locked for the whole window. The ticket's giant number is that
marker's AABB, taken at identity rotation against a yawed wall.

**Independently reproduced in a headed capture** (`Builds/raid-wall-tier-proof-diag1/REPORT.md`, run
2026-09-14T18:32): `:917` `FAIL Wall_Outer_SE_35: colliderSize=(1.50, 4.00, 2.86)
rendererSize=(6.26, 16.30, 13.11)`, and its own outlier dump `:918-923` names the children —
`CastTargetMarker` / `Flash` / `ShockWave`, `mesh='(none)'`, `localScale=(1,1,1)`, worldBounds
`(5.324, 12.259, 11.145)` / `(4.071, 9.386, 8.523)` / `(6.263, 14.413, 13.111)`, all on the same
centre. Note the collider size there — **(1.50, 4.00, 2.86)** — is bit-for-bit the WO's own
`Extents (0.75, 2.00, 1.43)` doubled. Same defect class on two other walls on two other tiers.

**The wall geometry itself is clean on X/Z and always was.** Same report, `:85-163`: all **78**
Regular-tier walls read `colliderSize=(2.85, 4.00, 1.50)` vs `rendererSize=(2.85, 3.00, 1.50)` —
`delta=(0.00, 1.00, 0.00)`. X and Z are exact. The 1.00 m Y residual is the WallSegment's own HIDDEN
placeholder mesh (`SegSize.y = 3.00`, disabled by `RaidBaseDresser.HideWallRenderers:602-617`) against
a collider deliberately stretched to the clad's achieved height by
`SyncWallColliderHeight` (`RaidBaseDresser.cs:987-1023`) — i.e. the WO-1719 fix working, not a defect.
The report's own 90 s-idle re-measure (`:244`) returns the identical numbers, so nothing drifts on its
own over time either.

**So §1's three open questions are answered:** it is not specific to one wall (three walls, three
tiers, same marker); nothing rescales or re-parents the wall's art during play (a transient VFX
attaches and self-destroys); and the WO-1719 rebake never regressed. **No `RaidBaseDresser` change is
owed.** The residue is instrument hygiene, and it is a code edit, so it belongs to an implementing
lane, not to this one:

1. `RaidDeployController.LogBreachTapDiagnostics` (`:1051-1057`) has **no** marker exclusion — the
   uncommitted `RaidWallTierProof.IsTransientCastMarker` (`RaidWallTierProof.cs:207-212`) exists only
   on the editor side. The device diagnostic will emit the same misleading giant bounds again today.
2. Worse now than at capture time: post-`55e3464b4` every segment also carries an **inactive** `Ruin_*`
   child (`RaidBaseDresser.cs:785-788`), and `GetComponentsInChildren<Renderer>(true)` takes inactive
   renderers, so that union is over-reporting **by construction** on every wall.
3. `:1045` picks `nearest` by distance from **`ray.origin` (the camera)**, not from the tap, despite
   the comment at `:1034` saying otherwise. `SE_17` was the wall nearest the camera — not necessarily
   the wall she was standing at.

### ITEM 2 — STILL OPEN, and its stated explanation is now DISPROVEN. Do not implement §2 as written.

§2 above says the walk-through is "the same bug as section 1" because the real collider is ~1.5 m wide
against an ~11 m visible wall. **That premise is false**: the 11 m was the VFX marker, and the wall's
X/Z footprint matched its art exactly on all 78 walls in the same-day headed capture. Item 2 therefore
loses its mechanism and returns to unexplained.

**What the capture actually shows about movement.** All 20 `[Flow:HeroOwner]` samples in
`logs/device/watch-wall-collision-check/logcat_live.txt` (`:1257`, `:1339`, `:1490`, …) read
`pos=(-37.77, 0.02, -29.90)`, `velSelf=0.00 velRoot=0.00`, unchanged across the whole window. **No
wall crossing is captured anywhere in this log.** Item 2 rests entirely on the owner's prose — which is
ground truth about what she saw, but it is not a measurement, and nothing here tells us which wall,
where, or in which direction.

**The seam that decides it, read at source.** `HeroLocomotion.cs:4-8` (the corrected header): the hero
is a **`NavMeshAgent` driven kinematically by `_agent.Move(step)`** — and the WO's own capture agrees,
`ownerCC=none ownerAgent=on-mesh`. So **a wall's `BoxCollider` does not stop the hero at all**; the
only thing that can is the navmesh. Two source facts now combine, and the second landed AFTER her
report:

* `RaidNavBake.PrepareDestructibleWalls` (`RaidNavBake.cs:220-245`) bakes the ground under walls
  walkable and gives each `WallSegment` a carving `NavMeshObstacle` sized from its `BoxCollider`
  (`:236-242`) — a **runtime** carve, enabled while `HpFraction > 0`.
* `RaidNavBake.cs:105-117` now excludes from `NavigationStatic` anything with a `WallSegment` or
  `DefenseTower` parent **or under `Zone_Clad`** (`IsUnderCladZone`, `:66-75`, added `8b88a5053`).

⚠ **The live gap that follows, and that no existing check asserts:** the **8 `Clad_Corner_S*_L/R`
stubs** in the current scene are visible wall with (a) **colliders stripped** —
`InstantiateVisual(..., stripColliders: true)` at `RaidBaseDresser.cs:972`, stripped at `:292`;
(b) **no `WallSegment`**, stated in the method's own header at `:904-914`, so
`PrepareDestructibleWalls` (which iterates `WallSegment` only) gives them **no carving obstacle**; and
(c) **excluded from the bake** by `IsUnderCladZone`. Every other span of visible wall is covered by
something; these eight are covered by **nothing that this lane could find**. By construction their
length is ≈ `towerHalf` (`run = 2*halfExtent - 2*towerHalf`, `:906`) and the corner tower does get a
collider-enclosing carving obstacle (`RaidNavBake.cs:180-212`), so they are **probably** covered by the
tower — **and "probably" is exactly what §11B forbids shipping.** It is unmeasured, and it is the one
thing worth measuring first.

⛔ **Do NOT retro-fit this to her 09-14 session.** `IsUnderCladZone` landed at 19:57, **after** the
17:52 capture, and the `NavMesh.asset` in that build was the `f06a73600` (04:19) bake — in which the
clad ring still baked `NavigationStatic`, i.e. nav-solid. **The capture-era mechanism for what she saw
is UNPROVEN and this lane could not close it from the evidence that exists.** What is described above
is a live gap in the CURRENT tree, which is the tree the next build ships.

---

### (b) The proving line / method, per item

**Item 1 — already proven; nothing new to run.** The `[Flow:CastTelegraph] target-marker START
unit=Wall_Outer_*` line (`CastingTelegraphVfx.cs:271-275`) is the existing instrumentation, and
`logcat_live.txt:1371` is the captured instance. If the lead wants it re-confirmed on the current
58-segment ring, the oracle already exists uncommitted:
`RaidWallTierProof.MeasureTierChildFootprint` + `IsTransientCastMarker`
(`RaidWallTierProof.cs:207-212, 255+`) — its marker exclusion is the fix for the false positive, and
its `Regular-after90sIdle` pass is the drift control. **No new FlowTrace is needed for item 1.**

**Item 2 — no existing instrument answers it, and the nearest one says so in its own words.**
`RaidWallContinuityRegression.CheckBoundary` (`Assets/Editor/WallTools/RaidWallContinuityRegression.cs:286-328`)
ray-sweeps the ring at ankle/torso/head using **temporary `MeshCollider` probes** (`:290`, because the
clad has no colliders of its own) and states at `:306`: *"This is not a NavMesh claim."* It proves the
**mesh** is continuous; it never asks whether the **navmesh** is blocked. That is precisely the
question item 2 asks.

⚠ **Whatever is written must run in PLAY MODE.** `NavMeshObstacle.carving` applies at runtime; an
edit-mode `NavMesh.SamplePosition` sweep over the ring will read the whole wall line as walkable and
prove nothing, because `RaidNavBake` deliberately bakes that ground walkable (`:217-219`). The two
existing templates:

* `RaidBreachRuntimeProof` (`Assets/Editor/RaidBreachRuntimeProof.cs:28-40`) — already does exactly the
  right sample (`NavMesh.SamplePosition(centre, out _, .3f, _filter)` at each wall's collider centre,
  skipping walls whose carve already blocks it) but is **MenuItem-armed in a live Play session** and
  samples **`WallSegment` centres only** — it can never see a `Clad_Corner_*` stub, because no
  `WallSegment` exists there.
* `RaidWallTierProof.Run` / `MaybeArm` (`RaidWallTierProof.cs:49-73`) — the SessionState-arm +
  `EnterPlaymode` + `RuntimeInitializeOnLoadMethod` driver pattern, i.e. a self-driving Play session
  from one `-executeMethod`. Its "must be headed" rule (`:24-26`) is about **renderer bounds**; a
  navmesh sample does not need pixels.

**Proposed new probe (one method, no new FlowTrace system needed):** a `RaidWallNavCoverageProof` built
on the `RaidWallTierProof` arm pattern that, in Play mode after a ~3 s carve settle, walks **every
renderer under `Zone_Clad`** — panels AND the 8 corner stubs — and at each panel's XZ centre at
`y = bounds.min.y + 0.15` reports `NavMesh.SamplePosition(..., 0.3f, filter)`. Emit one line per panel,
tagged so it reads in a device log as well as an editor log:

```
[Flow:RaidWallNav] panel='<name>' ownerSegment='<WallSegment or NONE>' obstacle=<none|carving|off>
  centre=<xyz> navSampled=<true|false> -> <BLOCKED|WALKABLE>
```

`navSampled=true` on any panel whose owner wall is alive = a hole in a standing wall, named. Expected
today: 58 `BLOCKED`, and the 8 `Clad_Corner_*` stubs are the answer this ticket is waiting for.
`FlowTrace.Step` is the right verb (once per panel, bake-frequency, not a frame path — the 4-arg
`Measure` overload of §12 does not apply).

### (c) The minimal batchmode command the lead should run

Item 1 — nothing. It is closed on captured data.

Item 2, once the probe above exists (it is an editor-only `.cs`, so it needs an implementing lane
first). Entry point mirrors `RaidWallTierProof`, but a nav sample needs no pixels, so it runs
`-batchmode` and can be judged by a marker on a fresh log:

```powershell
.\run-unity-method.ps1 -Method DeNelle.Editor.RaidWallNavCoverageProof.Run `
  -LogFile Builds\raid-wall-nav-1722.log
Select-String -Path Builds\raid-wall-nav-1722.log -Pattern 'RAID_WALL_NAV_COVERAGE_(OK|FAIL)'
```

Judge the **marker on a fresh log**, never the exit code (CLAUDE.md §8/§16). Absence of the marker is a
FAILURE, not an unknown. The scene under test is
`Assets/Scenes/RaidBase_raider_camp_small.unity` (Regular tier, the tier of the original report) — and
expect **58** segments there, not the 78 this ticket was written against.

If the lead wants a zero-code answer first, the cheapest partial is to re-run the already-written
(uncommitted) tier proof headed, which at least re-confirms item 1 on the new ring:
`RAID_WALL_TIER_PROOF_DIR=Builds\raid-wall-tier-proof-1722b` with
`-executeMethod DeNelle.Editor.RaidWallTierProof.Run` on a **headed** editor (never inside a batchmode
gate run, per `RaidWallTierProof.cs:24-26`). That does **not** answer item 2.

### Unproven, stated as unproven

* Whether the 8 `Clad_Corner_*` stub spans are actually walkable at runtime, or covered by the corner
  tower's own carving obstacle. Geometry says they should be covered; **not measured.** This is the
  whole point of the probe above.
* The capture-era mechanism for the owner's 09-14 walk-through. The clad ring was nav-solid in that
  build's bake, and the log captures no movement at all. **Not closable from the evidence that exists**
  — it needs a fresh felt-test on the current build, with the wall name and the hero's position
  captured at the moment of crossing.
* Whether the hero can be pushed off the navmesh by anything else (knockback, `Warp`,
  `ForeignMoverOwnsTransform`). Not examined by this lane.

**Not done by this lane (read-only):** the `IsTransientCastMarker` port into
`RaidDeployController.LogBreachTapDiagnostics`, the `Ruin_*`/inactive-renderer exclusion in that same
union, the `ray.origin`-vs-tap fix at `:1045`, and the new coverage probe. All four are code edits.

**Also for the lead:** `Assets/Editor/RaidWallTierProof.cs` (+112) and
`Assets/_Modules/Village/Troops/TroopController.cs` (+74) are **uncommitted in the main tree**. The
tier-proof half is the WO-1722 oracle this RCA leans on — it is lost if it is not gated and committed.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_019gsRiEvHBgmyXhPf5bEh3J

---

## IMPLEMENTATION RECORD (2026-09-17) — the four "not done by this lane" items, three closed here

This lane read the RCA's own "Not done by this lane (read-only)" list (four items) and the "(b)"/"(c)"
proposed-probe section, then went to source for each before writing anything (CLAUDE.md §12 — static
read first, no fix on inference).

**Files touched**
- `Assets/_Modules/Village/Troops/RaidDeployController.cs` — added `IsTransientCastMarker` (ported
  verbatim from `Assets/Editor/RaidWallTierProof.cs:207-212`) and applied it inside
  `LogBreachTapDiagnostics`'s active-renderer union, plus a new `renderersSkippedTransientCastMarker=`
  field on the `nearestLine` log so a live capture can show the exclusion firing.
- `Assets/Editor/RaidWallNavCoverageProof.cs` (new) — the item-2 nav-coverage probe the RCA's §(b)
  designed but did not write.

**Item 1 residue, re-checked at source before touching anything (two of the RCA's three listed residue
items were ALREADY FIXED by an intervening commit, `fc893f894` "WO-1777 + WO-1790", landed AFTER this
RCA was written on 09-15):**
1. `ray.origin`-vs-tap bug (`:1045` in the RCA's line numbering) — FIXED by WO-1790. The nearest-wall
   search now measures perpendicular distance from the tap ray (`DistanceFromRay`, ported comment cites
   "WO-1790 sec.3 item 1"), not distance from the camera. Verified by reading the live method: the loop
   at `RaidDeployController.cs:1300-1308` calls `DistanceFromRay(ray, walls[i].transform.position)`.
2. `Ruin_*`/inactive-renderer over-reporting — FIXED by WO-1790. The union at (what is now)
   `RaidDeployController.cs:1312-1325` already filters `!r.enabled || !r.gameObject.activeInHierarchy`
   before encapsulating, which excludes both the disabled placeholder mesh and the `SetActive(false)`
   `Ruin_*` rubble (`RaidBaseDresser.cs:789`). Confirmed by reading `RaidBaseDresser.CladRing` at source:
   `ruin.SetActive(false)` is unconditional on every ruin it builds.
3. **The one residue item still live, and the one this lane actually fixed:** `IsTransientCastMarker`
   had NOT been ported. The active/enabled filter alone does not exclude
   `CastingTelegraphVfx.TryBeginTargetMarker`'s `"CastTargetMarker"` child — it is fully active and
   enabled for the whole windup+1s it lives, per `CastingTelegraphVfx.cs:256-269` read at source this
   session. A hero target-locking a wall while the player is aiming the breach tap at (or near) that
   same wall would still inflate `activeRendererBounds` with the AoE-scaled marker mesh, reproducing the
   exact "wall reads 7x oversized" symptom item 1 diagnosed — the runtime diagnostic was NOT immune to
   its own root cause until this change. Ported the identical name-walk helper
   (`for (var cur = t; cur != null; cur = cur.parent) if (cur.name == "CastTargetMarker") return true;`)
   and wired it into the loop; added a `transientSkipped` counter surfaced on the log line so a future
   capture can show it firing rather than inferring it.

**Item 2 — the coverage probe, built exactly to the RCA's §(b)/(c) spec, not run this session:**
`RaidWallNavCoverageProof.Run()` (mirrors `RaidWallTierProof`'s SessionState-arm +
`EnterPlaymode` + `RuntimeInitializeOnLoadMethod` driver pattern) routes through the real production
flow (`SceneRouter.GoCastle()` then `GoRaid("RaidBase_raider_camp_small")`, Regular tier only — the
tier of the owner's original report; the mechanism under test, `RaidNavBake`/`RaidBaseDresser`, is
identical across tiers, read at source), waits 3s for carving to settle, then for every `Renderer` whose
transform name starts with `"Clad_"` under the scene's `Zone_Clad` root:
- resolves its owning `WallSegment` via `GetComponentInParent<WallSegment>()` (null for the 8
  `Clad_Corner_*` stubs, by design — `RaidBaseDresser.cs:904-914`'s own header),
- reads that owner's `NavMeshObstacle` state (`none|carving|off|disabled`),
- samples `NavMesh.SamplePosition` at the piece's XZ centre, `y = bounds.min.y + 0.15`,
- emits one `[Flow:RaidWallNav] panel=… ownerSegment=… obstacle=… centre=… navSampled=… -> BLOCKED|WALKABLE`
  line (`FlowTrace.Step`, matching the RCA's proposed shape exactly) and the same text to the written
  `REPORT.md`.

Fails the run (`RAID_WALL_NAV_COVERAGE_FAIL`) only when a piece with a **live** owning `WallSegment`
(`HpFraction > 0`) samples walkable — that is the named, provable version of "an intact wall you can
walk through". An ownerless corner-stub gap is reported (with its own `ownerlessWalkable` tally in the
per-tier summary line) but does NOT fail the marker on its own, because whether the corner tower's own
carving obstacle covers that span is the RCA's own explicitly-unproven question, not something this lane
is entitled to assume true or false.

**NOT RUN THIS SESSION — flagged, not claimed.** Per this lane's brief ("do not run the Unity compile
gate or DataRegression yourself... the lead will batch-gate"), `RaidWallNavCoverageProof.Run` has not
been executed. `RAID_WALL_NAV_COVERAGE_OK`/`FAIL` on a fresh log is therefore still open — the lead (or
the next lane) should run:
```
.\run-unity-method.ps1 -Method DeNelle.Editor.RaidWallNavCoverageProof.Run -LogFile Builds\raid-wall-nav-1722.log
Select-String -Path Builds\raid-wall-nav-1722.log -Pattern 'RAID_WALL_NAV_COVERAGE_(OK|FAIL)'
```
and read `Builds\raid-wall-nav-coverage\REPORT.md` for the per-panel lines. **Whether the 8
`Clad_Corner_*` stubs are actually covered by the corner tower's obstacle remains unmeasured until that
run happens** — this lane built the oracle, it did not fire it.

**Verification performed this lane (read-only, no Unity run):**
- `python tools/gate_brace.py Assets/_Modules/Village/Troops/RaidDeployController.cs` →
  `GATE_BRACE_SUMMARY bad=0 of 1`.
- `python tools/gate_brace.py Assets/Editor/RaidWallNavCoverageProof.cs` →
  `GATE_BRACE_SUMMARY bad=0 of 1`.
- NUL-byte scan (`data.count(b'\x00')`) on both files → `0` on each.
- Confirmed namespaces/usings by grep at source: `WallSegment` and `RaidDeployController` both
  `namespace DeNelle.Village`; `SceneRouter` is `namespace DeNelle.Core` (`public static class
  SceneRouter`, `SceneRouter.cs:37,119`) — matches the `using DeNelle.Core;` in the new probe file, same
  as the existing `RaidWallTierProof.cs`.
- Did **not** touch `Assets/Editor/RaidWallTierProof.cs` or
  `Assets/_Modules/Village/Troops/TroopController.cs` — both were already flagged uncommitted by the RCA
  and are out of this lane's silo (WO-1719/1720 overlap risk named in this WO's own header).
- No `.unity` scene file touched. No git action taken (staging/commit is the lead's, per this lane's
  brief).

**Open for the lead:** run the new probe (command above), read the marker + `REPORT.md`, and decide
whether the corner-stub gap (if any) needs its own fix (e.g. giving `Clad_Corner_*` stubs their own
carving obstacle, or widening the corner tower's obstacle to provably enclose that span) before this WO
can close for real. Until that run happens, item 2 is **instrumented, not closed**.
