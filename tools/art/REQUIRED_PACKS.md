# Required Art Packs â€” runtime manifest + travel policy

**Historical authority:** `docs/PAIN_POINTS_2026-07-26.md` Â§1.2 â€” RULING *"Tracked runtime + zip travel"*.
**Scope:** This file is the human checklist half of that ruling. The machine half is
`tools/art/verify-runtime-art.ps1` (run it on every fresh clone â€” see the onboarding checklist at
the bottom).

**Current contract (2026-09-13):** WO-1338 (`WorkOrders/WORK_ORDER_1338_heroes_to_r2_remote_content.md`, owner R2 ruling) supersedes the July Resources-only body locations. Enemy assets live in `Assets/EnemyContent/`; the selected KnightV3 body lives in `Assets/HeroContent/KnightV3.fbx`. Their extensionless runtime keys remain `Enemies/<slug>` and `Heroes/KnightV3`. They require the matching Addressables catalog and built/published remote bundles; a bare clone is not an offline-playability proof.

Source evidence: `EnemyAssetLoader.LoadEnemyPrefab` and `LoadEnemyController` serve the `EnemyContentWarmer` resident cache and request misses; `EnemyFactory`, `EnemyAnimatorFactory`, and `AtbCombatantSwapper` consume this seam. `HeroBodySwapper` consumes `HeroAssetLoader.LoadHeroPrefab`; `HeroContentPrewarmer` retains the chosen body before scene entry. `EnemyContentMigrator` preserves the address scheme. The migration authority explicitly retains `Resources/Heroes/Knight.fbx` and local troop controllers; do not move those to satisfy this checker.

The checker pins all eleven migrated asset GUIDs and exact addresses, requires one GUID entry in the expected registered group (`Enemy_Models`, `Enemy_Controllers`, or `Hero_KnightV3`), requires its linked enabled remote bundle schema, and rejects empty/LFS-pointer files. NPC Resources and committed People checks remain. This is a static input/binding gate, not a dependency-closure, Unity import, CDN, or rendered-art test. Build and publish matching content and run the platform smoke test separately.

**Why this exists:** the big character/environment packs are **gitignored** (owner policy: git never
holds the multi-GB source packs). On a machine that only did `git pull`, the packs are absent, so the
game rendered **generic silhouettes / capsules ("Bryn is a pill"), untextured bodies, and identical
unarmed enemies** â€” and CI saw zero art. The fix is *not* to commit the packs; it is to (a) guarantee
**runtime art inputs remain tracked at their current content roots**, with remote bodies delivered through Addressables and local NPCs retained in Resources, and (b) write down how a new machine obtains the full packs (**zip / local
copy, never `git pull`**).

---

## 1. The policy in one paragraph

Git tracks the runtime body inputs under `Assets/EnemyContent/`, migrated hero bodies under `Assets/HeroContent/`, deliberately local troop/prop assets under `Resources/Heroes/`, and local NPC prefabs. The committed People NPC pack is hydrated through LFS. Large ignored source packs travel by reviewed local copy or archive; Git does not supply them. Their absence does not remove the tracked inputs, but remote bodies still require matching Addressables content. This manifest does not promise offline playability on a bare clone.

---

## 2. Pack table

| Pack | Needed by | Expected on-disk path | Tracked? | How to get it |
|---|---|---|---|---|
| **KayKit Skeletons 1.1** | Hollow Ones enemies (Minion / Golem / Necromancer bodies + Mage/Warrior/Rogue variants) | `Assets/Models/KayKit/KayKit Skeletons 1.1/` (source) â€” *note the top-level `Assets/Models/KayKit Skeletons 1.1/` is an empty stub, the real tree is under `KayKit/`* | **gitignored** | Zip / local copy from owner. Runtime fallback = `Assets/EnemyContent/Skeleton_*.fbx` (Addressables `Enemies/*`) (tracked). |
| **KayKit Adventurers 2.0** | Troop bodies (WO-771.13) + hero test bodies | `Assets/Models/KayKit Adventurers 2.0/` **and/or** `Assets/Models/KayKit/KayKit Adventurers 2.0/` | **gitignored** | Zip / local copy. Runtime bodies = tracked `Assets/HeroContent/*` via remote Addressables, plus deliberately local Knight troop assets. |
| **KayKit Dungeon Remastered 1.1** | Dungeon geometry (WO-770.8) â€” already staged per audit | `Assets/Models/KayKit/dungeon/` (fbx/gltf) + `Assets/Models/KayKit/KayKit Dungeon Remastered 1.1.zip` | **gitignored** | Zip present in-tree at `KayKit/â€¦1.1.zip`; unzipped tree under `KayKit/dungeon/`. Fallback = box doorway / builder placeholders. |
| **People pack** (optimized NPC pack, DEF-91) | Bryn-class NPCs, Blacksmith / Merchant / Peasant, `FighterClass` body | `Assets/Models/People/` (FBX + per-model `*/Textures/`) | **TRACKED (LFS)** â€” the one `Models/*` exception (`.gitignore` line 107 `!/Assets/Models/People/`) | Comes with the clone. If LFS pointers didn't hydrate: `git lfs pull`. |
| **People shared skin textures** | `FighterClass` / CC_Base body skin (the "untextured-Bryn" half) | `Assets/Models/People/textures/` **(gitignored â€” line 310)** | **gitignored** | Zip / local copy. NOTE: per-model `People/<NPC>/Textures/*.png` and the `*.fbm/textures/` embedded skins ARE tracked, so the committed Blacksmith/Merchant/Peasant/Fighter NPCs are textured; this shared folder only bites bodies that reference it. |
| **People Human / Orc / Troll variants** | extra NPC/enemy body variants | `Assets/Models/People/{Human,Orc,Troll}/` **(gitignored â€” lines 304-309)** | **gitignored** | Zip / local copy. Not required to boot. |
| **AccuRig remote enemy family** | Tracked Hollow Ones / Orc / boss build inputs | `Assets/EnemyContent/*.fbx` + `Boss_Dragon.prefab` + controllers | **TRACKED** | Comes with the clone. Requires the registered `Enemies/*` catalog keys and published remote content. |
| **Unity Technologies Particle Pack** (VFX source) | **54 owner-tagged VFX keys** in `Assets/Editor/VfxManualPicks.json` point into this tree; `Enemy.cs` (travelling fireball body) and `PoiCalloutSystem.cs` (`TreeofLifeAura_Aura` = ParticlePack FireFlies) consume it | `Assets/UnityTechnologies/ParticlePack/` (191 MB / 886 files) | **gitignored** (2026-07-30) | Zip / local copy from the owner. âš  **NO runtime fallback** â€” unlike the character packs, a machine without this pack silently loses those 54 tagged effects (`VFXManager.PlayKey` finds no prefab). Follow-up: promote the used prefabs into a tracked `Resources/VFX/` path, or ship the zip alongside. |

**Shared humanoid animator path (per the ruling):** all humanoids retarget through the AccuRig
`SkeletonHumanoid` controller / KayKit `Rig_Medium` (see `EnemyAnimatorFactory.cs` +
`docs/SME/KAYKIT_SME.md`). Modular weapons attach to that one path â€” **one perfect armed type
(Hollow Warrior) first**, then variants. Do not enable multi-weapon spam before Warrior feels good.

---

## 3. Tracked vs gitignored (quick reference â€” source: `.gitignore`)

**Committed / LFS (required source inputs on a hydrated clone â€” the fallback cast):**
- `Assets/EnemyContent/*` â€” AccuRig bodies: `Skeleton_{Warrior,Rogue,Mage,Healer,Golem,Minion}.fbx`, `Orc_*.fbx`, `Necromancer.fbx`, `Boss_Dragon.prefab`, controllers
- `Assets/Resources/NPCs/*.prefab` â€” `NPC_{Blacksmith,Merchant,Peasant_Mevina,Peasant_Tob}`
- `Assets/HeroContent/*` - migrated remote hero bodies; `Assets/Resources/Heroes/*` - deliberately local Knight troop body/controllers and props
- `Assets/Models/People/` â€” optimized People pack (the sole `Models/*` exception), incl. per-model `*/Textures/*.png`

**Gitignored (travel by zip / local copy):**
- `Assets/Models/*` **except** `People/` â€” all raw KayKit trees, Mystery Monthly, medieval, weapons
- `Assets/Models/People/{textures,Human,Orc,Troll}/` â€” shared skins + variant bodies
- `Assets/polyperfect/`, `Assets/Quaternius/`, `Assets/Supercyan/`, `Assets/Tech hud elements/`, bulky VFX packs, `Assets/Resources/Structures/` (Tripo)

**Do NOT commit** the multi-GB source packs â€” that is a separate owner LFS/zip decision, not this manifest.

---

## 4. Onboarding checklist â€” new clone

Run these in order on a fresh machine before expecting art to render:

1. `git lfs pull` â€” hydrate the tracked People pack + any LFS bodies.
2. **`pwsh tools/art/verify-runtime-art.ps1`** (or `powershell -File tools\art\verify-runtime-art.ps1`)
   â€” proves the CRITICAL tracked runtime keys + committed People body/textures exist, and WARNS about
   any gitignored source pack that is absent. **Non-zero exit = a tracked fallback is missing â†’ fix
   before building** (the build would render pills/magenta).
3. If step 2 WARNs about a gitignored pack you actually need (KayKit dungeon, skeleton source, People
   skins), **copy the pack in from the owner's zip / source folder** into the expected path from the
   table above. Do not `git pull` it â€” it isn't in git.
4. Re-import the humanoid skeleton family if you staged new KayKit bodies:
   **`Defenders â†’ Animation â†’ Import Skeleton Family`** (documented required onboarding step per the
   ruling).
5. If a pack uses Built-in shaders and renders magenta, run its URP fixer
   (`Defenders/Art/Fix Polyperfect URP Materials`, `Defenders/Art/Fix Supercyan URP Materials`, etc.).

A passing checker proves only the listed static inputs and bindings. Build and publish matching remote catalog/bundles, then verify art on each target platform. The absent `People/textures` folder remains an explicit warning; relocation does not make it present.
