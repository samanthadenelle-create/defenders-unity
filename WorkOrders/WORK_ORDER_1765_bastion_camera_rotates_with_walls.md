# WORK ORDER 1765 — Iron Bastion: the camera still ROTATES with the walls (yaw, not pull-in)

**Status:** IMPLEMENTED, NOT YET GATED
**Lane:** read-only RCA 2026-09-16 (§0-§9, PRESERVED VERBATIM — it is the evidence record), then the
implementation lane 2026-09-16 (Part I-B §9B + Part II §10-§16). The lead gates the combined tree;
this lane ran no Unity
gate and made no commit.
**⭐ A REAL CAPTURE LANDED MID-LANE AND IT IS IN PART I-B.** 377 MB / 3.16 M lines from the owner's own
Iron Bastion run on build **2026.09.16.371701** (not the `371627` §0 names — §9B.1 corrects it). It
**confirms** two things Part I could only argue from source and **refutes** one: the combat zoom is
pinned on for the whole raid (boom measured at **5.75-6.94 m**, not 4.5), 83 distinct walls are
admitted as hostile structures in this build, and the pull-in **never once** reaches the 1.2 m floor
(worst ratio 0.883) — so the "collapse" theory is out on magnitude. `yawSrc=` appears **zero** times in
the raid, which is the measured reason the instrument ships.
**⚠ STILL OUTSTANDING, AND NOT A CODE TASK:** the yaw itself. The field that would name the dominant
cause does not exist in a raid log until this build ships. See §9B.5, §9B.7 and §16.
**⚠ 1765 WAS MINTED TWICE.** The sibling ticket is folded in here (Part II) and its file deleted.
**⚠ THE C1 CORRECTION IS RAID-SCOPED BY RULING (§11.6):** the lead ruled 2026-09-16 that the TOWN must
not change felt behaviour in this build, and both halves of C1 *would* change it (the baked town mask is
Structure-only, so the town frames structures today). Town/Village2 behaviour is unchanged, byte for
byte. **Four follow-ups this lane named are listed in §17 for the lead to mint.**
**Silo:** camera. Files in scope: `Assets/_Modules/Village/Hero/SmartMobileCamera.cs`,
`Assets/Editor/Regression/CameraWallOcclusionRegression.cs`. Disjoint from **WO-1764** (troop
targeting: `TroopController.cs` / `RaidAssaultAi.cs`) — do not touch those.
**Judge this by:** a fresh device capture containing the instrument in §5, then a pinpointed fix.
Nothing below is a fix claim.

---

## 0. The felt report and why it is not a repeat of WO-1753

Owner, 2026-09-16, Seeker, APK **2026.09.16.371627** built from **f74bf0829**, raid
`RaidBase_IronBastion`: the camera is ***"still rotating too much with the walls"*** — it
swings/rotates when the hero is near the raid walls.

All three camera commits ARE in that APK (verified this session, `git merge-base --is-ancestor`
returned true for each against `f74bf0829`):

| Commit | Date | What it changed |
|---|---|---|
| `06da9b7b6` | 09-15 | WO-1734 — restored the occluder FADE (the `RestoreAllFaded()` that made the fade path inert), and re-gated the pull-in on `_occluderPullInDistance` instead of `float.MaxValue`. |
| `4d3ec15c5` | 09-15 | WO-1751 — put layer `Structure`(8) into `ResolveCollisionMask`, so the 158 raid `Wall_*` became visible to the occlusion cast; `FadeOccluder` now resolves ALL of a wall's renderers. |
| `877fe785d` | 09-15 | WO-1753 — the pull-in gate moved from the hero-chest-nearest hit to the **seat-side** gap (`SelectOccluderGateDistance` = the farthest hit); `AllowedCameraDistance` fed the same distance. |

⚠ **Every one of those three works on DISTANCE (the boom) — not one of them touches yaw.** The
owner's word is *rotating*. So "still" is exactly what the code predicts: the three commits could
not have addressed a yaw defect, because no line in them writes yaw. Do not re-open the pull-in
arithmetic; WO-1753's five behavioural cases pin it (`CameraWallOcclusionRegression.cs:242-296`).

---

## 1. Every code path that changes camera YAW / orbit — enumerated, with line numbers

Read at source in `Assets/_Modules/Village/Hero/SmartMobileCamera.cs` on 2026-09-16 (HEAD `dev`).
There is exactly ONE stored yaw (`_panYaw`, `:304`) and one place it rotates the seat
(`:1033`, `zoomOffset = Quaternion.Euler(_panPitch, _panYaw, 0f) * zoomOffset`). The *view*
rotation is written in exactly one place: `AimAt` (`:1764-1769`),
`transform.rotation = Quaternion.LookRotation(_leadPoint - transform.position)`.

| # | Writer | Line(s) | Reads an occluder / wall? |
|---|---|---|---|
| Y1 | `AddYaw(float)` — player pan only (`CameraPanInput.cs:178/219/231`) | `:351-355` | **No** |
| Y2 | **Facing-recenter**: `_panYaw += Clamp(angleErr * _facingRecenterStiffness * dt, ±_facingRecenterSpeed*dt)`, where `angleErr = DeltaAngle(_panYaw, _target.eulerAngles.y)` | `:1013-1023` | **No** — chases the HERO's facing |
| Y3 | First-frame seed `_panYaw = _target.eulerAngles.y` | `:998` | **No** |
| Y4 | `SnapBehindTarget()` — re-seats `_panYaw` to hero facing and snaps; sole caller `BattleArena.cs:646` | `:1186-1205` | **No** |
| Y5 | `AddPitch` → `_panPitch`, clamped `[-10, 35]` | `:357-361` | **No** |
| Y6 | **`AimAt(_leadPoint)`** — the actual `transform.rotation`. Yaw = the bearing from the SEAT to `_leadPoint`, so it moves when EITHER the seat OR the lead point moves. | `:1125`, `:1764-1769` | **Indirectly — see §2** |
| Y7 | `ForceFollowImmediate()` — `transform.position = _target.position + _followOffset`, the **UNROTATED** offset (unlike Y4, which applies `Euler(_panPitch, _panYaw, 0)`), then `AimAt` | `:1155-1173` | **No** |

### ⛔ THE FIRST FINDING: NO YAW PATH IN THIS FILE READS AN OCCLUDER, A WALL OR THE COLLISION MASK.
`ApplyCollision` (`:1323-1434`) only ever returns a POSITION along `dir` (`pivot -> desired`); it
never writes `_panYaw`, `_panPitch` or `transform.rotation`. Therefore the owner's rotation is
**emergent**, and every candidate below is a route by which a wall moves the seat or the look-at
point and `AimAt` converts that into screen yaw. The RCA must be run on that axis, not on the boom.

---

## 2. Candidates, ranked. Ranked ≠ proven — §12 says static reading locates, never concludes.

### ⭐ C1 — THE AUTO-FRAMING SCAN ADMITS WALL SEGMENTS AS "ENEMIES". Strongest, and untouched by all three commits.

`ScanForEnemies` (`:1281-1311`) sweeps `OverlapSphereNonAlloc(hero, _combatScanRadius, _scanBuffer,
_enemyMask, QueryTriggerInteraction.Collide)` and admits anything whose parents carry an
`IDamageable` that `IsAlive` and has `Faction == CombatFaction.Hostile` (`:1295-1297`). The
resulting `_nearestEnemyPos` (`:1310`) is then lerped into the look-at at `_framingBias` (`:1118`):

```
Vector3 midpoint = Vector3.Lerp(heroBase, _nearestEnemyPos, _framingBias);
leadTarget = Vector3.Lerp(leadTarget, midpoint, _combatBlend);
```

Read at source, the admission is wide open — **and these are the RUNTIME values in a raid, not
merely code defaults.** That distinction matters because this very file records the baked-value trap
at `:164-165` (Village.unity bakes `_orbitBehind=0`, so a BUILD shipped without the fix). Verified
this session:
- `Assets/Scenes/RaidBase_IronBastion.unity` contains **no `SmartMobileCamera` component at all** —
  `grep -n fbe5788c0485400459d4d3c3808798ba` (the script guid from
  `SmartMobileCamera.cs.meta`) returns nothing, and `grep -n "_enemyMask"` on that scene returns
  nothing.
- The component is therefore **attached at RUNTIME**: `HeroControlEnsurer.cs:464-468`,
  `cam.gameObject.AddComponent<SmartMobileCamera>()` when the gameplay camera has none, logging
  `[HeroControlEnsurer] runtime-attached SmartMobileCamera to gameplay camera '<name>'`.
- A runtime `AddComponent` takes the **field initialisers**, so in a raid the live values ARE
  `_enemyMask = ~0` (**EVERY layer**, `:118` — the tooltip at `:117` says "Set to the Enemy layer";
  the default is not that), `_combatScanRadius = 12f` (`:103`), `_framingBias = 0.2f` (`:128`),
  `_framingEnabled = true` (`:123`).
- For contrast, the baked town cameras DO narrow it: `Main_Castle_Overworld.unity:3034-3036` and
  `Village2.unity:3387-3389` serialize `_enemyMask m_Bits: 256` (a single layer), while a second
  instance at `Main_Castle_Overworld.unity:22972-22974` carries `m_Bits: 4294967295` (= `~0`). So the
  narrow mask exists in the baked town and is **absent exactly where the defect is reported.**
- **`WallSegment.Faction => SceneOwnership.IsEnemyOwned ? CombatFaction.Hostile : Friendly`**
  (`Assets/_Modules/Village/Walls/WallSegment.cs:288-289`). A raid base is enemy-owned, so **every
  standing wall segment in Iron Bastion is a live Hostile IDamageable** — exactly the thing this
  scan admits.

**CAPTURED PROVING LINE for the precondition** (device, Seeker `SM02G4061955851`, Bastion session,
`logs/device/post-lane-a-370139/logcat_full.txt:139574`):

```
09-14 20:10:33.981 16131 16169 I Unity : [Flow:Reticle] [hostile-admit] HOSTILE STRUCTURE
'Wall_Keep1_SS_1' impl=DeNelle.Village.WallSegment faction=Hostile via physics sweep (mask=Enemy|Structure)
```

In that ONE session there are **283 `hostile-admit` lines covering 115 distinct `Wall_*` segments**
plus `Watchtower_Archer_*`, `Watchtower_Mage_*`, `RaidSpire` (counted this session with
`grep -oE "HOSTILE STRUCTURE '[^']+'" | sort -u`). The reticle's sweep uses the NARROWER mask
`Enemy|Structure`; the camera's is `~0`, a superset — so anything the reticle admitted, the camera's
scan admits too, and it takes the **nearest** one.

Consequences, all arithmetic from the values above:
1. `_enemyInRange` is **permanently true** in a raid, so `_combatBlend` pins at 1 (`:978`,
   `_combatBlend = Mathf.MoveTowards(_combatBlend, combatTarget, _combatZoomSpeed * dt)`) — the seat is permanently zoomed out by `_combatZoomOut = 2.5`
   (`:109`) and FOV boosted by 4 (`:111`). Combat framing never releases in a raid.
2. The look-at is dragged 20% of the way toward the nearest wall segment. A wall 10 m to the side
   puts the lead point ~2.0 m lateral; **when the nearest segment SWITCHES** (115 candidates, a
   hero walking a wall line crosses their Voronoi boundaries constantly, and a breach/gate flips the
   nearest from one wall line to the opposite one) the lateral target jumps sign. At the ~7.0 m
   combat boom that is a ±16° view-yaw swing per switch; at a pulled-in 1.2 m seat, ±59°.
   `_leadSmoothTime = 0.3 s` (`:95`) turns each jump into a visible SWING rather than a pop — which
   is precisely the felt word "rotating".
3. It is **walls** that drive it, which is why the owner's sentence ties the rotation to the walls
   and why WO-1734/1751/1753 could not have helped: none of them touched `ScanForEnemies`.

### C2 — The movement lead is UNSCALED by speed, and wall-slide flips its direction

`:1082-1084`:
```
if (heroVelFlat.sqrMagnitude > 0.01f) leadTarget += heroVelFlat.normalized * _leadDistance;
```
Any speed above **0.1 m/s** gets the **FULL** lead, because the term is `normalized`. `_leadDistance`
is authored 3.5 (`:92`) but `_forceCameraFix` (default **true**, `:169`) clamps it to **1.5** at
`:414-415` for any value above 1.5 — so read 1.5 m in town/raid, and re-read it rather than trusting
this sentence. A hero pressed against a wall slides at a fraction of a metre per second with the
slide DIRECTION flipping as she scrubs along the surface → the lead point swings ±1.5 m laterally at
full magnitude. At a 7.0 m boom that is ±12°; at 1.2 m, ±51°. Wall contact is the exact condition
that perturbs velocity direction while leaving speed tiny.

### C3 — Facing-recenter swinging to a wall-slid hero facing

Y2 above: `_facingRecenterEnabled` is forced **true** at `:403` by `_forceCameraFix`, with
`_facingRecenterDelay = 0.4 s` (`:199`), `_facingRecenterSpeed = 220 deg/s` (`:205`),
`_facingRecenterStiffness = 4` (`:211`). It is suspended while the player steers or the hero has
speed (`ShouldSuspendFacingRecenter`, `:902-914`), so it fires in the moment she STOPS — i.e. every
time she stops against a wall, the whole seat orbits to whatever facing the wall-slide or a wall
auto-target left the hero holding, at up to 220 deg/s. Because `HeroLocomotion` reads `CameraYaw`
(`:348`) as its movement basis, that swing also re-aims the next stick press.

### C4 — The pull-in lever arm (much smaller after WO-1753, but not zero)

`AimAt` measures the bearing to an OFF-AXIS look-at, so angular sensitivity to any lateral lead
scales as 1/boom. Pull-in fast / release slow is asymmetric — `_collisionApproachSpeed = 40`
vs `_collisionReturnSpeed = 8` (`:242`, `:246`) — so a flickering occluder set makes `_distanceFrac`
sawtooth, and the same lateral lead then reads as an oscillating yaw. **Caveat, stated honestly:**
after WO-1753 the gate needs an occluder within 0.6 m of the SEAT, and the seat sits ~7.0 m out in
raid combat, so a corridor wall 1.5-3 m from the chest no longer fires it at all. C4 is therefore
expected to be a MINOR contributor now — which is another reason the felt report survived 1753.

### C5 — `ForceFollowImmediate` snaps to the UNROTATED offset

Y7 writes `_target.position + _followOffset` with no `_panYaw` applied, unlike `SnapBehindTarget`.
With orbit-behind on and `_panYaw` far from 0, each snap teleports the seat to the wrong side of the
hero and `SmoothDamp` (`_smoothTime = 0.10 s`, `:99`) swings it back — a fast rotation not caused by
walls at all. In the 09-14 Bastion log this fired **13 times** (`[SmartMobileCamera]
ForceFollowImmediate snap executed`, alongside 5 `SetTarget wired to: Hero (Blaise)`). That session
also carries 8 `[Flow:HeroDeath]` entries, so death/respawn target re-acquisition is a plausible
trigger. **Not proven to coincide** — the timestamps were not correlated; do that when the capture
in §6 lands, it is a grep, not a theory.

---

## 3. ⛔ THE SECOND FINDING: THERE IS NO CAMERA INSTRUMENT IN A RAID AT ALL. The silence is the evidence.

`EmitDungeonHeartbeat` (`:684-700`) — the only trace that prints `panYaw`, `yawSrc`, boom — is
called **only** under `if (_dungeonProfileActive)` (`:1128-1129`), and `_dgYawSource` is likewise
only assigned under that gate (`:1029-1031`). `_dungeonProfileActive` is set by
`ApplyDungeonProfileIfNeeded` from `HubScenes.IsDungeon(activeScene)` (`:519-521`), and
`HubScenes.IsDungeon` (`HubScenes.cs:243-249`) matches composed dungeons / `Dungeon*` /
`OutpostSceneName` — **`RaidBase*` is a different kind entirely** (`HubScenes.IsRaid`,
`HubScenes.cs:61-65`; `Classify` tests `IsRaid` before `IsDungeon`, `:169-176`).

**So in `RaidBase_IronBastion` the camera emits no yaw evidence whatsoever.** Confirmed against
captured data, not inferred:

- `logs/device/post-lane-a-370139/logcat_full.txt` — 32.8 MB, Seeker, **886** `RaidBase_IronBastion`
  lines, camera provably alive (13 `ForceFollowImmediate snap executed`, 5 `SetTarget wired to:
  Hero (Blaise)`). The **only** `[Flow:Camera]` line in the entire file is
  `:122006` → `[Flow:Camera] room-sense publisher installed (WO-958 sceneLoaded hook)`.
  Zero `OCCLUDER PULL-IN`, zero `OCCLUDER FADED`, zero `seat-embedded`, zero `panYaw`.
- All 582 `logs/f8-inbox/capture-device-*.md` files: `grep -iE "Flow:Camera|OCCLUDER|SmartMobileCamera|
  seat-embedded|panYaw"` → **no output**.
- `logs/f8-inbox/device/SM02G4061955851/break-log.jsonl` (970 entries, newest 09-15 22:04): the
  `[Flow:*]` systems present are ArcaneDiag/MagentaGuard/RaidArt/TripoMatFix/StructureAssets/
  Manage/HeroDeath/Update/Tutorial/TickWatchdog/Quiescence/Raid/HeroWeapon/TroopVisual/Quest/
  ArcaneAura — **no Camera at all**.
- Windows `Player.log` under `%USERPROFILE%\AppData\LocalLow\DeNelle\Echoes of Elarion\` is dated
  **2026-09-12**, i.e. it PREDATES all three camera commits (09-15). It cannot speak to this build.

**⚠ THERE IS NO CAPTURE FROM TODAY'S RUN — the device is unplugged, so nothing from APK 371627 /
`f74bf0829` exists on this machine.** Every log cited above is from build **370139 (09-14)**, which
predates WO-1734/1751/1753. It is therefore evidence about the PRECONDITIONS (walls are Hostile
damageables; the camera is the sole rig; no camera trace exists in a raid) and NOT evidence about
today's build's behaviour. That gap is why this WO is NEEDS DATA and not READY TO IMPLEMENT.

Nothing was acked. The inbox backlog (266 un-acked at the time of writing) is untouched.

---

## 4. What is pinned today (so the instrument does not trip the suite)

`Assets/Editor/Regression/CameraWallOcclusionRegression.cs` pins only DISTANCE behaviour:
`AllowedCameraDistance` arithmetic (`:32-49`), `CheckSeatSidePullInGate`'s five behavioural cases
(`:242-296`), source-text pins inside `ApplyCollision` (`FadeOccluder(col)` required; the two
retired gates forbidden), `CheckOccluderResolution`, and mask membership helpers.
**Nothing pins yaw, `_panYaw`, `AimAt`, `ScanForEnemies`, `_framingBias` or the heartbeat gate.**
So the instrument in §5 is additive, and a new yaw case must be ADDED (§7) — not substituted.

---

## 5. STEP 1 (do this FIRST, alone — §12): instrument the yaw, then capture. No fix in this step.

Add to `SmartMobileCamera.cs` only. One heartbeat + one spike edge. Both must discriminate ALL five
candidates from ONE line, and must SEPARATE rotation from pull-in, because that separation is the
whole question.

**5a. Un-gate the yaw evidence from the dungeon.** Change the two dungeon-only gates so raids emit
too — `_dungeonProfileActive || DeNelle.Core.HubScenes.IsRaid(SceneManager.GetActiveScene().name)`
(cache the scene name on `sceneLoaded`; do NOT call `GetActiveScene()` per frame). That is the
`_dgYawSource` assignment (`:1029-1031`) and the `EmitDungeonHeartbeat` call (`:1128-1129`). Keep the
dungeon-only fields dungeon-only; print `room=n/a` in a raid rather than inventing room data.

**5b. Measure the OWNER'S WORD — screen yaw rate — not a proxy.** Cache
`_prevViewYaw = transform.eulerAngles.y` at the end of `LateUpdate`; each frame compute
`viewYawRate = Mathf.DeltaAngle(_prevViewYaw, transform.eulerAngles.y) / dt` (deg/s). This is the
measurement; `panYaw` alone cannot prove or refute C1/C2/C4, which move the LOOK-AT, not the seat.

**5c. The heartbeat — throttled, ~1 Hz, ONE line, these fields:**
`viewYawRate` (and a running max since the last line) · `panYaw` · `dPanYaw` this second ·
`yawSrc` (input / recenter / hold — the existing `:1029-1031` logic) · `heroFacingY` ·
`velMag` + `velDirY` · **`leadLateral`** (the signed lateral component of `_leadPoint - pivot` in the
camera's own frame — the lever arm, in metres) · **`framingTarget`** (the `_nearestEnemyPos`
owner's `name` + its type name + distance, or `none`) · `framingTargetSwitches` since the last line ·
`boom` (`Vector3.Distance(transform.position, pivot)`) · `distanceFrac` · `pullingIn` · `snapCount`.
`framingTarget` naming a `Wall_*` / `WallSegment` **is the C1 verdict in one word**; a high
`viewYawRate` with `yawSrc=hold` and a flat `distanceFrac` refutes C3 and C4 together.

**5d. The spike edge — the line that survives a busy log.** A `FlowTrace.Warn` on the EDGE when
`|viewYawRate|` exceeds a threshold (start at 90 deg/s) for N consecutive frames (start at 3), naming
the dominant cause from the same fields, then re-armed by a cooldown. Warn, not Step, so it also has
a chance of landing in `break-log.jsonl` with a screenshot (see §6.2).

**5e. Costs and the rules this must obey:**
- This is a FRAME path. Use `FlowTrace.Throttle` for 5c and the EDGE for 5d — **never a per-frame
  log**: `FlowTrace.cs:293-300` records why (the firehose evicts the boot window out of the device
  logcat ring; memory `logcat-ring-buffer-destroys-evidence`), and `logcat -g` on the Seeker must be
  read before claiming anything about eviction.
- Gate the STRING BUILD behind the timer exactly as `EmitDungeonHeartbeat` already does
  (`:688-691`) — interpolating every frame just to have `Throttle` drop it allocates per frame.
- ⛔ Do not strip or weaken any existing trace (CLAUDE.md §12). `OCCLUDER PULL-IN ENTERED/RELEASED`,
  `OCCLUDER FADED` and `seat-embedded` stay exactly as they are — the pinned substrings are asserted
  by the regression.
- Per-frame `FlowTrace.Measure("Perf", "SmartMobileCamera.LateUpdate", 4f, 1f)` already sits at
  `:946-949`; if the added work shows up there, it will say so in ms — do not guess about cost.

---

## 6. STEP 2: THE CAPTURE PROCEDURE (execute verbatim; §11B-B)

The device is unplugged today, so this is the first thing to run once it is back.

1. `adb devices` → expect `SM02G4061955851`. (`adb` lives in the Unity Hub Android SDK
   platform-tools; memory `adb-path-and-seeker-deploy`.)
2. `adb logcat -g` → **record the actual ring size in the WO before anything else.** The Seeker has
   read 16 MiB and 256 KiB on different days; the number decides whether a long session can hold the
   evidence.
3. **The instrument only exists in a NEW build.** Rebuild with §5 in the tree and install through
   the sanctioned script, never raw `adb install` (CLAUDE.md §16): `install-apk-to-seeker.ps1` at
   repo root (it calls `tools2-ship.ps1 -WarnOnly`; memory `r2-push-gap-on-non-device-distribution`
   — if the run skips the device-install branch, call `r2-ship.ps1` explicitly). Record the version
   stamp of what installed.
4. `adb logcat -c`, then start the capture **in a second window / via `Start-Process`** — a plain
   `adb logcat > file` blocks the shell:
   `adb logcat > logs/device/pull-<yyyyMMdd-HHmmss>-bastion-camera/logcat.txt` (`mkdir` first).
5. Owner action, one minute, narrow: enter `RaidBase_IronBastion`; walk the hero ALONG an outer wall
   line; stop against the wall and release input (that is the C3 window); pass through a
   breach/gate so the nearest hostile wall flips from one line to the opposite (that is the C1
   window); F8 the moment the rotation is felt.
6. Harvest, in this order:
   - **FIRST, prove the build under test** (memory `diagnose-the-build-under-test`): grep the version /
     commit stamp line out of the logcat and check it against what step 3 installed. A stale install
     reads exactly like "the instrument did not ship", and that confusion costs the session.
   - `grep -nE "\[Flow:Camera\]" logcat.txt` → the heartbeat + spike lines. **If this is empty the
     instrument did not ship — stop and fix that, do not theorise.**
   - `grep -nE "framingTarget=" logcat.txt | grep -ciE "Wall_|WallSegment"` → the C1 verdict.
   - `grep -nE "viewYawRate" logcat.txt | sort -t= -k2 -rn | head` → the worst rotation moments.
   - `grep -nE "ForceFollowImmediate|OCCLUDER PULL-IN|HeroDeath" logcat.txt` → C5 / C4 correlation
     against the spike timestamps.
7. ⚠ **The F8 inbox will NOT carry this on its own.** Verified this session: all 582
   `capture-device-*.md` files carry Message/Stack/Raw/Screenshots and no harvested Flow lines, and
   `break-log.jsonl` holds only error/exception/flagged-class entries. So the logcat pull in step 4
   is the record; the F8 capture supplies the SCREENSHOT that timestamps the felt moment. Do not ack
   any inbox item for this WO.

---

## 7. STEP 3: the fix, AFTER the data names the dominant cause — and its regression

Do not implement any of these before §6 returns a line. Sketched so the lane is short once it does:

- **If C1 (`framingTarget=Wall_*`)** — the auto-framing scan must stop admitting structures. The
  narrow change is in `ScanForEnemies` (`:1281-1311`): reject a candidate that is also an
  `IDamageableStructure` (walls / towers / gates / buildings), so `_nearestEnemyPos` and
  `_enemyInRange` mean live MOBILE hostiles again. ⚠ `IDamageableStructure` does **not** extend
  `IDamageable` — it declares its own `IsAlive` (`Assets/_Modules/Core/Combat/IDamageableStructure.cs:70-73`)
  — so the test is a second interface check on the resolved component, not a cast up a hierarchy;
  `WallSegment` implements BOTH (`Assets/_Modules/Village/Walls/WallSegment.cs:58`), which is what
  makes the check work. Re-read both files before implementing. Side effects to state, not smuggle:
  combat blend stops pinning to 1 in a raid, so the permanent +2.5 zoom-out and +4 FOV also release.
  Narrowing `_enemyMask` off `~0` is the second half, and since the raid camera is runtime-attached
  it must be narrowed **in the field initialiser / a raid-aware resolve**, never by editing a baked
  scene — nothing in `RaidBase_IronBastion.unity` carries this component to edit.
  *(Note for the lead/owner: framing the camera on a WALL is wrong by the method's own documented
  intent — "hero + nearest enemy" (`:120-122`) — so this could be ruled a design correction
  independently of the measurement. That is a ruling to make explicitly, not something this RCA may
  assume.)*
- **If C2** — scale the lead by speed: `heroVelFlat.normalized * _leadDistance *
  Mathf.Clamp01(velMag / referenceRunSpeed)`, so a 0.15 m/s wall scrub no longer commands the full
  lever. One clause, no new field if the reference speed is read from the locomotion authority.
- **If C3** — the recenter needs a dead-band on `angleErr` and/or a hold while the hero is in
  contact with geometry; tune `_facingRecenterStiffness` / `_facingRecenterSpeed` rather than
  deleting the recenter (it is WO-385's cure for the world-locked seat in enclosed hubs).
- **If C5** — `ForceFollowImmediate` should apply the same `Quaternion.Euler(_panPitch, _panYaw, 0f)`
  rotation `SnapBehindTarget` does (`:1192-1194`), or call `SnapBehindTarget` outright.

**Regression, in `CameraWallOcclusionRegression.cs` (or a sibling `CameraYawRegression`) — pin the
behaviour, not the source text.** WO-1753's own header records why: a `Contains()` cannot tell a max
from a min, and this suite once pinned the defect. Wanted:
1. A behavioural case for the C1 admission rule: a `IDamageableStructure`-shaped hostile candidate is
   REJECTED by the framing selector while a mobile hostile is ADMITTED — through a pure static, the
   way `SelectOccluderGateDistance` / `ShouldPullIn` were extracted.
2. A pin that the yaw instrument's gate includes `HubScenes.IsRaid`, so this evidence can never go
   silent in a raid again (the defect this WO opened with).
3. `_enemyMask` must not be `~0` once ruled.

---

## 8. What NOT to touch

- `TroopController.cs`, `RaidAssaultAi.cs` — **WO-1764's lane.** Disjoint by file.
- `PlayerAttackController.cs`, `HeroTargetIndicator.cs`, `HeroAbilities.cs`, `HeroLocomotion.cs`,
  `WallSegment.cs` — named here only as seams. `WallSegment.Faction` returning Hostile in a raid is
  CORRECT (`WallSegment.cs:26` records that without it a raid wall is indestructible); the defect
  candidate is the CAMERA admitting it as a framing subject, not the wall's faction.
- The WO-1753 pull-in arithmetic and its five pinned cases.
- Any existing `FlowTrace` call (§12: instrumentation is permanent).
- `CLI_LANES_WO_NUMBERS.md` — 1765 was pre-assigned by the lead.

---

## 9. Acceptance

1. `[Flow:Camera]` heartbeat + spike lines present in a FRESH `RaidBase_IronBastion` device logcat,
   naming `viewYawRate`, `yawSrc`, `framingTarget`, `leadLateral`, `boom`, `distanceFrac`.
2. The dominant cause named from a quoted log line — not from this document's ranking.
3. The fix addresses THAT cause, with a behavioural regression case that fails without it.
4. `COMPILE_GATE_OK` + `REGRESSION_OK <n>/<n>` on a FRESH log (marker, never exit code), plus
   `python tools/gate_brace.py` clean on every touched `.cs`.
5. Owner felt-verify in Iron Bastion: walking a wall line and stopping against a wall no longer
   swings the view. PO closes (§13) — headless cannot judge feel.

---

# PART I-B — ⭐ THE CAPTURE LANDED. This section is MEASURED, from the build the owner played.

**Source:** `logs/device/pull-20260916-143101-bastion-owner-run/logcat_full.txt` (377,140,454 bytes,
3.16 M lines — **grep it, never open it; it is excluded from commits and carries tokens**). Pulled by
the lead 2026-09-16 with the Seeker attached. Line numbers below are that file's.

## 9B.1 First: PROVE THE BUILD UNDER TEST (memory `diagnose-the-build-under-test`)

⚠ **The build on the device is `2026.09.16.371701`, NOT the `371627` Part I §0 names.**
`:2932865` — `ApplicationInfo 'com.denellestudios.echoesofelarion', Version '2026.09.16.371701'`
(corroborated at `:2933424` and `:2933611`). Part I's APK id is therefore superseded for every claim
below. The raid: `RAID START` / `GoRaid('RaidBase_IronBastion')` at **09-16 13:24:26**
(`:3036747`, `:3036751`), hero carried across (`:3036764`), scene active at `:3036838`; the raid's log
range is `:3038000`-`:3060112`.

**All three camera commits ARE in this build — proven from the log's own text, not from git:**

| Commit | The line that proves it | Where |
|---|---|---|
| WO-1751 (mask) | `OCCLUSION MASK RESOLVED = 0:Default|3:Tower|6:Building|8:Structure (raw 0x00000149)` | in-run |
| WO-1753 (seat-side gate) | `the seat-side gap 0.07m (occluder nearest the seat at 6.87m of 6.94m; nearest to the hero 6.87m)` | `:3038146` |
| WO-1734 (fade restored) | `OCCLUDER FADED x11 … seat HELD at 6.89m of 6.89m. WO-385 contract: fade the wall, hold the seat.` | `:3038305` |

So Part I §0's conclusion holds on measured ground: **the three distance commits shipped, and the
owner still reports rotation.**

## 9B.2 ⛔ MEASURED FINDING 1 — THE RAID BOOM IS ~6.9 m, NOT 4.5. The combat zoom is pinned ON all raid.

Every pull-in line prints the full boom. Across the **42** `OCCLUDER PULL-IN ENTERED` events inside the
raid window, the full boom measures **5.75 m – 6.94 m**.

The authored town seat is 4.5 m from the pivot. `4.5 + _combatZoomOut (2.5) = 7.0`. **So
`_combatBlend` sat at ~0.9-1.0 for the entire raid** — which is exactly the consequence Part I §2 C1
predicted from source and could not then prove: with every standing wall admitted as a live Hostile
`IDamageable`, `_enemyInRange` never releases, so the seat is permanently zoomed out and the FOV
permanently boosted. **That prediction is now measured, in her build.**

It also means every boom figure in Part I §2 UNDERSTATED the lever: the yaw gain of a lateral look-at
offset is 1/boom, so at 6.9 m a given offset reads *smaller* on screen than at 4.5 m — but the
occluder band moves out with it, which is finding 2.

## 9B.3 ⛔ MEASURED FINDING 2 — C1's precondition, in THIS build: 83 distinct walls admitted as hostile

Inside the raid window — counted from the line the raid scene became ACTIVE (`:3036838`), not from a
round number — `[Flow:Reticle] [hostile-admit] HOSTILE STRUCTURE` fires **106 times over 83 distinct
`Wall_*` GameObjects** (`Wall_Outer_SS_8`, `Wall_Outer_SS_9`, …) plus `Watchtower_Archer_*`
and `Watchtower_Mage_*`. That sweep uses the NARROWER `Enemy|Structure` mask; the camera's
`ScanForEnemies` ran with `_enemyMask = ~0`, a superset — **so every one of those 83 walls was a
candidate framing subject for the camera, and it took the nearest.** Part I §2 C1's admission argument
was source-only and dated 09-14; it is now measured in the build under test.

## 9B.4 ⛔ MEASURED FINDING 3 — the pull-in COLLAPSE theory is REFUTED. Magnitude ≠ frequency.

This is the finding that changes the ranking, and it argues **against** part of the sibling ticket's
driver (2):

| Quantity, over the 42 in-raid pull-in events | Measured |
|---|---|
| worst seat after pull-in | **5.97 m of 6.76 m — ratio 0.883** |
| mean seat/full ratio | **0.932** |
| the 1.2 m floor | **NEVER reached. Not once.** |
| enter/release pairs in ~75 s of raid | **42 in, 42 out** |

**So WO-1753 worked.** The pull-in is a ≤12% shortening, i.e. at most a **1.13x** yaw-gain change — it
cannot be the felt "spins about as if possessed". The sibling ticket's *"a 5.199 m boom collapsing
toward 1.2 m at 40 units/s is a 4.3x zoom lurch"* is **[refuted]** for this build: the collapse it
describes was the pre-WO-1753 behaviour and it no longer happens.

What IS remarkable is the **frequency**: 42 enter/exit edges in ~75 seconds, asymmetric
(`_collisionApproachSpeed` 40 in vs `_collisionReturnSpeed` 8 out), so `_distanceFrac` sawtooths
continuously — a persistent 6.9↔6.0 boom wobble. Small in yaw terms, but it is a real, measured,
constant motion, and it is why the raid profile zeroes the combat zoom (which is what pushed the seat
out into the occluder band in the first place: occluders fired at **6.17-6.87 m**, right where
`4.5 + 2.5` puts the seat).

## 9B.5 ⛔ MEASURED FINDING 4 — THE YAW SILENCE, CONFIRMED. This is why the instrument ships.

`yawSrc=` appears **8 times in 3.16 M lines, and ZERO times after 13:24** — i.e. **not once in the
raid**. All eight are from a DUNGEON at 13:15 (`:2985334`-`:2986416`), and they carry
`room='…' size=(10x10) small=True … avoidance=collision-off (WO-920: no wall hits by design)`, which
is the dungeon profile by its own printout. Their values are `input`, `hold` x4, `recenter` x2, `input`
— the trace working exactly as designed, **in the one scene kind that was never the complaint.**

**In the raid the only `[Flow:Camera]` lines that exist at all are occluder lines** (42 + 42 pull-in
edges and 3 fades). Nothing measures yaw. So:

- Part I §3's finding is confirmed on a fresh 377 MB capture of the build under test, not inferred.
- **The sibling ticket's central claim — that the 220 deg/s village recenter is the spin — remains
  `[unverified]`, because the field that would prove or refute it is not written in a raid.** It is
  unproven, not wrong; the whip is switched on in a raid (`_forceCameraFix`) and that part IS provable
  from source.
- This is the direct, measured justification for the §14 instrument, and for `ShouldEmitYawEvidence`
  covering raids.

## 9B.6 Other measured counts in the raid window

All counted from `:3036838` (the frame the raid scene became active) to the end of the raid range.

- `ForceFollowImmediate snap executed` — **6** (Part I §2 C5). Present but rare; `snaps=` on the new
  heartbeat lets a future capture correlate them against a spike.
- `SetTarget wired to:` — **1**.
- `OCCLUDER FADED` — **4** lines, fading **x11 / x2 / x10 / …** renderers, with the seat HELD at full
  boom each time (`:3038305`, `:3051925`, `:3052321`). The WO-385 fade contract is alive in the raid and
  reaching multiple renderers per wall (WO-1751). **A raid must keep this** — see §12.

### ⭐ 9B.6a — A FREE SECOND TICKET FOR THE LEAD, FROM THE SAME LOG. NOT 1765.

The single biggest pull-in burst in the whole capture is **78 `OCCLUDER PULL-IN ENTERED` events in the
minute 13:22** — and that minute is **`Main_Castle_Overworld`, the TOWN** (`:3022710` sits after the
last scene load, `LoadSceneWithFade name='Main_Castle_Overworld'` at `:3002017`). The town camera is
flapping its point-blank backstop ~78 times a minute in the hub the player spends the most time in,
with `_collisionApproachSpeed` 40 in and `_collisionReturnSpeed` 8 out — a continuous boom sawtooth
nobody has filed. Neither 1765 ticket is about the town, and nothing here says it is felt; it is an
observation the lead now has for free, and it should be its own ticket rather than scope creep in this
one. (Other bursts, all pre-raid: 33 at 13:10, 47 at 13:17, 18 at 13:20.)

## 9B.7 What the capture does NOT settle

- **It does not name the dominant cause.** It rules the pull-in collapse OUT by magnitude (9B.4),
  confirms C1's precondition and its combat-blend consequence (9B.2, 9B.3), and shows the yaw itself is
  unmeasured (9B.5). The candidates still standing — the recenter whip (C3), the movement lead (C2) and
  the framing arm (C1's *effect*, as opposed to its precondition) — **all live in the fields that do not
  exist in a raid log yet.**
- **No screenshot or F8 capture from this run was correlated**, so no line here is tied to the instant
  the owner felt the rotation.
- Every count above is a grep over one session. It is evidence about this run, not a rate.

---

# PART II — THE MERGED TICKET (2026-09-16)

> ## ⚠ TWO LANES MINTED 1765 FOR THIS ONE DEFECT. THIS FILE IS THE SURVIVOR.
> A second read-only lane wrote `WorkOrders/WORK_ORDER_1765_raid_camera_spins_over_the_shoulder_profile.md`
> — a stronger, complementary RCA on the same owner report. On the lead's instruction (2026-09-16) its
> RCA, knob table, capture procedure and acceptance criteria are folded in below **with attribution**,
> and that file is **DELETED** so the board carries exactly one 1765. Sections attributed to it are
> marked **[from the sibling ticket]**. Where its arithmetic was wrong, the correction is stated beside
> it rather than silently applied — see §11.1.
>
> This is the known failure mode: memory `parallel-worktree-lanes-collide-on-wo-numbers`. The lead
> pre-assigns number blocks per lane; two isolated lanes both minted 1631 on 09-10 the same way.

---

## 10. THE PIVOTAL FINDING — **[from the sibling ticket §2]**, and it is the one that explains "possessed"

**A RAID RUNS THE TOWN CAMERA, UNMODIFIED, INSIDE A WALLED FORTRESS.**
`ApplyDungeonProfileIfNeeded` gates the entire "LockedOTS" profile on
`DeNelle.Core.HubScenes.IsDungeon(activeScene.name)`. A raid scene is `RaidBase_*`
(`HubScenes.IsRaid`), so `IsDungeon` is **false** and none of the dungeon profile applies.

And in the town profile, `_forceCameraFix` (default **true**) runs on every `Awake`:

```csharp
if (_forceCameraFix)
{
    _orbitBehind = true;
    _facingRecenterEnabled = true;   // ← FORCED ON in every raid, with the VILLAGE tuning
    …
}
```

| Knob | Raid today (town) | Dungeon "LockedOTS" |
|---|---|---|
| `_facingRecenterEnabled` | **true** (forced) | true |
| `_facingRecenterDelay` | **0.4 s** | **1.25 s** |
| `_facingRecenterSpeed` | **220 deg/s** | **70 deg/s** |
| `_facingRecenterStiffness` | **4** | **1.4** |
| `_collisionEnabled` (pull-in **and** fade, one flag) | **true** | **false** |
| `_framingEnabled` / `_framingBias` | **true / 0.2** | **false** |
| `_combatZoomOut` / `_combatFovBoost` | **2.5 m / 4 deg** | **0 / 0** |
| `_leadDistance` | **1.5** (clamped from 3.5) | **0** |
| `_panPitchMin` / `_panPitchMax` | **-10 / 35** | **-5 / 20** |
| seat (height, back) | **2.6, 4.5** | **1.9, 3.2** |
| `_lookAtHeight` | **2.5** | **1.5** |

**220 deg/s is a half-turn in 0.82 s, starting 0.4 s after the player's thumb leaves the screen.**
That is the shape of "spins about as if possessed" — **and this exact tuning has already been ruled
against once.** `SmartMobileCamera.cs` records the ruling verbatim in its own dungeon branch:

> WO-958 (owner F8 seq 2289, **"its auto rotating"**): her input owns yaw in a dungeon. … the
> recenter is re-tuned from the village whip (0.4 s / 220 deg/s / stiffness 4 — **a swing at every
> pause in a small room**) to a lazy idle drift …

**The fix shipped behind `IsDungeon` and the raids never got it.** A walled raid base is a "small
room" in every way that matters to this camera.

**The five drivers, in the order they degrade the view** — all five are OFF in the dungeon profile and
all five were ON in a raid:

1. **Auto-yaw whip** — 220 deg/s / 0.4 s / stiffness 4. **The spin.**
2. **Occluder pull-in** — a boom collapsing toward the 1.2 m floor at `_collisionApproachSpeed` 40 in
   and 8 out: a zoom lurch, and a 1/boom multiplier on every lateral look-at offset.
3. **Occluder fade churn** — the same flag owns the `ShadowsOnly` fade, and WO-1751 had to teach it
   that a raid wall owns THREE renderer subtrees, so every sweep past masonry flickers three.
4. **Framing yank** — `_framingEnabled` + `_framingBias 0.2` drags the look-at toward the nearest
   hostile… which, until this ticket, **included every wall** (Part I §2 C1).
5. **Combat zoom pump** — `_combatZoomOut 2.5` + `_combatFovBoost 4` re-extend the boom the moment a
   hostile enters the 12 m scan — constantly in a raid — re-arming (2) and (3).

### ⛔ THE OWNER'S TWO QUESTIONS, ANSWERED FROM THE CODE

**Q1 — "would LARGER raid maps fix it?" NO. No arena dimension enters any camera formula.**
Both lanes reached this independently. The complete input set to the seat is the hero's transform,
player pan input, and authored constants:

1. **The seat** is `pivot + Quaternion.Euler(_panPitch, _panYaw, 0f) * zoomOffset`. No scene bounds,
   no map extent, no wall count.
2. **The view rotation** is `AimAt` — `Quaternion.LookRotation(_leadPoint - transform.position)`. Two
   points, both hero-relative.
3. **The occlusion pass** is a spherecast from `pivot` to `desired`; **its length IS the boom.** A wall
   50 m away and a wall 5 m away are equally invisible to it once they are past the seat. A bigger map
   moves walls apart; it does not shorten the cast.
4. **The framing scan** is a fixed 12 m `OverlapSphereNonAlloc` around the hero.
5. **The ONE formula in this file that reads room size at all** is `DungeonRoomSeat` /
   `UpdateDungeonRoom` via `DungeonRoomSense` — gated on `_dungeonProfileActive`, so it never runs in a
   raid. (Pinned by `DungeonCameraTightRoomRegression.cs:96`.)

A bigger map changes how OFTEN the hero is near a wall — a **frequency, not a cause** — and fighting
*at* a wall, which is what a breach is, puts her 0-2 m from it by definition. **[from the sibling
ticket §1]** it is also the most expensive item on the page (re-bake + nav + art). **Ranked last.**

**Q2 — "would moving the camera MORE OVER THE SHOULDER reduce the rotation?" Partly — and the sibling
ticket's warning is the load-bearing half:** the recenter step is
`_panYaw += clamp(angleErr * stiffness * dt, ±speed*dt)`, **with no boom term at all**, so shortening
the boom alone leaves the whip exactly as fast. **A shorter boom and the recenter re-tune must ship
together**, which is what §11 does.

---

## 11. WHAT SHIPPED

| Ruling | Implemented as |
|---|---|
| **R1 — extend the LockedOTS seam to raids** | **As a THIRD BRANCH in the one seam, not a widened `IsDungeon` gate** — see §11.2. Routed by `ResolvesToRaidCameraProfile` → the canonical `HubScenes.IsRaid`; **never** a fresh `StartsWith("RaidBase")`. |
| **R2 — a raid profile beside the dungeon one, value table** | `SmartMobileCamera.RaidCam`, taking the dungeon seat + recenter + pitch band **by reference**, with one raid-only value (the shoulder). Table in §11.1. |
| **R2b — never auto-yaw around a wall; fade instead; pull-in point-blank only** | Delivered with **zero new state** — `CollisionEnabled = true`. **The split switch and the 0.6→0.35 change were NOT implemented; §12 is the refusal, with the arithmetic.** |
| **R3 — the lateral shoulder term** | **Included, 0.6 m** (the sibling's own figure). The lead's condition — "only if the dungeon numbers leave the hero visibly centred" — is met by construction: `_followOffset.x` is the only lateral term in the file, the dungeon sets it to 0, and `AimAt` looks at the hero's own chest, so she renders dead centre and her body occludes the aim line ahead of her. |
| **C1 — the framing scan must never frame a wall** (Part I §2) | `IsFramingSubject(IDamageable)` rejects a candidate that is ALSO an `IDamageableStructure`. **Applied in the RAID branch only** (§11.6): `_enemyInRange` stops pinning true on masonry *in a raid*, so `_combatBlend` releases there; every other scene keeps its shipped admission test verbatim. |
| **C1b — the runtime raid camera gets a narrowed `_enemyMask`, inside `SmartMobileCamera`** | `ComputeDefaultEnemyScanMask()` + `ResolveEnemyScanMask()`, called from **`ApplyRaidSeat`, not `Awake`** — **RAID ONLY** per the lead's 2026-09-16 town-protection ruling (§11.6). `HeroControlEnsurer` untouched. **⚠ NOT the literal `m_Bits 256` — §13 item 1.** |
| **The instrument** | §14. Un-gated to raids, with `viewYawRate`, `step=`, `recenterSpeed=`, `heroYaw=`, `framingTarget=`, `structsRejected=`, `leadLateral=`, `boom=`, `pullingIn=`. |
| **Regression** | New `Assets/Editor/Regression/CameraRaidFramingRegression.cs`. `CameraWallOcclusionRegression.cs` **not modified** (its pins re-verified green). |

### 11.1 The value table — and the ONE arithmetic correction both tickets needed

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

Seat height, back, look-at, the recenter trio and the pitch band are **read through
`DungeonCameraProfile`, not re-typed** — they are already owner-approved (WO-920 / WO-958) and a copy
would drift (CLAUDE.md §5). They are exposed as **properties, not `const`**: a cross-assembly `const`
is inlined into `DeNelle.Village` at compile time, so a later re-tune in Core could leave a stale copy
baked in — the exact failure referencing the profile was meant to avoid.

**⛔ 11.1a — THE BOOM IS MEASURED FROM THE PIVOT, NOT FROM THE HERO'S FEET. Both 1765 tickets got
this wrong at first and it matters, because the sibling's acceptance criterion was written to it.**
`ApplyCollision` sets `pivot = _target.position + Vector3.up * _lookAtHeight`, then
`fullDist = (desired − pivot).magnitude`. So:

- town boom = `|(0, 2.6−2.5, −4.5)|` = **4.5011 m** — which is exactly what
  `CameraWallOcclusionRegression.CheckSeatSidePullInGate` already states
  (`const float boom = 4.5f; // the shipped seat distance from the pivot`);
- dungeon boom = `|(0, 1.9−1.5, −3.2)|` = **3.2249 m**;
- raid boom = `|(0.6, 0.4, −3.2)|` = **3.2802 m**.

**[from the sibling ticket]** quoted `sqrt(2.6² + 4.5²) = 5.199 m` at 30.0°, and `sqrt(1.9² + 3.2²) =
3.72 m` at 30.7° — those are **seat-from-FEET**, a quantity the pull-in gate never sees. Its
acceptance criterion `boom length 3.72 ± 0.05 m` **would FAIL on correct code**, so the merged
acceptance (§15) pins **3.2802** and drives the real statics. The Part I lane's own first pass quoted
a 2.26 m boom for a seat it has since replaced; the number is corrected here rather than in both places.

### 11.2 Why a THIRD BRANCH and not `IsDungeon(scene) || IsRaid(scene)`

**[from the sibling ticket §3]** flagged three **room-topology** blocks inside the dungeon profile that
an open arena has no rooms for, and said explicitly *"prove it, do not assume it"* / *"if any of the
three is NOT inert in a raid, stop and do R2 instead"*:

- the **ceiling clamp** to `heroFeetY + CeilingHeightRef(4) − CeilingClearance(0.5)`;
- **`DungeonRoomSeat`** room-aware seat damping (`SmallRoomMaxExtent 12`, `SmallRoomCameraDistance 2.4`,
  `RoomSeatSmoothTime 0.55`);
- **`DungeonCam.FacingLookAhead`** (0.8 m) — a SECOND look-at lever arm, which is precisely the class of
  thing this ticket exists to remove.

**That proof cannot be produced from this machine** (no device, no raid capture), and CLAUDE.md §11B
forbids shipping "probably inert". So the sibling's own fallback, **R2, is what shipped**: a third
branch in the same seam. All three blocks stay gated on `_dungeonProfileActive`, so they are inert **by
construction** — there is nothing left to prove on a device, and the regression pins the *inverse*
(none of the three may be re-gated to include the raid).

**One deviation from R2 as written:** the profile lives as `SmartMobileCamera.RaidCam`, not as a new
`Assets/_Modules/Core/World/RaidCameraProfile.cs`. `Assets/_Modules/Core/**` is outside this lane's
file scope. Moving it to Core later is a pure lift — the regression reads it through `RaidCam`, so one
alias keeps the oracle intact.

### 11.3 The pull-in arithmetic at the shipped boom

The gate is `(boom − gateDistance) < _occluderPullInDistance (0.6)`, where `gateDistance` is a
**spherecast hit distance from the hero's chest**, i.e. `surfaceDistance − _collisionRadius (0.35)`.
So a wall surface at **D** metres fires it for `D ∈ (boom − 0.25, boom + 0.35]`, and a wall past
`boom + 0.35` is not hit at all. Hero body radius **0.4 m** (`HeroLocomotion.cs:989`).

| Geometry | Far face from pivot | Gate dist | Fires? | Resulting seat |
|---|---|---|---|---|
| **Raid boom 3.2802**, 3.0 m conservative span | 2.6 | 2.25 | **no** (gap 1.03) | 3.2802, full |
| **Raid boom 3.2802**, documented ~3.9 m aperture | 3.5 | 3.15 | **yes** (gap 0.13) | **2.95** — a 0.33 m (10%) correction |
| **Town boom 4.5011**, 3.0 m span | 2.6 | 2.25 | no | seat sits **1.9 m BEYOND the far wall** — outside the space, that wall faded |
| **Town boom 4.5011**, next occluder in band | 4.3-4.85 | — | **yes** | free to run to the **1.2 m floor**: a 3.75x zoom and a **3.7x** larger rotation for the same lateral offset |

**That collapse is the amplifier.** Both worst cases at the raid boom are bounded by the occluder and
never reach the floor, so the yaw gain moves by at most 1.11x. Both rows are **pinned behaviourally**
in the regression by calling the real `ShouldPullIn` / `AllowedCameraDistance`.

**This is how the lead's sentence** *"so the shoulder camera sits INSIDE the pull-in distance when the
hero is at a wall"* **is read:** the seat lands **inside the space**, in the regime where the backstop
is a small bounded correction rather than a collapse. Stated because the sentence also parses as "the
gate should fire", and deliberately firing a pull-in would be the opposite of the intent.

### 11.4 What deletes the lever arm entirely — the strongest claim of the fix

With `LeadDistance = 0` **and** framing off, `leadTarget` **is** the hero's chest, so `_leadPoint`
damps to the pivot and `leadLateral → 0`. **In a raid the ONLY thing that can rotate the view is
`_panYaw` — player drag plus the lazy 70 deg/s recenter.** Every look-at-driven route (C1 framing,
C2 movement lead, and C4's 1/boom amplification of either) is structurally absent, not merely
detuned. The heartbeat prints `leadLateral=` so a capture confirms it rather than trusting this
paragraph.

### 11.6 ⛔ THE C1 CORRECTION IS RAID-SCOPED. Lead ruling 2026-09-16, and it is the right call.

> **Lead, verbatim intent:** the TOWN must not change felt behaviour in this build. Leave the baked
> town / Village2 cameras exactly as they behave today.

**Why "reject walls everywhere" is not the safe-looking change it appears to be.** A baked town camera
carries `_enemyMask m_Bits: 256` = layer 8 **"Structure" ONLY** (Enemy is layer 7). So structures are
the *only* thing a town camera's scan can currently see. That makes BOTH halves of the C1 correction
felt changes in the hub:

- **Narrowing the mask** to `Default|Enemy` would make the town scan see mobs **for the first time** —
  the combat zoom (2.5 m out) and +4 FOV would start firing mid-wave.
- **Filtering structures out** would take the town's `_enemyInRange` from "true near a wall" to
  **never true**, silently retiring the town's combat zoom and auto-framing altogether.

Either edit moves what the owner feels in the scene she spends the most time in, in a build whose whole
point is the raid camera. **So both halves are scoped by ONE predicate,
`AppliesRaidScanNarrowing(sceneName)` (= `HubScenes.IsRaid`)**, and the raid camera is the one that is
ours to fix anyway: it is runtime-attached, so it took the `~0` field initialiser, while the town's
value is authored.

Mechanically: `ResolveEnemyScanMask()` moved out of `Awake` and is called from **`ApplyRaidSeat` only**
(exactly one call site, pinned); `ScanForEnemies` computes `bool filterStructures = _raidProfileActive`
and otherwise runs the **pre-WO-1765 admission test verbatim**; `SnapshotVillageCameraValues` captures
`_enemyMask` and `RestoreVillageCameraValues` hands it back, so a camera surviving a raid→town
transition cannot carry the narrowing into the hub one scene later.

**The regression pins the SCOPE, not just the rule** (`CheckTownIsUntouched`): the raid gets it, the
town / Village2 / a dungeon do not, the scope predicate and the profile predicate agree for every scene
tested, the snapshot+restore pair exists, and `ResolveEnemyScanMask()` has **exactly one** call site. A
suite that pinned only `IsFramingSubject` would have stayed green while somebody globalised it and moved
the town — which is precisely the failure this case exists to catch.

**The global correction is still the right end state.** It ships paired with the scene-side fix of the
baked `256`, as a follow-up (§17), because neither half makes sense alone.

### 11.5 Known trades, named rather than discovered later

1. **Shoulder-side asymmetry.** Hugging the wall on the shoulder side puts that wall ~0.28 m off the
   cast axis; it grazes or misses, so no pull-in fires and the wall is FADED — the camera body is then
   physically inside an invisible wall. That is the WO-385 contract working as designed, but it is a
   real trade at a 0.6 m shoulder.
2. **`ForceFollowImmediate` (Part I §2 C5) now carries the shoulder unrotated.** It snaps to
   `_target.position + _followOffset` with no `Euler(_panPitch, _panYaw, 0)`, unlike
   `SnapBehindTarget`; with a non-zero `_panYaw` that already put the seat on the wrong side of the
   hero, and the 0.6 m shoulder rides along. **Not fixed** — out of the rulings' scope, and changing
   that snap's geometry moves every scene-seam and teleport landing. It is now COUNTED (`snaps=`).
3. **The WO-512 lock-on framing override bypasses `_framingEnabled` entirely.** Inert at ship
   (`FeatureFlags.LockOn` defaults **OFF**, `FeatureFlags.cs:400`), but if that flag is ever turned on
   a raid gets a 0.32-bias framing pull at a 3.28 m boom. Flagged, not changed.
4. **A raid keeps the occluder FADE, which the dungeon switches off.** Deliberate (§12) — a raid hero
   stands against masonry constantly and must not be hidden behind it. The cost is the fade churn the
   sibling lists as driver (3). If the owner reports flicker, the three-subtree fade is the place to
   look, not the seat.

---

## 12. ⛔ R2b: THE BEHAVIOUR SHIPPED. THE SPLIT SWITCH AND `0.6 → 0.35` DID NOT. Here is the arithmetic.

R2b asked for three things. **The first two are already true with `CollisionEnabled = true`:**

- *"NEVER auto-yaw around a wall; fade the occluder instead."* ✓ Nothing in the collision path writes
  `_panYaw`, `_panPitch` or `transform.rotation` — it returns a POSITION along `dir` — and this lane
  added nothing that reads geometry into yaw. Part I §1 enumerates every yaw writer.
- *"fade on; pull-in disabled except as a point-blank backstop."* ✓ Read `ApplyCollision`:
  `targetFrac` starts at **1** (hold the full seat), and the ONLY path that lowers it is already gated
  by `ShouldPullIn(...)` against `_occluderPullInDistance`. **Since WO-1734 the point-blank backstop is
  the only pull-in that exists** — the "pull in whenever ANY occluder is anywhere" behaviour was the
  DEF-151 defect WO-1734 deleted, and `CameraWallOcclusionRegression` fails if it returns.

**So no split is needed for the raid.** Splitting `_collisionEnabled` into a pull-in flag plus an
`_occluderFadeEnabled` flag would add a second piece of state whose only *distinct* setting — fade
WITHOUT the backstop — lets the camera body embed in a mesh. Its one genuine consumer would be the
DUNGEON (whose `_collisionEnabled = false` does kill both, and whose own doc comment calls fade-only
"the soft alternative … needs a new switch"), and changing dungeon behaviour is a different ticket with
its own pinned suite. **Duplicated state with no live consumer is what CLAUDE.md §5 forbids.**

**And `_occluderPullInDistance` 0.6 → 0.35 is NOT SAFE. The proposal's own rationale is off by the
near plane.** It reads *"roughly `_collisionRadius`, so it fires only when the camera body itself would
enter the mesh"*. The camera body needs **radius 0.35 + near clip 0.08 = 0.43 m** of clearance — the
number the code states at the gate: *"sphere radius 0.35 + near clip 0.08 = 0.43 m needed, ~0.17 m
margin"*. At a 0.35 threshold, a gap of 0.40 m fires **nothing** while the near plane is already
through the surface: the player sees the far side of the wall. **0.6 m is correct and unchanged.**

If the lead wants the split anyway, it is one bool plus one branch in `ApplyCollision` — but it should
be its own ticket, because it changes the dungeon, and the raid does not need it.

---

## 13. ⛔ WHAT I COULD NOT DO AS INSTRUCTED, AND WHAT I COULD NOT PROVE (CLAUDE.md §11B)

**1. The `m_Bits 256` mask instruction names the WRONG LAYER.** The brief said to give the raid camera
*"the same narrowed mask the baked town cameras carry, `m_Bits` 256"*.
`ProjectSettings/TagManager.asset` (read at source 2026-09-16) declares
`0 Default, 1 TransparentFX, 2 Ignore Raycast, 3 Tower, 4 Water, 5 UI, 6 Building, 7 Enemy,
8 Structure`. **`256 = 1 << 8` is the STRUCTURE layer; Enemy is 7 (128).** So the baked town cameras'
`_enemyMask` is a **structure-only** mask — those cameras can frame walls and *only* walls, and no mob
could ever enter their scan. Copying 256 would have shipped that into the raid while looking like a fix.
**Implemented the intent instead, by NAME:** `ComputeDefaultEnemyScanMask() = Default | Enemy`.
`Default` is kept because `DragonBoss.cs` sets no layer at all (grepped 2026-09-16), so a boss may sit
there and excluding it would blind the framing to the one fight that needs it. `Structure`, `Building`,
`Tower`, `UI`, `Water` are OUT — which is what removes a raid base's 158 `Wall_*` colliders from a
**32-slot** non-alloc buffer they could otherwise fill before a single mob was seen.
**Two decisions for the lead:** (a) the baked `_enemyMask: 256` is still wrong on disk
(`Main_Castle_Overworld.unity:3034-3036`, `Village2.unity:3387-3389`) — overridden at runtime, no bake
needed, but a `.unity`-owning lane should fix it; (b) **owner-felt side effect** — in TOWN the combat
zoom / framing go from never-firing-on-a-mob to firing on a mob (2.5 m zoom-out, +4 FOV during a wave).
That is the feature's documented intent, but she will see a change she did not ask for. The revert is
one condition in `ResolveEnemyScanMask`.

**2. THE RENAME R1 ASKED FOR WOULD TURN TWO OTHER SUITES RED, SO IT WAS NOT DONE.** R1 says to rename
`ApplyDungeonProfileIfNeeded` → `ApplySceneCameraProfileIfNeeded` and `_dungeonProfileActive` →
`_lockedOtsProfileActive`. Both are **source-text-pinned by other lanes' files**:
`DungeonFpvRegression.cs:168-172` requires the literal `ApplyDungeonProfileIfNeeded` (and
`HubScenes.IsDungeon` within 4000 chars of it), and `DungeonCameraTightRoomRegression.cs:100` requires
the regex `if\s*\(_dungeonProfileActive\)\s*\n\s*EmitDungeonHeartbeat\(dt\)` — i.e. it pins the exact
dungeon-only gate this ruling widens. So the historical names are kept (documented at their
declaration), and the heartbeat call site is written `if (_dungeonProfileActive) … else if
(_raidProfileActive) …` rather than one `||`. **Behaviourally identical; both suites stay green.**
The rename is a **two-line follow-up in those two files** and belongs to whoever owns them — this lane
was told "nothing else in Assets", and a lint that pins the old shape is the real defect.

**3. THE YAW ITSELF IS STILL UNMEASURED.** No capture on this machine, from any build, contains a
`[Flow:Camera]` yaw line — the instrument did not exist until now. So: the Part I §2 ranking is
unproven, **C1 is not shown to be DOMINANT** (only real by construction and wrong by documented
intent, which is the ruling's basis), and the sibling's central claim — that the 220 deg/s recenter IS
the possessed spin — is likewise **[unverified]**, exactly as it said. The shipped change removes the
whip (C3), shortens the boom (C4), deletes the lead (C2) and stops the wall admission (C1), so it
addresses all four; *that it is enough* is a claim only §16's capture settles.

**4. No Unity gate was run and no Unity marker is claimed** — by instruction (single seat). No
`COMPILE_GATE_OK`, no `REGRESSION_OK`, no `CAMERA_RAID_FRAMING_OK` has been observed. **"It compiles"
is NOT proven.** `python tools/gate_brace.py` (exit 0) and a NUL scan were run; the existing
`CameraWallOcclusion` pins and both dungeon suites' pins were re-verified by re-implementing their
assertions against the edited file. That proves strings and method spans, not the compiler.

**4b. ⚠ SCOPE GAP THE CANONICAL CLASSIFIER LEAVES OPEN — `Garrison_*` and `Outpost1-2`.**
`HubScenes.IsRaid` matches **only** `RaidBase*`, and `IsDungeon`'s own remark says `Garrison_*` /
`RaidBase_*` / `Outpost1-2` are *"deliberately NOT dungeons: they are open-air raid targets that keep
the outdoor camera and a real sky."* So a raid routed to a **Garrison** scene still gets the TOWN
camera and the 220 deg/s whip — this fix does not reach it. That is correct per the ruling (use the
canonical classifier, never a hand-rolled test) and it is deliberately NOT widened here, because
`IsRaid` is shared with the HUD combat-cluster gate and the RaidDeployController self-install, so
broadening it would move behaviour well outside a camera ticket. **It is recorded so the owner's next
"still rotating" report from a Garrison is not read as a 1765 regression.** If Garrisons should get the
over-the-shoulder seat, that is one added term in `ResolvesToRaidCameraProfile` and a ruling.

**4c. Nothing outside the camera flips framing back on — checked, not assumed.** `_framingEnabled` has
a public setter (`FramingEnabled { get; set; }`), so a runtime caller could defeat the raid profile's
`false` and restore C1's effect at a 3.28 m boom. `grep -rn "\.FramingEnabled" Assets --include=*.cs`
returns **no caller outside `SmartMobileCamera.cs`** (and the new suite). The profile's value is
therefore the shipped value.

**5. The shoulder 0.6 m is a FELT number with no capture behind it** — the sibling ticket says so
itself. One constant.

**6. "3.0 m wall partition per WO-1723" could not be confirmed at 3.0.** Both lanes tried. WO-1723's
RESULT records a wall **module width** of ~2.78 → **~3.9 m** and the raid gate opening narrowing from
~8.3 to **~3.9 m**; the sibling's `grep -i thick RaidBaseGenerator.cs` returned nothing. §11.3
therefore runs the arithmetic at **both** 3.0 m (the brief's conservative case) and **3.9 m** (the
documented aperture), and the regression pins both.

---

## 14. THE INSTRUMENT AS BUILT

Permanent per CLAUDE.md §12. All of it in `SmartMobileCamera.cs`.

- **`ShouldEmitYawEvidence(sceneName)`** — raid OR dungeon. Scene name cached on `Awake` /
  `sceneLoaded` (`_profileSceneName`); never read per frame.
- **`MeasureViewYaw(dt, heroVelFlat, heroBase)`**, immediately AFTER `AimAt` so it measures the
  rotation `AimAt` just wrote. `viewYawRate = Mathf.DeltaAngle(prev, transform.eulerAngles.y) / dt`,
  guarded against `dt == 0` (`unscaledDeltaTime` is 0 on the first frame after a load).
  **This is the owner's word measured.** `panYaw` alone cannot prove or refute C1/C2/C4, which move
  the LOOK-AT; `viewYawRate` sees both.
- **`_lastRecenterStep`** — the degrees the recenter ACTUALLY applied, recorded where it is applied.
  **[from the sibling ticket §6]**: a run of `yawSrc=recenter step=` lines summing past ~90 deg with
  the player's thumb off the screen **is** the possessed spin, measured; `yawSrc=input` throughout
  **refutes** this RCA and re-points the ticket at the pull-in. Falsifiable both ways.
- **`leadLateral`** = `Vector3.Dot(_leadPoint - pivot, transform.right)` — the signed lever arm in
  metres, in the camera's frame. Should read ~0 in a raid now (§11.4); if it does not, something still
  moves the look-at.
- **Heartbeat** (1 Hz in a raid): `viewYawRate` + `maxSince`, `dPanYaw`, `yawSrc`, `heroYaw`, `step`,
  `recenterSpeed`, `recenterDelay`, `velMag`, `velDirY`, `leadLateral`, `framingTarget` + `type` +
  `dist` + `switches`, `structsRejected`, `framing`, `enemyInRange`, `combatBlend`, `boom`,
  `seat=(h, back, shoulder)`, `distanceFrac`, `pullingIn`, `snaps`, and `room=n/a` in a raid (never
  invented room data). The **string build is timer-gated** before `FlowTrace.Throttle`, exactly as
  WO-958 does it — no per-frame interpolation.
- **Spike edge**: `FlowTrace.Warn` when `|viewYawRate| >= 90 deg/s` for **3** consecutive frames,
  **2 s** cooldown, naming a `suspect=` (C1 / C4 / C3 / C2 / player input / unattributed) from the same
  fields. `Warn` not `Step` so it can also reach `break-log.jsonl` beside the F8 screenshot.
- **`yawSrc` un-gated** — it was assigned only under `_dungeonProfileActive`, so in a raid the one
  field naming the yaw authority was never written at all.
- **No per-frame log anywhere** (`FlowTrace.cs:293-300`; memory
  `logcat-ring-buffer-destroys-evidence`). The existing 4-arg
  `FlowTrace.Measure("Perf", "SmartMobileCamera.LateUpdate", 4f, 1f)` is untouched and will report the
  added cost in ms — **read that scope, do not guess about it.**

---

## 15. ACCEPTANCE — merged, with the sibling's criteria corrected where needed

1. **Behavioural oracle, never a source lint, for every number.**
   `Assets/Editor/Regression/CameraRaidFramingRegression.cs` (marker **`CAMERA_RAID_FRAMING_OK`**)
   drives the real statics and asserts: the raid boom is **3.2802 m** and shorter than the town seat;
   the shoulder term is non-zero; the seat still TRACKS `DungeonCameraProfile` (not a re-typed copy);
   `FacingRecenterMaxSpeed <= 100`; recenter enabled; `FramingEnabled == false`; `CollisionEnabled ==
   true`; `CombatZoomOut == CombatFovBoost == 0`; `LeadDistance == 0`; the pitch ceiling below the
   town's 35; **and the pull-in outcome at BOTH the 3.0 m and the documented 3.9 m aperture never
   reaches the 1.2 m floor nor costs more than a quarter of the boom** — computed through
   `ShouldPullIn` / `AllowedCameraDistance`.
   ⚠ **[from the sibling ticket §5]** its `boom 3.72 ± 0.05 m` / `pitch 30.7 ± 0.5` pins are
   **superseded** — they are feet-relative and would fail on correct code (§11.1a). Its instruction
   *"read the values off the object, never off the text"* is honoured: the suite calls the real
   statics and never instantiates a camera (no PlayMode).
2. **No collateral on town or dungeon.** Asserted as the INVERSE pin: none of the three room-topology
   blocks may be re-gated to include the raid; plus every existing pin in
   `CameraWallOcclusionRegression`, `DungeonCameraTightRoomRegression` and `DungeonFpvRegression` stays
   green (re-verified this lane — see the RESULT).
3. **The C1 admission rule** is pinned through `IsFramingSubject` with a structure-shaped and a
   mobile-shaped double; and the scan mask is pinned by layer NAME, including a case that fails if
   anyone re-introduces `m_Bits 256` as "the Enemy layer".
4. **Yaw evidence reaches a raid**: `ShouldEmitYawEvidence("RaidBase_IronBastion")` is true, a dungeon
   still true, the TOWN false (a 1 Hz heartbeat in the hub is a logcat firehose).
5. `COMPILE_GATE_OK` + `REGRESSION_OK <n>/<n> suites` on **FRESH** logs — markers, never exit codes
   (CLAUDE.md §8; memory `gates-report-success-without-proving-it`).
6. **A fresh raid logcat containing `yawSrc=` / `step=` lines** — §16.
7. **Owner felt-test:** one Iron Bastion raid, fighting **against a wall**, camera does not spin.
   **PO closes, not CLI** (CLAUDE.md §13).

---

## 16. WHAT CLOSES THIS TICKET

1. Lead gates the combined tree (§15.5), with `CAMERA_RAID_FRAMING_OK` present and
   `CAMERA_WALL_OCCLUSION_OK` still present.
2. Add the `DataRegression.cs` registration line — it is in the `.RESULT.md`; this lane does not own
   that file.
3. **Run Part I §6 verbatim** on a build carrying the instrument (device plugged in, `adb logcat -g`
   recorded FIRST, `adb logcat -c`, one Iron Bastion raid fighting at a wall, then the pull to
   `logs/device/pull-<ts>-bastion-1765/logcat_full.txt`). Paste the `[Flow:Camera]` heartbeat and any
   `YAW SPIKE` line here. The discriminator: `yawSrc=recenter` with `step=` summing past ~90 deg while
   the thumb is off the screen confirms the recenter; `yawSrc=input` throughout refutes it and points
   at the pull-in; `framingTarget=Wall_*` or a non-zero `leadLateral` in a raid would mean something
   still moves the look-at despite lead 0 + framing off.
4. Owner felt-verify in Iron Bastion. PO closes.

---

## 17. FOLLOW-UPS TO MINT — each one named by this lane, none of them fixed here

Four separate tickets. Every one is out of this lane's file scope or out of the ruling's scope, and each
is stated with the evidence that found it so the next lane does not re-derive it.

### 17.1 The baked `_enemyMask: 256` is the STRUCTURE layer, in two scenes. `.unity` lane.
`ProjectSettings/TagManager.asset` (read 2026-09-16): layers are `0 Default, 1 TransparentFX,
2 Ignore Raycast, 3 Tower, 4 Water, 5 UI, 6 Building, **7 Enemy**, **8 Structure**`. So
`m_Bits: 256` = `1 << 8` = **Structure**, not Enemy. It is serialized on the camera in
`Assets/Scenes/Main_Castle_Overworld.unity:3034-3036` and `Assets/Scenes/Village2.unity:3387-3389`
(a second Main_Castle_Overworld instance at `:22972-22974` carries `4294967295` = `~0`). Consequence:
**the town camera's combat zoom and auto-framing have only ever been driven by masonry** — no mob can
enter that scan. The correct value is the Enemy layer (`128`), and it should ship **together** with
globalising `IsFramingSubject` (§11.6), because each alone changes town framing in the opposite
direction. **Owner-felt either way** — it should be a deliberate, felt-tested change, which is exactly
why the lead kept it out of this build.

### 17.2 `Garrison_*` / `Outpost1-2` raids still run the TOWN camera and the 220 deg/s whip.
`HubScenes.IsRaid` matches **only** `RaidBase*`, and `IsDungeon`'s own remark says `Garrison_*` /
`Outpost1-2` are *"deliberately NOT dungeons: they are open-air raid targets that keep the outdoor
camera and a real sky."* So the over-the-shoulder profile, the lazy recenter and the raid scan scoping
all miss them: a Garrison raid gets the town seat and the village whip. Not widened here because
`IsRaid` is **shared** with the HUD combat-cluster gate and RaidDeployController's self-install, so
broadening it reaches well outside a camera ticket. **Recorded so the owner's next "still rotating"
report from a Garrison is not read as a 1765 regression.** The fix is one added term in
`SmartMobileCamera.ResolvesToRaidCameraProfile` (+ `AppliesRaidScanNarrowing`) plus a ruling on whether
Garrisons want the raid seat — or a new `HubScenes` predicate for "open-air raid target".

### 17.3 ⭐ The TOWN hub flaps the point-blank pull-in ~78 times a minute. Nobody has filed this.
Measured in the same capture (§9B.6a): the largest burst of `OCCLUDER PULL-IN ENTERED` in the whole
3.16 M-line log is **78 in the minute 13:22**, and that minute is **`Main_Castle_Overworld`** — the hub,
not the raid (`:3022710`, after `LoadSceneWithFade name='Main_Castle_Overworld'` at `:3002017`). Other
pre-raid bursts: 33 at 13:10, 47 at 13:17, 18 at 13:20. With `_collisionApproachSpeed` 40 in and
`_collisionReturnSpeed` 8 out, that is a continuous boom sawtooth in the scene the player spends the most
time in. **Nothing in the capture says it is felt** — it is an observation, and it wants its own ticket
rather than scope creep into a raid-camera fix. Start by asking whether it is felt at all before
changing a number.

### 17.4 Two source-text lints pin the shape this ruling widens. Two-line fix, their owners' files.
- `Assets/Editor/Regression/DungeonFpvRegression.cs:168-172` requires the literal
  `ApplyDungeonProfileIfNeeded` (and `HubScenes.IsDungeon` within 4000 chars of it).
- `Assets/Editor/Regression/DungeonCameraTightRoomRegression.cs:100` requires the regex
  `if\s*\(_dungeonProfileActive\)\s*
\s*EmitDungeonHeartbeat\(dt\)` — i.e. it pins the exact
  dungeon-only heartbeat gate WO-1765 widens to raids.

Because of those two, the R1 rename (`ApplyDungeonProfileIfNeeded` → `ApplySceneCameraProfileIfNeeded`,
`_dungeonProfileActive` → `_lockedOtsProfileActive`) was **not** done, and the heartbeat call site is
written `if (_dungeonProfileActive) … else if (_raidProfileActive) …` instead of one `||`. Both suites
are green and the behaviour is identical, but **a lint that pins the old shape is the real defect** —
re-shape those two assertions (dungeon-gating is what they care about, not the identifier spelling), then
the rename is free. Flagged in-code at both sites so the next reader does not "fix" the odd shape and
turn the suites red.
