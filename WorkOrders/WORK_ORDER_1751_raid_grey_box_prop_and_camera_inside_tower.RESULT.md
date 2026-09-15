# WORK ORDER 1751 — RESULT

**Status:** IMPLEMENTED, NOT YET GATED
**Lane:** edit-only implementation lane, 2026-09-15. No Unity, no gate, no bake, no build, no git — the lead holds the Unity lock and is the sole committer.
**Constraint honoured:** `Assets/Editor/WallTools/RaidBaseDresser.cs` and `RaidNavBake.cs` were READ but **NOT EDITED** (WO-1749 owns `RaidBaseDresser.cs:1715-1760`). `RaidAssaultAi.cs`, `TroopController.cs`, `TroopFactory.cs`, `HeroHealth.cs` untouched. No `.unity` file was edited.

---

## Files changed

| File | Change | Braces (gate rule) | NUL |
|---|---|---|---|
| `D:\EoA\Assets\_Modules\Core\Diagnostics\RaidUntexturedCensus.cs` | **NEW** — read-only raid-scene untextured/flat-grey renderer census | `tools/gate_brace.py` exit 0; raw 32/32 | 0 |
| `D:\EoA\Assets\_Modules\Village\Hero\SmartMobileCamera.cs` | `ResolveCollisionMask` split into `ComputeDefaultCollisionMask()` **+ the "Structure" layer**; `DescribeMask`/`AddNamedLayer`; `FlowTrace.Once` mask trace; `_seatOverlap` + `_fadeScratch` fields; `TraceSeatEmbeddedButUnseen` detector; **`FadeOccluder` now fades EVERY active renderer via the new `public static CollectOccluderRenderers` + `FadeOneRenderer`** | exit 0; raw 147/147 | 0 |
| `D:\EoA\Assets\Editor\Regression\CameraWallOcclusionRegression.cs` | behavioural mask pins (calls `ComputeDefaultCollisionMask`), **behavioural occluder-resolution case `CheckOccluderResolution` built from the baked wall shape**, `FadeOneRenderer` restore-contract pin, `DefenseTower.Awake → EnsureContactCollider` pin, three §12 instrumentation pins, helpers, `using UnityEngine;` | exit 0; raw 25/25 | 0 |
| `D:\EoA\WorkOrders\WORK_ORDER_1751_raid_grey_box_prop_and_camera_inside_tower.md` | Status flipped to `IMPLEMENTED, NOT YET GATED` | n/a | n/a |

⚠ `RaidUntexturedCensus.cs` has **no `.meta` yet** — the lead's first Unity run generates it; it must be staged with the `.cs`.

---

## PART (A) — the grey box

### PROVEN

**1. Two detectors exist for a badly-materialled renderer, and NEITHER can see this object. That gap is the finding.**

- **MagentaGuard cannot see it.** `SweepRenderers` acts only when `anyOffender` is true, and `anyOffender` requires a **NULL material slot** or `IsBrokenShader(m.shader)` — `Assets/_Modules/Core/MagentaGuard.cs:576-580` (`if (!anyOffender) continue;`). The stray-primitive HIDE is additionally gated on `brokenMat != null` (`:598`). A material on a valid `Universal Render Pipeline/Lit` shader with **no albedo bound and a white tint** is skipped entirely — and that combination renders exactly as the owner's screenshot: flat, light grey, no texture. **This is the direct answer to "why did `ProtectPrimitiveArt` not catch it": the guard was never looking at this class of defect at all.**
- **Secondary, and worth a ticket of its own:** `MagentaGuard.ProtectPrimitiveArt` stores **editor-session `GetInstanceID()` values** (`MagentaGuard.cs:100-112`). `RaidBaseDresser` registers `KeepPlatform`/`KeepRamp` at **bake** time (`RaidBaseDresser.cs:1734, 1758`). That static set is **empty in the player**, so bake-time registration protects nothing at runtime. (Read-only observation; nothing changed.)
- **TripoMaterialFixer DOES catch this class — but is not armed in raid scenes.** `TripoMaterialFixer.VerifyAllRenderersUrp` (`Assets/_Modules/Core/TripoMaterialFixer.cs:497-575`) is the WO-1707 flat-white detector, and on the **same device session** it named the town's Jeweler:
  `2026-09-15T18:55:30Z [Flow:TripoMatFix] NO ALBEDO on 'Jeweler' renderer 'Rim' slot 0: material='Sprites-Default (URP)' shader='Universal Render Pipeline/Lit' tint=(1.00,1.00,1.00) … this is the flat-white-structure symptom (WO-1707).`
  (`logs/f8-inbox/device/SM02G4061955851/break-log.jsonl`.) But it is a **per-object MonoBehaviour** that only inspects its own subtree. The `RaidBase_*` scenes are baked geometry nothing attaches one to — and in the **three** raid loads in that same log (`18:39:53`, `18:44:51`, `18:51:54` UTC) there is **not one `[Flow:TripoMatFix]` line**. The one instrument that would have named the box is not armed in the one scene that needs it.

**2. The scene's own primitives are RULED OUT.** `Assets/Scenes/RaidBase_IronBastion.unity` contains exactly **three** scene-level `MeshRenderer`s: `RaidGround` (Plane), `KeepPlatform` (Cube, 26.73×1.5×26.73) and `KeepRamp` (Cube, 4.2×0.35×7.57). All three carry generated URP/Lit materials **with a real `_BaseMap` bound** — `Assets/Generated/RaidGround/RaidBase_IronBastion{,_KeepPlatform,_KeepRamp}.mat`, each `_BaseMap m_Texture: {guid: 97dcba0a0960fa34a99fe7b62cf2178d}`. None is untextured. **This also confirms part (A) does not collide with WO-1749's span.**

**3. The other runtime primitive fallbacks are RULED OUT by colour.** `StructureFactory.BuildPendingArtProxy` (`Assets/_Modules/Village/Catalog/StructureFactory.cs:277-293`) tints its cube **orange** `(0.72, 0.28, 0.08)`, not grey. `RaidBaseGenerator.BuildFallbackObelisk`/`BuildFallbackTurret` (`:930-955`, `:1353-1369`) both route through `MagentaGuard.BuildUrpLitMaterial` with authored dark tints. `RaidBaseDresser`'s `KeepPlatform`/`KeepRamp` tints are `(0.38,0.34,0.30)` / `(0.32,0.30,0.28)` — dark brown-grey. `RuinStep` is a bare `BoxCollider` with no renderer (already ruled out in the WO).

### NOT PROVEN — best-evidenced candidate, deliberately NOT fixed

The defect CLASS is **a renderer on a valid URP/Lit shader with no albedo bound and a light tint** (white albedo × no base map = flat light grey) — *not* a `CreatePrimitive` placeholder, all of which are ruled out above by colour. Within that class, ranked:

**Best-evidenced: the deployed Cleric troop body.** The raid load that the 13:53:55 screenshot belongs to (`18:51:54Z` = 13:51:54 local) logged, **1 m 47 s before the screenshot**:

- `2026-09-15T18:52:08.528Z [Flow:StructureAssets] dep MISS on 'NPCs/KayKit/Cleric': material 'glass' has NO albedo and NO tint — renders as an untextured grey blob. shader='Universal Render Pipeline/Lit' albedo slots scanned: _BaseMap=EMPTY, _MainTex=EMPTY`
  stack: `StructureAssetLoader.Load → VisualFactory.Skin → TroopFactory.Build → TroopDeployer.SpawnTroop → TroopDeployer.SpawnFromArmy → RaidDeployController.DeployAll`
- `2026-09-15T18:52:08.556Z [Flow:TroopVisual] id=: NO Animator anywhere under the troop root - the body cannot animate at all (model missing -> tinted-capsule fallback, or a rig-less prop was skinned).`
  stack: `TroopController.Awake ← TroopFactory.Build ← … ← RaidDeployController.DeployAll`

Both lines fire in **every** IronBastion load in the log (18:40:12 and 18:52:08). A `SkinnedMeshRenderer` whose rig is absent renders its vertices at garbage positions — a large angular grey mass with a jagged silhouette, which fits the screenshot's stepped edges and the troop clipping through it better than a flat wall panel does. **Not proven**: nothing measures that renderer's bounds or position, and the `id=` in the TroopVisual line is empty, so the log does not even name which troop.

**Second: an untextured clad panel.** The raid scene's wall art is prefab-instanced `steel_wall` ×158, `Clad_Wall_*` ×118, `Clad_Corner_*` ×16, `RuinPiece_*` ×158 — any one of which, missing its albedo, would read as a wall-height untextured slab. Supported by the same defect class being live in town this build (the Jeweler lines above).

The census covers **both**: it includes `SkinnedMeshRenderer`, and it sorts biggest-first by renderer bounds — which is precisely the measurement that separates these two candidates in one line.

⛔ **I did not fix (A).** Nothing in the captured data names the object, and §12 forbids the edit without it. What I added instead is the instrument that will name it on the next device run.

### What was added for (A)

`Assets/_Modules/Core/Diagnostics/RaidUntexturedCensus.cs` — a **read-only**, scene-wide census armed on `sceneLoaded` for `HubScenes.IsRaid` scenes plus a bounded deferred ladder at **3 s, 8 s, 20 s, 45 s, 90 s**.

⛔ **The ladder deliberately runs longer than MagentaGuard's, and the device log is why.** MagentaGuard stops at 8 s (`MagentaGuard.cs:67`). In the raid load the screenshot belongs to, the scene loaded at `18:51:54Z` and the first untextured-art error arrived at **`18:52:08Z` — 14 s later**, from `RaidDeployController.DeployAll`. Troop deployment is **player-triggered**, so the best-evidenced candidate does not exist during MagentaGuard's whole ladder. A census that stopped at 8 s would have been structurally blind to exactly the object it was written to name. `RunPass(scene, why)` is `public` so the lead can hook the deploy seam directly if even this ladder misses.

- Uses the **same criterion TripoMaterialFixer already proved on device** — `DependencyClosureTrace.GetAlbedo(m)` + shader class + tint — so the two instruments can never disagree about what "untextured" means. Flags: null material slot, null/error shader, `!shader.isSupported`, non-URP shader, **or URP shader with no albedo and tint luminance ≥ 0.6**.
- Reports **biggest-first by renderer bounds**, so the ~6 m offender is line one. Logs hierarchy path, mesh name, layer, slot, material, shader, tint, bounds size, world centre, and `DescribeAlbedo` slot detail.
- Per-offender lines are **`FlowTrace.Fail`** on purpose: a `Warn` never reaches `break-log.jsonl` / the F8 device bridge (both error-level only — the lesson written into `TripoMaterialFixer.cs:530-539`).
- **Capped at 12 lines per pass** — memory `logcat-ring-buffer-destroys-evidence`.
- **It never modifies anything.** Recovery stays with MagentaGuard/TripoMaterialFixer; a second authority that also "fixes" is how MagentaGuard came to hide the dungeon portal (WO-869).

---

## PART (B) — camera inside a watchtower

### PROVEN

**The camera's occlusion mask omitted the layer every raid wall is on.** `SmartMobileCamera.ResolveCollisionMask` built `Default(0) | Building(6) | Tower(3)` and **not `Structure(8)`** (the pre-edit body at `:695-710`).

- `ProjectSettings/TagManager.asset:12-20` — layers are `0 Default, 1 TransparentFX, 2 Ignore Raycast, 3 Tower, 4 Water, 5 UI, 6 Building, 7 Enemy, **8 Structure**`.
- `RaidBaseGenerator.cs:1999-2000` explicitly moves every wall panel to `Structure`; its own comment at `:1991` reads *"the 'Structure' layer is what every LoS linecast is masked to."*
- Census of `Assets/Scenes/RaidBase_IronBastion.unity` (parsed 2026-09-15): **158 `Wall_*` GameObjects on `m_Layer: 8`**, plus the 2 gatehouse layer-8 overrides from `RaidBaseDresser.cs:1040-1041`. Every other object in the scene is layer 0.

**Consequence, and it explains the silence exactly.** With `Structure` outside the mask, `Physics.SphereCastNonAlloc` (`SmartMobileCamera.cs:1285-1286`) finds nothing, `nearestOccluderDist` stays `float.MaxValue`, `_faded` stays empty — and `TraceOcclusionOutcome` **only speaks on a pull-in EDGE (`pullingIn != _wasPullingIn`) or when `_faded.Count > 0`**. So *"no occluder at all"* is **indistinguishable in a log from "the camera is not running"**. That is precisely the WO's evidence ("the occluder trace emitted NOTHING").

**Every collider in the raid scene belongs to `Wall_*` (layer 8), `RuinStep`, `KeepPlatform`, `KeepRamp`** — 318 `BoxCollider`s, mapped to owners. **No watchtower, corner post, gatehouse, clad panel or spire carries an authored collider**: `addColliders: 0` on both `Assets/StructureContent/ArcaneSpire_1.fbx.meta:43` (the `Watchtower_*` host prefab) and `Assets/Models/KayKit/.../building_watchtower_green.fbx.meta:43` (its `Visual` child). The towers' only scene-level physics presence is a **`NavMeshObstacle` ×10** — nav carving, which a `SphereCast` cannot hit.

**The camera is created at runtime with default field values.** `RaidBase_IronBastion.unity` contains **no `SmartMobileCamera`** serialized (its `Main Camera` GameObject carries zero MonoBehaviours), so `_collisionMask` is the `~0` sentinel and `ResolveCollisionMask` really does compute the mask. `_collisionEnabled` stays `true`: `HubScenes.IsDungeon` (`Assets/_Modules/Core/HubScenes.cs:243-249`) is prefix/exact and its remarks say *"Garrison_\* / RaidBase_\* / Outpost1-2 are deliberately NOT dungeons"*, so the `_collisionEnabled = false` dungeon branch (`SmartMobileCamera.cs:538`) does not fire. The early-return at `ApplyCollision`'s top is therefore **not** the cause.

### NOT PROVEN

**Whether the watchtower itself can occlude, even after the mask fix.** `DefenseTower.Awake` (`Assets/_Modules/Village/Buildings/DefenseTower.cs:413`) calls `EnsureContactCollider` (`:545-578`), which — finding no non-trigger collider — adds a root `CapsuleCollider` on layer 0 sized from renderer bounds. Layer 0 *is* in the mask, so statically the tower *should* have been castable even before this change. **That contradiction is unresolved from static reading**, and I did not guess at it: it is exactly what the new detector below is built to settle on the next device run. The remaining possibilities are (i) the capsule is not created or is mis-sized on device, or (ii) the tower in `Screenshot_20260915-134636.png` is **beside** the camera path (in-frustum clip), not **on** the pivot→seat segment — the WO's framing assumed the latter.

### Fixes + instrumentation added for (B)

1. **`ComputeDefaultCollisionMask()`** — `public static`, adds `Structure`. Extracted so the regression can **call** it instead of grepping source text for a layer list (a source-text pin on layer names would be the duplicated state CLAUDE.md §5 forbids — and this suite has already pinned the defect it was written to prevent, twice).
2. **`FlowTrace.Once("Camera", "collision-mask", "OCCLUSION MASK RESOLVED = …")`** — one line answers "which layers can occlude me", with the raw hex. Once, never per-frame.
3. **`TraceSeatEmbeddedButUnseen(seat)`** — runs **only** when the cast returned no non-target hit, and then `OverlapSphereNonAlloc(seat, _collisionRadius, …, ~0)`. It **skips anything with a `Rigidbody` or a `NavMeshAgent` in its parents**: in a raid the hero stands inside a melee, and without that filter the 8-slot buffer fills with troop and mob bodies before any wall or tower is reached. It speaks **only** in the defect shape — the cast saw nothing yet the camera body overlaps real geometry — naming the collider, its layer, and **whether that layer is in the mask**, which separates all four causes in one line. Throttled 3 s, silent in every healthy frame. ⛔ Deliberately **not** a "no occluder found" trace: clear line of sight is the normal case and tracing it would fire in town forever, evicting the boot window out of the logcat ring.
4. **`FadeOccluder` now fades EVERY active renderer the collider owns**, via the new `public static CollectOccluderRenderers` + `FadeOneRenderer`. **This is the change that makes the mask fix shippable** — without it, adding `Structure` to the mask would ship pull-in with a no-op fade across 158 raid walls, which is the owner's 09-14 complaint amplified. Full reasoning and the retraction it corrects are in the **RETRACTION** section above.

5. **`CameraWallOcclusionRegression` extended** — a **behavioural** occluder-resolution case (`CheckOccluderResolution`) built from the baked wall's real shape; behavioural pins that `Default`/`Building`/`Tower`/`Structure` are in the mask and `Enemy`/`UI` are not (resolved through `LayerMask.NameToLayer`, so it reads `TagManager.asset` at run time); a source-text pin that `DefenseTower.Awake` still calls `EnsureContactCollider` (delete it and the mask fix buys the towers nothing, silently); and three §12 pins that the new traces are never stripped.

---

---

## ⛔ RETRACTION — my first hand-back's caveat 1 was WRONG, and the correction is the important part

**What I claimed:** *"The `Wall_*` blocker has no renderer — the visible art is the separate `Clad_Wall_*` instance, a **sibling** under `Zone_Clad` … so `FadeOccluder` will find nothing to hide. Closing that needs a blocker→clad-visual link, a redesign, out of scope."*

**What the baked scene actually says.** Parsed from `Assets/Scenes/RaidBase_IronBastion.unity`, the GameObject `Wall_Outer_SN_0` (`m_Layer: 8`, one `BoxCollider`, one `NavMeshObstacle`, two MonoBehaviours) has **three direct children**:

```
Wall_Outer_SN_0                    <- the layer-8 blocker, collider lives HERE
  |- steel_wall                    (prefab instance, guid 2098d40550a0c704a93f8f12410faace)
  |- Clad_Wall_Outer_SN_0          (prefab instance, guid 9fe035eae32949e4b8936268a6754978)
  +- Ruin_Wall_Outer_SN_0  ->  RuinPiece_0     (breached-state art)
```

All **158** `Clad_Wall_*` instances are parented to their own `Wall_*` blocker. **Only the 16 `Clad_Corner_*` hang under `Zone_Clad`** — and those are the ones I generalised from. So `FadeOccluder`'s existing `GetComponentInChildren<Renderer>()` **already reached the clad panel**; there was never a missing link and no redesign was needed.

**How it got in, recorded so the next seat can discount it properly:** it arrived as a *review steer* inferred from the generator's structure (`Zone_Clad` as the clad parent), and **I repeated it as fact without opening the baked scene**. The review's own later note — "check the BAKED scene, not the generator's intent; they disagreed before" — is what caught it. Under §11B the failure is mine either way: an unverified claim restated as a finding. This is exactly the failure `CLAUDE.md`'s mandatory-catalog rule names — *"verified from the actual code, NOT from comments"* applies to a builder's intent as much as to a doc. Had it shipped as written, the lead would have been handed a redesign ticket for a problem that did not exist, while the real one-word defect stayed.

**Also verified rather than assumed this pass** (same class of claim, so it was checked before being written into a code comment): all **158** `Ruin_Wall_*` GameObjects carry `m_IsActive: 0` and all 158 `Wall_*` carry `m_IsActive: 1` — counted from the scene file, which is what licenses the `includeInactive: false` argument below.

**The real defect, which IS fixed in this pass.** `FadeOccluder` used **singular** `GetComponentInChildren<Renderer>()` and hid only the **first** match. A raid wall has **two active renderer subtrees** (`steel_wall` + `Clad_Wall_*`), so the fade hid one and the wall kept drawing — a fade that is a visual no-op, indistinguishable in a capture from no fade at all. `FadeOccluder` now resolves through the new `public static SmartMobileCamera.CollectOccluderRenderers(Collider, List<Renderer>)`:

- **Rung 1** — every **active** renderer under the collider's own GameObject (tightest root, covers compound bodies). `includeInactive: false` is deliberate: the breached-state `Ruin_Wall_*` subtree is inactive, and registering it would hand `RestoreFadedNotHitThisFrame` a renderer to "restore", **revealing art the game deliberately hid**.
- **Rung 2** — nothing renderable below, so the **nearest ancestor renderer only**, never its whole subtree (which could be a zone root holding half the base).
- **Silent-safe at every rung:** a collider that resolves to nothing simply returns, exactly as before — that wall still pulls in at the point-blank backstop, nothing throws, and nothing is left faded. Bounded by `MaxFadedRenderersPerCollider = 32`. Every faded renderer goes through `FadeOneRenderer`, which records the **original** `shadowCastingMode` before overwriting it and marks `_fadedThisFrame`, so `RestoreFadedNotHitThisFrame` and `RestoreAllFaded` both still work unchanged.

**Pinned**, behaviourally, by the new `CheckOccluderResolution` case in `CameraWallOcclusionRegression`: it builds that exact baked shape (layer-8 root + `BoxCollider` + two active renderer children + an inactive ruin child) and asserts the clad **and** the steel renderer resolve, the inactive ruin renderer does **not**, a bare blocker falls back to its ancestor, and a null collider yields an empty set without throwing. Plus a source-text pin that `FadeOneRenderer` still records the original shadow mode (lose that and a restore puts back `ShadowsOnly` — an invisible wall forever).

---

## ⚠ Caveats the lead must carry forward — do NOT drop these

1. **Town side effect, owner-felt.** Every `Structure`-layer collider **in town** now enters the occlusion cast. Intended (a town wall should fade like a raid wall), but it is a change the owner will feel, not a silent one.

2. **THE PULL-IN THRESHOLD: 0.6 m is measured from the wrong end, and here is the number.** *(Asked for explicitly; deliberately NOT changed.)*

   **What it does today.** `bool pullingIn = nearestOccluderDist < _occluderPullInDistance` (0.6 m, `SmartMobileCamera.cs:252`). `nearestOccluderDist` is `_occluderHits[i].distance` from `Physics.SphereCastNonAlloc(pivot, …)` — so it is measured **from the hero's chest**, the cast ORIGIN, not from the camera.

   **The geometry.** `_followOffset = (0, 2.6, -4.5)` (`:71`), `_lookAtHeight = 2.5` (`:87`) → `fullDist ≈ 4.5 m`. So the backstop fires when a wall is within 0.6 m of the hero's chest — which puts it **≈3.9 m in FRONT of the camera seat**. The camera is nowhere near embedding in it. When it fires, `AllowedCameraDistance(4.5, 0.6, skin 0.2, floor 1.2)` = `clamp(0.4, 1.2, 4.5)` = **1.2 m**: the seat collapses 4.5 m → 1.2 m, a **3.75× zoom-in**, and at 1.2 m a small yaw is an enormous screen rotation. That is the owner's 2026-09-14 "camera spin" in one line of arithmetic.

   **Frequency.** Before this change: **zero** in raids (nothing was in the mask). After: every time the hero backs within ~0.6 m of a wall — `MaxSegmentWidth = 3.0 m`, `MinGateWidth = 3.5 m` (`RaidBaseGenerator.cs:218`, `RaidBaseDresser.cs:28`), so in a gate or against the keep that is **common**, not rare.

   **Is it shippable as-is?** Yes, but with a felt jolt. The normal case (wall crosses the line of sight further out) now **fades and holds the seat** — that is the fix for the 09-14 complaint. The residual is a zoom-jolt when hugging a wall, and the wall is faded while it happens, so nothing is ever hidden behind an opaque mesh.

   **What the geometry actually warrants** — **not applied, owner's call.** Gate on the occluder nearest the **SEAT**, which is the thing the backstop exists to protect, instead of the one nearest the pivot:

   ```
   // in the hit loop, alongside the existing nearestOccluderDist:
   if (hitDist > farthestOccluderDist) farthestOccluderDist = hitDist;   // init 0f
   ...
   bool pullingIn = farthestOccluderDist > 0f
                 && (fullDist - farthestOccluderDist) < _occluderPullInDistance;
   ```

   ⚠ **It must be the FARTHEST hit, not `nearestOccluderDist`.** Writing `(fullDist - nearestOccluderDist)` looks equivalent and is not: `nearestOccluderDist` is a **min**, so with two occluders on the segment — one 0.5 m behind the hero, one right at the seat — it yields `4.5 - 0.5 = 4.0`, no pull-in fires, and the camera sits **inside** the seat-side wall. One extra local fixes it. (Flagged so a verbatim implementation of this caveat isn't wrong.)

   The threshold value **0.6 stays**; only the end it is measured from changes. Sizing check: camera body radius `_collisionRadius = 0.35` + the capped near plane `0.08` = **0.43 m** of real clearance needed, so 0.6 m keeps ~0.17 m of margin. This converts the backstop from "fires constantly in a corridor and yanks 3.75×" into "fires only when the camera would genuinely embed". **I did not change it** — it is a felt behaviour change, and you asked for the number and the reasoning, not the edit.
3. **Acceptance items that need Unity were NOT RUN** — this lane has no Unity lock:
   - headless capture PNG of the IronBastion courtyard showing no untextured primitive — **not run, lead's gate**;
   - "the trace names zero default-material renderers" — **not run**; the census instrument now exists to produce that line, but it has never executed;
   - `OCCLUDER FADED` firing for a **watchtower** in a headless camera run — **not run**. Per the NOT-PROVEN note above this still depends on `DefenseTower`'s runtime capsule actually existing on device; a **wall** should now both enter the cast and fade, but that too is unverified here.
4. **`RaidUntexturedCensus.cs.meta` does not exist yet.** Generated on the lead's first Unity run; stage it with the `.cs`.
5. **Nothing was gated, built, baked, committed or pushed.** Brace + NUL checks are the only verification this lane ran, and they are reported above verbatim.

---

## Suggested follow-ups (not minted — lead's call)

- `MagentaGuard.ProtectPrimitiveArt` registers **editor-session InstanceIDs** at bake time; that static set is empty in the player, so the protection it advertises does not exist at runtime for baked scenes.
- **`Clad_Corner_*` (16 instances) are the one wall-art family still parented to `Zone_Clad`**, not to a blocker — and `Zone_Clad` carries no collider. The eight ring corners therefore have no occluder at all. Out of scope here (the corners are not what the owner reported), but it is the residue of the structure I mis-generalised from, and someone should decide whether corners should block the camera.
- `[Flow:Raid] Precombat capture census failed: Captured structure lacks a baked stable identity: Wall_Outer_SS_0` fires on **every** IronBastion load in the device log (18:39, 18:44, 18:51 UTC) — unrelated to this WO, but it is an error-level line on every raid entry.
