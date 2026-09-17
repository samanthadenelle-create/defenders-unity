# WO-1813 RESULT: white untextured quad on hero during fireball / level-up

**Status:** READY

⚠ **Deliberately NOT flipped to IMPLEMENTED.** The root cause is now **reproduced and measured**,
and two other real defects of the same class were found and fixed — but the remedy for the
level-up effect itself is an owner call (it changes how a tagged VFX key looks), and this lane does
not pick VFX looks (`vfx-map-owner-tags-no-creative-pick`). What is READY is one question, below.

---

## 1. The 2026-09-16 exclusion is REFUTED, by a rendered frame

The previous pass narrowed the quad to three `Level_up.prefab` candidates and then excluded all
three with two arguments:

> *"Hard edge excludes any slot healed with `SoftDotTexture` — a radial fade cannot draw a straight
> edge."* and *"Occlusion excludes an ADDITIVE slot — additive brightens what is behind it, never
> hides it."*

Both are wrong, and the proof is a picture rather than an argument.
`Builds/vfx-whitequad/Juice_LevelUp__AFTER.png` — `VFXType.Juice_LevelUp` staged alone from the
real catalog, from the owner's own camera seat (3.5 m back, 1.4 m up, 60° vfov, 2670x1200),
simulated to 1.0 s, **with the soft-dot heal proven to have run in that same batch**
(`Builds/wo1813-whitequad3.log`: `HealHalfUpgradedParticleMaterial: '1AB_mat' …` and `'1Add_mat' …`):

| measurement | value | how |
|---|---|---|
| edge at y=400 | `(15,17,20)` → `(140,96,34)` inside a single 10 px step at **x=1000**, and back at **x=1680** | pixel scan of the PNG |
| fill between them | uniform `(139-143, 97-100, 34-37)` across all 680 px | same scan |
| width | 680 px ÷ 297 px·m⁻¹ = **2.29 m** | 1200 / (2·tan30 · 3.5 m) |
| shape | razor-sharp, screen-axis-aligned, uniformly filled **rectangle** | the PNG |

So the effect **does** draw a hard-edged axis-aligned slab after the heal. And the "additive cannot
occlude" half fails too — additively compositing that measured fill over a bright daytime town:

```
(143,100,37) + grass (150,170,110) -> (255,255,147)
(143,100,37) + sky   (205,225,245) -> (255,255,255)
(143,100,37) + wall  (214,200,150) -> (255,255,187)
```

The owner's frame measures **`(254,254,246)`** in one region and **`(254,230,208)`** in another —
R and G pinned at the top, B varying with whatever is behind. That is the signature of an additive
quad saturating, not of an opaque one occluding, and it explains in one line why the frame shows
*two different whites* and why it reads as solid. **Measuring something is not the same as
measuring the right thing** — the previous pass measured the screenshot correctly and then reasoned
from a property of the blend mode that a render disproves.

**Root cause: `VFXType.Juice_LevelUp` → `Assets/Lana Studio/Casual RPG VFX/Prefabs/States/Level_up.prefab`.**
All **eleven** of its renderer slots carry `1AB_mat` / `1Add_mat`, both `_BaseMap: {fileID: 0}` —
**no albedo at all** — so every quad is an untextured rectangle carrying only a tint. It fired at
`20:51:30.481` at `(0.20, 0.13, -4.15)`, the hero, 0.6 s before the capture.

**Not pinned to ONE fileID, and stated as such.** The soft-dot heal makes the *billboard* slots
round, yet the slab survives it — which points at `area` (`!u!199 &4557073697047843055`,
renderMode **Mesh**, `1Add_mat`, startSize 3.46: a cylinder silhouette is a rectangle with an
elliptical base, and the render shows exactly that ring). That is an inference from shape, not a
measurement of which renderer wrote which pixel, so it is written here as the lead and not as the
answer.

⚠ **And there is a shape MISMATCH that this lane did not resolve.** The harness renders ONE tall
column; the owner's frame shows **three overlapping square-ish quads** (~455x405 px each). A
cylinder mesh does not produce three squares — the *billboards* do (`circle` 2.25, `circle_wave`
3.18, `flash` 6.36). But the BEFORE and AFTER frames are visually near-identical (904,824 vs
907,199 px changed) even though the heal demonstrably fired between them, which is what you would
see EITHER if the healed billboards are hidden inside the column OR if the heal is not reaching
whatever actually draws. **That is the open thread, and the cheapest closer is still the previous
lane's instrument:** `AuditDrawingBillboardCensus` has never run on device (it was added after the
owner's build). One town session to a level-up and
`adb logcat | grep "DRAWING BILLBOARD CENSUS prefab='Juice_LevelUp'"` prints, per child,
`albedo=SOFTDOT(radial-fade)` vs `NONE(samples-white)`, the blend and the world size on the POOLED
instance — which settles both the mismatch and the fileID in one read.

## 2. `PP_FleshImpacts` is ELIMINATED — and it took a real defect down with it

The previous RESULT left it as the leading unexamined candidate. It is not the quad:
`Mist`'s `startLifetime` is `minMaxState: 3`, **0.08-0.5 s**; the last `PP_FleshImpacts` fired at
**20:51:29.429** (`logcat.txt:827735`, the only one after 20:51:12) with `[Flow:Pause]` showing
`timeScale 1.00` throughout (the two combat dips in between last 0.07 s each). Every particle was
dead ~1.1 s before the capture. **The `lifetime=8.30s` in the log line is the pooled HOST's
despawn timer, not the particles' life.**

But examining it found a genuine shipped defect that nothing in the tree could see:

`Assets/Resources/VFX/Impact/FleshImpacts.prefab` child **`Mist`**, `!u!199` fileID
**`199462925942737768`**, `m_Enabled: 1`, renderMode **Billboard**, **slot 0**, material
**`GoopMist`** (`Assets/Resources/VFX/_Shared/Materials/GoopMist.mat`, guid
`8197b9eb112c6a1428518197b3ad2dbb`): `Universal Render Pipeline/**Lit**`, `_Surface: 0`,
`_SrcBlend: 1`, `_DstBlend: 0`, `_ZWrite: 1`, `RenderType: Opaque`, **`_AlphaClip: 0`** — and its
`_BaseMap`, `DustPuffSmallParticleSheet.png`, is **`(255,255,255)` at every sampled texel with the
entire sprite in the ALPHA channel** (alpha min 0, max 227, mean 12 — direct pixel read). Opaque
discards that alpha, so it paints a solid sun-lit white square on **every flesh hit**.

Why every net missed it, all four structural:
`IsLegacyParticleShader` → false (URP name) · `HealHalfUpgradedParticleMaterial` → bails at
`particleLike` ("URP/Lit" has neither "Particles" nor "Unlit") · `TryRepairOpaqueLitParticleSlot` →
name-gated `MagentaFix*` · `AuditParticleSlotsAfterRepair` → requires opaque **AND** albedo-less,
and **the albedo is the very thing that makes it white**. And above all: `PP_FleshImpacts` is a
**PlayKey** row, and `VFXManager.Hovl.CreateHovlInstance` ran **no pass at all**, so none of them
was even reached.

## 3. What shipped

| file | lines | what |
|---|---|---|
| `Assets/_Modules/Village/Hero/AbilityVfxKit.cs` | `:356-418` derivation comment, `:419-424` clone cache, `:426-436` `IsOpaqueNonCutout`, `:442-509` `RepairOpaqueDrawingParticleSlots`, `:511-541` `RepairMagentaFixParticleSlots` | the one owner of the opaque-billboard rule, and a subtree sweep so the PlayKey path reaches the existing WO-1806 helper instead of growing a copy |
| `Assets/_Modules/Village/Vfx/VFXManager.cs` | `:1041-1047` | the repair runs before the audits on the VFXType path |
| `Assets/_Modules/Village/Vfx/VFXManager.Hovl.cs` | `:577-606` | **the hole**: `CreateHovlInstance` now runs the narrow repair pair + both audits. It still never re-shades the HS_* graphs |
| `Assets/Editor/Regression/VfxParticleNullSlotRegression.cs` | `:270-272`, `:296-306`, `:308-407` | proves the repair COVERS the class instead of baselining it |
| `Assets/Editor/VfxProofCapture.cs` | `:97-103`, `:187-210`, `:246-350`, `:800-816`, `:868-876`, `:1263-1330`, `:1420-1432` | the WO-1813 white-quad hunt entry point |
| `docs/handoffs/overnight-2026-09-16/VFX_SLOT_CLASSIFICATION.md` | whole file | the complete measured slot classification (was "PARTIAL / deferred") |

**The rule, and why it is exactly this narrow.** Enabled + renderMode neither `None` nor `Mesh` +
`_Surface == 0` + `_AlphaClip == 0`. A camera-facing quad drawn opaque cannot express a sprite
whose shape lives in its alpha, at any texture or size. `Mesh` is excluded (debris and shards are
legitimately opaque — the WO-1806 carve-out). **Alpha cutout is excluded, and that carve-out is
measured, not assumed:** the two sibling prefabs, `StoneImpacts/ImpactDebris`
(`TinyStonesParticle`) and `WoodImpacts/WoodSplinters` (`WoodSplintersParticle`), are *also* opaque
billboards, both `_AlphaClip: 1`, and both render correctly today. Without the carve-out this
"repair" would have broken two working effects.

**It repairs, it does not substitute** (owner ruling): a cached clone of the authored material —
same shader, same textures, same colours, same keywords — with only the blend state changed.
Additive vs alpha is read off the source's own `_DstBlend`, the same read
`HealHalfUpgradedParticleMaterial` already makes. No colour was chosen, no owner-tagged key was
re-pointed, no prefab or `.mat` on disk was edited.

**Second defect fixed on the way**: the capture printed
`PARTICLE SLAB after repair … slot=1 material='MagentaFix_DefaultLit'` for **SimpleCast_Cast,
Spear_Impact, Lightningspellmaybe_Cast, lighteningOnSpellLand_Impact and ArcherTower_Projectile** —
five COMBAT effects whose trail slabs the WO-1806 remedy had never reached, because the PlayKey
path never ran it. It runs there now.

## 4. Evidence

* Owner frame: `logs/device/owner-fireball-20260916/Screenshot_20260916-205131.png`
* **42 before/after PNGs** (21 keys x authored / repaired), `Builds/vfx-whitequad/` + `INDEX.md`.
  Load-bearing ones opened at 1:1: `Juice_LevelUp__BEFORE.png`, `Juice_LevelUp__AFTER.png`,
  `PP_FleshImpacts__BEFORE.png`, `PP_FleshImpacts__AFTER.png`.
* Runtime proof the repair fires on the named renderer, `Builds/wo1813-whitequad3.log`:
  `[Flow:VFX] OPAQUE BILLBOARD REPAIRED: prefab='key:PP_FleshImpacts' child='Mist' slot=0
  material='GoopMist' shader='Universal Render Pipeline/Lit' renderMode=Billboard
  albedo='DustPuffSmallParticleSheet'…` — **one line, one renderer, exactly the one the YAML named.**
* Gates, fresh logs: `COMPILE_GATE_OK :: scripts compiled clean` (`Builds/wo1813-compile.log`, 0
  `error CS` under `Assets/`); `REGRESSION_OK 564/564 suites -- 564 green, 0 red, 0 skipped`
  (`Builds/wo1813-regression.log`), including
  `VFX_NULL_SLOT_OK … 21 authored opaque drawing particle slot(s) proved REPAIRED …; 184 prefab(s) checked`.
* `python tools/gate_brace.py` on all five `.cs`: `GATE_BRACE_SUMMARY bad=0 of 5`, exit 0; NUL scan 0
  on each.

## 5. Honest limits

* **A headless capture could not reproduce the owner's frame end to end**, and no attempt was made
  to pretend otherwise. The stage is dark by design, so the additive slab reads gold there and
  white on her sunlit town; the bridge between them is the arithmetic in §1, not a matching picture.
* **The specific `Level_up.prefab` renderer is not pinned to a fileID.** `area` (Mesh) is the lead
  because it is the only slot whose shape survives the soft-dot heal. Stated as a lead.
* `PP_FleshImpacts`, `PP_MuzzleFlash`, `Cast_MuzzleFlash` and `ArcherTower_Projectile` render
  **0 px** in the harness at every rung even with 23 / 2 / 11 particles alive — classified as
  *built-but-invisible*, not *data-empty*, by the new per-rung particle count in `INDEX.md`. Why
  they are invisible from that seat is unexplained and is a harness limit, not a game finding.
* No AutoPilot headless raid on `RaidBase_fortified_garrison` was run: this lane held the single
  Unity gate for five batch runs (compile, two captures, the positive control, the regression) and
  a sixth play-mode run did not fit. The edit-mode capture answers the same question more directly.
* **Two counts of the same class disagree and were NOT reconciled.** The Python YAML scan finds
  **19** opaque-draw slots across 264 prefabs; the Unity oracle reports **21** across 184. The scan
  sets are different (the oracle is Hovl rows + `Resources/VFX` + 4 pinned paths; Python walked
  every `VFXCatalog` + `HovlVfxCatalog` + `Resources/VFX` prefab) and the Python regex only sees a
  `_Surface` that is actually serialised. Named rather than chased — neither number changes the
  finding or the fix.
* **`ForceAuthoredBurst` was added to the SHARED `SimulateAll`, so it also affects
  `VfxProofCapture.Run`,** not only the hunt. It can only turn a NOT-DRAWN shot into a drawn one
  (it emits only where a system came back empty), and the hunt's own failure count moved 14 -> 12
  between runs — but which two flipped was not isolated. Left ungated deliberately; a lane running
  the standard proof capture should expect verdicts to move in that direction only.
* **The Hovl path now emits one `DRAWING BILLBOARD CENSUS` `Once` line per PlayKey key** (~1.5 KB,
  100+ catalog rows). Bounded, but it is a real logcat-ring cost on top of the VFXType path's —
  worth watching against `logcat-ring-buffer-destroys-evidence` if a device capture ever looks thin.

### ⚠ Tree left dirty by ONE file, and it was not this lane's edit

Five batch runs write shared materials back to disk. Sixteen `.mat`/`.prefab` files under `Assets/`
have a newer mtime than the first run; **fifteen are gitignored pack files or were reverted**
(`Death_Brute.prefab` is byte-clean, verified). **One is tracked and modified:**

```
 M Assets/Resources/VFX/_Shared/Materials/GoopMist.mat
     - _MainTex:  m_Texture: {fileID: 0}
     + _MainTex:  m_Texture: {fileID: 2800000, guid: ae07a4b1a7821614d830866e22315a72, type: 3}
```

`_MainTex` was synced to the same texture `_BaseMap` already held (`DustPuffSmallParticleSheet`) —
Unity's own URP alias write-back, not a line of this lane's code (the repair CLONES, it never
mutates a source material). It is benign and self-consistent, but it is an undeclared tracked
change and the lead should decide, not inherit it silently:
`git checkout -- "Assets/Resources/VFX/_Shared/Materials/GoopMist.mat"` reverts it and changes
nothing about the fix. **Deviation declared:** establishing this needed one read-only
`git status --short` and one `git diff` beyond the single revert this lane was permitted.

## 6. THE ONE QUESTION THAT CLOSES THIS — for the owner

`Level_up.prefab` ships with **no texture on any of its eleven slots**, so the level-up burst is
made of untextured tinted rectangles. On a dark background that reads as a golden column; on the
sunlit town it saturates to the white slab she photographed. Three ways forward, and this is a look
decision, not an engineering one:

1. **Bind the pack's intended sprites** to `1AB_mat` / `1Add_mat` (the reference is not recorded
   anywhere in the YAML — both carry orphaned Lana-shader properties `_AlphaMult`, `_ColourMult`,
   `_EmisColor` — so it may need the pack re-imported).
2. **Extend the soft-dot heal to the Mesh slot** (`area`), which the current code deliberately
   refuses because a dot UV-mapped onto a cylinder can look worse than the untextured one.
3. **Tone the additive intensity** so it stops clipping against a bright sky.

Say which, and it is a short change. **Do not let a lane pick one** — that is exactly the
"never substitute a prettier prefab" line.
