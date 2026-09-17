# WORK ORDER 1806 — a particle material left OPAQUE and albedo-less renders as a flat untextured quad

**Status:** IMPLEMENTED

**Silo:** VFX / materials (Assets/_Modules/Village/Vfx, Village/Hero, Village/Buildings, Assets/Editor)
**Raised by:** owner F8 flag, 2026-09-16 19:46:26 device time, build 2026.09.17.372984 (tester),
scene `RaidBase_raider_camp_small`
**Owner words:** *"there is a spell on the archer that the vfx is broken on and shows as a flat plane"*

---

## 1. Evidence, all read or measured this session

**Screenshot (primary evidence for a visual defect — memory `screenshots-are-primary-evidence-for-visual-defects`):**
`D:\EoA\logs\f8-inbox\device\SM02G4061955851\flags-20260917\flag_20260917-003653_00.png`, read with the
Read tool. Two large flat grey-blue **screen-aligned rectangles** stand between the hero and the enemy
line. They are **opaque** — they occlude the terrain and the boundary wall behind them — while the
yellow-green arrow streaks and a small smoke puff draw **on top** of them (transparent queue after
opaque). HUD reads Troops 9/10, SPIRE 100%, Razed 11%, raid clock 2:10, `+90 XP` floating at the
left. Hero is the Mage (`CAST / SHELL / Mend / Arcane Bolt / Dash / ITEM`).

**Log:** `D:\EoA\logs\f8-inbox\device\SM02G4061955851\flags-20260917\logcat-after-372984.txt`
(3,124,128 lines; raid window 3,097,236–3,109,548; flag at 3,109,548).

The log states the defective material's shape in its own words, at line **2518323**:

```
[Flow:ArcaneDiag] SLOT[3] rend='ElectricyCenter' ... VISIBLE=True (enabled=True activeInHier=True)
  mat='MagentaFix_DefaultLit' shader='Universal Render Pipeline/Lit'
  baseColor=(0.70,0.70,0.70,1.00) baseMap=False mainTex=False
```

A **URP/Lit, RenderType Opaque, 0.70-grey, `_BaseMap` NULL** material on a particle renderer draws
exactly the object in the screenshot: a flat, untextured, opaque grey billboard.

**The asset, read at source:** `Assets/Materials/MagentaFix_DefaultLit.mat`
— `:10 m_Name: MagentaFix_DefaultLit`, `:11 m_Shader: {fileID: 4800000, guid: 933532a4fcc9baf4fa0491de14d08ed7}`
(URP/Lit), `stringTagMap: RenderType: Opaque`, `_BaseMap: m_Texture: {fileID: 0}`, `m_CustomRenderQueue: -1`.
Its guid is `751cde1de5b29b247bba48305ded45f5`.

---

## 2. ROOT CAUSE — proven at source

### 2a. The producer

`Assets/Editor/MagentaMaterialFixer.cs`
* `:284 FixNullSlotsInPrefabs(Material def)` → `:300 AssignDefaultToNullSlots(r, def)` for **every
  `Renderer` in every prefab under `Assets/`**.
* `:356-382 AssignDefaultToNullSlots` fills every null material slot with the single `def`.
* `:385 GetOrCreateDefaultMaterial` builds that `def`: `new Material(lit)` named `MagentaFix_DefaultLit`,
  `_BaseColor` 0.7 grey. **There is no `ParticleSystemRenderer` branch.**
* The correct particle default **already exists in the same file** —
  `:244 GetOrCreateUrpDefaultParticleMaterial()` → `Assets/Materials/MagentaFix_DefaultParticle_URP.mat`
  (`:10 m_Name`, `:11 m_Shader` guid `0406db5a14f94604a8c57ccfbc9f3b46` = URP/Particles/Unlit,
  `RenderType: Transparent`, `m_CustomRenderQueue: 3000`) — but it is consulted **only** by the sibling
  pass `:170 FixBuiltinLegacyParticleSlotsInPrefabs`, never by the null-slot pass.

**Census of the working tree, 2026-09-16 (script output reproduced in the RESULT):**

| measure | count |
|---|---|
| prefabs carrying guid `751cde…` | **969** |
| `MagentaFix_DefaultLit` slots on `ParticleSystemRenderer`s | **4,197** |
| …inert (`renderMode: None` or renderer `m_Enabled: 0`) | 990 |
| …trail slot `i > 0` on a live renderer (covered by the runtime re-point) | 3,186 |
| …**slot 0 on a live renderer — repaired by NOTHING** | **21** |

### 2b. Why every existing net misses it — three structural skips

1. `VFXManager.IsLegacyParticleShader` returns **false** for any shader name containing
   `"Universal Render Pipeline"` — so the legacy→URP remap never looks at it.
2. `AbilityVfxKit.HealHalfUpgradedParticleMaterial` (`:164-166`) requires `"Particles"` **or**
   `"Unlit"` in the shader name. `"Universal Render Pipeline/Lit"` contains **neither**, so it bails
   at its `particleLike` gate.
3. The only guard that knew the string `"MagentaFix"` was gated **`i > 0`** — it repairs the TRAIL
   slot and nothing else. It existed in **three copy-pasted places**
   (`VFXManager.cs`, `ProjectileVFXCatalog.cs`, `VfxProofCapture.cs`), which is the duplicated-state
   failure CLAUDE.md §5 and §16 each describe in their own words.

### 2c. Why nothing in the game reported it

`[Flow:RaidArt] UNTEXTURED CENSUS` **does** run in this scene and reported
**401 offending slots across 680 mesh renderer(s) / 1221 slot(s)** (log line 406) — but it is
**MESH-renderer only**. Nothing in the codebase has ever audited a PARTICLE material. That is why
this class reaches the owner's eyes instead of a log line, and it is the §14 dependency this WO
removes.

---

## 3. ⚠ WHAT IS **NOT** PROVEN — read this before closing the ticket

**Pixel-level attribution of the two quads in `flag_20260917-003653_00.png` to a specific prefab is
NOT proven.** I tried to prove it and the evidence refuted each candidate in turn:

| candidate at the corpse coordinate (11.7, ~0.5, −12.7) | disposition | evidence |
|---|---|---|
| `Explosion_Storm` (archer arrow impact, fired 19:46:26.317, 176 ms before the flag) | **excluded** | its MagentaFix slots are trail slots (`slot[1]` ×2 PSRs) and **all five `TrailModule: enabled: 0`** in the prefab YAML — the trail material never renders. `ImpactFXPool.cs:129` also calls `PreparePooledInstance` → `FixUrpShaders`, so the trail re-point ran anyway. |
| `Aura_EnemyCaster` (the ONE live loop at the flag instant, log line 3109546, pos (11.7, 0.0, −12.7)) | **excluded** | its MagentaFix slot is slot 0 but the PSR is `m_RenderMode: 5` (`None`) — draws nothing. |
| `FleshImpacts` / `PP_FleshImpacts` (×8, last 19:46:21.992, lifetime 8.3 s, still alive) | **excluded** | same: slot 0, `m_RenderMode: 5`. |
| `Spear_Impact` → `Hit 11 orange arrow` (×19-25; 6 live PSRs with slot-1 MagentaFix) | **excluded by time** | last played 19:46:12.317, lifetime 2.30 s — expired ~14 s before the flag. |
| `Impact_Physical` (19:46:25.934, 1.70 s) / `Death_Generic` (19:46:25.592, 5.30 s) — both alive at the flag | **not carriers** | neither prefab references guid `751cde…`. Their Lana `1AB_mat`/`1Add_mat` are the same *symptom* class and `HealHalfUpgradedParticleMaterial` logged healing both at boot (13:06:20.173-174). |

So: **the defect class and its producer are proven; the specific prefab in the owner's frame is not.**
The instrumentation in §4.3 is precisely what closes that gap on the next device run — it prints
prefab + child + slot + material + shader for the first slab it finds.

---

## 4. The fix (four layers, none creative, no colour chosen)

Per memory `owner-colorblind-delegate-visual-creative` and `vfx-map-owner-tags-no-creative-pick`:
**no owner-tagged key was touched, and no colour was picked.** The slot-0 rebuild reuses the exact
recipe already in the tree at `AbilityVfxKit.cs:194-204` (shared soft-dot texture, white tint,
alpha-blend) — the remedy the codebase already chose for "a particle material with no texture".

### 4.1 Producer — `Assets/Editor/MagentaMaterialFixer.cs` (editor only)
`AssignDefaultToNullSlots` now resolves `GetOrCreateUrpDefaultParticleMaterial()` when the renderer
`is ParticleSystemRenderer`, and falls back (loudly, `Debug.LogWarning`) to the Lit default if the
particle default cannot be built — a null slot renders engine-default MAGENTA, which is worse than a
grey slab. This stops the tree being re-seeded on the next `Defenders/Art/Fix Magenta Materials` run.

### 4.2 Runtime net — ONE owner, three call sites
New in `Assets/_Modules/Village/Hero/AbilityVfxKit.cs`:
* `IsMagentaFixParticlePlaceholder(Material)` — URP, **not** a Particles shader, name `MagentaFix*`.
  **Deliberately name-gated, not heuristic.** The tempting wider rule ("any opaque URP material with
  no `_BaseMap`") also matches a legitimate `ParticleSystemRenderMode.Mesh` particle — debris and
  shards are vertex-coloured URP/Lit, untextured and opaque *by design* (13 Mesh-mode renderers in a
  60-prefab sample). Rewriting one into a soft-dot billboard would be an owner-visible regression
  invented by a heuristic no evidence asked for. `MagentaFix*` is the producer this WO proved, so
  `MagentaFix*` is what gets repaired.
* `TryRepairOpaqueLitParticleSlot(Renderer, Material[], int slot)` — **only acts on a
  `ParticleSystemRenderer`** (URP/Lit on a MeshRenderer is legitimate and is left alone).
  `slot > 0` → re-point at `mats[0]` (the old behaviour, preserved verbatim).
  `slot == 0` → no donor exists, so rebuild as URP/Particles/Unlit, transparent, soft-dot `_BaseMap`,
  white tint — **skipped for `renderMode == Mesh`**, where a soft dot UV-mapped onto geometry looks
  worse than the grey placeholder. If the URP particle shader is unavailable it **leaves it broken
  and says so** rather than making it worse. Rebuilt materials are cached per SOURCE material in a
  static dictionary (mirrors `ProjectileVFXCatalog._fixedMaterials`), because `SpawnFlying` /
  `ReskinFlying` run per shot and an uncached `new Material(...)` would leak one per projectile.

The three copy-pasted `i > 0` branches are replaced by calls to that one helper:
`VFXManager.ProofUrpParticleShaders`, `ProjectileVFXCatalog.FixUrpShaders`,
`VfxProofCapture.ProofUrpParticleShaders`.

### 4.3 Instrumentation — the line that would have named this at boot
`AbilityVfxKit.AuditParticleSlotsAfterRepair(GameObject, string label)`: after each prefab's repair
pass, **read back** what the renderers actually hold and `FlowTrace.Once` the FIRST slot still
drawing opaque-and-albedo-less, with **prefab + child + slot + material + shader + renderMode**.
Throttled once per prefab label so a hot pool cannot evict the boot window from the logcat ring
(memory `logcat-ring-buffer-destroys-evidence`). Read-only; modifies nothing. Called from all three
sites. Skips disabled renderers (the WO-1100 vendor container pattern) and `renderMode: None`.

### 4.4 Regression — `Assets/Editor/Regression/VfxParticleNullSlotRegression.cs` `[vfx-null-slot]`
The existing suite already walks every prefab under `Assets/Resources/VFX/**` plus every resolving
`HovlVfxCatalog` row with renderMode/enabled awareness. Added: an ENABLED, drawable
`ParticleSystemRenderer` (renderMode neither `None` nor `Mesh`) whose **slot 0** satisfies
`IsMagentaFixParticlePlaceholder` is a **FAIL**. Slot 0 only — a trail slot is covered by the runtime
re-point, so failing it would be noise. It **passes today by measurement, not by hope**:
`Resources/VFX` carries 38 such slots across 22 prefabs, 27 of them slot-0 and **every one of those
on a renderer that is `renderMode: None` or disabled** → `slot0_on_a_live_billboard=0`.
**No `DataRegression.cs` change:** the suite is already registered.

---

## 5. Files touched

| file | change |
|---|---|
| `Assets/_Modules/Village/Hero/AbilityVfxKit.cs` | +2 public helpers + the audit census (+155 lines) |
| `Assets/_Modules/Village/Vfx/VFXManager.cs` | inline `i>0` branch → shared helper; audit call |
| `Assets/_Modules/Village/Buildings/ProjectileVFXCatalog.cs` | same |
| `Assets/Editor/VfxProofCapture.cs` | same (the capture must repair what the runtime repairs) |
| `Assets/Editor/MagentaMaterialFixer.cs` | particle default for `ParticleSystemRenderer` slots |
| `Assets/Editor/Regression/VfxParticleNullSlotRegression.cs` | new slot-0 assertion + header |

**Not touched, per the brief:** any `.unity`, `SmartMobileCamera.cs`, `Troops/TroopController.cs`,
`DataRegression.cs`, the WO banner. **No YAML asset was patched** — see §6.

---

## 6. Deliberately NOT done, and why

**The `Assets/Resources/VFX` prefab slots were NOT guid-swapped to
`MagentaFix_DefaultParticle_URP`.** Measured this session, by that exact guid:

```
Resources/VFX: prefabs=22 slots=38 slot0=27 slot0_on_a_live_billboard=0 trail=11 inert=27
```

All **27 slot-0** occurrences sit on renderers that are `renderMode: None` or `m_Enabled: 0` — they
draw nothing, so a swap is visually a no-op. The **11 trail slots** (Boss_FireBreath 1,
Explosion_Fire 1, Explosion_Ice 1, Explosion_Storm 2, Projectile_Arcane 3, Projectile_Fire 1,
Projectile_Ice 1, Projectile_Storm 1) are already repaired at runtime by re-pointing them at the
head material; swapping the asset would *replace* that correct behaviour with a generic soft dot —
a regression bought for no proven gain. The remaining ~3,175 project-wide trail slots live in
vendor packs and are handled by the same runtime net. If the owner wants the assets cleaned at
source, that is a separate, larger ticket.

---

## 7. Acceptance criteria

- [ ] `COMPILE_GATE_OK` on a fresh log (lead's gate — not run by this lane).
- [ ] `[vfx-null-slot]` passes inside `REGRESSION_OK <n>/<n>` on a fresh log.
- [ ] **Positive control:** point a drawable `Resources/VFX` particle renderer's slot 0 at
      `Assets/Materials/MagentaFix_DefaultLit.mat`, re-run `[vfx-null-slot]` → it must FAIL naming
      that prefab/child/slot. Revert.
- [ ] Device run: `adb logcat | grep "PARTICLE SLAB after repair"`. **Zero lines = the class is clean
      in the shipped set. One or more lines = the attribution gap in §3 is CLOSED** — the line names
      the prefab, child, slot, material and shader that drew the owner's flat plane.
- [ ] Owner felt-test in a raid: no flat grey plane. **PO closes** (§13).

## 8. Headless proof

No capture entry exists for the troop archer's projectile body. `Defenders/VFX/Capture VFX Proof`
(`Assets/Editor/VfxProofCapture.cs:224`) covers tower projectiles, not the pooled `RangerArrowVfx`
body, and adding a new shot to it was out of proportion to the evidence. **What the capture DOES now
prove** is that its own material repair is byte-identical to the runtime's (same helper) and that it
runs the same read-back census, so a green capture can no longer hide a slab. The device check in §7
is the real proof line here.

---

**Lane:** VFX/Audio (CLAUDE.md §9 — no gameplay dependencies, file-disjoint from raid/AI work).
**Gate + commit:** the lead. This lane ran `tools/gate_brace.py` + a NUL scan only.
