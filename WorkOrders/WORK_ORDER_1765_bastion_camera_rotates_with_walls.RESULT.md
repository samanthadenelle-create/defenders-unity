# WO-1765 RESULT — raid over-the-shoulder profile, the framing scan stops admitting walls, and the yaw instrument

**Status:** IMPLEMENTED, NOT YET GATED
**Lane:** implementation, 2026-09-16. No Unity gate run, no commit, no `.unity` touched, no
`CLI_LANES_WO_NUMBERS.md` edit — all by instruction (single Unity seat; the lead gates the combined
tree).
**⚠ REVISED 2026-09-16 after the lead's town-protection ruling** — the C1 correction (narrowed scan
mask AND the structure filter) is now **RAID-SCOPED**, not global; town / Village2 behaviour is unchanged
byte for byte. Everything else ships as before (third branch, shoulder 0.6, collision on, 0.6 pull-in, no
rename). Diff summary in §11.
**WO (single survivor):** `WorkOrders/WORK_ORDER_1765_bastion_camera_rotates_with_walls.md`
**Merged in and DELETED:** `WorkOrders/WORK_ORDER_1765_raid_camera_spins_over_the_shoulder_profile.md`
(it was untracked, so the deletion needs no git stage; `ls WorkOrders | grep 1765` now returns the WO
and this RESULT and nothing else).

---

## 1. Files touched — TWO `.cs`, both in scope, plus the two WO files

| Path | Change |
|---|---|
| `Assets/_Modules/Village/Hero/SmartMobileCamera.cs` | **MODIFIED** |
| `Assets/Editor/Regression/CameraRaidFramingRegression.cs` | **NEW** |
| `WorkOrders/WORK_ORDER_1765_bastion_camera_rotates_with_walls.md` | Status flipped; Part I-B (the measured capture) and Part II (the merged ticket) added |
| `WorkOrders/WORK_ORDER_1765_raid_camera_spins_over_the_shoulder_profile.md` | **DELETED** after folding in, per the lead |

**NOT touched, deliberately:** `CameraWallOcclusionRegression.cs` (its pins were re-verified, not
rewritten), `HeroControlEnsurer.cs`, `WallSegment.cs`, `TroopController.cs`, `RaidAssaultAi.cs`,
`DataRegression.cs`, `DungeonFpvRegression.cs`, `DungeonCameraTightRoomRegression.cs`, every `.unity`,
`CLI_LANES_WO_NUMBERS.md`, `BOARD.html`.

### What changed in `SmartMobileCamera.cs`

1. **A raid camera profile as a THIRD BRANCH in the existing seam** — `RaidCam` (nested, public so the
   oracle drives it), `ApplyRaidSeat()`, `SnapshotVillageCameraValues()` /
   `RestoreVillageCameraValues()`, and one resolver that writes both profile flags from
   `CameraSceneProfile {Town, Dungeon, Raid}`. Routed by `ResolvesToRaidCameraProfile` → the canonical
   `HubScenes.IsRaid`; **never** a fresh `StartsWith("RaidBase")`.
2. **`IsFramingSubject(IDamageable)`** — pure, public. A live Hostile candidate that is ALSO an
   `IDamageableStructure` is REJECTED, so the framing scan can never choose a wall, gate, tower or the
   spire. **Applied in the RAID branch only** (`bool filterStructures = _raidProfileActive`); every other
   scene runs the pre-WO-1765 admission test verbatim. `ScanForEnemies` records the winner's
   name/type/distance, the switch count and a `structsRejected` count.
3. **`ComputeDefaultEnemyScanMask()` + `ResolveEnemyScanMask()`** — narrow the scan mask by layer NAME,
   called from **`ApplyRaidSeat` only** (one call site, pinned), with `_villageEnemyMask` snapshotted and
   restored so a raid→town transition cannot carry it into the hub. `HeroControlEnsurer` untouched.
   **`AppliesRaidScanNarrowing(sceneName)`** is the single pure predicate that scopes both halves.
4. **The yaw instrument** — `MeasureViewYaw`, `_lastRecenterStep`, `ShouldEmitYawEvidence`, the
   heartbeat extended and un-gated to raids, and a spike-edge `Warn`.
5. **Two stale comments corrected** — the header that said the collision mask omits "Enemy(8)" (Enemy
   is **7**; 8 is Structure), and the `_enemyMask` tooltip that promised the Enemy layer while the
   initialiser was `~0`.

---

## 2. `DataRegression.cs` REGISTRATION LINE — for the lead (this lane does not own that file)

Insert immediately **after** the camera-wall-occlusion line at
`Assets/Editor/Regression/DataRegression.cs:1124`, matching its shape exactly:

```csharp
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "camera-raid-framing suite", () => { if (!DeNelle.Editor.Regression.CameraRaidFramingRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[camera-raid-framing] " + r); });
```

Markers: **`CAMERA_RAID_FRAMING_OK …`** / `CAMERA_RAID_FRAMING_FAIL: …`. It adds **one** case, so the
`REGRESSION_OK <n>/<n>` total moves by one — judge it off the marker on a fresh log, never a doc.

---

## 3. THE MEASURED CAPTURE (mid-lane; full write-up in the WO, Part I-B)

`logs/device/pull-20260916-143101-bastion-owner-run/logcat_full.txt` — 377,140,454 bytes, 3.16 M lines.
**Path cited only; it is excluded from commits and carries tokens. Grep it, never open it.**

- **Build under test is `2026.09.16.371701`**, not the `371627` the RCA named (`:2932865`). All three
  camera commits are in it, proven from the log's own text: the WO-1751 mask
  (`OCCLUSION MASK RESOLVED = 0:Default|3:Tower|6:Building|8:Structure`), the WO-1753 gate wording
  (`the seat-side gap 0.07m (occluder nearest the seat at 6.87m of 6.94m …)`, `:3038146`) and the
  WO-1734 fade (`OCCLUDER FADED x11 … seat HELD at 6.89m of 6.89m`, `:3038305`).
- **⛔ The raid boom measured 5.75-6.94 m, not 4.5.** Town seat 4.5 + `_combatZoomOut` 2.5 = 7.0, so
  `_combatBlend` sat at ~0.9-1.0 for the WHOLE raid — **the exact consequence C1 predicted from source
  and could not prove.** Now measured in her build.
- **⛔ 83 distinct `Wall_*` GameObjects** were admitted as `HOSTILE STRUCTURE` inside the raid (106
  admits, plus watchtowers), counted from the frame the raid scene became ACTIVE (`:3036838`), through
  the *narrower* `Enemy|Structure` mask. The camera's scan ran on
  `~0`, a superset — so all 83 were candidate framing subjects for the camera, and it took the nearest.
- **⛔ THE COLLAPSE THEORY IS REFUTED ON MAGNITUDE.** Over 42 in-raid pull-in events: worst seat
  **5.97 of 6.76 = ratio 0.883**, mean 0.932, and the 1.2 m floor is **never** reached. WO-1753 worked;
  the pull-in is a ≤12% shortening = at most a 1.13x yaw-gain change. The sibling ticket's "4.3x zoom
  lurch" describes pre-WO-1753 behaviour and no longer happens. What IS notable is **frequency**: 42
  enter + 42 release edges in ~75 s, asymmetric (approach 40 vs return 8), so the boom sawtooths — and
  the occluders fired at 6.17-6.87 m, exactly where the combat zoom had pushed the seat.
- **⛔ `yawSrc=` appears 8 times in 3.16 M lines and ZERO times in the raid.** All eight are from a
  DUNGEON at 13:15 (`:2985334`-`:2986416`). The yaw silence is now measured, not inferred — it is the
  direct justification for the instrument, and it is why the sibling's central claim (the 220 deg/s
  whip IS the spin) is still **unproven**, not wrong.
- Also, same window: **6** `ForceFollowImmediate snap executed` (C5), 1 `SetTarget`, **4**
  `OCCLUDER FADED` lines (x11 / x2 / x10 / …) with the seat held at full boom each time.
- **⭐ A FREE SECOND TICKET, NOT 1765:** the biggest pull-in burst in the whole capture is **78 events
  in the minute 13:22 — in `Main_Castle_Overworld`**, i.e. the TOWN hub (`:3022710`, after
  `LoadSceneWithFade name='Main_Castle_Overworld'` at `:3002017`). The town camera flaps its point-blank
  backstop ~78x/minute in the scene the player spends the most time in. Nothing says it is felt; it is
  an observation, and it should be its own ticket rather than scope creep here. (Other pre-raid bursts:
  33 at 13:10, 47 at 13:17, 18 at 13:20.)

---

## 4. Chosen profile values, with the arithmetic

| | Town | Dungeon | **Raid (shipped)** |
|---|---|---|---|
| seat height / back / **shoulder** | 2.6 / 4.5 / 0 | 1.9 / 3.2 / 0 | **1.9 / 3.2 / 0.6** |
| look-at height | 2.5 | 1.5 | **1.5** |
| **boom, pivot→seat** | **4.5011** | 3.2249 | **3.2802** |
| lead / zoomOut / fovBoost | 1.5 / 2.5 / 4 | 0 / 0 / 0 | **0 / 0 / 0** |
| framing | on | off | **off** |
| occlusion (fade + point-blank backstop) | on | **off** | **on** |
| recenter delay / max / stiffness | 0.4 / **220** / 4 | 1.25 / 70 / 1.4 | **1.25 / 70 / 1.4** |
| pitch band | [-10, 35] | [-5, 20] | **[-5, 20]** |

Seat, look-at, recenter trio and pitch band are **read through `DungeonCameraProfile`, not re-typed** —
already owner-approved (WO-920 / WO-958) and a copy would drift (CLAUDE.md §5). Exposed as
**properties, not `const`**, because a cross-assembly `const` is inlined into `DeNelle.Village` at
compile time and a later Core re-tune would leave a stale copy baked in.

**Boom** = `|(0.6, 1.9−1.5, −3.2)|` = `sqrt(0.36 + 0.16 + 10.24)` = **3.2802 m**, a derived property.

**⛔ Measured from the PIVOT, not the hero's feet — both 1765 tickets had this wrong.**
`ApplyCollision` casts from `pivot = position + up * _lookAtHeight`, so town = `|(0, 0.1, −4.5)|` =
**4.5011** (which is what `CameraWallOcclusionRegression.CheckSeatSidePullInGate` already states:
`const float boom = 4.5f; // the shipped seat distance from the pivot`) and dungeon = **3.2249**.
The sibling's `sqrt(2.6²+4.5²) = 5.199` / `sqrt(1.9²+3.2²) = 3.72` are feet-relative and the gate never
sees them — **its acceptance pin `boom 3.72 ± 0.05` would FAIL on correct code**, so the merged
acceptance pins 3.2802 and drives the real statics.

**Pull-in arithmetic.** The gate fires when `(boom − gateDistance) < 0.6`, and `gateDistance` is a
spherecast hit from the chest = `surfaceDistance − 0.35`. So a wall surface at D fires it for
`D ∈ (boom − 0.25, boom + 0.35]`. Hero body radius **0.4** (`HeroLocomotion.cs:989`).

| Geometry | Far face | Gate | Fires? | Seat |
|---|---|---|---|---|
| raid 3.2802, 3.0 m conservative span | 2.6 | 2.25 | no (gap 1.03) | full |
| raid 3.2802, documented ~3.9 m aperture | 3.5 | 3.15 | **yes** (gap 0.13) | **2.95** = 0.33 m (10%) |
| town 4.5011, 3.0 m span | 2.6 | 2.25 | no | seat lands **1.9 m beyond** the far wall |
| **measured raid ~6.9 m** (zoom pinned on) | 6.17-6.87 | — | **42x** | 0.883-1.0 of full |

Both raid rows are bounded by the occluder and never reach the floor, so the yaw gain moves ≤1.11x.
**Both are pinned behaviourally** in the new suite by calling `ShouldPullIn` /
`AllowedCameraDistance`. The measured row is why zeroing `CombatZoomOut` matters: it is what pushed the
seat out into the occluder band 42 times.

**The strongest claim of the fix:** with `LeadDistance = 0` **and** framing off, `leadTarget` IS the
hero's chest, so `leadLateral → 0` and **in a raid the only thing that can rotate the view is
`_panYaw`** — player drag plus the lazy 70 deg/s recenter. C1's effect, C2 and C4's amplification of
either are structurally absent, not merely detuned. The heartbeat prints `leadLateral=` so a capture
confirms it rather than trusting this sentence.

---

## 5. `python tools/gate_brace.py` — PASTED OUTPUT (exit 0)

```
$ python tools/gate_brace.py Assets/_Modules/Village/Hero/SmartMobileCamera.cs Assets/Editor/Regression/CameraRaidFramingRegression.cs
GATE_BRACE_SUMMARY bad=0 of 2
exit=0
```

Per file, both clean:

```
$ python tools/gate_brace.py Assets/_Modules/Village/Hero/SmartMobileCamera.cs
GATE_BRACE_SUMMARY bad=0 of 1
exit=0
$ python tools/gate_brace.py Assets/Editor/Regression/CameraRaidFramingRegression.cs
GATE_BRACE_SUMMARY bad=0 of 1
exit=0
```

## 6. NUL-byte scan + raw brace count — PASTED OUTPUT

```
Assets/_Modules/Village/Hero/SmartMobileCamera.cs        bytes=160547 NUL=0 braces=213/213
Assets/Editor/Regression/CameraRaidFramingRegression.cs  bytes=41944  NUL=0 braces=29/29
```

Both NUL-free (CLAUDE.md §1 / WO-434 guard) and raw-brace balanced, which satisfies both the §1
one-liner's count and `gate_brace.py`'s stricter port of the gate's own scanner.

**Nested quotes inside interpolation holes were hoisted into locals** (`profileName`, `roomField`,
`avoidance`, `mode`, `suspect`) because `CompileGate.BraceBalanced` has no interpolated-string model —
CLAUDE.md §1's named trap, where a file reads balanced raw and unbalanced at the gate and
`COMPILE_GATE_OK` is silently withheld.

**Paren check, done properly rather than by subtraction.** The raw count is 900 open / 898 close, i.e.
two unbalanced — so I stripped comments and string/char literals and counted **CODE ONLY**:
`parens=529/529 BALANCED`, `braces=113/113` (and the suite: `parens=240/240 BALANCED`, `braces=29/29`).
Both unbalanced parens are in `///` doc comments: HEAD's pre-existing one, and one this lane added at
`SmartMobileCamera.cs:593` — the interval notation `D ∈ (boom − 0.25, boom + 0.35]`, an open paren closed
by a bracket, which is correct mathematics and unbalanced text. Checked this way because a stray paren in
*code* is a compile error the brace gate cannot see, and "the delta is symmetric" is not the same claim as
"the code is balanced". Braces are 213/213 raw and `gate_brace.py` — the port of the gate's own scanner —
is clean.

## 7. Board

`**Status:** IMPLEMENTED, NOT YET GATED` — the exact `**Status:** <label>` form. Verified against the
board parser itself rather than assumed: `board_build.bucket_of("IMPLEMENTED, NOT YET GATED", True)` →
**`Done`**, and `board_build.status_contradiction(...)` → **`''`** (no contradiction; "NOT YET GATED"
is not one of the `NOT (DONE|BUILT|ADDED)` phrases the lint looks for). **`BOARD.html` was NOT
regenerated** — the lead regenerates and commits it with the work.

---

## 8. Existing pins re-verified green against the EDITED file (nothing weakened)

Each assertion re-implemented in Python over the edited source, including the brace-matched method
spans. **All pass.**

- **`CameraWallOcclusionRegression`** (not modified): inside `ApplyCollision` — `FadeOccluder(col)`,
  `RestoreFadedNotHitThisFrame()`, `SelectOccluderGateDistance(_occluderDistances)`,
  `ShouldPullIn(fullDist, occluderGateDist, _occluderPullInDistance)`,
  `fullDist, occluderGateDist, _collisionSkin, _minCollisionDistance`, `_minCollisionDistance`,
  `TraceSeatEmbeddedButUnseen(seat)` all present; the three forbidden strings
  (`nearestOccluderDist < float.MaxValue`, `nearestOccluderDist < _occluderPullInDistance`,
  `fullDist - nearestOccluderDist`) all absent. File-wide: `OCCLUDER PULL-IN ENTERED/RELEASED`,
  `OCCLUDER FADED`, `CAMERA SEAT EMBEDDED IN UNSEEN GEOMETRY`, `OCCLUSION MASK RESOLVED`, the
  near-plane cap and the smooth-then-collide order; `FadeOneRenderer`'s two restore pins.
- **`DungeonCameraTightRoomRegression`** (out of scope): all eight regexes, including
  `if\s*\(_dungeonProfileActive\)\s*\n\s*EmitDungeonHeartbeat\(dt\)` — see §9 item 2.
- **`DungeonFpvRegression`** (out of scope): `ApplyDungeonProfileIfNeeded` present and keyed off
  `HubScenes.IsDungeon` within 4000 chars; dungeon seat from `DungeonCameraProfile.*`; all five "off"
  regexes; reversibility; no `DeNelle.Dungeons` reference.
- **`FrameBudgetMeasureRegression`**: the 4-arg `Measure("Perf", "SmartMobileCamera.LateUpdate", 4f, 1f)`
  scope is untouched.

⚠ **This proves STRINGS and method spans, not that C# compiles.** See §9 item 4.

---

## 9. ⛔ WHAT I COULD NOT DO AS INSTRUCTED, AND WHAT I COULD NOT PROVE (CLAUDE.md §11B)

**1. The `m_Bits 256` instruction names the WRONG LAYER, so it was implemented by intent, not
literally.** `ProjectSettings/TagManager.asset` (read at source 2026-09-16):
`0 Default, 1 TransparentFX, 2 Ignore Raycast, 3 Tower, 4 Water, 5 UI, 6 Building, 7 Enemy,
8 Structure`. **`256 = 1 << 8` is STRUCTURE; Enemy is 7 (128).** The baked town cameras'
`_enemyMask: 256` is therefore a *structure-only* mask — those cameras can frame walls and only walls.
Copying 256 would have shipped that into the raid while looking like a fix. Implemented:
`ComputeDefaultEnemyScanMask() = Default | Enemy`, by name, **and RAID-ONLY per the lead's follow-up
ruling — see §11.** `Default` kept because `DragonBoss.cs` sets
no layer at all (grepped), so a boss may sit there; `Structure`/`Building`/`Tower`/`UI`/`Water` OUT,
which is what keeps a raid's 158 `Wall_*` colliders out of a **32-slot** buffer they could fill before
a mob was seen. The new suite pins the finding so 256 cannot come back.
**⚠ ITEM (b) OF THIS — the town felt-change — WAS RULED ON AND IS NOW CLOSED.** The lead ruled the town
must not move in this build, so the narrowing is raid-scoped (§11) and the baked `256` **stays effective
in town**, as a follow-up for a `.unity`-owning lane (§12.1). Nothing about town framing changes here.

**2. THE RENAME R1 ASKED FOR WOULD TURN TWO OTHER LANES' SUITES RED, so it was not done.** R1 wanted
`ApplyDungeonProfileIfNeeded` → `ApplySceneCameraProfileIfNeeded` and `_dungeonProfileActive` →
`_lockedOtsProfileActive`. Both are source-text-pinned **outside my scope**:
`DungeonFpvRegression.cs:168-172` requires the literal `ApplyDungeonProfileIfNeeded`, and
`DungeonCameraTightRoomRegression.cs:100` requires
`if\s*\(_dungeonProfileActive\)\s*\n\s*EmitDungeonHeartbeat\(dt\)` — i.e. it pins the exact
dungeon-only gate the ruling widens. So the historical names are kept (documented at their
declarations) and the heartbeat call site reads `if (_dungeonProfileActive) … else if
(_raidProfileActive) …` instead of one `||`. Behaviourally identical, both suites green. **The rename
is a two-line follow-up in those two files**, and a lint that pins the old shape is the real defect.

**3. R1 became R2 (the third branch), which is the sibling ticket's own fallback — and deliberately
so.** R1's prerequisite was to PROVE three room-topology blocks inert in an open arena (ceiling clamp,
`DungeonRoomSeat`, `FacingLookAhead`). That proof cannot be produced from here, and "probably inert" is
what §11B forbids. A third branch leaves all three gated on `_dungeonProfileActive`, inert **by
construction**, nothing to prove on a device. The regression pins the *inverse* (none may be re-gated
to include the raid). **One deviation from R2 as written:** the profile lives as
`SmartMobileCamera.RaidCam`, not a new `Assets/_Modules/Core/World/RaidCameraProfile.cs` —
`Assets/_Modules/Core/**` is outside this lane's file scope. Moving it later is a pure lift.

**4. R2b: the BEHAVIOUR shipped; the split switch and `0.6 → 0.35` did NOT — and the 0.35 is unsafe.**
With `CollisionEnabled = true` a raid already gets fade **and** a pull-in that only ever fires
point-blank: `targetFrac` starts at 1 and the only path that lowers it is already gated by
`ShouldPullIn` against `_occluderPullInDistance`. Splitting `_collisionEnabled` would add state whose
only distinct setting (fade without the backstop) lets the camera body embed in a mesh, and its one
genuine consumer is the DUNGEON — a different ticket with its own pinned suite. And **0.35 is off by
the near plane**: the body needs `radius 0.35 + near clip 0.08 = 0.43 m`, the figure the code states at
that very gate. At a 0.35 threshold a 0.40 m gap fires nothing while the near plane is already through
the surface. **0.6 kept.** (The measured data supports leaving it: the shipped 0.6 produced a worst-case
0.883 ratio and never touched the floor.)

**5. THE YAW ITSELF IS STILL UNMEASURED, even with the 377 MB capture.** `yawSrc=` does not exist in a
raid log at HEAD, so the field that would name the dominant cause is absent. The capture **refutes**
the pull-in collapse on magnitude and **confirms** C1's precondition and its combat-blend consequence,
but the recenter whip (C3), the movement lead (C2) and C1's *effect* all live in fields the shipped
instrument creates. **No claim here says C1 or C3 is proven dominant.**

**6. No Unity gate was run and no Unity marker is claimed.** No `COMPILE_GATE_OK`, no `REGRESSION_OK`,
no `CAMERA_RAID_FRAMING_OK` observed — by instruction. **"It compiles" is NOT proven.** The suite is
written to run in batchmode with no PlayMode (no `SmartMobileCamera` is instantiated; the framing cases
use plain C# doubles, which is why `IsFramingSubject` takes an interface).
`DeNelle.Editor.asmdef` references `DeNelle.Core` and `DeNelle.Village` (read today), so the types
resolve.

**6b. Two things I checked rather than assumed, both clean.** (a) **Nothing outside the camera flips
framing back on:** `_framingEnabled` has a public setter, so a runtime caller could defeat the raid
profile and restore C1's effect at a 3.28 m boom — `grep -rn "\.FramingEnabled" Assets --include=*.cs`
returns no caller outside `SmartMobileCamera.cs`. (b) **The project does not treat warnings as errors**
(`grep -rn "warnaserror|TreatWarningsAsErrors" Assets/*.rsp Assets/Editor/CompileGate.cs
ProjectSettings/*.asset` → nothing), but every raid-only value was still converted from `const` to an
expression-bodied property: a `const` is inlined into `DeNelle.Editor` too, which would have made the
oracle's own assertions compile-time constant conditions (CS0162 unreachable-code on the very checks
that must fail when a value moves) **and** baked a second copy of each value into the Editor assembly.

**6c. ⚠ SCOPE GAP FOR THE LEAD — `Garrison_*` / `Outpost1-2` do NOT get this fix.** `HubScenes.IsRaid`
matches only `RaidBase*`, and `IsDungeon`'s own remark calls `Garrison_*` / `Outpost1-2` "open-air raid
targets that keep the outdoor camera". So a raid routed to a Garrison still runs the town camera and the
220 deg/s whip. Deliberately not widened: `IsRaid` is shared with the HUD combat-cluster gate and
RaidDeployController's self-install, so broadening it reaches well outside a camera ticket. Recorded so
the owner's next "still rotating" from a Garrison is not read as a 1765 regression; if Garrisons should
get the seat, it is one added term in `ResolvesToRaidCameraProfile` plus a ruling.

**7. The shoulder 0.6 m is a FELT number with no capture behind it** — the sibling ticket says so
itself. Included because the lead's condition ("only if the dungeon numbers leave the hero visibly
centred") is met by construction: `_followOffset.x` is the only lateral term in the file, the dungeon
sets it to 0, and `AimAt` looks at the hero's own chest. One constant if she disagrees.

**8. "3.0 m wall partition per WO-1723" could not be confirmed at 3.0.** Both lanes tried; WO-1723's
RESULT records a wall module width ~2.78 → **~3.9 m** and the gate opening narrowing from ~8.3 to
**~3.9 m**. The arithmetic therefore runs at **both** 3.0 (the brief's conservative case) and 3.9 (the
documented aperture), and the regression pins both.

**9. Residual paths named but not changed:** `ForceFollowImmediate` still snaps to the UNROTATED
`_followOffset` (C5) and now carries the 0.6 m shoulder unrotated too — out of scope, and changing that
snap's geometry moves every scene-seam/teleport landing; it is COUNTED (`snaps=`, measured at 4 in this
raid). The WO-512 lock-on override bypasses `_framingEnabled`; inert at ship
(`FeatureFlags.LockOn` defaults OFF, `FeatureFlags.cs:400`).

**10. The number collision is closed but worth recording:** two lanes minted 1765 for one defect
(memory `parallel-worktree-lanes-collide-on-wo-numbers`; two lanes both minted 1631 on 09-10 the same
way). Merged and deleted as instructed. Its content was **not** redundant — it carried the pivotal
"a raid runs the TOWN camera because the seam is gated on `IsDungeon`" finding, and its arithmetic
needed correcting in two places (§4 above, §11.1a in the WO).

---

## 10. What closes this

1. Lead gates the combined tree — `COMPILE_GATE_OK` + `REGRESSION_OK <n>/<n>` on a FRESH log (marker,
   never exit code), with `CAMERA_RAID_FRAMING_OK` present and `CAMERA_WALL_OCCLUSION_OK` still present.
2. Add the §2 registration line to `DataRegression.cs`; regenerate `BOARD.html`.
3. Ship a build with the instrument and re-run the capture. The discriminator, now that the fields
   exist: `yawSrc=recenter` with `step=` summing past ~90 deg while the thumb is off the screen confirms
   the recenter whip; `yawSrc=input` throughout refutes it; `framingTarget=Wall_*` or a non-zero
   `leadLateral` in a raid would mean something still moves the look-at despite lead 0 + framing off.
4. Owner felt-verify in Iron Bastion — walk a wall line, stop against a wall, fight at a wall, pass a
   gate. **PO closes, not CLI** (CLAUDE.md §13).

---

## 11. DIFF SUMMARY — the town-protection revision (2026-09-16, second lead ruling)

> **Ruling:** the TOWN must not change felt behaviour in this build. Restrict the narrowed scan mask
> *and* the C1 structure filter to the raid branch; leave the baked town / Village2 cameras exactly as
> they behave today; record the `256 = Structure` finding as a follow-up. Everything else as shipped.

**Why the filter had to be gated too, and not just the mask.** The ruling's own test — *"`IsFramingSubject`
may stay global only if it cannot change town framing today"* — fails: a baked town camera's `_enemyMask`
is `m_Bits: 256` = layer 8 **"Structure" only**, so structures are the *only* thing its scan can see.
Rejecting structures there takes town `_enemyInRange` from "true near a wall" to **never true**, retiring
the town's combat zoom and auto-framing. So both halves are gated, on one predicate, and this is said out
loud in the code at three sites.

### Changes made in this revision — `SmartMobileCamera.cs`

| Before (first pass) | After (ruled) |
|---|---|
| `ResolveEnemyScanMask()` called from `Awake` — **every scene** | Called from **`ApplyRaidSeat` only**; the `Awake` line is replaced by a comment stating the ruling and why the omission is deliberate |
| `_enemyMask` narrowing not reversible | `_villageEnemyMask` snapshotted in `SnapshotVillageCameraValues`, restored in `RestoreVillageCameraValues` — a camera surviving raid→town cannot carry it into the hub |
| `ScanForEnemies` rejected structures in **every** scene | `bool filterStructures = _raidProfileActive;` — raid takes the new rule, **every other scene runs the pre-WO-1765 admission test verbatim** (live + hostile, structures included) |
| — | **new** `public static bool AppliesRaidScanNarrowing(string sceneName)` = `HubScenes.IsRaid` — the single pure predicate that scopes both halves, so they cannot drift apart |
| `_enemyMask` tooltip + header promised a global narrowing | Both rewritten: raid-only, baked values left as authored, "do not tidy the 256 from here" |
| `RaidCam.FramingEnabled` doc said `IsFramingSubject` is "the design correction for every other scene" | Corrected — it is raid-scoped; what it still does in a raid is keep `_enemyInRange` honest and feed `structsRejected` |

### Changes made in this revision — `CameraRaidFramingRegression.cs`

**New case `CheckTownIsUntouched`** — it pins the **SCOPE**, which is the thing a rule-only suite would
have missed:
- `AppliesRaidScanNarrowing` is **true** for `RaidBase_IronBastion`, and **false** for
  `Main_Castle_Overworld`, `Village2`, `Dungeon_HealersCottage`, null and empty.
- The scope predicate and `ResolvesToRaidCameraProfile` **agree for every scene tested** — so the mask can
  never be narrowed in a scene that does not get the raid seat, or vice versa.
- The snapshot/restore pair for `_villageEnemyMask` exists (both halves).
- `ResolveEnemyScanMask()` has **exactly ONE** call site — it used to be called from `Awake` for every
  scene, which is the town change the ruling forbids, and a regex counts it so a second call site fails
  the suite.

**Everything else is unchanged from the first pass:** the third-branch profile, shoulder 0.6, collision
ON, `_occluderPullInDistance` 0.6, no rename, the instrument, and all the earlier cases.

### Re-run proofs after the revision

```
$ python tools/gate_brace.py Assets/_Modules/Village/Hero/SmartMobileCamera.cs Assets/Editor/Regression/CameraRaidFramingRegression.cs
GATE_BRACE_SUMMARY bad=0 of 2
exit=0
```

```
Assets/_Modules/Village/Hero/SmartMobileCamera.cs        bytes=160547 NUL=0 braces=213/213
Assets/Editor/Regression/CameraRaidFramingRegression.cs  bytes=41944  NUL=0 braces=29/29
```

The new suite's own assertions were driven against the edited source and all pass (snapshot present,
restore present, exactly 1 `ResolveEnemyScanMask()` call site, `filterStructures` gate present,
`AppliesRaidScanNarrowing` present), and every out-of-scope pin was re-verified green a second time
(`ALL_PINS_GREEN`: CameraWallOcclusion, DungeonFpv, DungeonCameraTightRoom, FrameBudgetMeasure).

---

## 12. FOLLOW-UPS FOR THE LEAD TO MINT — four, all named by this lane, none fixed here

Full write-ups with evidence are in the WO §17; this is the mintable list.

**12.1 — The baked `_enemyMask: 256` is the STRUCTURE layer, in two scenes.** `.unity` lane.
`Assets/Scenes/Main_Castle_Overworld.unity:3034-3036` and `Assets/Scenes/Village2.unity:3387-3389`
serialize `m_Bits: 256` = `1 << 8` = **Structure**; Enemy is layer **7** (`128`), per
`ProjectSettings/TagManager.asset`. So the town camera's combat zoom and auto-framing have only ever
been driven by masonry — no mob can enter that scan. Correct value `128`, and it must ship **together**
with globalising `IsFramingSubject`, because each alone moves town framing in the opposite direction.
**Owner-felt either way**, which is why it was kept out of this build. (A second
`Main_Castle_Overworld` camera instance at `:22972-22974` carries `4294967295` = `~0`.)

**12.2 — `Garrison_*` / `Outpost1-2` raids still run the TOWN camera and the 220 deg/s whip.**
`HubScenes.IsRaid` matches only `RaidBase*`; `IsDungeon`'s own remark calls `Garrison_*` / `Outpost1-2`
"open-air raid targets that keep the outdoor camera". So the over-the-shoulder seat, the lazy recenter
and the raid scan scoping all miss them. Not widened here because `IsRaid` is **shared** with the HUD
combat-cluster gate and RaidDeployController's self-install. **Mint this so her next "still rotating"
from a Garrison is not read as a 1765 regression.** Fix = one added term in
`ResolvesToRaidCameraProfile` + `AppliesRaidScanNarrowing`, plus a ruling on whether Garrisons want the
raid seat (or a new `HubScenes` predicate for "open-air raid target").

**12.3 — ⭐ The TOWN hub flaps the point-blank pull-in ~78 times a minute.** Measured in the same
capture: the largest `OCCLUDER PULL-IN ENTERED` burst in 3.16 M lines is **78 in the minute 13:22**, in
**`Main_Castle_Overworld`** (`:3022710`; the preceding load is `LoadSceneWithFade
name='Main_Castle_Overworld'` at `:3002017`). Other pre-raid bursts: 33 at 13:10, 47 at 13:17, 18 at
13:20. With approach 40 / return 8 that is a continuous boom sawtooth in the scene the player lives in.
**Nothing says it is felt** — start by asking that before changing a number.

**12.4 — Two source-text lints pin the shape this ruling widens.** Two-line fix, their owners' files.
`DungeonFpvRegression.cs:168-172` requires the literal `ApplyDungeonProfileIfNeeded`;
`DungeonCameraTightRoomRegression.cs:100` requires
`if\s*\(_dungeonProfileActive\)\s*\n\s*EmitDungeonHeartbeat\(dt\)` — the exact dungeon-only heartbeat
gate WO-1765 widens. Because of them the R1 rename was not done and the heartbeat call site is written
as two branches instead of one `||`. Both suites are green and behaviour is identical, but **a lint that
pins the old shape is the real defect**: re-shape those two assertions (they care about dungeon-gating,
not identifier spelling) and the rename is free. Flagged in-code at both sites so nobody "fixes" the odd
shape and turns the suites red.

---

## 13. GATE FIX — `hub-scene-literal` FAIL (REGRESSION_FAIL 548/550, `Builds/data-regression.log` 15:21)

The lead's gate run caught my own oracle breaking a project lint, and it was right to:

> `hub-scene-literal FAIL x1: CameraRaidFramingRegression.cs (line 120) hardcodes the hub scene name
> "Main_Castle_Overworld" as a string literal.`

**Fixed the right way — resolved, never typed, and not via the allowlist.** Four literals were involved,
not the one the lint named: `HubSceneLiteralRegression` reports the FIRST hit per (file, hub) pair, so
fixing line 120 alone would have gone red again on the next run. `[camera-raid-framing]` itself passed,
so this was purely the literal.

| Site | Before | After |
|---|---|---|
| `CheckTownIsUntouched` scope pin | `AppliesRaidScanNarrowing("Main_Castle_Overworld")` | `foreach (string hub in DeNelle.Core.SceneRouter.CastleCandidates)` |
| same, Village2 | `AppliesRaidScanNarrowing("Village2")` | `AppliesRaidScanNarrowing(DeNelle.Core.SceneRouter.Village)` |
| the agreement loop | array literal containing both names | `List<string>` + `AddRange(SceneRouter.CastleCandidates)` |
| `CheckYawEvidenceGate`, heartbeat | `ShouldEmitYawEvidence("Main_Castle_Overworld")` | `foreach (… CastleCandidates)` |
| `CheckYawEvidenceGate`, profile | `ResolvesToRaidCameraProfile("Main_Castle_Overworld")` | `foreach (… CastleCandidates)` |

**Why ITERATE `CastleCandidates` here rather than read `SceneRouter.Castle` — the opposite choice from
`DungeonCameraFeelRegression`, on purpose.** `Castle` resolves only the branch `ff.MergedWorld` happens
to be flagged into, so it would prove this for one hub and leave the other unguarded. Every claim in
these cases is a **NEGATIVE that must hold for BOTH** ("no hub, in either configuration, gets the raid
narrowing / the heartbeat / the raid seat"), and the legacy `MainCastle_Hall` — still on disk, explicitly
not the hub (CLAUDE.md §7) — must satisfy it just as much as the merged scene. `DungeonCameraFeelRegression`
iterating was wrong there because its claim was a POSITIVE about the ACTIVE hub ("the hub must be
outdoor"), which the legacy interior correctly fails. Iterating is strictly stronger here and carries no
false failure. The distinction is written into the code so the next reader does not "align" the two.

**Verified, not assumed:**
- I replicated the lint over the edited file (strip comments, search `"<hub>"`): both hub names **clean**.
  The two remaining textual mentions are in comments — which `HubSceneLiteralRegression` strips — and
  neither is a quoted literal.
- Drove the new cases by hand against `CastleCandidates`: both hubs return **false** for
  `AppliesRaidScanNarrowing`, `ShouldEmitYawEvidence` and `ResolvesToRaidCameraProfile`, and the
  scope/profile agreement holds for every scene in the list. **No false failure introduced.**
- `python tools/gate_brace.py Assets/Editor/Regression/CameraRaidFramingRegression.cs` →
  `GATE_BRACE_SUMMARY bad=0 of 1`, exit 0.
- `bytes=41944 NUL=0 braces=29/29`; CODE-ONLY (comments + literals stripped) `parens=240/240 BALANCED
  braces=29/29`.

`SmartMobileCamera.cs` was **not** touched by this fix.

⚠ **I did not use `HubSceneLiteralRegression.Allowed`.** The lint offers it for a deliberate test input,
and these are test inputs — but the allowlist would have preserved a typed hub name in a gate, which is
the exact drift the lint exists to stop, and `SceneRouter` already owns both names. An allowlist entry
here would have been the cheap answer, not the right one.
