# WO-1813 RESULT: white untextured quad on hero during fireball / level-up

**Status:** IMPLEMENTED

Closed 2026-09-17 under the lead's repair ruling. The drawer is **named by isolation frames, not by
elimination**, the remedy is **the tree's own prior ruling for this exact class**, and the fix is
confirmed in a rendered frame from the owner's camera seat. Gates green.

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

---

## 7. THE DRAWER, NAMED — and the remedy, 2026-09-17

### It is `Level_up.prefab` child `area`, proven by isolation and nothing else

The census (§1) printed all ten drawing slots as `albedo=SOFTDOT(radial-fade)` while the frame still
carried a 680 px razor-sharp edge — so **what a renderer HOLDS does not settle what it DREW**, and
no amount of further material reading could close it. `VfxProofCapture` gained
`Shot.OnlyChild`: ten frames of `Level_up.prefab`, same seat, all repairs applied, every child but
one deactivated. Hard-jump scan on row y=400 of each solo PNG:

| solo frame | hard jumps on y=400 |
|---|---|
| **`solo_Juice_LevelUp__area.png`** | **x=995, x=1675** |
| `glow_start`, `lines_start`, `boke_start`, `flash`, `circle`, `circle_wave`, `arrows`, `lines`, `boke` | *none, all nine* |

`area` = `!u!199 &4557073697047843055`, `m_Enabled: 1`, renderMode **Mesh**, slot 0 `1Add_mat`,
additive, startSize 3.46. One candidate, positively identified.

### The pack's texture is GONE, and that was checked before anything else was done

`Assets/Lana Studio/**` IS tracked (`git ls-files` → 595 files), so editing the `.mat` was on the
table. It has nothing to edit *in*: `1AB_mat.mat` and `1Add_mat.mat` (both URP Particles/Unlit)
carry `_BaseMap: {fileID: 0}` **and** `_MainTex: {fileID: 0}` — no guid survives in either YAML.
Their siblings DID survive the upgrade (`AB_01` → `t_trail01.png`, `Add_01` → `t_trail02.png`),
which is how we know the loss is real and specific to these two. The pack's `Upgrade for URP/`
folder holds only a `.unitypackage`, no loose materials. Choosing one of the pack's ~30 loose
sprites for a material shared by ten differently-shaped children would be picking a look.

### ⛔ The fade-texture remedy was built, shipped, MEASURED and REFUTED

Step (3) of the ruling asked for the tree's soft fade on the mesh slot. It was implemented as a
two-axis `SoftEdgeTexture` (alpha → 0 on every UV border), the `MESH PARTICLE FADE` line fired on
`area`, and **the edge did not move**: the re-shot frame still stepped (29,72,25) → (143,122,38)
inside one 5 px step at x=995 and back at x=1675. The reason is geometric and no texture can reach
it — a cylinder's side unwraps with u running AROUND the barrel, so the screen-left and -right
silhouette edges sit at u = 0.25 / 0.75, the MIDDLE of the texture, where an edge fade is still at
alpha ≈ 0.51. **A texture cannot soften a silhouette.** The refutation is kept in the code comment
at `AbilityVfxKit.RepairUntexturedMeshParticleSlots` so it is not re-attempted.

### The remedy is the tree's OWN ruling, nine days old

`VFXManager.SuppressUntexturedImpactMesh` (`VFXManager.cs:869`) says it in its own words:
*"Lana Slash_stone_once draws a MESH quad. With 1AB_mat's empty _BaseMap it is a white rectangle;
after SoftDot heal it is a giant grey card (owner Seeker 2026-09-09 09:46:58). Disable those mesh
slots."* **Same pack, same two materials, same owner, same artefact, nine days earlier.** The only
reason `Juice_LevelUp` kept drawing it is that the 09-09 fix was hard-gated to
`VFXType.Impact_Physical`. `AbilityVfxKit.RepairUntexturedMeshParticleSlots` is that ruling applied
**by the condition instead of by the type name**, on both spawn paths. Nothing new was decided about
how anything should look — and this is why the flip to IMPLEMENTED is a repair, not a creative pick.

Narrow on purpose: `renderMode == Mesh` only, and only when **every** slot's albedo is absent (null,
or one of the generated soft textures). A mesh particle carrying real pack art — debris, shards, a
textured beam — is never touched, and no billboard is.

### Acceptance (ruling step 4), measured

`Builds/vfx-whitequad/Juice_LevelUp__AFTER.png`, re-shot after the change, owner's seat:

* `[Flow:VFX] UNTEXTURED MESH SLAB DISABLED: prefab='VFXType.Juice_LevelUp' child='area'
  material='1Add_mat' renderMode=Mesh …`
* hard-jump scan: **y=400 → none. y=550 → none.** (Before: x=995 and x=1675 on y=400.)
  y=250 still has jumps at 1060-1355 — those are the `arrows` sprite's own outlines, a real textured
  asset drawing its authored shape.
* Opened at 1:1: the column is gone; the rising gold arrows, the double ground ring and the warm
  floor glow all still read. Nothing was re-tinted and no effect was swapped.

### Gates (ruling step 5), fresh logs, marker-judged

| gate | marker |
|---|---|
| `Builds/wo1813-compile2.log` | `COMPILE_GATE_OK :: scripts compiled clean` (0 `error CS` under `Assets/`) |
| `Builds/wo1813-regression2.log` | `REGRESSION_OK 564/564 suites -- 564 green, 0 red, 0 skipped` |
| same log | `VFX_NULL_SLOT_OK … 23 drawing slot(s) on the WO-1813 capture prefabs proved TEXTURED after the runtime repair chain; 21 authored opaque drawing particle slot(s) proved REPAIRED …` |
| `python tools/gate_brace.py` (5 files) | `GATE_BRACE_SUMMARY bad=0 of 5`, exit 0; NUL 0 each |

`VfxParticleNullSlotRegression.CheckEveryDrawingSlotEndsUpTextured` is the new oracle for ruling
step (5): it runs the REAL chain (heal → opaque repair → MagentaFix repair → mesh suppress) on a
throwaway instance of each of the four capture prefabs and fails any **enabled, drawing** slot left
without a `_BaseMap`. It clones every material first, so — unlike the batch runs that dirtied
`GoopMist.mat` — the oracle can never write to the tree it is checking.

### Files changed by this pass (on top of §3)

| file | lines |
|---|---|
| `Assets/_Modules/Village/Hero/AbilityVfxKit.cs` | `:1433-1487` `SoftEdgeTexture` + generator, `:1489-1573` `RepairUntexturedMeshParticleSlots` (carries the refutation) |
| `Assets/_Modules/Village/Vfx/VFXManager.cs` | `:1049-1056` |
| `Assets/_Modules/Village/Vfx/VFXManager.Hovl.cs` | `:603-607` |
| `Assets/Editor/Regression/VfxParticleNullSlotRegression.cs` | `:274-275`, `:308-317`, `:385-490` |
| `Assets/Editor/VfxProofCapture.cs` | `:193-198` `Shot.OnlyChild`, `:788-797` isolation gate, `:335-361` the ten solo shots |

### Still not proven, and left that way

* **`SoftEdgeTexture` is now UNUSED by any repair** — kept because
  `RepairUntexturedMeshParticleSlots` reads it when deciding whether an albedo is "generated", and
  because deleting it would delete the evidence for the refutation above.
* **The owner has not felt-verified this.** The picture is from the harness's dark stage; her town
  is bright. The *mechanism* is closed (the slab no longer draws at all, so there is nothing left to
  saturate), but PO closure is still hers (§13).
* The `GoopMist.mat` dirty-tree note in §5 still stands and still needs the lead's one-line revert.
